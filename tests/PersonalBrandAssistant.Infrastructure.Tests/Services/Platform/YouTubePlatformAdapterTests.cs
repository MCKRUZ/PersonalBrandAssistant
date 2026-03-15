using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;
using PersonalBrandAssistant.Infrastructure.Tests.Helpers;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;

public class YouTubePlatformAdapterTests
{
    private readonly Mock<IApplicationDbContext> _dbContext = new();
    private readonly Mock<IEncryptionService> _encryption = new();
    private readonly Mock<IRateLimiter> _rateLimiter = new();
    private readonly Mock<IOAuthManager> _oauthManager = new();
    private readonly Mock<IMediaStorage> _mediaStorage = new();
    private readonly Mock<HttpMessageHandler> _httpHandler = new();
    private readonly YouTubePlatformAdapter _sut;

    public YouTubePlatformAdapterTests()
    {
        var httpClient = new HttpClient(_httpHandler.Object)
        {
            BaseAddress = new Uri("https://www.googleapis.com"),
        };

        _encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>())).Returns("yt-token");

        _rateLimiter.Setup(r => r.CanMakeRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RateLimitDecision(true, null, null)));
        _rateLimiter.Setup(r => r.RecordRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        _sut = new YouTubePlatformAdapter(
            httpClient, _dbContext.Object, _encryption.Object, _rateLimiter.Object,
            _oauthManager.Object, _mediaStorage.Object,
            NullLogger<YouTubePlatformAdapter>.Instance);
    }

    [Fact]
    public void Type_IsYouTube() =>
        Assert.Equal(PlatformType.YouTube, _sut.Type);

    [Fact]
    public async Task ValidateContentAsync_NoTitle_ReturnsInvalid()
    {
        var content = new PlatformContent("Description", null, ContentType.VideoDescription, [], new Dictionary<string, string>());
        var result = await _sut.ValidateContentAsync(content, CancellationToken.None);

        Assert.False(result.Value!.IsValid);
        Assert.Contains("YouTube video requires a title", result.Value.Errors);
    }

    [Fact]
    public async Task ValidateContentAsync_NoMedia_ReturnsInvalid()
    {
        var content = new PlatformContent("Description", "Title", ContentType.VideoDescription, [], new Dictionary<string, string>());
        var result = await _sut.ValidateContentAsync(content, CancellationToken.None);

        Assert.False(result.Value!.IsValid);
        Assert.Contains("YouTube video requires a video file", result.Value.Errors);
    }

    [Fact]
    public async Task GetEngagementAsync_ReturnsStats()
    {
        var platform = new Domain.Entities.Platform
        {
            Type = PlatformType.YouTube,
            DisplayName = "YouTube",
            IsConnected = true,
            EncryptedAccessToken = [1, 2, 3],
        };
        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(new[] { platform });
        _dbContext.Setup(db => db.Platforms).Returns(mockSet.Object);

        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    items = new[]
                    {
                        new
                        {
                            statistics = new
                            {
                                viewCount = "5000",
                                likeCount = "200",
                                commentCount = "50",
                                favoriteCount = "0",
                            },
                        },
                    },
                })),
            });

        var result = await _sut.GetEngagementAsync("dQw4w9WgXcQ", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.Value!.Likes);
        Assert.Equal(50, result.Value.Comments);
        Assert.Equal(5000, result.Value.Impressions);
    }

    [Fact]
    public async Task GetEngagementAsync_VideoNotFound_ReturnsNotFound()
    {
        var platform = new Domain.Entities.Platform
        {
            Type = PlatformType.YouTube,
            DisplayName = "YouTube",
            IsConnected = true,
            EncryptedAccessToken = [1, 2, 3],
        };
        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(new[] { platform });
        _dbContext.Setup(db => db.Platforms).Returns(mockSet.Object);

        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { items = Array.Empty<object>() })),
            });

        var result = await _sut.GetEngagementAsync("xxxxxxxxxxx", CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
}
