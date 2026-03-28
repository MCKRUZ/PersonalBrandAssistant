using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;
using PersonalBrandAssistant.Infrastructure.Tests.Helpers;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;

public class TwitterPlatformAdapterTests
{
    private readonly Mock<IApplicationDbContext> _dbContext = new();
    private readonly Mock<IEncryptionService> _encryption = new();
    private readonly Mock<IRateLimiter> _rateLimiter = new();
    private readonly Mock<IOAuthManager> _oauthManager = new();
    private readonly Mock<IMediaStorage> _mediaStorage = new();
    private readonly Mock<HttpMessageHandler> _httpHandler = new();
    private readonly TwitterPlatformAdapter _sut;

    public TwitterPlatformAdapterTests()
    {
        var httpClient = new HttpClient(_httpHandler.Object)
        {
            BaseAddress = new Uri("https://api.x.com/2"),
        };

        _encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>())).Returns("test-access-token");

        _rateLimiter.Setup(r => r.CanMakeRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RateLimitDecision(true, null, null)));

        _rateLimiter.Setup(r => r.RecordRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        _sut = new TwitterPlatformAdapter(
            httpClient, _dbContext.Object, _encryption.Object, _rateLimiter.Object,
            _oauthManager.Object, _mediaStorage.Object, NullLogger<TwitterPlatformAdapter>.Instance);
    }

    private void SetupConnectedPlatform()
    {
        var platform = new Domain.Entities.Platform
        {
            Type = PlatformType.TwitterX,
            DisplayName = "Twitter",
            IsConnected = true,
            EncryptedAccessToken = [1, 2, 3],
        };
        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(new[] { platform });
        _dbContext.Setup(db => db.Platforms).Returns(mockSet.Object);
    }

    private void SetupHttpResponse(HttpStatusCode status, object body, Dictionary<string, string>? headers = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body)),
        };

        if (headers != null)
        {
            foreach (var (key, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(key, value);
            }
        }

        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    [Fact]
    public void Type_IsTwitterX() =>
        Assert.Equal(PlatformType.TwitterX, _sut.Type);

    [Fact]
    public async Task PublishAsync_DecryptsTokenBeforeApiCall()
    {
        SetupConnectedPlatform();
        SetupHttpResponse(HttpStatusCode.OK, new { data = new { id = "123" } });

        var content = new PlatformContent("Hello world", null, ContentType.SocialPost, [], new Dictionary<string, string>());
        await _sut.PublishAsync(content, CancellationToken.None);

        _encryption.Verify(e => e.Decrypt(It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_ChecksRateLimitBeforeRequest()
    {
        SetupConnectedPlatform();
        SetupHttpResponse(HttpStatusCode.OK, new { data = new { id = "123" } });

        var content = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
        await _sut.PublishAsync(content, CancellationToken.None);

        _rateLimiter.Verify(r => r.CanMakeRequestAsync(PlatformType.TwitterX, "publish", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_ReturnsFailure_WhenRateLimited()
    {
        SetupConnectedPlatform();
        _rateLimiter.Setup(r => r.CanMakeRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RateLimitDecision(false, DateTimeOffset.UtcNow.AddMinutes(5), "Too many requests")));

        var content = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
        var result = await _sut.PublishAsync(content, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task PublishAsync_PostsTweetAndReturnsResult()
    {
        SetupConnectedPlatform();
        SetupHttpResponse(HttpStatusCode.OK, new { data = new { id = "tweet-456" } });

        var content = new PlatformContent("Test tweet", null, ContentType.SocialPost, [], new Dictionary<string, string>());
        var result = await _sut.PublishAsync(content, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("tweet-456", result.Value!.PlatformPostId);
        Assert.Contains("tweet-456", result.Value.PostUrl);
    }

    [Fact]
    public async Task PublishAsync_RecordsRateLimitFromHeaders()
    {
        SetupConnectedPlatform();
        SetupHttpResponse(HttpStatusCode.OK, new { data = new { id = "123" } },
            new Dictionary<string, string>
            {
                ["x-rate-limit-remaining"] = "42",
                ["x-rate-limit-reset"] = "1700000000",
            });

        var content = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
        await _sut.PublishAsync(content, CancellationToken.None);

        _rateLimiter.Verify(r => r.RecordRequestAsync(
            PlatformType.TwitterX, "publish", 42, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_ReturnsFailure_WhenNotConnected()
    {
        var platform = new Domain.Entities.Platform
        {
            Type = PlatformType.TwitterX,
            DisplayName = "Twitter",
            IsConnected = false,
        };
        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(new[] { platform });
        _dbContext.Setup(db => db.Platforms).Returns(mockSet.Object);

        var content = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
        var result = await _sut.PublishAsync(content, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task PublishAsync_RetriesOn401AfterTokenRefresh()
    {
        SetupConnectedPlatform();

        var callCount = 0;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { data = new { id = "refreshed-tweet" } })),
                };
            });

        _oauthManager.Setup(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new OAuthTokens("new-token", null, DateTimeOffset.UtcNow.AddHours(1), null)));

        var content = new PlatformContent("Hello", null, ContentType.SocialPost, [], new Dictionary<string, string>());
        var result = await _sut.PublishAsync(content, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("refreshed-tweet", result.Value!.PlatformPostId);
        _oauthManager.Verify(o => o.RefreshTokenAsync(PlatformType.TwitterX, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEngagementAsync_ReturnsMetrics()
    {
        SetupConnectedPlatform();
        SetupHttpResponse(HttpStatusCode.OK, new
        {
            data = new
            {
                public_metrics = new
                {
                    like_count = 10,
                    reply_count = 5,
                    retweet_count = 3,
                    impression_count = 1000,
                    quote_count = 2,
                    bookmark_count = 1,
                },
            },
        });

        var result = await _sut.GetEngagementAsync("1234567890123456789", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value!.Likes);
        Assert.Equal(5, result.Value.Comments);
        Assert.Equal(3, result.Value.Shares);
    }

    [Fact]
    public async Task GetEngagementAsync_ReturnsFailureOn403()
    {
        SetupConnectedPlatform();
        SetupHttpResponse(HttpStatusCode.Forbidden, new { });

        var result = await _sut.GetEngagementAsync("1234567890123456789", CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ValidateContentAsync_EmptyText_ReturnsInvalid()
    {
        var content = new PlatformContent("", null, ContentType.SocialPost, [], new Dictionary<string, string>());
        var result = await _sut.ValidateContentAsync(content, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsValid);
        Assert.Contains("Tweet text cannot be empty", result.Value.Errors);
    }
}
