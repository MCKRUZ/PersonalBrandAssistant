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

public class YouTubeOAuthProviderTests : IDisposable
{
    private readonly Mock<ITokenEncryptor> _encryptor = new();
    private readonly Mock<HttpMessageHandler> _httpHandler = new();
    private readonly HttpClient _httpClient;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly YouTubeOAuthOptions _options = new()
    {
        Enabled = true,
        ClientId = "yt-client-id",
        ClientSecret = "yt-client-secret",
        RedirectUri = "https://localhost:5001/api/auth/youtube/callback",
        ApiKey = "yt-api-key"
    };

    public YouTubeOAuthProviderTests()
    {
        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string s) => s.Replace("encrypted:", ""));
        _httpClient = new HttpClient(_httpHandler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);
        _httpClientFactory = factory.Object;
    }

    private YouTubeOAuthProvider CreateProvider() => new(
        _httpClientFactory, Options.Create(_options), _encryptor.Object, NullLogger<YouTubeOAuthProvider>.Instance);

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

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth", request.Url);
        var q = HttpUtility.ParseQueryString(new Uri(request.Url).Query);
        Assert.Contains("yt-analytics.readonly", q["scope"]);
        Assert.Contains("youtube.readonly", q["scope"]);
        Assert.Equal(_options.RedirectUri, q["redirect_uri"]);
        Assert.Equal("offline", q["access_type"]);
        Assert.Equal("consent", q["prompt"]);
        Assert.Equal("STATE1", q["state"]);
        Assert.Null(request.Additions.CodeVerifier);
    }

    [Fact]
    public async Task ExchangeCode_MapsTokenResponseToOAuthTokenResult()
    {
        SetupJson(JsonSerializer.Serialize(new
        {
            access_token = "yt-access", refresh_token = "yt-refresh", expires_in = 3600
        }));

        var result = await CreateProvider().ExchangeCodeAsync("code", new OAuthStateEntry(Platform.YouTube), CancellationToken.None);

        Assert.Equal("yt-access", result.AccessToken);
        Assert.Equal("yt-refresh", result.RefreshToken);
        Assert.Equal(3600, result.ExpiresIn);
    }

    [Fact]
    public async Task RefreshAsync_UsesRefreshToken()
    {
        var body = SetupJson(JsonSerializer.Serialize(new { access_token = "yt-new-access", expires_in = 3600 }));
        var credential = new PlatformCredential { Platform = Platform.YouTube, EncryptedRefreshToken = "encrypted:yt-refresh" };

        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("yt-new-access", result.Tokens!.AccessToken);
        Assert.Contains("grant_type=refresh_token", body());
        Assert.Contains("refresh_token=yt-refresh", body());
    }

    [Fact]
    public async Task RefreshAsync_MapsRevokedSignal_ToRefreshFailureReasonRevoked()
    {
        SetupJson("{\"error\":\"invalid_grant\"}", HttpStatusCode.BadRequest);
        var credential = new PlatformCredential { Platform = Platform.YouTube, EncryptedRefreshToken = "encrypted:yt-refresh" };

        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RefreshFailureReason.Revoked, result.FailureReason);
    }

    [Fact]
    public async Task RefreshAsync_MapsNetworkError_ToRefreshFailureReasonTransient()
    {
        SetupThrow();
        var credential = new PlatformCredential { Platform = Platform.YouTube, EncryptedRefreshToken = "encrypted:yt-refresh" };

        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RefreshFailureReason.Transient, result.FailureReason);
    }

    [Fact]
    public async Task RefreshAsync_400WithoutInvalidGrant_MapsToTransient()
    {
        // SAFETY direction: a non-revoked error must NOT deactivate a still-valid credential.
        SetupJson("{\"error\":\"rate_limit_exceeded\"}", HttpStatusCode.BadRequest);
        var credential = new PlatformCredential { Platform = Platform.YouTube, EncryptedRefreshToken = "encrypted:yt-refresh" };

        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);

        Assert.Equal(RefreshFailureReason.Transient, result.FailureReason);
    }

    [Fact]
    public async Task RefreshAsync_MalformedSuccessBody_MapsToTransient()
    {
        SetupJson("{ not json", HttpStatusCode.OK);
        var credential = new PlatformCredential { Platform = Platform.YouTube, EncryptedRefreshToken = "encrypted:yt-refresh" };

        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RefreshFailureReason.Transient, result.FailureReason);
    }

    [Fact]
    public async Task RefreshAsync_MapsServerError_ToRefreshFailureReasonTransient()
    {
        SetupJson("{\"error\":\"backend\"}", HttpStatusCode.ServiceUnavailable);
        var credential = new PlatformCredential { Platform = Platform.YouTube, EncryptedRefreshToken = "encrypted:yt-refresh" };

        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);

        Assert.Equal(RefreshFailureReason.Transient, result.FailureReason);
    }

    [Fact]
    public void NeedsRefresh_ReturnsTrue_WithinProviderLeadTime()
    {
        var provider = CreateProvider();
        var now = DateTimeOffset.UtcNow;
        var credential = new PlatformCredential { Platform = Platform.YouTube, AccessTokenExpiresAt = now.AddMinutes(30) };

        Assert.True(provider.NeedsRefresh(credential, now));   // within 1h lead
        credential.AccessTokenExpiresAt = now.AddHours(5);
        Assert.False(provider.NeedsRefresh(credential, now));
    }

    public void Dispose() => _httpClient.Dispose();
}
