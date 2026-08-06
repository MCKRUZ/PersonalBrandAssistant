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

public class InstagramOAuthProviderTests : IDisposable
{
    private readonly Mock<ITokenEncryptor> _encryptor = new();
    private readonly Mock<HttpMessageHandler> _httpHandler = new();
    private readonly HttpClient _httpClient;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly InstagramOAuthOptions _options = new()
    {
        Enabled = true,
        ClientId = "ig-client-id",
        ClientSecret = "ig-client-secret",
        RedirectUri = "https://localhost:5001/api/auth/instagram/callback"
    };

    public InstagramOAuthProviderTests()
    {
        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string s) => s.Replace("encrypted:", ""));
        _httpClient = new HttpClient(_httpHandler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);
        _httpClientFactory = factory.Object;
    }

    private InstagramOAuthProvider CreateProvider() => new(
        _httpClientFactory, Options.Create(_options), _encryptor.Object, NullLogger<InstagramOAuthProvider>.Instance);

    // Routes each request to a response by inspecting its URL; records the last request URI for assertions.
    private Func<Uri?> SetupRouter(Func<HttpRequestMessage, HttpResponseMessage> route)
    {
        Uri? lastUri = null;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                lastUri = req.RequestUri;
                return Task.FromResult(route(req));
            });
        return () => lastUri;
    }

    private static HttpResponseMessage Json(object payload, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json") };

    private void SetupThrow() =>
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network down"));

    [Fact]
    public void BuildAuthorization_IncludesCorrectScopesAndRedirectUri()
    {
        var request = CreateProvider().BuildAuthorization("STATE1", CredentialPurpose.Publishing);

        Assert.StartsWith("https://www.instagram.com/oauth/authorize", request.Url);
        var q = HttpUtility.ParseQueryString(new Uri(request.Url).Query);
        Assert.Contains("instagram_business_basic", q["scope"]);
        Assert.Contains("instagram_business_manage_insights", q["scope"]);
        Assert.Equal(_options.RedirectUri, q["redirect_uri"]);
        Assert.Equal("STATE1", q["state"]);
    }

    [Fact]
    public async Task ExchangeCode_MapsTokenResponseToOAuthTokenResult()
    {
        // Two-step: short-lived token (POST api.instagram.com) then long-lived (GET graph.instagram.com).
        SetupRouter(req => req.RequestUri!.Host == "api.instagram.com"
            ? Json(new { access_token = "ig-short", user_id = 123 })
            : Json(new { access_token = "ig-long", token_type = "bearer", expires_in = 5184000 }));

        var result = await CreateProvider().ExchangeCodeAsync("code", new OAuthStateEntry(Platform.Instagram), CancellationToken.None);

        Assert.Equal("ig-long", result.AccessToken);
        Assert.Null(result.RefreshToken);                 // no separate refresh token
        Assert.Equal(5184000, result.ExpiresIn);
    }

    [Fact]
    public async Task RefreshAsync_ExtendsLongLivedAccessToken_NoRefreshToken()
    {
        // Credential has NO refresh token — refresh must use the current access token and still succeed.
        var lastUri = SetupRouter(_ => Json(new { access_token = "ig-extended", token_type = "bearer", expires_in = 5184000 }));
        var credential = new PlatformCredential
        {
            Platform = Platform.Instagram,
            EncryptedAccessToken = "encrypted:ig-current",
            EncryptedRefreshToken = null
        };

        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ig-extended", result.Tokens!.AccessToken);
        Assert.Null(result.Tokens.RefreshToken);
        // Refresh used the current access token, not a refresh token.
        Assert.Contains("grant_type=ig_refresh_token", lastUri()!.Query);
        Assert.Contains("access_token=ig-current", lastUri()!.Query);
    }

    [Fact]
    public async Task RefreshAsync_MapsRevokedSignal_Code190_ToRefreshFailureReasonRevoked()
    {
        // Code 190 alone (no subcode) must map to Revoked — isolates the code branch.
        SetupRouter(_ => Json(new { error = new { code = 190, message = "invalid token" } }, HttpStatusCode.BadRequest));
        var credential = new PlatformCredential { Platform = Platform.Instagram, EncryptedAccessToken = "encrypted:ig-current" };

        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RefreshFailureReason.Revoked, result.FailureReason);
    }

    [Fact]
    public async Task RefreshAsync_BenignMetaError_MapsToTransient()
    {
        // SAFETY direction: a non-190/458/463 Meta error must NOT deactivate a valid credential.
        SetupRouter(_ => Json(new { error = new { code = 4, message = "rate limited" } }, HttpStatusCode.BadRequest));
        var credential = new PlatformCredential { Platform = Platform.Instagram, EncryptedAccessToken = "encrypted:ig-current" };

        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);

        Assert.Equal(RefreshFailureReason.Transient, result.FailureReason);
    }

    [Fact]
    public async Task RefreshAsync_MapsNetworkError_ToRefreshFailureReasonTransient()
    {
        SetupThrow();
        var credential = new PlatformCredential { Platform = Platform.Instagram, EncryptedAccessToken = "encrypted:ig-current" };

        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);

        Assert.Equal(RefreshFailureReason.Transient, result.FailureReason);
    }

    [Fact]
    public void NeedsRefresh_ReturnsTrue_WithinProviderLeadTime()
    {
        var provider = CreateProvider();
        var now = DateTimeOffset.UtcNow;
        var credential = new PlatformCredential { Platform = Platform.Instagram, AccessTokenExpiresAt = now.AddDays(40) };

        Assert.True(provider.NeedsRefresh(credential, now));   // within 50-day lead
        credential.AccessTokenExpiresAt = now.AddDays(59);
        Assert.False(provider.NeedsRefresh(credential, now));
    }

    public void Dispose() => _httpClient.Dispose();
}
