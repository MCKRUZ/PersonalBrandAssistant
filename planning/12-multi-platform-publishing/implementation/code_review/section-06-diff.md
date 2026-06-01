diff --git a/src/PBA.Application/Common/Interfaces/IOAuthService.cs b/src/PBA.Application/Common/Interfaces/IOAuthService.cs
new file mode 100644
index 0000000..a7f30b8
--- /dev/null
+++ b/src/PBA.Application/Common/Interfaces/IOAuthService.cs
@@ -0,0 +1,11 @@
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+
+namespace PBA.Application.Common.Interfaces;
+
+public interface IOAuthService
+{
+    Task<string> GetAuthorizationUrlAsync(Platform platform, CancellationToken ct);
+    Task<PlatformCredential> ExchangeCodeAsync(Platform platform, string code, string state, CancellationToken ct);
+    Task<string> RefreshTokenAsync(PlatformCredential credential, CancellationToken ct);
+}
diff --git a/src/PBA.Application/Common/Interfaces/ITokenEncryptor.cs b/src/PBA.Application/Common/Interfaces/ITokenEncryptor.cs
new file mode 100644
index 0000000..20213a3
--- /dev/null
+++ b/src/PBA.Application/Common/Interfaces/ITokenEncryptor.cs
@@ -0,0 +1,7 @@
+namespace PBA.Application.Common.Interfaces;
+
+public interface ITokenEncryptor
+{
+    string Encrypt(string plaintext);
+    string Decrypt(string ciphertext);
+}
diff --git a/src/PBA.Infrastructure/Configuration/EncryptionOptions.cs b/src/PBA.Infrastructure/Configuration/EncryptionOptions.cs
new file mode 100644
index 0000000..91535b1
--- /dev/null
+++ b/src/PBA.Infrastructure/Configuration/EncryptionOptions.cs
@@ -0,0 +1,8 @@
+namespace PBA.Infrastructure.Configuration;
+
+public sealed class EncryptionOptions
+{
+    public const string SectionName = "Encryption";
+
+    public required string Key { get; init; }
+}
diff --git a/src/PBA.Infrastructure/Configuration/LinkedInOptions.cs b/src/PBA.Infrastructure/Configuration/LinkedInOptions.cs
new file mode 100644
index 0000000..eee26e0
--- /dev/null
+++ b/src/PBA.Infrastructure/Configuration/LinkedInOptions.cs
@@ -0,0 +1,11 @@
+namespace PBA.Infrastructure.Configuration;
+
+public sealed class LinkedInOptions
+{
+    public const string SectionName = "Publishing:LinkedIn";
+
+    public bool Enabled { get; init; }
+    public required string ClientId { get; init; }
+    public required string ClientSecret { get; init; }
+    public required string RedirectUri { get; init; }
+}
diff --git a/src/PBA.Infrastructure/Configuration/TwitterOptions.cs b/src/PBA.Infrastructure/Configuration/TwitterOptions.cs
new file mode 100644
index 0000000..fe78d84
--- /dev/null
+++ b/src/PBA.Infrastructure/Configuration/TwitterOptions.cs
@@ -0,0 +1,13 @@
+namespace PBA.Infrastructure.Configuration;
+
+public sealed class TwitterOptions
+{
+    public const string SectionName = "Publishing:Twitter";
+
+    public bool Enabled { get; init; }
+    public required string ClientId { get; init; }
+    public required string ClientSecret { get; init; }
+    public required string RedirectUri { get; init; }
+    public string? ApiKey { get; init; }
+    public string? ApiSecret { get; init; }
+}
diff --git a/src/PBA.Infrastructure/DependencyInjection.cs b/src/PBA.Infrastructure/DependencyInjection.cs
index 5c303a7..72c9aef 100644
--- a/src/PBA.Infrastructure/DependencyInjection.cs
+++ b/src/PBA.Infrastructure/DependencyInjection.cs
@@ -7,6 +7,7 @@ using PBA.Infrastructure.Data;
 using PBA.Infrastructure.Connectors;
 using PBA.Infrastructure.Publishing;
 using PBA.Infrastructure.Seeding;
+using PBA.Infrastructure.Security;
 using PBA.Infrastructure.Services;
 
 namespace PBA.Infrastructure;
@@ -44,6 +45,12 @@ public static class DependencyInjection
         services.Configure<BlogConnectorOptions>(configuration.GetSection(BlogConnectorOptions.SectionName));
         services.AddKeyedScoped<IPlatformConnector, BlogConnector>(PBA.Domain.Enums.Platform.Blog);
 
+        services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));
+        services.Configure<LinkedInOptions>(configuration.GetSection(LinkedInOptions.SectionName));
+        services.Configure<TwitterOptions>(configuration.GetSection(TwitterOptions.SectionName));
+        services.AddSingleton<ITokenEncryptor, TokenEncryptor>();
+        services.AddScoped<IOAuthService, OAuthService>();
+
         services.AddScoped<IContentPublisher, ContentPublisher>();
         services.AddScoped<IContentScheduler, HangfireContentScheduler>();
         services.AddHostedService<ScheduledPublishReconciler>();
diff --git a/src/PBA.Infrastructure/Security/OAuthService.cs b/src/PBA.Infrastructure/Security/OAuthService.cs
new file mode 100644
index 0000000..a94a33c
--- /dev/null
+++ b/src/PBA.Infrastructure/Security/OAuthService.cs
@@ -0,0 +1,280 @@
+using System.Collections.Concurrent;
+using System.Net.Http.Headers;
+using System.Security.Cryptography;
+using System.Text;
+using System.Text.Json;
+using System.Web;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+
+namespace PBA.Infrastructure.Security;
+
+public sealed class OAuthService(
+    IHttpClientFactory httpClientFactory,
+    ITokenEncryptor encryptor,
+    IAppDbContext db,
+    IOptions<LinkedInOptions> linkedInOptions,
+    IOptions<TwitterOptions> twitterOptions,
+    ILogger<OAuthService> logger) : IOAuthService
+{
+    private static readonly ConcurrentDictionary<string, OAuthStateEntry> StateStore = new();
+    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);
+
+    public Task<string> GetAuthorizationUrlAsync(Platform platform, CancellationToken ct)
+    {
+        CleanExpiredStates();
+
+        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
+
+        var url = platform switch
+        {
+            Platform.LinkedIn => BuildLinkedInAuthUrl(state),
+            Platform.Twitter => BuildTwitterAuthUrl(state),
+            _ => throw new NotSupportedException($"OAuth is not supported for {platform}")
+        };
+
+        return Task.FromResult(url);
+    }
+
+    public async Task<PlatformCredential> ExchangeCodeAsync(
+        Platform platform, string code, string state, CancellationToken ct)
+    {
+        if (!StateStore.TryRemove(state, out var stateEntry) || stateEntry.IsExpired)
+            throw new InvalidOperationException("Invalid or expired OAuth state");
+
+        var tokenResponse = platform switch
+        {
+            Platform.LinkedIn => await ExchangeLinkedInCodeAsync(code, ct),
+            Platform.Twitter => await ExchangeTwitterCodeAsync(code, stateEntry.CodeVerifier!, ct),
+            _ => throw new NotSupportedException($"OAuth is not supported for {platform}")
+        };
+
+        var credential = await db.PlatformCredentials
+            .FirstOrDefaultAsync(c => c.Platform == platform, ct);
+
+        if (credential is null)
+        {
+            credential = new PlatformCredential { Platform = platform };
+            db.PlatformCredentials.Add(credential);
+        }
+
+        credential.EncryptedAccessToken = encryptor.Encrypt(tokenResponse.AccessToken);
+        credential.EncryptedRefreshToken = tokenResponse.RefreshToken is not null
+            ? encryptor.Encrypt(tokenResponse.RefreshToken)
+            : null;
+        credential.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
+        credential.RefreshTokenExpiresAt = tokenResponse.RefreshTokenExpiresIn.HasValue
+            ? DateTimeOffset.UtcNow.AddSeconds(tokenResponse.RefreshTokenExpiresIn.Value)
+            : null;
+        credential.Scopes = tokenResponse.Scopes;
+        credential.IsActive = true;
+        credential.UpdatedAt = DateTimeOffset.UtcNow;
+
+        await db.SaveChangesAsync(ct);
+
+        logger.LogInformation("OAuth tokens stored for {Platform}", platform);
+        return credential;
+    }
+
+    public async Task<string> RefreshTokenAsync(PlatformCredential credential, CancellationToken ct)
+    {
+        var refreshToken = encryptor.Decrypt(credential.EncryptedRefreshToken!);
+
+        var (tokenEndpoint, clientId, clientSecret) = credential.Platform switch
+        {
+            Platform.LinkedIn => (
+                "https://www.linkedin.com/oauth/v2/accessToken",
+                linkedInOptions.Value.ClientId,
+                linkedInOptions.Value.ClientSecret),
+            Platform.Twitter => (
+                "https://api.twitter.com/2/oauth2/token",
+                twitterOptions.Value.ClientId,
+                twitterOptions.Value.ClientSecret),
+            _ => throw new NotSupportedException($"Token refresh not supported for {credential.Platform}")
+        };
+
+        var parameters = new Dictionary<string, string>
+        {
+            ["grant_type"] = "refresh_token",
+            ["refresh_token"] = refreshToken,
+            ["client_id"] = clientId,
+            ["client_secret"] = clientSecret
+        };
+
+        var client = httpClientFactory.CreateClient();
+        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
+        {
+            Content = new FormUrlEncodedContent(parameters)
+        };
+
+        if (credential.Platform == Platform.Twitter)
+        {
+            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
+            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
+        }
+
+        var response = await client.SendAsync(request, ct);
+
+        if (!response.IsSuccessStatusCode)
+        {
+            logger.LogWarning("Token refresh failed for {Platform}: {Status}", credential.Platform, response.StatusCode);
+            credential.IsActive = false;
+            credential.UpdatedAt = DateTimeOffset.UtcNow;
+            await db.SaveChangesAsync(ct);
+            return string.Empty;
+        }
+
+        var json = await response.Content.ReadAsStringAsync(ct);
+        var tokenData = JsonSerializer.Deserialize<JsonElement>(json);
+
+        var newAccessToken = tokenData.GetProperty("access_token").GetString()!;
+        var expiresIn = tokenData.GetProperty("expires_in").GetInt32();
+
+        credential.EncryptedAccessToken = encryptor.Encrypt(newAccessToken);
+        credential.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
+
+        if (tokenData.TryGetProperty("refresh_token", out var newRefreshProp))
+        {
+            var newRefreshToken = newRefreshProp.GetString()!;
+            credential.EncryptedRefreshToken = encryptor.Encrypt(newRefreshToken);
+        }
+
+        credential.UpdatedAt = DateTimeOffset.UtcNow;
+        await db.SaveChangesAsync(ct);
+
+        return newAccessToken;
+    }
+
+    private string BuildLinkedInAuthUrl(string state)
+    {
+        var li = linkedInOptions.Value;
+        var entry = new OAuthStateEntry(Platform.LinkedIn);
+        StateStore[state] = entry;
+
+        var qs = HttpUtility.ParseQueryString(string.Empty);
+        qs["response_type"] = "code";
+        qs["client_id"] = li.ClientId;
+        qs["redirect_uri"] = li.RedirectUri;
+        qs["scope"] = "openid profile w_member_social";
+        qs["state"] = state;
+
+        return $"https://www.linkedin.com/oauth/v2/authorization?{qs}";
+    }
+
+    private string BuildTwitterAuthUrl(string state)
+    {
+        var tw = twitterOptions.Value;
+        var codeVerifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(96))
+            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
+        var codeChallenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
+
+        var entry = new OAuthStateEntry(Platform.Twitter, codeVerifier);
+        StateStore[state] = entry;
+
+        var qs = HttpUtility.ParseQueryString(string.Empty);
+        qs["response_type"] = "code";
+        qs["client_id"] = tw.ClientId;
+        qs["redirect_uri"] = tw.RedirectUri;
+        qs["scope"] = "tweet.read tweet.write users.read media.write offline.access";
+        qs["state"] = state;
+        qs["code_challenge"] = codeChallenge;
+        qs["code_challenge_method"] = "S256";
+
+        return $"https://twitter.com/i/oauth2/authorize?{qs}";
+    }
+
+    private async Task<OAuthTokenResponse> ExchangeLinkedInCodeAsync(string code, CancellationToken ct)
+    {
+        var li = linkedInOptions.Value;
+        var parameters = new Dictionary<string, string>
+        {
+            ["grant_type"] = "authorization_code",
+            ["code"] = code,
+            ["redirect_uri"] = li.RedirectUri,
+            ["client_id"] = li.ClientId,
+            ["client_secret"] = li.ClientSecret
+        };
+
+        var client = httpClientFactory.CreateClient();
+        var response = await client.PostAsync(
+            "https://www.linkedin.com/oauth/v2/accessToken",
+            new FormUrlEncodedContent(parameters), ct);
+        response.EnsureSuccessStatusCode();
+
+        var json = await response.Content.ReadAsStringAsync(ct);
+        var data = JsonSerializer.Deserialize<JsonElement>(json);
+
+        return new OAuthTokenResponse(
+            AccessToken: data.GetProperty("access_token").GetString()!,
+            RefreshToken: data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
+            ExpiresIn: data.GetProperty("expires_in").GetInt32(),
+            RefreshTokenExpiresIn: data.TryGetProperty("refresh_token_expires_in", out var rtExp)
+                ? rtExp.GetInt32() : null,
+            Scopes: "openid profile w_member_social");
+    }
+
+    private async Task<OAuthTokenResponse> ExchangeTwitterCodeAsync(
+        string code, string codeVerifier, CancellationToken ct)
+    {
+        var tw = twitterOptions.Value;
+        var parameters = new Dictionary<string, string>
+        {
+            ["grant_type"] = "authorization_code",
+            ["code"] = code,
+            ["redirect_uri"] = tw.RedirectUri,
+            ["client_id"] = tw.ClientId,
+            ["code_verifier"] = codeVerifier
+        };
+
+        var client = httpClientFactory.CreateClient();
+        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{tw.ClientId}:{tw.ClientSecret}"));
+        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.twitter.com/2/oauth2/token")
+        {
+            Content = new FormUrlEncodedContent(parameters)
+        };
+        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
+
+        var response = await client.SendAsync(request, ct);
+        response.EnsureSuccessStatusCode();
+
+        var json = await response.Content.ReadAsStringAsync(ct);
+        var data = JsonSerializer.Deserialize<JsonElement>(json);
+
+        return new OAuthTokenResponse(
+            AccessToken: data.GetProperty("access_token").GetString()!,
+            RefreshToken: data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
+            ExpiresIn: data.GetProperty("expires_in").GetInt32(),
+            RefreshTokenExpiresIn: null,
+            Scopes: "tweet.read tweet.write users.read media.write offline.access");
+    }
+
+    private static string Base64UrlEncode(byte[] input) =>
+        Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
+
+    private static void CleanExpiredStates()
+    {
+        foreach (var kvp in StateStore)
+        {
+            if (kvp.Value.IsExpired)
+                StateStore.TryRemove(kvp.Key, out _);
+        }
+    }
+
+    private sealed record OAuthStateEntry(Platform Platform, string? CodeVerifier = null)
+    {
+        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
+        public bool IsExpired => DateTimeOffset.UtcNow - CreatedAt > StateTtl;
+    }
+
+    private sealed record OAuthTokenResponse(
+        string AccessToken,
+        string? RefreshToken,
+        int ExpiresIn,
+        int? RefreshTokenExpiresIn,
+        string Scopes);
+}
diff --git a/src/PBA.Infrastructure/Security/TokenEncryptor.cs b/src/PBA.Infrastructure/Security/TokenEncryptor.cs
new file mode 100644
index 0000000..5d6cbd3
--- /dev/null
+++ b/src/PBA.Infrastructure/Security/TokenEncryptor.cs
@@ -0,0 +1,57 @@
+using System.Security.Cryptography;
+using System.Text;
+using Microsoft.Extensions.Options;
+using PBA.Application.Common.Interfaces;
+using PBA.Infrastructure.Configuration;
+
+namespace PBA.Infrastructure.Security;
+
+public sealed class TokenEncryptor : ITokenEncryptor
+{
+    private const int NonceSize = 12;
+    private const int TagSize = 16;
+
+    private readonly byte[] _key;
+
+    public TokenEncryptor(IOptions<EncryptionOptions> options)
+    {
+        _key = Convert.FromBase64String(options.Value.Key);
+    }
+
+    public string Encrypt(string plaintext)
+    {
+        ArgumentNullException.ThrowIfNull(plaintext);
+
+        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
+        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
+        var ciphertext = new byte[plaintextBytes.Length];
+        var tag = new byte[TagSize];
+
+        using var aes = new AesGcm(_key, TagSize);
+        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
+
+        var result = new byte[NonceSize + ciphertext.Length + TagSize];
+        nonce.CopyTo(result, 0);
+        ciphertext.CopyTo(result, NonceSize);
+        tag.CopyTo(result, NonceSize + ciphertext.Length);
+
+        return Convert.ToBase64String(result);
+    }
+
+    public string Decrypt(string ciphertext)
+    {
+        ArgumentNullException.ThrowIfNull(ciphertext);
+
+        var encryptedBytes = Convert.FromBase64String(ciphertext);
+
+        var nonce = encryptedBytes[..NonceSize];
+        var tag = encryptedBytes[^TagSize..];
+        var ciphertextBytes = encryptedBytes[NonceSize..^TagSize];
+        var plaintext = new byte[ciphertextBytes.Length];
+
+        using var aes = new AesGcm(_key, TagSize);
+        aes.Decrypt(nonce, ciphertextBytes, tag, plaintext);
+
+        return Encoding.UTF8.GetString(plaintext);
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Security/OAuthServiceTests.cs b/tests/PBA.Infrastructure.Tests/Security/OAuthServiceTests.cs
new file mode 100644
index 0000000..e825ef5
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Security/OAuthServiceTests.cs
@@ -0,0 +1,247 @@
+using System.Net;
+using System.Security.Cryptography;
+using System.Text.Json;
+using System.Web;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using Moq;
+using Moq.Protected;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+using PBA.Infrastructure.Data;
+using PBA.Infrastructure.Security;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Security;
+
+public class OAuthServiceTests : IDisposable
+{
+    private readonly ApplicationDbContext _dbContext;
+    private readonly Mock<ITokenEncryptor> _encryptor = new();
+    private readonly Mock<HttpMessageHandler> _httpHandler = new();
+    private readonly IHttpClientFactory _httpClientFactory;
+    private readonly Mock<ILogger<OAuthService>> _logger = new();
+
+    private readonly LinkedInOptions _linkedInOptions = new()
+    {
+        Enabled = true,
+        ClientId = "linkedin-client-id",
+        ClientSecret = "linkedin-client-secret",
+        RedirectUri = "https://localhost:5001/api/auth/linkedin/callback"
+    };
+
+    private readonly TwitterOptions _twitterOptions = new()
+    {
+        Enabled = true,
+        ClientId = "twitter-client-id",
+        ClientSecret = "twitter-client-secret",
+        RedirectUri = "https://localhost:5001/api/auth/twitter/callback"
+    };
+
+    public OAuthServiceTests()
+    {
+        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
+            .UseInMemoryDatabase(Guid.NewGuid().ToString())
+            .Options;
+        _dbContext = new ApplicationDbContext(options);
+
+        _encryptor.Setup(e => e.Encrypt(It.IsAny<string>()))
+            .Returns((string s) => $"encrypted:{s}");
+        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>()))
+            .Returns((string s) => s.Replace("encrypted:", ""));
+
+        var httpClient = new HttpClient(_httpHandler.Object);
+        var factoryMock = new Mock<IHttpClientFactory>();
+        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
+        _httpClientFactory = factoryMock.Object;
+    }
+
+    private OAuthService CreateService() => new(
+        _httpClientFactory,
+        _encryptor.Object,
+        _dbContext,
+        Options.Create(_linkedInOptions),
+        Options.Create(_twitterOptions),
+        _logger.Object);
+
+    private void SetupHttpResponse(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
+    {
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .ReturnsAsync(new HttpResponseMessage(status)
+            {
+                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
+            });
+    }
+
+    [Fact]
+    public async Task GetAuthorizationUrl_LinkedIn_ReturnsCorrectUrlWithScopes()
+    {
+        var service = CreateService();
+        var url = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CancellationToken.None);
+
+        Assert.StartsWith("https://www.linkedin.com/oauth/v2/authorization", url);
+        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
+        Assert.Equal("openid profile w_member_social", query["scope"]);
+        Assert.Equal(_linkedInOptions.ClientId, query["client_id"]);
+        Assert.NotNull(query["redirect_uri"]);
+        Assert.NotNull(query["state"]);
+    }
+
+    [Fact]
+    public async Task GetAuthorizationUrl_Twitter_IncludesPKCECodeChallenge()
+    {
+        var service = CreateService();
+        var url = await service.GetAuthorizationUrlAsync(Platform.Twitter, CancellationToken.None);
+
+        Assert.StartsWith("https://twitter.com/i/oauth2/authorize", url);
+        Assert.Contains("code_challenge=", url);
+        Assert.Contains("code_challenge_method=S256", url);
+    }
+
+    [Fact]
+    public async Task GetAuthorizationUrl_IncludesStateParameter()
+    {
+        var service = CreateService();
+        var url = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CancellationToken.None);
+
+        var uri = new Uri(url);
+        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
+        var state = query["state"];
+
+        Assert.NotNull(state);
+        Assert.True(state!.Length >= 32);
+    }
+
+    [Fact]
+    public async Task ExchangeCodeAsync_LinkedIn_StoresEncryptedTokens()
+    {
+        var service = CreateService();
+        var authUrl = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CancellationToken.None);
+        var state = System.Web.HttpUtility.ParseQueryString(new Uri(authUrl).Query)["state"]!;
+
+        SetupHttpResponse(JsonSerializer.Serialize(new
+        {
+            access_token = "li-access-token",
+            refresh_token = "li-refresh-token",
+            expires_in = 5184000,
+            refresh_token_expires_in = 31536000
+        }));
+
+        var credential = await service.ExchangeCodeAsync(Platform.LinkedIn, "auth-code", state, CancellationToken.None);
+
+        Assert.Equal(Platform.LinkedIn, credential.Platform);
+        Assert.True(credential.IsActive);
+        _encryptor.Verify(e => e.Encrypt("li-access-token"), Times.Once);
+        _encryptor.Verify(e => e.Encrypt("li-refresh-token"), Times.Once);
+
+        var saved = await _dbContext.PlatformCredentials.FirstOrDefaultAsync(c => c.Platform == Platform.LinkedIn);
+        Assert.NotNull(saved);
+    }
+
+    [Fact]
+    public async Task ExchangeCodeAsync_Twitter_UsesCodeVerifierForPKCE()
+    {
+        var service = CreateService();
+        var authUrl = await service.GetAuthorizationUrlAsync(Platform.Twitter, CancellationToken.None);
+        var state = HttpUtility.ParseQueryString(new Uri(authUrl).Query)["state"]!;
+
+        string? capturedBody = null;
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
+            {
+                capturedBody = await req.Content!.ReadAsStringAsync();
+                return new HttpResponseMessage(HttpStatusCode.OK)
+                {
+                    Content = new StringContent(JsonSerializer.Serialize(new
+                    {
+                        access_token = "tw-access-token",
+                        refresh_token = "tw-refresh-token",
+                        expires_in = 7200
+                    }), System.Text.Encoding.UTF8, "application/json")
+                };
+            });
+
+        await service.ExchangeCodeAsync(Platform.Twitter, "auth-code", state, CancellationToken.None);
+
+        Assert.NotNull(capturedBody);
+        Assert.Contains("code_verifier=", capturedBody);
+    }
+
+    [Fact]
+    public async Task ExchangeCodeAsync_InvalidState_ThrowsInvalidOperationException()
+    {
+        var service = CreateService();
+
+        await Assert.ThrowsAsync<InvalidOperationException>(() =>
+            service.ExchangeCodeAsync(Platform.LinkedIn, "code", "invalid-state", CancellationToken.None));
+    }
+
+    [Fact]
+    public async Task RefreshTokenAsync_LinkedIn_UpdatesStoredTokens()
+    {
+        var service = CreateService();
+        var credential = new Domain.Entities.PlatformCredential
+        {
+            Platform = Platform.LinkedIn,
+            EncryptedAccessToken = "encrypted:old-access",
+            EncryptedRefreshToken = "encrypted:old-refresh",
+            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5),
+            IsActive = true
+        };
+        _dbContext.PlatformCredentials.Add(credential);
+        await _dbContext.SaveChangesAsync();
+
+        SetupHttpResponse(JsonSerializer.Serialize(new
+        {
+            access_token = "new-access-token",
+            refresh_token = "new-refresh-token",
+            expires_in = 5184000
+        }));
+
+        var newToken = await service.RefreshTokenAsync(credential, CancellationToken.None);
+
+        Assert.Equal("new-access-token", newToken);
+        _encryptor.Verify(e => e.Encrypt("new-access-token"), Times.Once);
+    }
+
+    [Fact]
+    public async Task RefreshTokenAsync_ExpiredRefreshToken_ReturnsEmptyAndDeactivates()
+    {
+        var service = CreateService();
+        var credential = new Domain.Entities.PlatformCredential
+        {
+            Platform = Platform.LinkedIn,
+            EncryptedAccessToken = "encrypted:old-access",
+            EncryptedRefreshToken = "encrypted:old-refresh",
+            IsActive = true
+        };
+        _dbContext.PlatformCredentials.Add(credential);
+        await _dbContext.SaveChangesAsync();
+
+        SetupHttpResponse("{\"error\":\"invalid_grant\"}", HttpStatusCode.BadRequest);
+
+        var result = await service.RefreshTokenAsync(credential, CancellationToken.None);
+
+        Assert.Equal(string.Empty, result);
+        Assert.False(credential.IsActive);
+    }
+
+    [Fact]
+    public async Task GetAuthorizationUrl_UnsupportedPlatform_ThrowsNotSupportedException()
+    {
+        var service = CreateService();
+
+        await Assert.ThrowsAsync<NotSupportedException>(() =>
+            service.GetAuthorizationUrlAsync(Platform.Blog, CancellationToken.None));
+    }
+
+    public void Dispose() => _dbContext.Dispose();
+}
diff --git a/tests/PBA.Infrastructure.Tests/Security/TokenEncryptorTests.cs b/tests/PBA.Infrastructure.Tests/Security/TokenEncryptorTests.cs
new file mode 100644
index 0000000..1e1412a
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Security/TokenEncryptorTests.cs
@@ -0,0 +1,95 @@
+using System.Security.Cryptography;
+using Microsoft.Extensions.Options;
+using PBA.Infrastructure.Configuration;
+using PBA.Infrastructure.Security;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Security;
+
+public class TokenEncryptorTests
+{
+    private static string GenerateKey() =>
+        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
+
+    private static TokenEncryptor CreateEncryptor(string? key = null) =>
+        new(Options.Create(new EncryptionOptions { Key = key ?? GenerateKey() }));
+
+    [Fact]
+    public void Encrypt_ThenDecrypt_ReturnsOriginalValue()
+    {
+        var encryptor = CreateEncryptor();
+        var plaintext = "my-secret-token";
+
+        var ciphertext = encryptor.Encrypt(plaintext);
+        var decrypted = encryptor.Decrypt(ciphertext);
+
+        Assert.Equal(plaintext, decrypted);
+    }
+
+    [Fact]
+    public void Encrypt_ProducesDifferentCiphertextEachTime()
+    {
+        var encryptor = CreateEncryptor();
+        var plaintext = "same-token";
+
+        var cipher1 = encryptor.Encrypt(plaintext);
+        var cipher2 = encryptor.Encrypt(plaintext);
+
+        Assert.NotEqual(cipher1, cipher2);
+    }
+
+    [Fact]
+    public void Decrypt_WithWrongKey_ThrowsCryptographicException()
+    {
+        var encryptor1 = CreateEncryptor();
+        var encryptor2 = CreateEncryptor();
+
+        var ciphertext = encryptor1.Encrypt("secret");
+
+        Assert.ThrowsAny<CryptographicException>(() => encryptor2.Decrypt(ciphertext));
+    }
+
+    [Fact]
+    public void Encrypt_NullInput_ThrowsArgumentNullException()
+    {
+        var encryptor = CreateEncryptor();
+
+        Assert.Throws<ArgumentNullException>(() => encryptor.Encrypt(null!));
+    }
+
+    [Fact]
+    public void Decrypt_CorruptedCiphertext_ThrowsCryptographicException()
+    {
+        var encryptor = CreateEncryptor();
+        var ciphertext = encryptor.Encrypt("secret");
+
+        var bytes = Convert.FromBase64String(ciphertext);
+        bytes[bytes.Length / 2] ^= 0xFF;
+        var corrupted = Convert.ToBase64String(bytes);
+
+        Assert.ThrowsAny<CryptographicException>(() => encryptor.Decrypt(corrupted));
+    }
+
+    [Fact]
+    public void Encrypt_ThenDecrypt_HandlesEmptyString()
+    {
+        var encryptor = CreateEncryptor();
+
+        var ciphertext = encryptor.Encrypt("");
+        var decrypted = encryptor.Decrypt(ciphertext);
+
+        Assert.Equal("", decrypted);
+    }
+
+    [Fact]
+    public void Encrypt_ThenDecrypt_HandlesLongTokens()
+    {
+        var encryptor = CreateEncryptor();
+        var longToken = new string('x', 4096);
+
+        var ciphertext = encryptor.Encrypt(longToken);
+        var decrypted = encryptor.Decrypt(ciphertext);
+
+        Assert.Equal(longToken, decrypted);
+    }
+}
