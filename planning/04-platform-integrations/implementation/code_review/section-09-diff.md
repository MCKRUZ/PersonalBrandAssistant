diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/PublishingPipeline.cs b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/PublishingPipeline.cs
new file mode 100644
index 0000000..cf76673
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/PublishingPipeline.cs
@@ -0,0 +1,219 @@
+using System.Security.Cryptography;
+using System.Text;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices;
+
+public sealed class PublishingPipeline : IPublishingPipeline
+{
+    private readonly IApplicationDbContext _db;
+    private readonly Dictionary<PlatformType, ISocialPlatform> _adapters;
+    private readonly Dictionary<PlatformType, IPlatformContentFormatter> _formatters;
+    private readonly IRateLimiter _rateLimiter;
+    private readonly IMediaStorage _mediaStorage;
+    private readonly INotificationService _notificationService;
+    private readonly ILogger<PublishingPipeline> _logger;
+
+    public PublishingPipeline(
+        IApplicationDbContext db,
+        IEnumerable<ISocialPlatform> adapters,
+        IEnumerable<IPlatformContentFormatter> formatters,
+        IRateLimiter rateLimiter,
+        IMediaStorage mediaStorage,
+        INotificationService notificationService,
+        ILogger<PublishingPipeline> logger)
+    {
+        _db = db;
+        _adapters = adapters.ToDictionary(a => a.Type);
+        _formatters = formatters.ToDictionary(f => f.Platform);
+        _rateLimiter = rateLimiter;
+        _mediaStorage = mediaStorage;
+        _notificationService = notificationService;
+        _logger = logger;
+    }
+
+    public async Task<Result<MediatR.Unit>> PublishAsync(Guid contentId, CancellationToken ct = default)
+    {
+        var content = await _db.Contents
+            .FirstOrDefaultAsync(c => c.Id == contentId, ct);
+
+        if (content is null)
+            return Result.NotFound<MediatR.Unit>($"Content '{contentId}' not found");
+
+        var existingStatuses = await _db.ContentPlatformStatuses
+            .Where(s => s.ContentId == contentId)
+            .ToListAsync(ct);
+
+        var succeeded = 0;
+        var failed = 0;
+        var failedPlatforms = new List<PlatformType>();
+
+        foreach (var platform in content.TargetPlatforms)
+        {
+            var existingStatus = existingStatuses.FirstOrDefault(s => s.Platform == platform);
+
+            // Idempotency: skip already Published or Processing
+            if (existingStatus is { Status: PlatformPublishStatus.Published or PlatformPublishStatus.Processing })
+            {
+                succeeded++;
+                continue;
+            }
+
+            try
+            {
+                var platformResult = await PublishToPlatformAsync(content, platform, existingStatus, ct);
+                if (platformResult)
+                    succeeded++;
+                else
+                {
+                    failed++;
+                    failedPlatforms.Add(platform);
+                }
+            }
+            catch (DbUpdateConcurrencyException)
+            {
+                _logger.LogInformation("Concurrency conflict for {Platform} on content {ContentId}, skipping",
+                    platform, contentId);
+            }
+            catch (Exception ex)
+            {
+                _logger.LogError(ex, "Unexpected error publishing to {Platform} for content {ContentId}",
+                    platform, contentId);
+                failed++;
+                failedPlatforms.Add(platform);
+            }
+        }
+
+        // Determine overall status
+        await TransitionContentStatusAsync(content, succeeded, failed, ct);
+
+        // Notify on partial failure
+        if (succeeded > 0 && failed > 0)
+        {
+            var platformNames = string.Join(", ", failedPlatforms);
+            await _notificationService.SendAsync(
+                NotificationType.ContentFailed,
+                "Partial publish failure",
+                $"Failed to publish to: {platformNames}",
+                contentId, ct);
+        }
+
+        return succeeded > 0
+            ? Result.Success(MediatR.Unit.Value)
+            : Result.Failure<MediatR.Unit>(ErrorCode.InternalError, "All platforms failed to publish");
+    }
+
+    private async Task<bool> PublishToPlatformAsync(
+        Content content, PlatformType platform,
+        ContentPlatformStatus? existingStatus, CancellationToken ct)
+    {
+        // Create or reuse status record
+        ContentPlatformStatus status;
+        if (existingStatus is null)
+        {
+            status = new ContentPlatformStatus
+            {
+                ContentId = content.Id,
+                Platform = platform,
+                IdempotencyKey = ComputeIdempotencyKey(content.Id, platform, content.Version),
+                Status = PlatformPublishStatus.Pending,
+            };
+            _db.ContentPlatformStatuses.Add(status);
+        }
+        else
+        {
+            status = existingStatus;
+            status.Status = PlatformPublishStatus.Pending;
+        }
+
+        await _db.SaveChangesAsync(ct); // Acquire lease
+
+        // Format content
+        if (!_formatters.TryGetValue(platform, out var formatter))
+        {
+            status.Status = PlatformPublishStatus.Skipped;
+            status.ErrorMessage = $"No formatter registered for {platform}";
+            await _db.SaveChangesAsync(ct);
+            return false;
+        }
+
+        var formatResult = formatter.FormatAndValidate(content);
+        if (!formatResult.IsSuccess)
+        {
+            status.Status = PlatformPublishStatus.Skipped;
+            status.ErrorMessage = string.Join("; ", formatResult.Errors);
+            await _db.SaveChangesAsync(ct);
+            return false;
+        }
+
+        // Check rate limit
+        var rateLimitResult = await _rateLimiter.CanMakeRequestAsync(platform, "publish", ct);
+        if (rateLimitResult.IsSuccess && !rateLimitResult.Value!.Allowed)
+        {
+            status.Status = PlatformPublishStatus.RateLimited;
+            status.NextRetryAt = rateLimitResult.Value.RetryAt;
+            status.ErrorMessage = rateLimitResult.Value.Reason;
+            await _db.SaveChangesAsync(ct);
+            return false;
+        }
+
+        // Publish
+        if (!_adapters.TryGetValue(platform, out var adapter))
+        {
+            status.Status = PlatformPublishStatus.Failed;
+            status.ErrorMessage = $"No adapter registered for {platform}";
+            await _db.SaveChangesAsync(ct);
+            return false;
+        }
+
+        var publishResult = await adapter.PublishAsync(formatResult.Value!, ct);
+
+        if (publishResult.IsSuccess)
+        {
+            status.PlatformPostId = publishResult.Value!.PlatformPostId;
+            status.PostUrl = publishResult.Value.PostUrl;
+            status.PublishedAt = publishResult.Value.PublishedAt;
+            status.Status = PlatformPublishStatus.Published;
+            await _db.SaveChangesAsync(ct);
+            return true;
+        }
+
+        status.Status = PlatformPublishStatus.Failed;
+        status.ErrorMessage = string.Join("; ", publishResult.Errors);
+        status.RetryCount++;
+        await _db.SaveChangesAsync(ct);
+        return false;
+    }
+
+    private async Task TransitionContentStatusAsync(
+        Content content, int succeeded, int failed, CancellationToken ct)
+    {
+        try
+        {
+            if (failed == 0 && succeeded > 0)
+                content.TransitionTo(ContentStatus.Published);
+            else if (succeeded == 0 && failed > 0)
+                content.TransitionTo(ContentStatus.Failed);
+            // Mixed results: stay in Publishing state
+
+            await _db.SaveChangesAsync(ct);
+        }
+        catch (InvalidOperationException ex)
+        {
+            _logger.LogWarning(ex, "Could not transition content {ContentId} status", content.Id);
+        }
+    }
+
+    private static string ComputeIdempotencyKey(Guid contentId, PlatformType platform, uint version)
+    {
+        var input = $"{contentId}:{platform}:{version}";
+        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
+        return Convert.ToHexStringLower(hash);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/PublishingPipelineTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/PublishingPipelineTests.cs
new file mode 100644
index 0000000..5fb4cb1
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/PublishingPipelineTests.cs
@@ -0,0 +1,323 @@
+using System.Security.Cryptography;
+using System.Text;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging.Abstractions;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services.PlatformServices;
+using PersonalBrandAssistant.Infrastructure.Tests.Helpers;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;
+
+public class PublishingPipelineTests
+{
+    private readonly Mock<IApplicationDbContext> _db = new();
+    private readonly Mock<ISocialPlatform> _twitterAdapter = new();
+    private readonly Mock<ISocialPlatform> _linkedInAdapter = new();
+    private readonly Mock<IPlatformContentFormatter> _twitterFormatter = new();
+    private readonly Mock<IPlatformContentFormatter> _linkedInFormatter = new();
+    private readonly Mock<IRateLimiter> _rateLimiter = new();
+    private readonly Mock<IMediaStorage> _mediaStorage = new();
+    private readonly Mock<INotificationService> _notification = new();
+    private readonly PublishingPipeline _sut;
+
+    private readonly List<ContentPlatformStatus> _statuses = [];
+
+    public PublishingPipelineTests()
+    {
+        _twitterAdapter.Setup(a => a.Type).Returns(PlatformType.TwitterX);
+        _linkedInAdapter.Setup(a => a.Type).Returns(PlatformType.LinkedIn);
+        _twitterFormatter.Setup(f => f.Platform).Returns(PlatformType.TwitterX);
+        _linkedInFormatter.Setup(f => f.Platform).Returns(PlatformType.LinkedIn);
+
+        _rateLimiter.Setup(r => r.CanMakeRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new RateLimitDecision(true, null, null)));
+
+        SetupStatusDbSet([]);
+
+        _sut = new PublishingPipeline(
+            _db.Object,
+            new[] { _twitterAdapter.Object, _linkedInAdapter.Object },
+            new[] { _twitterFormatter.Object, _linkedInFormatter.Object },
+            _rateLimiter.Object,
+            _mediaStorage.Object,
+            _notification.Object,
+            NullLogger<PublishingPipeline>.Instance);
+    }
+
+    private Content CreateContent(params PlatformType[] targets)
+    {
+        var content = Content.Create(ContentType.SocialPost, "Hello world", "Title", targets);
+        content.TransitionTo(ContentStatus.Review);
+        content.TransitionTo(ContentStatus.Approved);
+        content.TransitionTo(ContentStatus.Scheduled);
+        content.TransitionTo(ContentStatus.Publishing);
+        return content;
+    }
+
+    private void SetupContent(Content content)
+    {
+        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(new[] { content });
+        _db.Setup(db => db.Contents).Returns(mockSet.Object);
+    }
+
+    private void SetupStatusDbSet(List<ContentPlatformStatus> statuses)
+    {
+        _statuses.Clear();
+        _statuses.AddRange(statuses);
+        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(_statuses.ToArray());
+        mockSet.Setup(s => s.Add(It.IsAny<ContentPlatformStatus>()))
+            .Callback<ContentPlatformStatus>(s => _statuses.Add(s));
+        _db.Setup(db => db.ContentPlatformStatuses).Returns(mockSet.Object);
+    }
+
+    private void SetupFormatters(PlatformType platform, Content content, Result<PlatformContent> result)
+    {
+        var formatter = platform == PlatformType.TwitterX ? _twitterFormatter : _linkedInFormatter;
+        formatter.Setup(f => f.FormatAndValidate(content)).Returns(result);
+    }
+
+    private static string ComputeExpectedKey(Guid contentId, PlatformType platform, uint version)
+    {
+        var input = $"{contentId}:{platform}:{version}";
+        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
+        return Convert.ToHexStringLower(hash);
+    }
+
+    [Fact]
+    public async Task PublishAsync_ContentNotFound_ReturnsNotFound()
+    {
+        var emptySet = AsyncQueryableHelpers.CreateAsyncDbSetMock(Array.Empty<Content>());
+        _db.Setup(db => db.Contents).Returns(emptySet.Object);
+
+        var result = await _sut.PublishAsync(Guid.NewGuid(), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task PublishAsync_SkipsAlreadyPublishedPlatform()
+    {
+        var content = CreateContent(PlatformType.TwitterX);
+        SetupContent(content);
+
+        var existing = new ContentPlatformStatus
+        {
+            ContentId = content.Id,
+            Platform = PlatformType.TwitterX,
+            Status = PlatformPublishStatus.Published,
+        };
+        SetupStatusDbSet([existing]);
+
+        var result = await _sut.PublishAsync(content.Id, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        _twitterAdapter.Verify(a => a.PublishAsync(It.IsAny<PlatformContent>(), It.IsAny<CancellationToken>()), Times.Never);
+    }
+
+    [Fact]
+    public async Task PublishAsync_SkipsProcessingPlatform()
+    {
+        var content = CreateContent(PlatformType.TwitterX);
+        SetupContent(content);
+
+        var existing = new ContentPlatformStatus
+        {
+            ContentId = content.Id,
+            Platform = PlatformType.TwitterX,
+            Status = PlatformPublishStatus.Processing,
+        };
+        SetupStatusDbSet([existing]);
+
+        var result = await _sut.PublishAsync(content.Id, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        _twitterAdapter.Verify(a => a.PublishAsync(It.IsAny<PlatformContent>(), It.IsAny<CancellationToken>()), Times.Never);
+    }
+
+    [Fact]
+    public async Task PublishAsync_SetsIdempotencyKey()
+    {
+        var content = CreateContent(PlatformType.TwitterX);
+        SetupContent(content);
+
+        var formatted = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
+        SetupFormatters(PlatformType.TwitterX, content, Result.Success(formatted));
+        _twitterAdapter.Setup(a => a.PublishAsync(formatted, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new PublishResult("t-1", "https://x.com/i/status/t-1", DateTimeOffset.UtcNow)));
+
+        await _sut.PublishAsync(content.Id, CancellationToken.None);
+
+        var status = _statuses.First(s => s.Platform == PlatformType.TwitterX);
+        var expectedKey = ComputeExpectedKey(content.Id, PlatformType.TwitterX, content.Version);
+        Assert.Equal(expectedKey, status.IdempotencyKey);
+    }
+
+    [Fact]
+    public async Task PublishAsync_FormatFailure_SetsSkipped()
+    {
+        var content = CreateContent(PlatformType.TwitterX);
+        SetupContent(content);
+
+        SetupFormatters(PlatformType.TwitterX, content,
+            Result.Failure<PlatformContent>(ErrorCode.ValidationFailed, "Too long"));
+
+        await _sut.PublishAsync(content.Id, CancellationToken.None);
+
+        var status = _statuses.First(s => s.Platform == PlatformType.TwitterX);
+        Assert.Equal(PlatformPublishStatus.Skipped, status.Status);
+        _twitterAdapter.Verify(a => a.PublishAsync(It.IsAny<PlatformContent>(), It.IsAny<CancellationToken>()), Times.Never);
+    }
+
+    [Fact]
+    public async Task PublishAsync_RateLimited_SetsRateLimitedWithRetryAt()
+    {
+        var content = CreateContent(PlatformType.TwitterX);
+        SetupContent(content);
+
+        var formatted = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
+        SetupFormatters(PlatformType.TwitterX, content, Result.Success(formatted));
+
+        var retryAt = DateTimeOffset.UtcNow.AddMinutes(10);
+        _rateLimiter.Setup(r => r.CanMakeRequestAsync(PlatformType.TwitterX, "publish", It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new RateLimitDecision(false, retryAt, "Too many requests")));
+
+        await _sut.PublishAsync(content.Id, CancellationToken.None);
+
+        var status = _statuses.First(s => s.Platform == PlatformType.TwitterX);
+        Assert.Equal(PlatformPublishStatus.RateLimited, status.Status);
+        Assert.Equal(retryAt, status.NextRetryAt);
+    }
+
+    [Fact]
+    public async Task PublishAsync_PublishesIndependently_FailureDoesNotBlockOthers()
+    {
+        var content = CreateContent(PlatformType.TwitterX, PlatformType.LinkedIn);
+        SetupContent(content);
+
+        var twitterContent = new PlatformContent("Tweet", null, ContentType.SocialPost, [], new Dictionary<string, string>());
+        var linkedInContent = new PlatformContent("Post", null, ContentType.BlogPost, [], new Dictionary<string, string>());
+        SetupFormatters(PlatformType.TwitterX, content, Result.Success(twitterContent));
+        SetupFormatters(PlatformType.LinkedIn, content, Result.Success(linkedInContent));
+
+        _twitterAdapter.Setup(a => a.PublishAsync(twitterContent, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Failure<PublishResult>(ErrorCode.InternalError, "API error"));
+        _linkedInAdapter.Setup(a => a.PublishAsync(linkedInContent, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new PublishResult("li-1", "https://linkedin.com/feed/update/li-1", DateTimeOffset.UtcNow)));
+
+        var result = await _sut.PublishAsync(content.Id, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        _linkedInAdapter.Verify(a => a.PublishAsync(linkedInContent, It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task PublishAsync_RecordsPlatformPostIdAndUrl()
+    {
+        var content = CreateContent(PlatformType.TwitterX);
+        SetupContent(content);
+
+        var formatted = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
+        SetupFormatters(PlatformType.TwitterX, content, Result.Success(formatted));
+
+        var publishedAt = DateTimeOffset.UtcNow;
+        _twitterAdapter.Setup(a => a.PublishAsync(formatted, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new PublishResult("tweet-999", "https://x.com/i/status/tweet-999", publishedAt)));
+
+        await _sut.PublishAsync(content.Id, CancellationToken.None);
+
+        var status = _statuses.First(s => s.Platform == PlatformType.TwitterX);
+        Assert.Equal(PlatformPublishStatus.Published, status.Status);
+        Assert.Equal("tweet-999", status.PlatformPostId);
+        Assert.Equal("https://x.com/i/status/tweet-999", status.PostUrl);
+    }
+
+    [Fact]
+    public async Task PublishAsync_AllSucceed_TransitionsToPublished()
+    {
+        var content = CreateContent(PlatformType.TwitterX);
+        SetupContent(content);
+
+        var formatted = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
+        SetupFormatters(PlatformType.TwitterX, content, Result.Success(formatted));
+        _twitterAdapter.Setup(a => a.PublishAsync(formatted, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new PublishResult("t-1", "https://x.com/i/status/t-1", DateTimeOffset.UtcNow)));
+
+        await _sut.PublishAsync(content.Id, CancellationToken.None);
+
+        Assert.Equal(ContentStatus.Published, content.Status);
+    }
+
+    [Fact]
+    public async Task PublishAsync_AllFail_TransitionsToFailed()
+    {
+        var content = CreateContent(PlatformType.TwitterX);
+        SetupContent(content);
+
+        var formatted = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
+        SetupFormatters(PlatformType.TwitterX, content, Result.Success(formatted));
+        _twitterAdapter.Setup(a => a.PublishAsync(formatted, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Failure<PublishResult>(ErrorCode.InternalError, "API down"));
+
+        var result = await _sut.PublishAsync(content.Id, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ContentStatus.Failed, content.Status);
+    }
+
+    [Fact]
+    public async Task PublishAsync_PartialFailure_NotifiesUser()
+    {
+        var content = CreateContent(PlatformType.TwitterX, PlatformType.LinkedIn);
+        SetupContent(content);
+
+        var twitterContent = new PlatformContent("Tweet", null, ContentType.SocialPost, [], new Dictionary<string, string>());
+        var linkedInContent = new PlatformContent("Post", null, ContentType.BlogPost, [], new Dictionary<string, string>());
+        SetupFormatters(PlatformType.TwitterX, content, Result.Success(twitterContent));
+        SetupFormatters(PlatformType.LinkedIn, content, Result.Success(linkedInContent));
+
+        _twitterAdapter.Setup(a => a.PublishAsync(twitterContent, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Success(new PublishResult("t-1", "https://x.com/i/status/t-1", DateTimeOffset.UtcNow)));
+        _linkedInAdapter.Setup(a => a.PublishAsync(linkedInContent, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result.Failure<PublishResult>(ErrorCode.InternalError, "LinkedIn error"));
+
+        await _sut.PublishAsync(content.Id, CancellationToken.None);
+
+        _notification.Verify(n => n.SendAsync(
+            NotificationType.ContentFailed,
+            It.Is<string>(s => s.Contains("Partial")),
+            It.IsAny<string>(),
+            content.Id,
+            It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task PublishAsync_ConcurrencyException_SkipsPlatform()
+    {
+        var content = CreateContent(PlatformType.TwitterX);
+        SetupContent(content);
+
+        var formatted = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
+        SetupFormatters(PlatformType.TwitterX, content, Result.Success(formatted));
+
+        var callCount = 0;
+        _db.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()))
+            .Returns<CancellationToken>(ct =>
+            {
+                callCount++;
+                if (callCount == 1)
+                    throw new DbUpdateConcurrencyException("Concurrent update");
+                return Task.FromResult(0);
+            });
+
+        var result = await _sut.PublishAsync(content.Id, CancellationToken.None);
+
+        // Should not crash — concurrency exception handled gracefully
+        _twitterAdapter.Verify(a => a.PublishAsync(It.IsAny<PlatformContent>(), It.IsAny<CancellationToken>()), Times.Never);
+    }
+}
