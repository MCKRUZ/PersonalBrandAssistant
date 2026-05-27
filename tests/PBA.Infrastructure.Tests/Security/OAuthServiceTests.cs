using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;
using PBA.Infrastructure.Security;
using Xunit;

namespace PBA.Infrastructure.Tests.Security;

public class OAuthServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<ITokenEncryptor> _encryptor = new();
    private readonly Mock<HttpMessageHandler> _httpHandler = new();
    private readonly HttpClient _httpClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Mock<ILogger<OAuthService>> _logger = new();

    private readonly LinkedInOptions _linkedInOptions = new()
    {
        Enabled = true,
        ClientId = "linkedin-client-id",
        ClientSecret = "linkedin-client-secret",
        RedirectUri = "https://localhost:5001/api/auth/linkedin/callback"
    };

    private readonly TwitterOptions _twitterOptions = new()
    {
        Enabled = true,
        ClientId = "twitter-client-id",
        ClientSecret = "twitter-client-secret",
        RedirectUri = "https://localhost:5001/api/auth/twitter/callback"
    };

    public OAuthServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        _encryptor.Setup(e => e.Encrypt(It.IsAny<string>()))
            .Returns((string s) => $"encrypted:{s}");
        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>()))
            .Returns((string s) => s.Replace("encrypted:", ""));

        _httpClient = new HttpClient(_httpHandler.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);
        _httpClientFactory = factoryMock.Object;
    }

    private OAuthService CreateService() => new(
        _httpClientFactory,
        _encryptor.Object,
        _dbContext,
        Options.Create(_linkedInOptions),
        Options.Create(_twitterOptions),
        _logger.Object);

    private void SetupHttpResponse(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
    {
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });
    }

    [Fact]
    public async Task GetAuthorizationUrl_LinkedIn_ReturnsCorrectUrlWithScopes()
    {
        var service = CreateService();
        var url = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CancellationToken.None);

        Assert.StartsWith("https://www.linkedin.com/oauth/v2/authorization", url);
        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Equal("openid profile w_member_social", query["scope"]);
        Assert.Equal(_linkedInOptions.ClientId, query["client_id"]);
        Assert.NotNull(query["redirect_uri"]);
        Assert.NotNull(query["state"]);
    }

    [Fact]
    public async Task GetAuthorizationUrl_Twitter_IncludesPKCECodeChallenge()
    {
        var service = CreateService();
        var url = await service.GetAuthorizationUrlAsync(Platform.Twitter, CancellationToken.None);

        Assert.StartsWith("https://twitter.com/i/oauth2/authorize", url);
        Assert.Contains("code_challenge=", url);
        Assert.Contains("code_challenge_method=S256", url);
    }

    [Fact]
    public async Task GetAuthorizationUrl_IncludesStateParameter()
    {
        var service = CreateService();
        var url = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CancellationToken.None);

        var uri = new Uri(url);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var state = query["state"];

        Assert.NotNull(state);
        Assert.True(state!.Length >= 32);
    }

    [Fact]
    public async Task ExchangeCodeAsync_LinkedIn_StoresEncryptedTokens()
    {
        var service = CreateService();
        var authUrl = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CancellationToken.None);
        var state = System.Web.HttpUtility.ParseQueryString(new Uri(authUrl).Query)["state"]!;

        SetupHttpResponse(JsonSerializer.Serialize(new
        {
            access_token = "li-access-token",
            refresh_token = "li-refresh-token",
            expires_in = 5184000,
            refresh_token_expires_in = 31536000
        }));

        var credential = await service.ExchangeCodeAsync(Platform.LinkedIn, "auth-code", state, CancellationToken.None);

        Assert.Equal(Platform.LinkedIn, credential.Platform);
        Assert.True(credential.IsActive);
        _encryptor.Verify(e => e.Encrypt("li-access-token"), Times.Once);
        _encryptor.Verify(e => e.Encrypt("li-refresh-token"), Times.Once);

        var saved = await _dbContext.PlatformCredentials.FirstOrDefaultAsync(c => c.Platform == Platform.LinkedIn);
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task ExchangeCodeAsync_Twitter_UsesCodeVerifierForPKCE()
    {
        var service = CreateService();
        var authUrl = await service.GetAuthorizationUrlAsync(Platform.Twitter, CancellationToken.None);
        var state = HttpUtility.ParseQueryString(new Uri(authUrl).Query)["state"]!;

        string? capturedBody = null;
        _httpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        access_token = "tw-access-token",
                        refresh_token = "tw-refresh-token",
                        expires_in = 7200
                    }), System.Text.Encoding.UTF8, "application/json")
                };
            });

        await service.ExchangeCodeAsync(Platform.Twitter, "auth-code", state, CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("code_verifier=", capturedBody);
    }

    [Fact]
    public async Task ExchangeCodeAsync_InvalidState_ThrowsInvalidOperationException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExchangeCodeAsync(Platform.LinkedIn, "code", "invalid-state", CancellationToken.None));
    }

    [Fact]
    public async Task RefreshTokenAsync_LinkedIn_UpdatesStoredTokens()
    {
        var service = CreateService();
        var credential = new Domain.Entities.PlatformCredential
        {
            Platform = Platform.LinkedIn,
            EncryptedAccessToken = "encrypted:old-access",
            EncryptedRefreshToken = "encrypted:old-refresh",
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            IsActive = true
        };
        _dbContext.PlatformCredentials.Add(credential);
        await _dbContext.SaveChangesAsync();

        SetupHttpResponse(JsonSerializer.Serialize(new
        {
            access_token = "new-access-token",
            refresh_token = "new-refresh-token",
            expires_in = 5184000
        }));

        var result = await service.RefreshTokenAsync(credential, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-access-token", result.Value);
        _encryptor.Verify(e => e.Encrypt("new-access-token"), Times.Once);
    }

    [Fact]
    public async Task RefreshTokenAsync_ExpiredRefreshToken_ReturnsEmptyAndDeactivates()
    {
        var service = CreateService();
        var credential = new Domain.Entities.PlatformCredential
        {
            Platform = Platform.LinkedIn,
            EncryptedAccessToken = "encrypted:old-access",
            EncryptedRefreshToken = "encrypted:old-refresh",
            IsActive = true
        };
        _dbContext.PlatformCredentials.Add(credential);
        await _dbContext.SaveChangesAsync();

        SetupHttpResponse("{\"error\":\"invalid_grant\"}", HttpStatusCode.BadRequest);

        var result = await service.RefreshTokenAsync(credential, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(credential.IsActive);
    }

    [Fact]
    public async Task GetAuthorizationUrl_UnsupportedPlatform_ThrowsNotSupportedException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            service.GetAuthorizationUrlAsync(Platform.Blog, CancellationToken.None));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _dbContext.Dispose();
    }
}
