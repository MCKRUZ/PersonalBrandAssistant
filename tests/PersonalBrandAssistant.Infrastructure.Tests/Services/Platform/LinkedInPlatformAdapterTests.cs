using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.PlatformServices;
using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;
using PersonalBrandAssistant.Infrastructure.Tests.Helpers;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;

public class LinkedInPlatformAdapterTests
{
    private readonly Mock<IApplicationDbContext> _dbContext = new();
    private readonly Mock<IEncryptionService> _encryption = new();
    private readonly Mock<IRateLimiter> _rateLimiter = new();
    private readonly Mock<IOAuthManager> _oauthManager = new();
    private readonly Mock<IMediaStorage> _mediaStorage = new();
    private readonly Mock<HttpMessageHandler> _httpHandler = new();
    private readonly LinkedInPlatformAdapter _sut;

    public LinkedInPlatformAdapterTests()
    {
        var httpClient = new HttpClient(_httpHandler.Object)
        {
            BaseAddress = new Uri("https://api.linkedin.com/rest"),
        };

        _encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>())).Returns("linkedin-token");

        _rateLimiter.Setup(r => r.CanMakeRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new RateLimitDecision(true, null, null)));
        _rateLimiter.Setup(r => r.RecordRequestAsync(It.IsAny<PlatformType>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var options = Options.Create(new PlatformIntegrationOptions
        {
            LinkedIn = new PlatformOptions { ApiVersion = "202401" },
        });

        _sut = new LinkedInPlatformAdapter(
            httpClient, _dbContext.Object, _encryption.Object, _rateLimiter.Object,
            _oauthManager.Object, _mediaStorage.Object, options,
            NullLogger<LinkedInPlatformAdapter>.Instance);
    }

    private void SetupConnectedPlatform()
    {
        var platform = new Domain.Entities.Platform
        {
            Type = PlatformType.LinkedIn,
            DisplayName = "LinkedIn",
            IsConnected = true,
            EncryptedAccessToken = [1, 2, 3],
        };
        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(new[] { platform });
        _dbContext.Setup(db => db.Platforms).Returns(mockSet.Object);
    }

    [Fact]
    public void Type_IsLinkedIn() =>
        Assert.Equal(PlatformType.LinkedIn, _sut.Type);

    [Fact]
    public async Task GetProfileAsync_ReturnsProfile()
    {
        SetupConnectedPlatform();

        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    sub = "user-123",
                    name = "Test User",
                    picture = "https://example.com/pic.jpg",
                })),
            });

        var result = await _sut.GetProfileAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("user-123", result.Value!.PlatformUserId);
        Assert.Equal("Test User", result.Value.DisplayName);
    }

    [Fact]
    public async Task ValidateContentAsync_EmptyText_ReturnsInvalid()
    {
        var content = new PlatformContent("", null, ContentType.BlogPost, [], new Dictionary<string, string>());
        var result = await _sut.ValidateContentAsync(content, CancellationToken.None);

        Assert.False(result.Value!.IsValid);
    }

    [Fact]
    public async Task ValidateContentAsync_TooLong_ReturnsInvalid()
    {
        var content = new PlatformContent(new string('A', 3500), null, ContentType.BlogPost, [], new Dictionary<string, string>());
        var result = await _sut.ValidateContentAsync(content, CancellationToken.None);

        Assert.False(result.Value!.IsValid);
    }
}
