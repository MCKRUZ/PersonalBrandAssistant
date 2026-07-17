using System.Net;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Security;
using PBA.Infrastructure.Security.OAuthProviders;
using Xunit;

namespace PBA.Infrastructure.Tests.Security.OAuthProviders;

public class TikTokOAuthProviderTests : IDisposable
{
    private readonly Mock<ITokenEncryptor> _encryptor = new();
    private readonly Mock<HttpMessageHandler> _httpHandler = new();
    private readonly HttpClient _httpClient;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly TikTokOAuthOptions _options = new()
    {
        Enabled = true,
        ClientId = "tt-client-key",
        ClientSecret = "tt-client-secret",
        RedirectUri = "https://localhost:5001/api/auth/tiktok/callback"
    };

    public TikTokOAuthProviderTests()
    {
        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string s) => s.Replace("encrypted:", ""));
        _httpClient = new HttpClient(_httpHandler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);
        _httpClientFactory = factory.Object;
    }

    private TikTokOAuthProvider CreateProvider() => new(
        _httpClientFactory, Options.Create(_options), _encryptor.Object, NullLogger<TikTokOAuthProvider>.Instance);

    private Func<string?> SetupJson(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        string? capturedBody = null;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = req.Content is not null ? await req.Content.ReadAsStringAsync() : null;
                return new HttpResponseMessage(status)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                };
            });
        return () => capturedBody;
    }

    private void SetupThrow() =>
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network down"));

    [Fact]
    public void BuildAuthorization_IncludesCorrectScopesAndRedirectUri()
    {
        var request = CreateProvider().BuildAuthorization("STATE1");

        Assert.StartsWith("https://www.tiktok.com/v2/auth/authorize/", request.Url);
        var q = HttpUtility.ParseQueryString(new Uri(request.Url).Query);
        Assert.Equal(_options.ClientId, q["client_key"]);
        Assert.Equal("user.info.stats,video.list", q["scope"]);
        Assert.Equal(_options.RedirectUri, q["redirect_uri"]);
        Assert.Equal("STATE1", q["state"]);
    }

    [Fact]
    public async Task ExchangeCode_MapsTokenResponseToOAuthTokenResult()
    {
        SetupJson(JsonSerializer.Serialize(new
        {
            access_token = "tt-access", refresh_token = "tt-refresh", expires_in = 86400,
            refresh_expires_in = 31536000, open_id = "open-1"
        }));

        var result = await CreateProvider().ExchangeCodeAsync("code", new OAuthStateEntry(Platform.TikTok), CancellationToken.None);

        Assert.Equal("tt-access", result.AccessToken);
        Assert.Equal("tt-refresh", result.RefreshToken);
        Assert.Equal(86400, result.ExpiresIn);
        Assert.Equal(31536000, result.RefreshTokenExpiresIn);
    }

    [Fact]
    public async Task RefreshAsync_PersistsRotatedRefreshToken()
    {
        var body = SetupJson(JsonSerializer.Serialize(new
        {
            access_token = "tt-new-access", refresh_token = "tt-rotated-refresh", expires_in = 86400
        }));
        var credential = new PlatformCredential { Platform = Platform.TikTok, EncryptedRefreshToken = "encrypted:tt-old-refresh" };

        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("tt-new-access", result.Tokens!.AccessToken);
        // TikTok rotates the refresh token — the new one must be surfaced for persistence.
        Assert.Equal("tt-rotated-refresh", result.Tokens.RefreshToken);
        Assert.Contains("grant_type=refresh_token", body());
        Assert.Contains("refresh_token=tt-old-refresh", body());
    }

    [Fact]
    public async Task RefreshAsync_MapsRevokedSignal_ToRefreshFailureReasonRevoked()
    {
        SetupJson(JsonSerializer.Serialize(new { error = "access_token_invalid", error_description = "invalid", log_id = "x" }),
            HttpStatusCode.BadRequest);
        var credential = new PlatformCredential { Platform = Platform.TikTok, EncryptedRefreshToken = "encrypted:tt-old-refresh" };

        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RefreshFailureReason.Revoked, result.FailureReason);
    }

    [Fact]
    public async Task RefreshAsync_400WithDifferentError_MapsToTransient()
    {
        // SAFETY direction: an error other than access_token_invalid must NOT deactivate a valid credential.
        SetupJson(JsonSerializer.Serialize(new { error = "rate_limit_exceeded", error_description = "slow down" }),
            HttpStatusCode.BadRequest);
        var credential = new PlatformCredential { Platform = Platform.TikTok, EncryptedRefreshToken = "encrypted:tt-old-refresh" };

        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);

        Assert.Equal(RefreshFailureReason.Transient, result.FailureReason);
    }

    [Fact]
    public async Task RefreshAsync_MapsNetworkError_ToRefreshFailureReasonTransient()
    {
        SetupThrow();
        var credential = new PlatformCredential { Platform = Platform.TikTok, EncryptedRefreshToken = "encrypted:tt-old-refresh" };

        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);

        Assert.Equal(RefreshFailureReason.Transient, result.FailureReason);
    }

    [Fact]
    public async Task RefreshAsync_MapsServerError_ToRefreshFailureReasonTransient()
    {
        SetupJson("{}", HttpStatusCode.ServiceUnavailable);
        var credential = new PlatformCredential { Platform = Platform.TikTok, EncryptedRefreshToken = "encrypted:tt-old-refresh" };

        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);

        Assert.Equal(RefreshFailureReason.Transient, result.FailureReason);
    }

    [Fact]
    public void NeedsRefresh_ReturnsTrue_WithinProviderLeadTime()
    {
        var provider = CreateProvider();
        var now = DateTimeOffset.UtcNow;
        var credential = new PlatformCredential { Platform = Platform.TikTok, AccessTokenExpiresAt = now.AddMinutes(30) };

        Assert.True(provider.NeedsRefresh(credential, now));   // within 1h lead
        credential.AccessTokenExpiresAt = now.AddHours(5);
        Assert.False(provider.NeedsRefresh(credential, now));
    }

    public void Dispose() => _httpClient.Dispose();
}
