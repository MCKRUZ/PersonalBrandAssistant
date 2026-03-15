diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/OAuthManager.cs b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/OAuthManager.cs
new file mode 100644
index 0000000..13cf54c
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/OAuthManager.cs
@@ -0,0 +1,552 @@
+using System.Net.Http.Json;
+using System.Security.Cryptography;
+using System.Text;
+using System.Text.Json;
+using MediatR;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Configuration;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices;
+
+public sealed class OAuthManager : IOAuthManager
+{
+    private static readonly TimeSpan StateExpiry = TimeSpan.FromMinutes(10);
+
+    private readonly IApplicationDbContext _dbContext;
+    private readonly IEncryptionService _encryption;
+    private readonly PlatformIntegrationOptions _options;
+    private readonly IHttpClientFactory _httpClientFactory;
+    private readonly IConfiguration _configuration;
+    private readonly ILogger<OAuthManager> _logger;
+
+    public OAuthManager(
+        IApplicationDbContext dbContext,
+        IEncryptionService encryption,
+        IOptions<PlatformIntegrationOptions> options,
+        IHttpClientFactory httpClientFactory,
+        IConfiguration configuration,
+        ILogger<OAuthManager> logger)
+    {
+        _dbContext = dbContext;
+        _encryption = encryption;
+        _options = options.Value;
+        _httpClientFactory = httpClientFactory;
+        _configuration = configuration;
+        _logger = logger;
+    }
+
+    public async Task<Result<OAuthAuthorizationUrl>> GenerateAuthUrlAsync(
+        PlatformType platform,
+        CancellationToken ct)
+    {
+        var state = GenerateRandomState();
+        string? codeVerifier = null;
+        string? codeChallenge = null;
+
+        if (platform == PlatformType.TwitterX)
+        {
+            codeVerifier = GenerateCodeVerifier();
+            codeChallenge = ComputeCodeChallenge(codeVerifier);
+        }
+
+        var oauthState = new OAuthState
+        {
+            State = state,
+            Platform = platform,
+            CodeVerifier = codeVerifier,
+            CreatedAt = DateTimeOffset.UtcNow,
+            ExpiresAt = DateTimeOffset.UtcNow.Add(StateExpiry),
+        };
+
+        _dbContext.OAuthStates.Add(oauthState);
+        await _dbContext.SaveChangesAsync(ct);
+
+        var url = BuildAuthUrl(platform, state, codeChallenge);
+
+        _logger.LogInformation("Generated OAuth URL for {Platform}", platform);
+        return Result.Success(new OAuthAuthorizationUrl(url, state));
+    }
+
+    public async Task<Result<OAuthTokens>> ExchangeCodeAsync(
+        PlatformType platform,
+        string code,
+        string state,
+        string? codeVerifier,
+        CancellationToken ct)
+    {
+        var oauthState = await _dbContext.OAuthStates
+            .FirstOrDefaultAsync(s => s.State == state && s.Platform == platform, ct);
+
+        if (oauthState is null)
+        {
+            _logger.LogWarning("OAuth state not found for {Platform}, possible CSRF", platform);
+            return Result.Failure<OAuthTokens>(ErrorCode.ValidationFailed, "Invalid OAuth state");
+        }
+
+        if (oauthState.ExpiresAt < DateTimeOffset.UtcNow)
+        {
+            _dbContext.OAuthStates.Remove(oauthState);
+            await _dbContext.SaveChangesAsync(ct);
+            return Result.Failure<OAuthTokens>(ErrorCode.ValidationFailed, "OAuth state expired");
+        }
+
+        // Use stored code_verifier for Twitter PKCE
+        var verifier = oauthState.CodeVerifier ?? codeVerifier;
+
+        // Delete state entry (single-use)
+        _dbContext.OAuthStates.Remove(oauthState);
+        await _dbContext.SaveChangesAsync(ct);
+
+        try
+        {
+            var tokenResponse = await ExchangeCodeForTokensAsync(platform, code, verifier, ct);
+            if (tokenResponse is null)
+            {
+                return Result.Failure<OAuthTokens>(ErrorCode.InternalError, "Token exchange failed");
+            }
+
+            var platformEntity = await _dbContext.Platforms
+                .FirstOrDefaultAsync(p => p.Type == platform, ct);
+
+            if (platformEntity is null)
+            {
+                return Result.NotFound<OAuthTokens>($"Platform '{platform}' not found");
+            }
+
+            platformEntity.EncryptedAccessToken = _encryption.Encrypt(tokenResponse.AccessToken);
+            platformEntity.EncryptedRefreshToken = tokenResponse.RefreshToken != null
+                ? _encryption.Encrypt(tokenResponse.RefreshToken)
+                : null;
+            platformEntity.TokenExpiresAt = tokenResponse.ExpiresAt;
+            platformEntity.IsConnected = true;
+            platformEntity.GrantedScopes = tokenResponse.GrantedScopes?.ToArray();
+
+            await _dbContext.SaveChangesAsync(ct);
+
+            _logger.LogInformation("OAuth token exchange completed for {Platform}", platform);
+            return Result.Success(tokenResponse);
+        }
+        catch (HttpRequestException ex)
+        {
+            _logger.LogError(ex, "HTTP error during token exchange for {Platform}", platform);
+            return Result.Failure<OAuthTokens>(ErrorCode.InternalError, $"Token exchange failed: {ex.Message}");
+        }
+    }
+
+    public async Task<Result<OAuthTokens>> RefreshTokenAsync(
+        PlatformType platform,
+        CancellationToken ct)
+    {
+        var platformEntity = await _dbContext.Platforms
+            .FirstOrDefaultAsync(p => p.Type == platform, ct);
+
+        if (platformEntity is null)
+        {
+            return Result.NotFound<OAuthTokens>($"Platform '{platform}' not found");
+        }
+
+        if (platformEntity.EncryptedRefreshToken is null)
+        {
+            return Result.Failure<OAuthTokens>(ErrorCode.ValidationFailed, "No refresh token available");
+        }
+
+        var refreshToken = _encryption.Decrypt(platformEntity.EncryptedRefreshToken);
+
+        try
+        {
+            var tokenResponse = await RefreshTokenWithPlatformAsync(platform, refreshToken, ct);
+            if (tokenResponse is null)
+            {
+                // Check for invalid_grant (revoked token)
+                platformEntity.IsConnected = false;
+                platformEntity.EncryptedAccessToken = null;
+                platformEntity.EncryptedRefreshToken = null;
+                platformEntity.GrantedScopes = null;
+                await _dbContext.SaveChangesAsync(ct);
+
+                _logger.LogWarning("Token refresh failed for {Platform}, marking disconnected", platform);
+                return Result.Failure<OAuthTokens>(ErrorCode.InternalError, "Token refresh failed, platform disconnected");
+            }
+
+            platformEntity.EncryptedAccessToken = _encryption.Encrypt(tokenResponse.AccessToken);
+            if (tokenResponse.RefreshToken != null)
+            {
+                platformEntity.EncryptedRefreshToken = _encryption.Encrypt(tokenResponse.RefreshToken);
+            }
+
+            platformEntity.TokenExpiresAt = tokenResponse.ExpiresAt;
+            await _dbContext.SaveChangesAsync(ct);
+
+            _logger.LogInformation("Token refreshed for {Platform}", platform);
+            return Result.Success(tokenResponse);
+        }
+        catch (HttpRequestException ex)
+        {
+            _logger.LogError(ex, "HTTP error during token refresh for {Platform}", platform);
+            return Result.Failure<OAuthTokens>(ErrorCode.InternalError, $"Token refresh failed: {ex.Message}");
+        }
+    }
+
+    public async Task<Result<Unit>> RevokeTokenAsync(
+        PlatformType platform,
+        CancellationToken ct)
+    {
+        var platformEntity = await _dbContext.Platforms
+            .FirstOrDefaultAsync(p => p.Type == platform, ct);
+
+        if (platformEntity is null)
+        {
+            return Result.NotFound<Unit>($"Platform '{platform}' not found");
+        }
+
+        if (platformEntity.EncryptedAccessToken != null)
+        {
+            var accessToken = _encryption.Decrypt(platformEntity.EncryptedAccessToken);
+            try
+            {
+                await RevokeTokenWithPlatformAsync(platform, accessToken, ct);
+            }
+            catch (HttpRequestException ex)
+            {
+                _logger.LogWarning(ex, "Failed to revoke token at {Platform} endpoint, clearing locally", platform);
+            }
+        }
+
+        platformEntity.EncryptedAccessToken = null;
+        platformEntity.EncryptedRefreshToken = null;
+        platformEntity.TokenExpiresAt = null;
+        platformEntity.IsConnected = false;
+        platformEntity.GrantedScopes = null;
+        await _dbContext.SaveChangesAsync(ct);
+
+        _logger.LogInformation("Token revoked for {Platform}", platform);
+        return Result.Success(Unit.Value);
+    }
+
+    private string BuildAuthUrl(PlatformType platform, string state, string? codeChallenge)
+    {
+        var platformConfig = GetPlatformConfig(platform);
+
+        return platform switch
+        {
+            PlatformType.TwitterX => BuildTwitterAuthUrl(state, codeChallenge!, platformConfig.CallbackUrl),
+            PlatformType.LinkedIn => BuildLinkedInAuthUrl(state, platformConfig.CallbackUrl),
+            PlatformType.Instagram => BuildInstagramAuthUrl(state, platformConfig.CallbackUrl),
+            PlatformType.YouTube => BuildYouTubeAuthUrl(state, platformConfig.CallbackUrl),
+            _ => throw new ArgumentOutOfRangeException(nameof(platform)),
+        };
+    }
+
+    private string BuildTwitterAuthUrl(string state, string codeChallenge, string callbackUrl)
+    {
+        var clientId = _configuration["PlatformIntegrations:Twitter:ClientId"];
+        return $"https://twitter.com/i/oauth2/authorize?response_type=code&client_id={clientId}" +
+               $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
+               $"&scope=tweet.read%20tweet.write%20users.read%20offline.access" +
+               $"&state={state}&code_challenge={codeChallenge}&code_challenge_method=S256";
+    }
+
+    private string BuildLinkedInAuthUrl(string state, string callbackUrl)
+    {
+        var clientId = _configuration["PlatformIntegrations:LinkedIn:ClientId"];
+        return $"https://www.linkedin.com/oauth/v2/authorization?response_type=code&client_id={clientId}" +
+               $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
+               $"&scope=w_member_social%20r_liteprofile&state={state}";
+    }
+
+    private string BuildInstagramAuthUrl(string state, string callbackUrl)
+    {
+        var appId = _configuration["PlatformIntegrations:Instagram:AppId"];
+        return $"https://www.facebook.com/v19.0/dialog/oauth?client_id={appId}" +
+               $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
+               $"&scope=instagram_basic,instagram_content_publish,pages_show_list&state={state}";
+    }
+
+    private string BuildYouTubeAuthUrl(string state, string callbackUrl)
+    {
+        var clientId = _configuration["PlatformIntegrations:YouTube:ClientId"];
+        return $"https://accounts.google.com/o/oauth2/v2/auth?response_type=code&client_id={clientId}" +
+               $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
+               $"&scope=https://www.googleapis.com/auth/youtube%20https://www.googleapis.com/auth/youtube.upload" +
+               $"&state={state}&access_type=offline&prompt=consent";
+    }
+
+    private async Task<OAuthTokens?> ExchangeCodeForTokensAsync(
+        PlatformType platform,
+        string code,
+        string? codeVerifier,
+        CancellationToken ct)
+    {
+        var client = _httpClientFactory.CreateClient("OAuth");
+
+        return platform switch
+        {
+            PlatformType.TwitterX => await ExchangeTwitterCodeAsync(client, code, codeVerifier, ct),
+            PlatformType.LinkedIn => await ExchangeLinkedInCodeAsync(client, code, ct),
+            PlatformType.Instagram => await ExchangeInstagramCodeAsync(client, code, ct),
+            PlatformType.YouTube => await ExchangeYouTubeCodeAsync(client, code, ct),
+            _ => null,
+        };
+    }
+
+    private async Task<OAuthTokens?> ExchangeTwitterCodeAsync(
+        HttpClient client, string code, string? codeVerifier, CancellationToken ct)
+    {
+        var clientId = _configuration["PlatformIntegrations:Twitter:ClientId"]!;
+        var clientSecret = _configuration["PlatformIntegrations:Twitter:ClientSecret"]!;
+        var callbackUrl = _options.Twitter.CallbackUrl;
+
+        var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
+        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.com/2/oauth2/token");
+        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);
+        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
+        {
+            ["grant_type"] = "authorization_code",
+            ["code"] = code,
+            ["redirect_uri"] = callbackUrl,
+            ["code_verifier"] = codeVerifier ?? string.Empty,
+        });
+
+        return await SendTokenRequestAsync(client, request, ct);
+    }
+
+    private async Task<OAuthTokens?> ExchangeLinkedInCodeAsync(
+        HttpClient client, string code, CancellationToken ct)
+    {
+        var request = new HttpRequestMessage(HttpMethod.Post, "https://www.linkedin.com/oauth/v2/accessToken");
+        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
+        {
+            ["grant_type"] = "authorization_code",
+            ["code"] = code,
+            ["redirect_uri"] = _options.LinkedIn.CallbackUrl,
+            ["client_id"] = _configuration["PlatformIntegrations:LinkedIn:ClientId"]!,
+            ["client_secret"] = _configuration["PlatformIntegrations:LinkedIn:ClientSecret"]!,
+        });
+
+        return await SendTokenRequestAsync(client, request, ct);
+    }
+
+    private async Task<OAuthTokens?> ExchangeInstagramCodeAsync(
+        HttpClient client, string code, CancellationToken ct)
+    {
+        // Step 1: Exchange for short-lived token
+        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.instagram.com/oauth/access_token");
+        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
+        {
+            ["client_id"] = _configuration["PlatformIntegrations:Instagram:AppId"]!,
+            ["client_secret"] = _configuration["PlatformIntegrations:Instagram:AppSecret"]!,
+            ["grant_type"] = "authorization_code",
+            ["redirect_uri"] = _options.Instagram.CallbackUrl,
+            ["code"] = code,
+        });
+
+        var shortLived = await SendTokenRequestAsync(client, request, ct);
+        if (shortLived is null) return null;
+
+        // Step 2: Exchange for long-lived token
+        var appSecret = _configuration["PlatformIntegrations:Instagram:AppSecret"]!;
+        var longLivedUrl = $"https://graph.instagram.com/access_token?grant_type=ig_exchange_token" +
+                           $"&client_secret={appSecret}&access_token={shortLived.AccessToken}";
+
+        var longRequest = new HttpRequestMessage(HttpMethod.Get, longLivedUrl);
+        return await SendTokenRequestAsync(client, longRequest, ct) ?? shortLived;
+    }
+
+    private async Task<OAuthTokens?> ExchangeYouTubeCodeAsync(
+        HttpClient client, string code, CancellationToken ct)
+    {
+        var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token");
+        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
+        {
+            ["grant_type"] = "authorization_code",
+            ["code"] = code,
+            ["redirect_uri"] = _options.YouTube.CallbackUrl,
+            ["client_id"] = _configuration["PlatformIntegrations:YouTube:ClientId"]!,
+            ["client_secret"] = _configuration["PlatformIntegrations:YouTube:ClientSecret"]!,
+        });
+
+        return await SendTokenRequestAsync(client, request, ct);
+    }
+
+    private async Task<OAuthTokens?> RefreshTokenWithPlatformAsync(
+        PlatformType platform, string refreshToken, CancellationToken ct)
+    {
+        var client = _httpClientFactory.CreateClient("OAuth");
+
+        return platform switch
+        {
+            PlatformType.TwitterX => await RefreshTwitterTokenAsync(client, refreshToken, ct),
+            PlatformType.LinkedIn => await RefreshLinkedInTokenAsync(client, refreshToken, ct),
+            PlatformType.Instagram => await RefreshInstagramTokenAsync(client, refreshToken, ct),
+            PlatformType.YouTube => await RefreshYouTubeTokenAsync(client, refreshToken, ct),
+            _ => null,
+        };
+    }
+
+    private async Task<OAuthTokens?> RefreshTwitterTokenAsync(
+        HttpClient client, string refreshToken, CancellationToken ct)
+    {
+        var clientId = _configuration["PlatformIntegrations:Twitter:ClientId"]!;
+        var clientSecret = _configuration["PlatformIntegrations:Twitter:ClientSecret"]!;
+        var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
+
+        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.com/2/oauth2/token");
+        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);
+        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
+        {
+            ["grant_type"] = "refresh_token",
+            ["refresh_token"] = refreshToken,
+        });
+
+        return await SendTokenRequestAsync(client, request, ct);
+    }
+
+    private async Task<OAuthTokens?> RefreshLinkedInTokenAsync(
+        HttpClient client, string refreshToken, CancellationToken ct)
+    {
+        var request = new HttpRequestMessage(HttpMethod.Post, "https://www.linkedin.com/oauth/v2/accessToken");
+        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
+        {
+            ["grant_type"] = "refresh_token",
+            ["refresh_token"] = refreshToken,
+            ["client_id"] = _configuration["PlatformIntegrations:LinkedIn:ClientId"]!,
+            ["client_secret"] = _configuration["PlatformIntegrations:LinkedIn:ClientSecret"]!,
+        });
+
+        return await SendTokenRequestAsync(client, request, ct);
+    }
+
+    private async Task<OAuthTokens?> RefreshInstagramTokenAsync(
+        HttpClient client, string currentAccessToken, CancellationToken ct)
+    {
+        var url = $"https://graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token&access_token={currentAccessToken}";
+        var request = new HttpRequestMessage(HttpMethod.Get, url);
+        return await SendTokenRequestAsync(client, request, ct);
+    }
+
+    private async Task<OAuthTokens?> RefreshYouTubeTokenAsync(
+        HttpClient client, string refreshToken, CancellationToken ct)
+    {
+        var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token");
+        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
+        {
+            ["grant_type"] = "refresh_token",
+            ["refresh_token"] = refreshToken,
+            ["client_id"] = _configuration["PlatformIntegrations:YouTube:ClientId"]!,
+            ["client_secret"] = _configuration["PlatformIntegrations:YouTube:ClientSecret"]!,
+        });
+
+        return await SendTokenRequestAsync(client, request, ct);
+    }
+
+    private async Task RevokeTokenWithPlatformAsync(
+        PlatformType platform, string accessToken, CancellationToken ct)
+    {
+        var client = _httpClientFactory.CreateClient("OAuth");
+
+        switch (platform)
+        {
+            case PlatformType.TwitterX:
+            {
+                var clientId = _configuration["PlatformIntegrations:Twitter:ClientId"]!;
+                var clientSecret = _configuration["PlatformIntegrations:Twitter:ClientSecret"]!;
+                var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
+                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.com/2/oauth2/revoke");
+                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);
+                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
+                {
+                    ["token"] = accessToken,
+                });
+                await client.SendAsync(request, ct);
+                break;
+            }
+            case PlatformType.LinkedIn:
+                // LinkedIn has no revocation API — just clear local tokens
+                break;
+            case PlatformType.Instagram:
+            {
+                // For simplicity, we revoke via Facebook Graph API
+                var request = new HttpRequestMessage(HttpMethod.Delete,
+                    $"https://graph.facebook.com/me/permissions?access_token={accessToken}");
+                await client.SendAsync(request, ct);
+                break;
+            }
+            case PlatformType.YouTube:
+            {
+                var request = new HttpRequestMessage(HttpMethod.Post,
+                    $"https://oauth2.googleapis.com/revoke?token={accessToken}");
+                request.Content = new StringContent(string.Empty);
+                await client.SendAsync(request, ct);
+                break;
+            }
+        }
+    }
+
+    private static async Task<OAuthTokens?> SendTokenRequestAsync(
+        HttpClient client, HttpRequestMessage request, CancellationToken ct)
+    {
+        var response = await client.SendAsync(request, ct);
+
+        if (!response.IsSuccessStatusCode)
+        {
+            return null;
+        }
+
+        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
+
+        var accessToken = json.TryGetProperty("access_token", out var at) ? at.GetString() : null;
+        if (accessToken is null) return null;
+
+        var refreshToken = json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
+        var expiresIn = json.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : (int?)null;
+        var scope = json.TryGetProperty("scope", out var sc) ? sc.GetString() : null;
+
+        var expiresAt = expiresIn.HasValue ? DateTimeOffset.UtcNow.AddSeconds(expiresIn.Value) : (DateTimeOffset?)null;
+        var scopes = scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList().AsReadOnly();
+
+        return new OAuthTokens(accessToken, refreshToken, expiresAt, scopes);
+    }
+
+    private PlatformOptions GetPlatformConfig(PlatformType platform) => platform switch
+    {
+        PlatformType.TwitterX => _options.Twitter,
+        PlatformType.LinkedIn => _options.LinkedIn,
+        PlatformType.Instagram => _options.Instagram,
+        PlatformType.YouTube => _options.YouTube,
+        _ => throw new ArgumentOutOfRangeException(nameof(platform)),
+    };
+
+    private static string GenerateRandomState()
+    {
+        var bytes = RandomNumberGenerator.GetBytes(32);
+        return Convert.ToBase64String(bytes)
+            .Replace("+", "-")
+            .Replace("/", "_")
+            .TrimEnd('=');
+    }
+
+    private static string GenerateCodeVerifier()
+    {
+        var bytes = RandomNumberGenerator.GetBytes(64);
+        return Convert.ToBase64String(bytes)
+            .Replace("+", "-")
+            .Replace("/", "_")
+            .TrimEnd('=');
+    }
+
+    private static string ComputeCodeChallenge(string codeVerifier)
+    {
+        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
+        return Convert.ToBase64String(bytes)
+            .Replace("+", "-")
+            .Replace("/", "_")
+            .TrimEnd('=');
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/OAuthManagerTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/OAuthManagerTests.cs
new file mode 100644
index 0000000..b10b2f9
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/OAuthManagerTests.cs
@@ -0,0 +1,350 @@
+using System.Net;
+using System.Text.Json;
+using MediatR;
+using Microsoft.Extensions.Configuration;
+using Microsoft.Extensions.Logging.Abstractions;
+using Microsoft.Extensions.Options;
+using Moq;
+using Moq.Protected;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services.PlatformServices;
+using PersonalBrandAssistant.Infrastructure.Tests.Helpers;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Platform;
+
+public class OAuthManagerTests
+{
+    private readonly Mock<IApplicationDbContext> _dbContext;
+    private readonly Mock<IEncryptionService> _encryption;
+    private readonly Mock<HttpMessageHandler> _httpHandler;
+    private readonly HttpClient _httpClient;
+    private readonly OAuthManager _sut;
+
+    private readonly PlatformIntegrationOptions _options = new()
+    {
+        Twitter = new PlatformOptions { CallbackUrl = "https://app.test/callback/twitter" },
+        LinkedIn = new PlatformOptions { CallbackUrl = "https://app.test/callback/linkedin" },
+        Instagram = new PlatformOptions { CallbackUrl = "https://app.test/callback/instagram" },
+        YouTube = new PlatformOptions { CallbackUrl = "https://app.test/callback/youtube" },
+    };
+
+    public OAuthManagerTests()
+    {
+        _dbContext = new Mock<IApplicationDbContext>();
+        _encryption = new Mock<IEncryptionService>();
+        _httpHandler = new Mock<HttpMessageHandler>();
+        _httpClient = new HttpClient(_httpHandler.Object);
+
+        var httpFactory = new Mock<IHttpClientFactory>();
+        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);
+
+        var config = new ConfigurationBuilder()
+            .AddInMemoryCollection(new Dictionary<string, string?>
+            {
+                ["PlatformIntegrations:Twitter:ClientId"] = "twitter-client-id",
+                ["PlatformIntegrations:Twitter:ClientSecret"] = "twitter-client-secret",
+                ["PlatformIntegrations:LinkedIn:ClientId"] = "linkedin-client-id",
+                ["PlatformIntegrations:LinkedIn:ClientSecret"] = "linkedin-client-secret",
+                ["PlatformIntegrations:Instagram:AppId"] = "instagram-app-id",
+                ["PlatformIntegrations:Instagram:AppSecret"] = "instagram-app-secret",
+                ["PlatformIntegrations:YouTube:ClientId"] = "youtube-client-id",
+                ["PlatformIntegrations:YouTube:ClientSecret"] = "youtube-client-secret",
+            })
+            .Build();
+
+        _encryption.Setup(e => e.Encrypt(It.IsAny<string>())).Returns<string>(s => System.Text.Encoding.UTF8.GetBytes(s));
+        _encryption.Setup(e => e.Decrypt(It.IsAny<byte[]>())).Returns<byte[]>(b => System.Text.Encoding.UTF8.GetString(b));
+
+        _sut = new OAuthManager(
+            _dbContext.Object,
+            _encryption.Object,
+            Options.Create(_options),
+            httpFactory.Object,
+            config,
+            NullLogger<OAuthManager>.Instance);
+    }
+
+    private void SetupOAuthStates(params OAuthState[] states)
+    {
+        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(states);
+        _dbContext.Setup(db => db.OAuthStates).Returns(mockSet.Object);
+    }
+
+    private void SetupPlatforms(params Domain.Entities.Platform[] platforms)
+    {
+        var mockSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(platforms);
+        _dbContext.Setup(db => db.Platforms).Returns(mockSet.Object);
+    }
+
+    private void SetupHttpResponse(HttpStatusCode status, object body)
+    {
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>(
+                "SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .ReturnsAsync(new HttpResponseMessage(status)
+            {
+                Content = new StringContent(JsonSerializer.Serialize(body)),
+            });
+    }
+
+    // --- GenerateAuthUrlAsync ---
+
+    [Fact]
+    public async Task GenerateAuthUrlAsync_CreatesOAuthStateInDb()
+    {
+        SetupOAuthStates();
+        OAuthState? capturedState = null;
+        _dbContext.Setup(db => db.OAuthStates.Add(It.IsAny<OAuthState>()))
+            .Callback<OAuthState>(s => capturedState = s);
+
+        var result = await _sut.GenerateAuthUrlAsync(PlatformType.TwitterX, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.NotNull(capturedState);
+        Assert.True(capturedState!.ExpiresAt > DateTimeOffset.UtcNow);
+        _dbContext.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task GenerateAuthUrlAsync_ReturnsDifferentStateValues()
+    {
+        SetupOAuthStates();
+        _dbContext.Setup(db => db.OAuthStates.Add(It.IsAny<OAuthState>()));
+
+        var result1 = await _sut.GenerateAuthUrlAsync(PlatformType.TwitterX, CancellationToken.None);
+        var result2 = await _sut.GenerateAuthUrlAsync(PlatformType.TwitterX, CancellationToken.None);
+
+        Assert.NotEqual(result1.Value!.State, result2.Value!.State);
+    }
+
+    [Fact]
+    public async Task GenerateAuthUrlAsync_IncludesPkceForTwitter()
+    {
+        SetupOAuthStates();
+        OAuthState? capturedState = null;
+        _dbContext.Setup(db => db.OAuthStates.Add(It.IsAny<OAuthState>()))
+            .Callback<OAuthState>(s => capturedState = s);
+
+        var result = await _sut.GenerateAuthUrlAsync(PlatformType.TwitterX, CancellationToken.None);
+
+        Assert.NotNull(capturedState?.CodeVerifier);
+        Assert.Contains("code_challenge=", result.Value!.Url);
+        Assert.Contains("code_challenge_method=S256", result.Value.Url);
+    }
+
+    [Theory]
+    [InlineData(PlatformType.TwitterX, "twitter.com/i/oauth2/authorize")]
+    [InlineData(PlatformType.LinkedIn, "linkedin.com/oauth/v2/authorization")]
+    [InlineData(PlatformType.Instagram, "facebook.com/v19.0/dialog/oauth")]
+    [InlineData(PlatformType.YouTube, "accounts.google.com/o/oauth2/v2/auth")]
+    public async Task GenerateAuthUrlAsync_ReturnsPlatformSpecificUrl(PlatformType platform, string expectedUrlFragment)
+    {
+        SetupOAuthStates();
+        _dbContext.Setup(db => db.OAuthStates.Add(It.IsAny<OAuthState>()));
+
+        var result = await _sut.GenerateAuthUrlAsync(platform, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Contains(expectedUrlFragment, result.Value!.Url);
+    }
+
+    [Fact]
+    public async Task GenerateAuthUrlAsync_YouTubeIncludesOfflineAccess()
+    {
+        SetupOAuthStates();
+        _dbContext.Setup(db => db.OAuthStates.Add(It.IsAny<OAuthState>()));
+
+        var result = await _sut.GenerateAuthUrlAsync(PlatformType.YouTube, CancellationToken.None);
+
+        Assert.Contains("access_type=offline", result.Value!.Url);
+    }
+
+    // --- ExchangeCodeAsync ---
+
+    [Fact]
+    public async Task ExchangeCodeAsync_RejectsInvalidState()
+    {
+        SetupOAuthStates(); // empty - no matching state
+        SetupPlatforms();
+
+        var result = await _sut.ExchangeCodeAsync(PlatformType.TwitterX, "code123", "bad-state", null, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+    }
+
+    [Fact]
+    public async Task ExchangeCodeAsync_RejectsExpiredState()
+    {
+        var state = new OAuthState
+        {
+            State = "valid-state",
+            Platform = PlatformType.TwitterX,
+            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-15),
+            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5),
+        };
+        SetupOAuthStates(state);
+        SetupPlatforms();
+
+        var result = await _sut.ExchangeCodeAsync(PlatformType.TwitterX, "code123", "valid-state", null, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+    }
+
+    [Fact]
+    public async Task ExchangeCodeAsync_StoresEncryptedTokens()
+    {
+        var state = new OAuthState
+        {
+            State = "valid-state",
+            Platform = PlatformType.LinkedIn,
+            CreatedAt = DateTimeOffset.UtcNow,
+            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
+        };
+        SetupOAuthStates(state);
+
+        var platform = new Domain.Entities.Platform
+        {
+            Type = PlatformType.LinkedIn,
+            DisplayName = "LinkedIn",
+        };
+        SetupPlatforms(platform);
+
+        SetupHttpResponse(HttpStatusCode.OK, new
+        {
+            access_token = "access-token-123",
+            refresh_token = "refresh-token-456",
+            expires_in = 3600,
+            scope = "w_member_social r_liteprofile",
+        });
+
+        var result = await _sut.ExchangeCodeAsync(PlatformType.LinkedIn, "auth-code", "valid-state", null, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.NotNull(platform.EncryptedAccessToken);
+        Assert.True(platform.IsConnected);
+        _encryption.Verify(e => e.Encrypt("access-token-123"), Times.Once);
+    }
+
+    [Fact]
+    public async Task ExchangeCodeAsync_StoresGrantedScopes()
+    {
+        var state = new OAuthState
+        {
+            State = "valid-state",
+            Platform = PlatformType.LinkedIn,
+            CreatedAt = DateTimeOffset.UtcNow,
+            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
+        };
+        SetupOAuthStates(state);
+
+        var platform = new Domain.Entities.Platform
+        {
+            Type = PlatformType.LinkedIn,
+            DisplayName = "LinkedIn",
+        };
+        SetupPlatforms(platform);
+
+        SetupHttpResponse(HttpStatusCode.OK, new
+        {
+            access_token = "token",
+            scope = "w_member_social r_liteprofile",
+            expires_in = 3600,
+        });
+
+        await _sut.ExchangeCodeAsync(PlatformType.LinkedIn, "code", "valid-state", null, CancellationToken.None);
+
+        Assert.NotNull(platform.GrantedScopes);
+        Assert.Contains("w_member_social", platform.GrantedScopes!);
+    }
+
+    // --- RefreshTokenAsync ---
+
+    [Fact]
+    public async Task RefreshTokenAsync_UpdatesTokens()
+    {
+        var platform = new Domain.Entities.Platform
+        {
+            Type = PlatformType.LinkedIn,
+            DisplayName = "LinkedIn",
+            IsConnected = true,
+            EncryptedRefreshToken = System.Text.Encoding.UTF8.GetBytes("old-refresh"),
+        };
+        SetupPlatforms(platform);
+
+        SetupHttpResponse(HttpStatusCode.OK, new
+        {
+            access_token = "new-access",
+            refresh_token = "new-refresh",
+            expires_in = 3600,
+        });
+
+        var result = await _sut.RefreshTokenAsync(PlatformType.LinkedIn, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        _encryption.Verify(e => e.Encrypt("new-access"), Times.Once);
+    }
+
+    [Fact]
+    public async Task RefreshTokenAsync_DisconnectsOnInvalidGrant()
+    {
+        var platform = new Domain.Entities.Platform
+        {
+            Type = PlatformType.YouTube,
+            DisplayName = "YouTube",
+            IsConnected = true,
+            EncryptedRefreshToken = System.Text.Encoding.UTF8.GetBytes("old-refresh"),
+        };
+        SetupPlatforms(platform);
+
+        SetupHttpResponse(HttpStatusCode.BadRequest, new
+        {
+            error = "invalid_grant",
+            error_description = "Token has been revoked",
+        });
+
+        var result = await _sut.RefreshTokenAsync(PlatformType.YouTube, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.False(platform.IsConnected);
+    }
+
+    // --- RevokeTokenAsync ---
+
+    [Fact]
+    public async Task RevokeTokenAsync_ClearsTokensAndDisconnects()
+    {
+        var platform = new Domain.Entities.Platform
+        {
+            Type = PlatformType.TwitterX,
+            DisplayName = "Twitter",
+            IsConnected = true,
+            EncryptedAccessToken = System.Text.Encoding.UTF8.GetBytes("access"),
+        };
+        SetupPlatforms(platform);
+
+        SetupHttpResponse(HttpStatusCode.OK, new { });
+
+        var result = await _sut.RevokeTokenAsync(PlatformType.TwitterX, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.False(platform.IsConnected);
+        Assert.Null(platform.EncryptedAccessToken);
+        Assert.Null(platform.EncryptedRefreshToken);
+        Assert.Null(platform.GrantedScopes);
+    }
+
+    [Fact]
+    public async Task RevokeTokenAsync_ReturnsNotFound_WhenPlatformMissing()
+    {
+        SetupPlatforms(); // empty
+
+        var result = await _sut.RevokeTokenAsync(PlatformType.TwitterX, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+    }
+}
