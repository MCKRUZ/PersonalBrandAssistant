using System.Net;
using System.Net.Http.Headers;
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

public class TwitterOAuthProviderTests : IDisposable
{
    private readonly Mock<ITokenEncryptor> _encryptor = new();
    private readonly Mock<HttpMessageHandler> _httpHandler = new();
    private readonly HttpClient _httpClient;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly TwitterOptions _options = new()
    {
        Enabled = true,
        ClientId = "twitter-client-id",
        ClientSecret = "twitter-client-secret",
        RedirectUri = "https://localhost:5001/api/auth/twitter/callback"
    };

    public TwitterOAuthProviderTests()
    {
        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>()))
            .Returns((string s) => s.Replace("encrypted:", ""));

        _httpClient = new HttpClient(_httpHandler.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);
        _httpClientFactory = factoryMock.Object;
    }

    private TwitterOAuthProvider CreateProvider() => new(
        _httpClientFactory,
        Options.Create(_options),
        _encryptor.Object,
        NullLogger<TwitterOAuthProvider>.Instance);

    // The ordered sequence of query keys as they appear on the wire (guards param ordering).
    private static string[] OrderedQueryKeys(string url) =>
        new Uri(url).Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=')[0])
            .ToArray();

    private (Func<string?> Body, Func<AuthenticationHeaderValue?> AuthHeader) SetupCapture(string responseJson)
    {
        string? capturedBody = null;
        AuthenticationHeaderValue? capturedAuth = null;

        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = req.Content is not null ? await req.Content.ReadAsStringAsync() : null;
                capturedAuth = req.Headers.Authorization;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
                };
            });

        return (() => capturedBody, () => capturedAuth);
    }

    [Fact]
    public void BuildAuthorization_IncludesPkceS256ChallengeAndPersistsVerifier()
    {
        var provider = CreateProvider();

        var request = provider.BuildAuthorization("STATE123");

        Assert.StartsWith("https://twitter.com/i/oauth2/authorize", request.Url);
        var query = HttpUtility.ParseQueryString(new Uri(request.Url).Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal(_options.ClientId, query["client_id"]);
        Assert.Equal("tweet.read tweet.write users.read media.write offline.access", query["scope"]);
        Assert.Equal("STATE123", query["state"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrEmpty(query["code_challenge"]));

        // The provider hands the coordinator the PKCE verifier to persist in state.
        Assert.False(string.IsNullOrEmpty(request.Additions.CodeVerifier));

        // Param ordering is a hard spec constraint; code_challenge/method must come last, in order.
        Assert.Equal(
            new[] { "response_type", "client_id", "redirect_uri", "scope", "state", "code_challenge", "code_challenge_method" },
            OrderedQueryKeys(request.Url));
    }

    [Fact]
    public async Task ExchangeCode_UsesBasicAuthAndCodeVerifier_Unchanged()
    {
        var provider = CreateProvider();
        var capture = SetupCapture(JsonSerializer.Serialize(new
        {
            access_token = "tw-access-token",
            refresh_token = "tw-refresh-token",
            expires_in = 7200
        }));

        var state = new OAuthStateEntry(Platform.Twitter, "the-code-verifier");
        var result = await provider.ExchangeCodeAsync("auth-code", state, CancellationToken.None);

        Assert.Equal("tw-access-token", result.AccessToken);
        Assert.Equal("tw-refresh-token", result.RefreshToken);
        Assert.Equal(7200, result.ExpiresIn);
        Assert.Null(result.RefreshTokenExpiresIn);
        Assert.Equal("tweet.read tweet.write users.read media.write offline.access", result.Scopes);

        var body = capture.Body();
        Assert.Contains("code_verifier=the-code-verifier", body);
        // Twitter uses Basic auth; client_secret is in the header, not the body.
        var auth = capture.AuthHeader();
        Assert.NotNull(auth);
        Assert.Equal("Basic", auth!.Scheme);
        Assert.DoesNotContain("client_secret=", body);
    }

    [Fact]
    public async Task RefreshAsync_UsesBasicAuth_BehaviorUnchanged()
    {
        var provider = CreateProvider();
        var credential = new PlatformCredential
        {
            Platform = Platform.Twitter,
            EncryptedRefreshToken = "encrypted:old-refresh",
            IsActive = true
        };
        var capture = SetupCapture(JsonSerializer.Serialize(new
        {
            access_token = "new-access-token",
            refresh_token = "new-refresh-token",
            expires_in = 7200
        }));

        var result = await provider.RefreshAsync(credential, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-access-token", result.Tokens!.AccessToken);

        var body = capture.Body();
        Assert.Contains("grant_type=refresh_token", body);
        Assert.Contains("refresh_token=old-refresh", body);
        // Basic auth header carries the secret; body must NOT contain client_secret.
        var auth = capture.AuthHeader();
        Assert.NotNull(auth);
        Assert.Equal("Basic", auth!.Scheme);
        Assert.DoesNotContain("client_secret=", body);
    }

    [Fact]
    public void NeedsRefresh_WithinTenMinuteWindow_ReturnsTrue()
    {
        var provider = CreateProvider();
        var now = DateTimeOffset.UtcNow;
        var credential = new PlatformCredential
        {
            Platform = Platform.Twitter,
            AccessTokenExpiresAt = now.AddMinutes(8)
        };

        Assert.True(provider.NeedsRefresh(credential, now));

        credential.AccessTokenExpiresAt = now.AddMinutes(30);
        Assert.False(provider.NeedsRefresh(credential, now));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
