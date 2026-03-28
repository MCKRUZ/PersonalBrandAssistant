using System.Net;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.PlatformServices;
using PersonalBrandAssistant.Infrastructure.Tests.Helpers;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;

public class OAuthManagerTests
{
    private readonly Mock<IApplicationDbContext> _dbContext;
    private readonly Mock<IEncryptionService> _encryption;
    private readonly Mock<HttpMessageHandler> _httpHandler;
    private readonly HttpClient _httpClient;
    private readonly OAuthManager _sut;

    private readonly PlatformIntegrationOptions _options = new()
    {
        Twitter = new PlatformOptions { CallbackUrl = "https://app.test/callback/twitter" },
        LinkedIn = new PlatformOptions { CallbackUrl = "https://app.test/callback/linkedin" },
        Instagram = new PlatformOptions { CallbackUrl = "https://app.test/callback/instagram" },
        YouTube = new PlatformOptions { CallbackUrl = "https://app.test/callback/youtube" },
    };

    public OAuthManagerTests()
    {
        _dbContext = new Mock<IApplicationDbContext>();
        _encryption = new Mock<IEncryptionService>();
        _httpHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpHandler.Object);

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformIntegrations:Twitter:ClientId"] = "twitter-client-id",
                ["PlatformIntegrations:Twitter:ClientSecret"] = "twitter-client-secret",
                ["PlatformIntegrations:LinkedIn:ClientId"] = "linkedin-client-id",
                ["PlatformIntegrations:LinkedIn:ClientSecret"] = "linkedin-client-secret",
                ["PlatformIntegrations:Instagram:AppId"] = "instagram-app-id",
                ["PlatformIntegrations:Instagram:AppSecret"] = "instagram-app-secret",
                ["PlatformIntegrations:YouTube:ClientId"] = "youtube-client-id",
                ["PlatformIntegrations:YouTube:ClientSecret"] = "youtube-client-secret",
            })
            .Build();

        _encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => System.Text.Encoding.UTF8.GetBytes(s));
        _encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>())).Returns<byte[]>(b => System.Text.Encoding.UTF8.GetString(b));

        _sut = new OAuthManager(
            _dbContext.Object,
            _encryption.Object,
            Options.Create(_options),
            httpFactory.Object,
            config,
            NullLogger<OAuthManager>.Instance);
    }

    private void SetupOAuthStates(params OAuthState[] states)
    {
        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(states);
        _dbContext.Setup(db => db.OAuthStates).Returns(mockSet.Object);
    }

    private void SetupPlatforms(params Domain.Entities.Platform[] platforms)
    {
        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(platforms);
        _dbContext.Setup(db => db.Platforms).Returns(mockSet.Object);
    }

    private void SetupHttpResponse(HttpStatusCode status, object body)
    {
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent(JsonSerializer.Serialize(body)),
            });
    }

    // --- GenerateAuthUrlAsync ---

    [Fact]
    public async Task GenerateAuthUrlAsync_CreatesOAuthStateInDb()
    {
        SetupOAuthStates();
        OAuthState? capturedState = null;
        _dbContext.Setup(db => db.OAuthStates.Add(It.IsAny<OAuthState>()))
            .Callback<OAuthState>(s => capturedState = s);

        var result = await _sut.GenerateAuthUrlAsync(PlatformType.TwitterX, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedState);
        Assert.True(capturedState!.ExpiresAt > DateTimeOffset.UtcNow);
        _dbContext.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateAuthUrlAsync_ReturnsDifferentStateValues()
    {
        SetupOAuthStates();
        _dbContext.Setup(db => db.OAuthStates.Add(It.IsAny<OAuthState>()));

        var result1 = await _sut.GenerateAuthUrlAsync(PlatformType.TwitterX, CancellationToken.None);
        var result2 = await _sut.GenerateAuthUrlAsync(PlatformType.TwitterX, CancellationToken.None);

        Assert.NotEqual(result1.Value!.State, result2.Value!.State);
    }

    [Fact]
    public async Task GenerateAuthUrlAsync_IncludesPkceForTwitter()
    {
        SetupOAuthStates();
        OAuthState? capturedState = null;
        _dbContext.Setup(db => db.OAuthStates.Add(It.IsAny<OAuthState>()))
            .Callback<OAuthState>(s => capturedState = s);

        var result = await _sut.GenerateAuthUrlAsync(PlatformType.TwitterX, CancellationToken.None);

        Assert.NotNull(capturedState?.EncryptedCodeVerifier);
        _encryption.Verify(e => e.Encrypt(It.IsAny<string>()), Times.Once);
        Assert.Contains("code_challenge=", result.Value!.Url);
        Assert.Contains("code_challenge_method=S256", result.Value.Url);
    }

    [Theory]
    [InlineData(PlatformType.TwitterX, "twitter.com/i/oauth2/authorize")]
    [InlineData(PlatformType.LinkedIn, "linkedin.com/oauth/v2/authorization")]
    [InlineData(PlatformType.Instagram, "facebook.com/v19.0/dialog/oauth")]
    [InlineData(PlatformType.YouTube, "accounts.google.com/o/oauth2/v2/auth")]
    public async Task GenerateAuthUrlAsync_ReturnsPlatformSpecificUrl(PlatformType platform, string expectedUrlFragment)
    {
        SetupOAuthStates();
        _dbContext.Setup(db => db.OAuthStates.Add(It.IsAny<OAuthState>()));

        var result = await _sut.GenerateAuthUrlAsync(platform, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(expectedUrlFragment, result.Value!.Url);
    }

    [Fact]
    public async Task GenerateAuthUrlAsync_YouTubeIncludesOfflineAccess()
    {
        SetupOAuthStates();
        _dbContext.Setup(db => db.OAuthStates.Add(It.IsAny<OAuthState>()));

        var result = await _sut.GenerateAuthUrlAsync(PlatformType.YouTube, CancellationToken.None);

        Assert.Contains("access_type=offline", result.Value!.Url);
    }

    // --- ExchangeCodeAsync ---

    [Fact]
    public async Task ExchangeCodeAsync_RejectsInvalidState()
    {
        SetupOAuthStates(); // empty - no matching state
        SetupPlatforms();

        var result = await _sut.ExchangeCodeAsync(PlatformType.TwitterX, "code123", "bad-state", null, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ExchangeCodeAsync_RejectsExpiredState()
    {
        var state = new OAuthState
        {
            State = "valid-state",
            Platform = PlatformType.TwitterX,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-15),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        };
        SetupOAuthStates(state);
        SetupPlatforms();

        var result = await _sut.ExchangeCodeAsync(PlatformType.TwitterX, "code123", "valid-state", null, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ExchangeCodeAsync_StoresEncryptedTokens()
    {
        var state = new OAuthState
        {
            State = "valid-state",
            Platform = PlatformType.LinkedIn,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
        };
        SetupOAuthStates(state);

        var platform = new Domain.Entities.Platform
        {
            Type = PlatformType.LinkedIn,
            DisplayName = "LinkedIn",
        };
        SetupPlatforms(platform);

        SetupHttpResponse(HttpStatusCode.OK, new
        {
            access_token = "access-token-123",
            refresh_token = "refresh-token-456",
            expires_in = 3600,
            scope = "w_member_social r_liteprofile",
        });

        var result = await _sut.ExchangeCodeAsync(PlatformType.LinkedIn, "auth-code", "valid-state", null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(platform.EncryptedAccessToken);
        Assert.True(platform.IsConnected);
        _encryption.Verify(e => e.Encrypt("access-token-123"), Times.Once);
    }

    [Fact]
    public async Task ExchangeCodeAsync_StoresGrantedScopes()
    {
        var state = new OAuthState
        {
            State = "valid-state",
            Platform = PlatformType.LinkedIn,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
        };
        SetupOAuthStates(state);

        var platform = new Domain.Entities.Platform
        {
            Type = PlatformType.LinkedIn,
            DisplayName = "LinkedIn",
        };
        SetupPlatforms(platform);

        SetupHttpResponse(HttpStatusCode.OK, new
        {
            access_token = "token",
            scope = "w_member_social r_liteprofile",
            expires_in = 3600,
        });

        await _sut.ExchangeCodeAsync(PlatformType.LinkedIn, "code", "valid-state", null, CancellationToken.None);

        Assert.NotNull(platform.GrantedScopes);
        Assert.Contains("w_member_social", platform.GrantedScopes!);
    }

    // --- RefreshTokenAsync ---

    [Fact]
    public async Task RefreshTokenAsync_UpdatesTokens()
    {
        var platform = new Domain.Entities.Platform
        {
            Type = PlatformType.LinkedIn,
            DisplayName = "LinkedIn",
            IsConnected = true,
            EncryptedRefreshToken = System.Text.Encoding.UTF8.GetBytes("old-refresh"),
        };
        SetupPlatforms(platform);

        SetupHttpResponse(HttpStatusCode.OK, new
        {
            access_token = "new-access",
            refresh_token = "new-refresh",
            expires_in = 3600,
        });

        var result = await _sut.RefreshTokenAsync(PlatformType.LinkedIn, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _encryption.Verify(e => e.Encrypt("new-access"), Times.Once);
    }

    [Fact]
    public async Task RefreshTokenAsync_DisconnectsOnInvalidGrant()
    {
        var platform = new Domain.Entities.Platform
        {
            Type = PlatformType.YouTube,
            DisplayName = "YouTube",
            IsConnected = true,
            EncryptedRefreshToken = System.Text.Encoding.UTF8.GetBytes("old-refresh"),
        };
        SetupPlatforms(platform);

        SetupHttpResponse(HttpStatusCode.BadRequest, new
        {
            error = "invalid_grant",
            error_description = "Token has been revoked",
        });

        var result = await _sut.RefreshTokenAsync(PlatformType.YouTube, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(platform.IsConnected);
    }

    // --- RevokeTokenAsync ---

    [Fact]
    public async Task RevokeTokenAsync_ClearsTokensAndDisconnects()
    {
        var platform = new Domain.Entities.Platform
        {
            Type = PlatformType.TwitterX,
            DisplayName = "Twitter",
            IsConnected = true,
            EncryptedAccessToken = System.Text.Encoding.UTF8.GetBytes("access"),
        };
        SetupPlatforms(platform);

        SetupHttpResponse(HttpStatusCode.OK, new { });

        var result = await _sut.RevokeTokenAsync(PlatformType.TwitterX, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(platform.IsConnected);
        Assert.Null(platform.EncryptedAccessToken);
        Assert.Null(platform.EncryptedRefreshToken);
        Assert.Null(platform.GrantedScopes);
    }

    [Fact]
    public async Task RevokeTokenAsync_ReturnsNotFound_WhenPlatformMissing()
    {
        SetupPlatforms(); // empty

        var result = await _sut.RevokeTokenAsync(PlatformType.TwitterX, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ExchangeCodeAsync_DeletesOAuthStateAfterExchange()
    {
        var state = new OAuthState
        {
            State = "valid-state",
            Platform = PlatformType.LinkedIn,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
        };
        SetupOAuthStates(state);

        var platform = new Domain.Entities.Platform
        {
            Type = PlatformType.LinkedIn,
            DisplayName = "LinkedIn",
        };
        SetupPlatforms(platform);

        SetupHttpResponse(HttpStatusCode.OK, new
        {
            access_token = "token",
            expires_in = 3600,
        });

        await _sut.ExchangeCodeAsync(PlatformType.LinkedIn, "code", "valid-state", null, CancellationToken.None);

        _dbContext.Verify(db => db.OAuthStates.Remove(It.Is<OAuthState>(s => s.State == "valid-state")), Times.Once);
    }

    [Fact]
    public async Task ExchangeCodeAsync_UsesStoredCodeVerifierForTwitterPkce()
    {
        var encryptedVerifier = System.Text.Encoding.UTF8.GetBytes("stored-verifier");
        var state = new OAuthState
        {
            State = "twitter-state",
            Platform = PlatformType.TwitterX,
            EncryptedCodeVerifier = encryptedVerifier,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
        };
        SetupOAuthStates(state);

        var platform = new Domain.Entities.Platform
        {
            Type = PlatformType.TwitterX,
            DisplayName = "Twitter",
        };
        SetupPlatforms(platform);

        SetupHttpResponse(HttpStatusCode.OK, new
        {
            access_token = "twitter-token",
            refresh_token = "twitter-refresh",
            expires_in = 7200,
        });

        await _sut.ExchangeCodeAsync(PlatformType.TwitterX, "auth-code", "twitter-state", "client-verifier", CancellationToken.None);

        // Should decrypt stored verifier, not use client-supplied one
        _encryption.Verify(e => e.Decrypt(encryptedVerifier), Times.Once);
    }

    [Fact]
    public async Task ExchangeCodeAsync_RejectsEmptyCode()
    {
        SetupOAuthStates();
        SetupPlatforms();

        var result = await _sut.ExchangeCodeAsync(PlatformType.LinkedIn, "", "state", null, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }
}
