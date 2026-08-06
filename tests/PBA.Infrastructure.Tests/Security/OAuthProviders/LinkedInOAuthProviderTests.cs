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

public class LinkedInOAuthProviderTests : IDisposable
{
    private readonly Mock<ITokenEncryptor> _encryptor = new();
    private readonly Mock<HttpMessageHandler> _httpHandler = new();
    private readonly HttpClient _httpClient;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly LinkedInOptions _options = new()
    {
        Enabled = true,
        ClientId = "linkedin-client-id",
        ClientSecret = "linkedin-client-secret",
        RedirectUri = "https://localhost:5001/api/auth/linkedin/callback"
    };

    public LinkedInOAuthProviderTests()
    {
        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>()))
            .Returns((string s) => s.Replace("encrypted:", ""));

        _httpClient = new HttpClient(_httpHandler.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);
        _httpClientFactory = factoryMock.Object;
    }

    private LinkedInOAuthProvider CreateProvider() => new(
        _httpClientFactory,
        Options.Create(_options),
        _encryptor.Object,
        NullLogger<LinkedInOAuthProvider>.Instance);

    // The ordered sequence of query keys as they appear on the wire (guards param ordering).
    private static string[] OrderedQueryKeys(string url) =>
        new Uri(url).Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=')[0])
            .ToArray();

    // Captures the outgoing request and returns a canned JSON body.
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
    public void BuildAuthorization_ProducesUnchangedUrlScopesAndState()
    {
        var provider = CreateProvider();

        var request = provider.BuildAuthorization("STATE123", CredentialPurpose.Publishing);

        Assert.StartsWith("https://www.linkedin.com/oauth/v2/authorization", request.Url);
        var query = HttpUtility.ParseQueryString(new Uri(request.Url).Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal(_options.ClientId, query["client_id"]);
        Assert.Equal(_options.RedirectUri, query["redirect_uri"]);
        Assert.Equal("openid profile w_member_social", query["scope"]);
        Assert.Equal("STATE123", query["state"]);
        Assert.Null(request.Additions.CodeVerifier);

        // Param ordering is a hard spec constraint; guard the exact key sequence, not just presence.
        Assert.Equal(
            new[] { "response_type", "client_id", "redirect_uri", "scope", "state" },
            OrderedQueryKeys(request.Url));
    }

    [Fact]
    public async Task ExchangeCode_ReturnsTokenResult_Unchanged()
    {
        var provider = CreateProvider();
        var capture = SetupCapture(JsonSerializer.Serialize(new
        {
            access_token = "li-access-token",
            refresh_token = "li-refresh-token",
            expires_in = 5184000,
            refresh_token_expires_in = 31536000
        }));

        var result = await provider.ExchangeCodeAsync(
            "auth-code", new OAuthStateEntry(Platform.LinkedIn), CancellationToken.None);

        Assert.Equal("li-access-token", result.AccessToken);
        Assert.Equal("li-refresh-token", result.RefreshToken);
        Assert.Equal(5184000, result.ExpiresIn);
        Assert.Equal(31536000, result.RefreshTokenExpiresIn);
        Assert.Equal("openid profile w_member_social", result.Scopes);

        // LinkedIn puts client_secret in the body, uses no Basic auth header.
        Assert.Contains("client_secret=linkedin-client-secret", capture.Body());
        Assert.Null(capture.AuthHeader());
    }

    [Fact]
    public async Task RefreshAsync_RefreshesToken_BehaviorUnchanged()
    {
        var provider = CreateProvider();
        var credential = new PlatformCredential
        {
            Platform = Platform.LinkedIn,
            EncryptedRefreshToken = "encrypted:old-refresh",
            IsActive = true
        };
        var capture = SetupCapture(JsonSerializer.Serialize(new
        {
            access_token = "new-access-token",
            refresh_token = "new-refresh-token",
            expires_in = 5184000
        }));

        var result = await provider.RefreshAsync(credential, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-access-token", result.Tokens!.AccessToken);
        Assert.Equal("new-refresh-token", result.Tokens.RefreshToken);
        Assert.Equal(5184000, result.Tokens.ExpiresIn);

        var body = capture.Body();
        Assert.Contains("grant_type=refresh_token", body);
        Assert.Contains("refresh_token=old-refresh", body);
        Assert.Contains("client_secret=linkedin-client-secret", body);
        Assert.Null(capture.AuthHeader());
    }

    [Fact]
    public async Task RefreshAsync_NonSuccessResponse_ReturnsFail()
    {
        var provider = CreateProvider();
        var credential = new PlatformCredential
        {
            Platform = Platform.LinkedIn,
            EncryptedRefreshToken = "encrypted:old-refresh"
        };
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"invalid_grant\"}")
            });

        var result = await provider.RefreshAsync(credential, CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void NeedsRefresh_WithinFiveMinuteWindow_ReturnsTrue()
    {
        var provider = CreateProvider();
        var now = DateTimeOffset.UtcNow;
        var credential = new PlatformCredential
        {
            Platform = Platform.LinkedIn,
            AccessTokenExpiresAt = now.AddMinutes(3)
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
