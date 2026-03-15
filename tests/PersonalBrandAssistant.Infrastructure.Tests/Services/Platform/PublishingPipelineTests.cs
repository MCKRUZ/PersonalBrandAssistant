using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.PlatformServices;
using PersonalBrandAssistant.Infrastructure.Tests.Helpers;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;

public class PublishingPipelineTests
{
    private readonly Mock<IApplicationDbContext> _db = new();
    private readonly Mock<ISocialPlatform> _twitterAdapter = new();
    private readonly Mock<ISocialPlatform> _linkedInAdapter = new();
    private readonly Mock<IPlatformContentFormatter> _twitterFormatter = new();
    private readonly Mock<IPlatformContentFormatter> _linkedInFormatter = new();
    private readonly Mock<IRateLimiter> _rateLimiter = new();
    private readonly Mock<INotificationService> _notification = new();
    private readonly PublishingPipeline _sut;

    private readonly List<ContentPlatformStatus> _statuses = [];

    public PublishingPipelineTests()
    {
        _twitterAdapter.Setup(a => a.Type).Returns(PlatformType.TwitterX);
        _linkedInAdapter.Setup(a => a.Type).Returns(PlatformType.LinkedIn);
        _twitterFormatter.Setup(f => f.Platform).Returns(PlatformType.TwitterX);
        _linkedInFormatter.Setup(f => f.Platform).Returns(PlatformType.LinkedIn);

        _rateLimiter.Setup(r => r.CanMakeRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RateLimitDecision(true, null, null)));

        SetupStatusDbSet([]);

        _sut = new PublishingPipeline(
            _db.Object,
            new[] { _twitterAdapter.Object, _linkedInAdapter.Object },
            new[] { _twitterFormatter.Object, _linkedInFormatter.Object },
            _rateLimiter.Object,
            _notification.Object,
            NullLogger<PublishingPipeline>.Instance);
    }

    private Content CreateContent(params PlatformType[] targets)
    {
        var content = Content.Create(ContentType.SocialPost, "Hello world", "Title", targets);
        content.TransitionTo(ContentStatus.Review);
        content.TransitionTo(ContentStatus.Approved);
        content.TransitionTo(ContentStatus.Scheduled);
        content.TransitionTo(ContentStatus.Publishing);
        return content;
    }

    private void SetupContent(Content content)
    {
        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(new[] { content });
        _db.Setup(db => db.Contents).Returns(mockSet.Object);
    }

    private void SetupStatusDbSet(List<ContentPlatformStatus> statuses)
    {
        _statuses.Clear();
        _statuses.AddRange(statuses);
        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(_statuses.ToArray());
        mockSet.Setup(s => s.Add(It.IsAny<ContentPlatformStatus>()))
            .Callback<ContentPlatformStatus>(s => _statuses.Add(s));
        _db.Setup(db => db.ContentPlatformStatuses).Returns(mockSet.Object);
    }

    private void SetupFormatters(PlatformType platform, Content content, Result<PlatformContent> result)
    {
        var formatter = platform == PlatformType.TwitterX ? _twitterFormatter : _linkedInFormatter;
        formatter.Setup(f => f.FormatAndValidate(content)).Returns(result);
    }

    private static string ComputeExpectedKey(Guid contentId, PlatformType platform, uint version)
    {
        var input = $"{contentId}:{platform}:{version}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }

    [Fact]
    public async Task PublishAsync_ContentNotFound_ReturnsNotFound()
    {
        var emptySet = AsyncQueryableHelpers.CreateAsyncDbSetMock(Array.Empty<Content>());
        _db.Setup(db => db.Contents).Returns(emptySet.Object);

        var result = await _sut.PublishAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task PublishAsync_SkipsAlreadyPublishedPlatform()
    {
        var content = CreateContent(PlatformType.TwitterX);
        SetupContent(content);

        var existing = new ContentPlatformStatus
        {
            ContentId = content.Id,
            Platform = PlatformType.TwitterX,
            Status = PlatformPublishStatus.Published,
        };
        SetupStatusDbSet([existing]);

        var result = await _sut.PublishAsync(content.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _twitterAdapter.Verify(a => a.PublishAsync(It.IsAny<PlatformContent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_SkipsProcessingPlatform()
    {
        var content = CreateContent(PlatformType.TwitterX);
        SetupContent(content);

        var existing = new ContentPlatformStatus
        {
            ContentId = content.Id,
            Platform = PlatformType.TwitterX,
            Status = PlatformPublishStatus.Processing,
        };
        SetupStatusDbSet([existing]);

        var result = await _sut.PublishAsync(content.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _twitterAdapter.Verify(a => a.PublishAsync(It.IsAny<PlatformContent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_SetsIdempotencyKey()
    {
        var content = CreateContent(PlatformType.TwitterX);
        SetupContent(content);

        var formatted = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
        SetupFormatters(PlatformType.TwitterX, content, Result.Success(formatted));
        _twitterAdapter.Setup(a => a.PublishAsync(formatted, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PublishResult("t-1", "https://x.com/i/status/t-1", DateTimeOffset.UtcNow)));

        await _sut.PublishAsync(content.Id, CancellationToken.None);

        var status = _statuses.First(s => s.Platform == PlatformType.TwitterX);
        var expectedKey = ComputeExpectedKey(content.Id, PlatformType.TwitterX, content.Version);
        Assert.Equal(expectedKey, status.IdempotencyKey);
    }

    [Fact]
    public async Task PublishAsync_FormatFailure_SetsSkipped()
    {
        var content = CreateContent(PlatformType.TwitterX);
        SetupContent(content);

        SetupFormatters(PlatformType.TwitterX, content,
            Result.Failure<PlatformContent>(ErrorCode.ValidationFailed, "Too long"));

        await _sut.PublishAsync(content.Id, CancellationToken.None);

        var status = _statuses.First(s => s.Platform == PlatformType.TwitterX);
        Assert.Equal(PlatformPublishStatus.Skipped, status.Status);
        _twitterAdapter.Verify(a => a.PublishAsync(It.IsAny<PlatformContent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublishAsync_RateLimited_SetsRateLimitedWithRetryAt()
    {
        var content = CreateContent(PlatformType.TwitterX);
        SetupContent(content);

        var formatted = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
        SetupFormatters(PlatformType.TwitterX, content, Result.Success(formatted));

        var retryAt = DateTimeOffset.UtcNow.AddMinutes(10);
        _rateLimiter.Setup(r => r.CanMakeRequestAsync(PlatformType.TwitterX, "publish", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RateLimitDecision(false, retryAt, "Too many requests")));

        await _sut.PublishAsync(content.Id, CancellationToken.None);

        var status = _statuses.First(s => s.Platform == PlatformType.TwitterX);
        Assert.Equal(PlatformPublishStatus.RateLimited, status.Status);
        Assert.Equal(retryAt, status.NextRetryAt);
    }

    [Fact]
    public async Task PublishAsync_PublishesIndependently_FailureDoesNotBlockOthers()
    {
        var content = CreateContent(PlatformType.TwitterX, PlatformType.LinkedIn);
        SetupContent(content);

        var twitterContent = new PlatformContent("Tweet", null, ContentType.SocialPost, [], new Dictionary<string, string>());
        var linkedInContent = new PlatformContent("Post", null, ContentType.BlogPost, [], new Dictionary<string, string>());
        SetupFormatters(PlatformType.TwitterX, content, Result.Success(twitterContent));
        SetupFormatters(PlatformType.LinkedIn, content, Result.Success(linkedInContent));

        _twitterAdapter.Setup(a => a.PublishAsync(twitterContent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PublishResult>(ErrorCode.InternalError, "API error"));
        _linkedInAdapter.Setup(a => a.PublishAsync(linkedInContent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PublishResult("li-1", "https://linkedin.com/feed/update/li-1", DateTimeOffset.UtcNow)));

        var result = await _sut.PublishAsync(content.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _linkedInAdapter.Verify(a => a.PublishAsync(linkedInContent, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_RecordsPlatformPostIdAndUrl()
    {
        var content = CreateContent(PlatformType.TwitterX);
        SetupContent(content);

        var formatted = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
        SetupFormatters(PlatformType.TwitterX, content, Result.Success(formatted));

        var publishedAt = DateTimeOffset.UtcNow;
        _twitterAdapter.Setup(a => a.PublishAsync(formatted, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PublishResult("tweet-999", "https://x.com/i/status/tweet-999", publishedAt)));

        await _sut.PublishAsync(content.Id, CancellationToken.None);

        var status = _statuses.First(s => s.Platform == PlatformType.TwitterX);
        Assert.Equal(PlatformPublishStatus.Published, status.Status);
        Assert.Equal("tweet-999", status.PlatformPostId);
        Assert.Equal("https://x.com/i/status/tweet-999", status.PostUrl);
    }

    [Fact]
    public async Task PublishAsync_AllSucceed_TransitionsToPublished()
    {
        var content = CreateContent(PlatformType.TwitterX);
        SetupContent(content);

        var formatted = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
        SetupFormatters(PlatformType.TwitterX, content, Result.Success(formatted));
        _twitterAdapter.Setup(a => a.PublishAsync(formatted, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PublishResult("t-1", "https://x.com/i/status/t-1", DateTimeOffset.UtcNow)));

        await _sut.PublishAsync(content.Id, CancellationToken.None);

        Assert.Equal(ContentStatus.Published, content.Status);
    }

    [Fact]
    public async Task PublishAsync_AllFail_TransitionsToFailed()
    {
        var content = CreateContent(PlatformType.TwitterX);
        SetupContent(content);

        var formatted = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
        SetupFormatters(PlatformType.TwitterX, content, Result.Success(formatted));
        _twitterAdapter.Setup(a => a.PublishAsync(formatted, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PublishResult>(ErrorCode.InternalError, "API down"));

        var result = await _sut.PublishAsync(content.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ContentStatus.Failed, content.Status);
    }

    [Fact]
    public async Task PublishAsync_PartialFailure_NotifiesUser()
    {
        var content = CreateContent(PlatformType.TwitterX, PlatformType.LinkedIn);
        SetupContent(content);

        var twitterContent = new PlatformContent("Tweet", null, ContentType.SocialPost, [], new Dictionary<string, string>());
        var linkedInContent = new PlatformContent("Post", null, ContentType.BlogPost, [], new Dictionary<string, string>());
        SetupFormatters(PlatformType.TwitterX, content, Result.Success(twitterContent));
        SetupFormatters(PlatformType.LinkedIn, content, Result.Success(linkedInContent));

        _twitterAdapter.Setup(a => a.PublishAsync(twitterContent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new PublishResult("t-1", "https://x.com/i/status/t-1", DateTimeOffset.UtcNow)));
        _linkedInAdapter.Setup(a => a.PublishAsync(linkedInContent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<PublishResult>(ErrorCode.InternalError, "LinkedIn error"));

        await _sut.PublishAsync(content.Id, CancellationToken.None);

        _notification.Verify(n => n.SendAsync(
            NotificationType.ContentFailed,
            It.Is<string>(s => s.Contains("Partial")),
            It.IsAny<string>(),
            content.Id,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_ConcurrencyException_SkipsPlatform()
    {
        var content = CreateContent(PlatformType.TwitterX);
        SetupContent(content);

        var formatted = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
        SetupFormatters(PlatformType.TwitterX, content, Result.Success(formatted));

        var callCount = 0;
        _db.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(ct =>
            {
                callCount++;
                if (callCount == 1)
                    throw new DbUpdateConcurrencyException("Concurrent update");
                return Task.FromResult(0);
            });

        var result = await _sut.PublishAsync(content.Id, CancellationToken.None);

        // Should not crash — concurrency exception counted as success (another instance handling it)
        Assert.True(result.IsSuccess);
        _twitterAdapter.Verify(a => a.PublishAsync(It.IsAny<PlatformContent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
