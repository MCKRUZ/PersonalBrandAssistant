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

public class InstagramPlatformAdapterTests
{
    private readonly Mock<IApplicationDbContext> _dbContext = new();
    private readonly Mock<IEncryptionService> _encryption = new();
    private readonly Mock<IRateLimiter> _rateLimiter = new();
    private readonly Mock<IOAuthManager> _oauthManager = new();
    private readonly Mock<IMediaStorage> _mediaStorage = new();
    private readonly Mock<HttpMessageHandler> _httpHandler = new();
    private readonly InstagramPlatformAdapter _sut;

    public InstagramPlatformAdapterTests()
    {
        var httpClient = new HttpClient(_httpHandler.Object)
        {
            BaseAddress = new Uri("https://graph.facebook.com/v21.0"),
        };

        _encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>())).Returns("ig-token");

        _rateLimiter.Setup(r => r.CanMakeRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RateLimitDecision(true, null, null)));
        _rateLimiter.Setup(r => r.RecordRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        _sut = new InstagramPlatformAdapter(
            httpClient, _dbContext.Object, _encryption.Object, _rateLimiter.Object,
            _oauthManager.Object, _mediaStorage.Object,
            NullLogger<InstagramPlatformAdapter>.Instance);
    }

    [Fact]
    public void Type_IsInstagram() =>
        Assert.Equal(PlatformType.Instagram, _sut.Type);

    [Fact]
    public async Task ValidateContentAsync_NoMedia_ReturnsInvalid()
    {
        var content = new PlatformContent("Caption", null, ContentType.SocialPost, [], new Dictionary<string, string>());
        var result = await _sut.ValidateContentAsync(content, CancellationToken.None);

        Assert.False(result.Value!.IsValid);
        Assert.Contains("Instagram requires at least one media attachment", result.Value.Errors);
    }

    [Fact]
    public async Task ValidateContentAsync_WithMedia_ReturnsValid()
    {
        var media = new List<MediaFile> { new("file1", "image/jpeg", null) };
        var content = new PlatformContent("Caption", null, ContentType.SocialPost, media, new Dictionary<string, string>());
        var result = await _sut.ValidateContentAsync(content, CancellationToken.None);

        Assert.True(result.Value!.IsValid);
    }

    [Fact]
    public async Task PublishAsync_UsesSignedUrlForMedia()
    {
        var platform = new Domain.Entities.Platform
        {
            Type = PlatformType.Instagram,
            DisplayName = "Instagram",
            IsConnected = true,
            EncryptedAccessToken = [1, 2, 3],
        };
        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(new[] { platform });
        _dbContext.Setup(db => db.Platforms).Returns(mockSet.Object);

        _mediaStorage.Setup(m => m.GetSignedUrlAsync("file1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.example.com/signed/file1");

        var callCount = 0;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                // First call: get user ID, second: create container, third: publish
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { id = $"ig-{callCount}" })),
                };
            });

        var media = new List<MediaFile> { new("file1", "image/jpeg", null) };
        var content = new PlatformContent("Caption", null, ContentType.SocialPost, media, new Dictionary<string, string>());
        var result = await _sut.PublishAsync(content, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _mediaStorage.Verify(m => m.GetSignedUrlAsync("file1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
