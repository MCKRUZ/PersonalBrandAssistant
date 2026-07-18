diff --git a/src/PBA.Infrastructure/DependencyInjection.cs b/src/PBA.Infrastructure/DependencyInjection.cs
index f95c236..2bc8817 100644
--- a/src/PBA.Infrastructure/DependencyInjection.cs
+++ b/src/PBA.Infrastructure/DependencyInjection.cs
@@ -11,6 +11,7 @@ using PBA.Infrastructure.Data;
 using PBA.Infrastructure.Publishing;
 using PBA.Infrastructure.Seeding;
 using PBA.Infrastructure.Security;
+using PBA.Infrastructure.Security.OAuthProviders;
 using PBA.Infrastructure.Services;
 using PBA.Infrastructure.Transformers;
 using Npgsql;
@@ -143,6 +144,10 @@ public static class DependencyInjection
         services.AddSingleton<ITokenEncryptor, TokenEncryptor>();
         services.AddScoped<IOAuthService, OAuthService>();
 
+        // Keyed OAuth providers (resolved by the OAuthService coordinator)
+        services.AddKeyedScoped<IOAuthProvider, LinkedInOAuthProvider>(Platform.LinkedIn);
+        services.AddKeyedScoped<IOAuthProvider, TwitterOAuthProvider>(Platform.Twitter);
+
         // Content transformation
         services.AddScoped<IContentTransformer, ContentTransformer>();
 
diff --git a/src/PBA.Infrastructure/Security/IOAuthProvider.cs b/src/PBA.Infrastructure/Security/IOAuthProvider.cs
new file mode 100644
index 0000000..0c6f003
--- /dev/null
+++ b/src/PBA.Infrastructure/Security/IOAuthProvider.cs
@@ -0,0 +1,25 @@
+using PBA.Domain.Common;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+
+namespace PBA.Infrastructure.Security;
+
+// One provider per platform. The coordinator (OAuthService) resolves these by keyed DI and owns
+// state storage, credential persistence, and encryption. Providers own the wire details.
+public interface IOAuthProvider
+{
+    Platform Platform { get; }
+
+    // Provider builds its own authorize URL AND any state it needs persisted (e.g. Twitter PKCE verifier).
+    AuthorizationRequest BuildAuthorization(string state);
+
+    Task<OAuthTokenResult> ExchangeCodeAsync(string code, OAuthStateEntry state, CancellationToken ct);
+
+    // Takes the full credential (not a bare refresh-token string) so future providers with no separate
+    // refresh token can implement their own refresh. Returns plaintext tokens for the coordinator to encrypt.
+    Task<Result<OAuthTokenResult>> RefreshAsync(PlatformCredential credential, CancellationToken ct);
+
+    // Provider-specific proactive refresh window. Declared for the poller (section-05); LinkedIn/Twitter
+    // mirror the effective window their connectors use today.
+    bool NeedsRefresh(PlatformCredential credential, DateTimeOffset now);
+}
diff --git a/src/PBA.Infrastructure/Security/OAuthContracts.cs b/src/PBA.Infrastructure/Security/OAuthContracts.cs
new file mode 100644
index 0000000..f247ab8
--- /dev/null
+++ b/src/PBA.Infrastructure/Security/OAuthContracts.cs
@@ -0,0 +1,24 @@
+namespace PBA.Infrastructure.Security;
+
+// What a provider returns from BuildAuthorization: the authorize URL plus any state the coordinator
+// must persist on the provider's behalf (provider-specific logic never leaks into the coordinator).
+public sealed record AuthorizationRequest(string Url, OAuthStateAdditions Additions);
+
+// Extra state a provider needs persisted alongside its flow. Today only Twitter uses CodeVerifier (PKCE).
+public sealed record OAuthStateAdditions(string? CodeVerifier = null);
+
+// Provider token result. Replaces the private OAuthTokenResponse record; same shape.
+public sealed record OAuthTokenResult(
+    string AccessToken,
+    string? RefreshToken,
+    int ExpiresIn,
+    int? RefreshTokenExpiresIn,
+    string Scopes);
+
+// RefreshAsync failure classification. Section-03 adds full revoked-vs-transient mapping per provider;
+// LinkedIn/Twitter in this section preserve today's behavior (any non-success refresh deactivates).
+public enum RefreshFailureReason
+{
+    Revoked,
+    Transient
+}
diff --git a/src/PBA.Infrastructure/Security/OAuthProviders/LinkedInOAuthProvider.cs b/src/PBA.Infrastructure/Security/OAuthProviders/LinkedInOAuthProvider.cs
new file mode 100644
index 0000000..11845fa
--- /dev/null
+++ b/src/PBA.Infrastructure/Security/OAuthProviders/LinkedInOAuthProvider.cs
@@ -0,0 +1,119 @@
+using System.Text.Json;
+using System.Web;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Common;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+
+namespace PBA.Infrastructure.Security.OAuthProviders;
+
+public sealed class LinkedInOAuthProvider(
+    IHttpClientFactory httpClientFactory,
+    IOptions<LinkedInOptions> options,
+    ITokenEncryptor encryptor,
+    ILogger<LinkedInOAuthProvider> logger) : IOAuthProvider
+{
+    private const string Scope = "openid profile w_member_social";
+    private const string AuthorizeBase = "https://www.linkedin.com/oauth/v2/authorization";
+    private const string TokenEndpoint = "https://www.linkedin.com/oauth/v2/accessToken";
+
+    // Mirrors LinkedInConnector.TokenRefreshWindow (5 min) — the effective refresh window today.
+    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(5);
+
+    public Platform Platform => Platform.LinkedIn;
+
+    public AuthorizationRequest BuildAuthorization(string state)
+    {
+        var li = options.Value;
+        var qs = HttpUtility.ParseQueryString(string.Empty);
+        qs["response_type"] = "code";
+        qs["client_id"] = li.ClientId;
+        qs["redirect_uri"] = li.RedirectUri;
+        qs["scope"] = Scope;
+        qs["state"] = state;
+
+        return new AuthorizationRequest($"{AuthorizeBase}?{qs}", new OAuthStateAdditions());
+    }
+
+    public async Task<OAuthTokenResult> ExchangeCodeAsync(string code, OAuthStateEntry state, CancellationToken ct)
+    {
+        var li = options.Value;
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
+        var response = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(parameters), ct);
+
+        if (!response.IsSuccessStatusCode)
+        {
+            var errorBody = await response.Content.ReadAsStringAsync(ct);
+            logger.LogError("LinkedIn token exchange failed: {Status} {Body}", response.StatusCode, errorBody);
+            throw new InvalidOperationException($"LinkedIn token exchange failed: {response.StatusCode}");
+        }
+
+        var json = await response.Content.ReadAsStringAsync(ct);
+        var data = JsonSerializer.Deserialize<JsonElement>(json);
+
+        return new OAuthTokenResult(
+            AccessToken: data.GetProperty("access_token").GetString()!,
+            RefreshToken: data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
+            ExpiresIn: data.GetProperty("expires_in").GetInt32(),
+            RefreshTokenExpiresIn: data.TryGetProperty("refresh_token_expires_in", out var rtExp)
+                ? rtExp.GetInt32() : null,
+            Scopes: Scope);
+    }
+
+    public async Task<Result<OAuthTokenResult>> RefreshAsync(PlatformCredential credential, CancellationToken ct)
+    {
+        var li = options.Value;
+        var refreshToken = encryptor.Decrypt(credential.EncryptedRefreshToken!);
+
+        var parameters = new Dictionary<string, string>
+        {
+            ["grant_type"] = "refresh_token",
+            ["refresh_token"] = refreshToken,
+            ["client_id"] = li.ClientId,
+            ["client_secret"] = li.ClientSecret
+        };
+
+        var client = httpClientFactory.CreateClient();
+        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
+        {
+            Content = new FormUrlEncodedContent(parameters)
+        };
+
+        var response = await client.SendAsync(request, ct);
+
+        if (!response.IsSuccessStatusCode)
+        {
+            var errorBody = await response.Content.ReadAsStringAsync(ct);
+            logger.LogWarning("Token refresh failed for {Platform}: {Status} {Body}",
+                Platform, response.StatusCode, errorBody);
+            return Result<OAuthTokenResult>.Fail($"Token refresh failed: {response.StatusCode}");
+        }
+
+        var json = await response.Content.ReadAsStringAsync(ct);
+        var tokenData = JsonSerializer.Deserialize<JsonElement>(json);
+
+        return Result<OAuthTokenResult>.Success(new OAuthTokenResult(
+            AccessToken: tokenData.GetProperty("access_token").GetString()!,
+            RefreshToken: tokenData.TryGetProperty("refresh_token", out var newRefreshProp)
+                ? newRefreshProp.GetString() : null,
+            ExpiresIn: tokenData.GetProperty("expires_in").GetInt32(),
+            RefreshTokenExpiresIn: null,
+            Scopes: Scope));
+    }
+
+    public bool NeedsRefresh(PlatformCredential credential, DateTimeOffset now) =>
+        credential.AccessTokenExpiresAt.HasValue &&
+        credential.AccessTokenExpiresAt.Value - now < RefreshWindow;
+}
diff --git a/src/PBA.Infrastructure/Security/OAuthProviders/TwitterOAuthProvider.cs b/src/PBA.Infrastructure/Security/OAuthProviders/TwitterOAuthProvider.cs
new file mode 100644
index 0000000..1c27b8e
--- /dev/null
+++ b/src/PBA.Infrastructure/Security/OAuthProviders/TwitterOAuthProvider.cs
@@ -0,0 +1,138 @@
+using System.Net.Http.Headers;
+using System.Security.Cryptography;
+using System.Text;
+using System.Text.Json;
+using System.Web;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Common;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+
+namespace PBA.Infrastructure.Security.OAuthProviders;
+
+public sealed class TwitterOAuthProvider(
+    IHttpClientFactory httpClientFactory,
+    IOptions<TwitterOptions> options,
+    ITokenEncryptor encryptor,
+    ILogger<TwitterOAuthProvider> logger) : IOAuthProvider
+{
+    private const string Scope = "tweet.read tweet.write users.read media.write offline.access";
+    private const string AuthorizeBase = "https://twitter.com/i/oauth2/authorize";
+    private const string TokenEndpoint = "https://api.twitter.com/2/oauth2/token";
+
+    // Mirrors TwitterConnector.TokenRefreshWindow (10 min) — the effective refresh window today.
+    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(10);
+
+    public Platform Platform => Platform.Twitter;
+
+    public AuthorizationRequest BuildAuthorization(string state)
+    {
+        var tw = options.Value;
+        var codeVerifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
+            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
+        var codeChallenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
+
+        var qs = HttpUtility.ParseQueryString(string.Empty);
+        qs["response_type"] = "code";
+        qs["client_id"] = tw.ClientId;
+        qs["redirect_uri"] = tw.RedirectUri;
+        qs["scope"] = Scope;
+        qs["state"] = state;
+        qs["code_challenge"] = codeChallenge;
+        qs["code_challenge_method"] = "S256";
+
+        return new AuthorizationRequest($"{AuthorizeBase}?{qs}", new OAuthStateAdditions(codeVerifier));
+    }
+
+    public async Task<OAuthTokenResult> ExchangeCodeAsync(string code, OAuthStateEntry state, CancellationToken ct)
+    {
+        var tw = options.Value;
+        var parameters = new Dictionary<string, string>
+        {
+            ["grant_type"] = "authorization_code",
+            ["code"] = code,
+            ["redirect_uri"] = tw.RedirectUri,
+            ["client_id"] = tw.ClientId,
+            ["code_verifier"] = state.CodeVerifier!
+        };
+
+        var client = httpClientFactory.CreateClient();
+        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{tw.ClientId}:{tw.ClientSecret}"));
+        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
+        {
+            Content = new FormUrlEncodedContent(parameters)
+        };
+        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
+
+        var response = await client.SendAsync(request, ct);
+
+        if (!response.IsSuccessStatusCode)
+        {
+            var errorBody = await response.Content.ReadAsStringAsync(ct);
+            logger.LogError("Twitter token exchange failed: {Status} {Body}", response.StatusCode, errorBody);
+            throw new InvalidOperationException($"Twitter token exchange failed: {response.StatusCode}");
+        }
+
+        var json = await response.Content.ReadAsStringAsync(ct);
+        var data = JsonSerializer.Deserialize<JsonElement>(json);
+
+        return new OAuthTokenResult(
+            AccessToken: data.GetProperty("access_token").GetString()!,
+            RefreshToken: data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
+            ExpiresIn: data.GetProperty("expires_in").GetInt32(),
+            RefreshTokenExpiresIn: null,
+            Scopes: Scope);
+    }
+
+    public async Task<Result<OAuthTokenResult>> RefreshAsync(PlatformCredential credential, CancellationToken ct)
+    {
+        var tw = options.Value;
+        var refreshToken = encryptor.Decrypt(credential.EncryptedRefreshToken!);
+
+        var parameters = new Dictionary<string, string>
+        {
+            ["grant_type"] = "refresh_token",
+            ["refresh_token"] = refreshToken,
+            ["client_id"] = tw.ClientId
+        };
+
+        var client = httpClientFactory.CreateClient();
+        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
+        {
+            Content = new FormUrlEncodedContent(parameters)
+        };
+        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{tw.ClientId}:{tw.ClientSecret}"));
+        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
+
+        var response = await client.SendAsync(request, ct);
+
+        if (!response.IsSuccessStatusCode)
+        {
+            var errorBody = await response.Content.ReadAsStringAsync(ct);
+            logger.LogWarning("Token refresh failed for {Platform}: {Status} {Body}",
+                Platform, response.StatusCode, errorBody);
+            return Result<OAuthTokenResult>.Fail($"Token refresh failed: {response.StatusCode}");
+        }
+
+        var json = await response.Content.ReadAsStringAsync(ct);
+        var tokenData = JsonSerializer.Deserialize<JsonElement>(json);
+
+        return Result<OAuthTokenResult>.Success(new OAuthTokenResult(
+            AccessToken: tokenData.GetProperty("access_token").GetString()!,
+            RefreshToken: tokenData.TryGetProperty("refresh_token", out var newRefreshProp)
+                ? newRefreshProp.GetString() : null,
+            ExpiresIn: tokenData.GetProperty("expires_in").GetInt32(),
+            RefreshTokenExpiresIn: null,
+            Scopes: Scope));
+    }
+
+    public bool NeedsRefresh(PlatformCredential credential, DateTimeOffset now) =>
+        credential.AccessTokenExpiresAt.HasValue &&
+        credential.AccessTokenExpiresAt.Value - now < RefreshWindow;
+
+    private static string Base64UrlEncode(byte[] input) =>
+        Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
+}
diff --git a/src/PBA.Infrastructure/Security/OAuthService.cs b/src/PBA.Infrastructure/Security/OAuthService.cs
index debdec3..e32dc0a 100644
--- a/src/PBA.Infrastructure/Security/OAuthService.cs
+++ b/src/PBA.Infrastructure/Security/OAuthService.cs
@@ -1,30 +1,24 @@
 using System.Collections.Concurrent;
-using System.Net.Http.Headers;
 using System.Security.Cryptography;
-using System.Text;
-using System.Text.Json;
-using System.Web;
 using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.Logging;
-using Microsoft.Extensions.Options;
 using PBA.Application.Common.Interfaces;
 using PBA.Domain.Common;
 using PBA.Domain.Entities;
 using PBA.Domain.Enums;
-using PBA.Infrastructure.Configuration;
 
 namespace PBA.Infrastructure.Security;
 
+// Thin coordinator: owns OAuth state storage, credential upsert, and token encryption. Per-platform
+// wire details (authorize URLs, code exchange, refresh) live in keyed IOAuthProvider implementations.
 public sealed class OAuthService(
-    IHttpClientFactory httpClientFactory,
+    IServiceProvider serviceProvider,
     ITokenEncryptor encryptor,
     IAppDbContext db,
-    IOptions<LinkedInOptions> linkedInOptions,
-    IOptions<TwitterOptions> twitterOptions,
     ILogger<OAuthService> logger) : IOAuthService
 {
     private static readonly ConcurrentDictionary<string, OAuthStateEntry> StateStore = new();
-    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);
     private const int MaxPendingStates = 1000;
 
     public Task<string> GetAuthorizationUrlAsync(Platform platform, CancellationToken ct)
@@ -34,16 +28,13 @@ public sealed class OAuthService(
         if (StateStore.Count >= MaxPendingStates)
             throw new InvalidOperationException("Too many pending OAuth flows");
 
+        var provider = ResolveProvider(platform);
         var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
+        var authorization = provider.BuildAuthorization(state);
 
-        var url = platform switch
-        {
-            Platform.LinkedIn => BuildLinkedInAuthUrl(state),
-            Platform.Twitter => BuildTwitterAuthUrl(state),
-            _ => throw new NotSupportedException($"OAuth is not supported for {platform}")
-        };
+        StateStore[state] = new OAuthStateEntry(platform, authorization.Additions.CodeVerifier);
 
-        return Task.FromResult(url);
+        return Task.FromResult(authorization.Url);
     }
 
     public async Task<PlatformCredential> ExchangeCodeAsync(
@@ -52,12 +43,8 @@ public sealed class OAuthService(
         if (!StateStore.TryRemove(state, out var stateEntry) || stateEntry.IsExpired)
             throw new InvalidOperationException("Invalid or expired OAuth state");
 
-        var tokenResponse = platform switch
-        {
-            Platform.LinkedIn => await ExchangeLinkedInCodeAsync(code, ct),
-            Platform.Twitter => await ExchangeTwitterCodeAsync(code, stateEntry.CodeVerifier!, ct),
-            _ => throw new NotSupportedException($"OAuth is not supported for {platform}")
-        };
+        var provider = ResolveProvider(platform);
+        var tokenResult = await provider.ExchangeCodeAsync(code, stateEntry, ct);
 
         var credential = await db.PlatformCredentials
             .FirstOrDefaultAsync(c => c.Platform == platform, ct);
@@ -68,15 +55,15 @@ public sealed class OAuthService(
             db.PlatformCredentials.Add(credential);
         }
 
-        credential.EncryptedAccessToken = encryptor.Encrypt(tokenResponse.AccessToken);
-        credential.EncryptedRefreshToken = tokenResponse.RefreshToken is not null
-            ? encryptor.Encrypt(tokenResponse.RefreshToken)
+        credential.EncryptedAccessToken = encryptor.Encrypt(tokenResult.AccessToken);
+        credential.EncryptedRefreshToken = tokenResult.RefreshToken is not null
+            ? encryptor.Encrypt(tokenResult.RefreshToken)
             : null;
-        credential.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
-        credential.RefreshTokenExpiresAt = tokenResponse.RefreshTokenExpiresIn.HasValue
-            ? DateTimeOffset.UtcNow.AddSeconds(tokenResponse.RefreshTokenExpiresIn.Value)
+        credential.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResult.ExpiresIn);
+        credential.RefreshTokenExpiresAt = tokenResult.RefreshTokenExpiresIn.HasValue
+            ? DateTimeOffset.UtcNow.AddSeconds(tokenResult.RefreshTokenExpiresIn.Value)
             : null;
-        credential.Scopes = tokenResponse.Scopes;
+        credential.Scopes = tokenResult.Scopes;
         credential.IsActive = true;
         credential.UpdatedAt = DateTimeOffset.UtcNow;
 
@@ -97,194 +84,33 @@ public sealed class OAuthService(
             return Result<string>.Fail("No refresh token available");
         }
 
-        var refreshToken = encryptor.Decrypt(credential.EncryptedRefreshToken);
-
-        var (tokenEndpoint, clientId, clientSecret) = credential.Platform switch
-        {
-            Platform.LinkedIn => (
-                "https://www.linkedin.com/oauth/v2/accessToken",
-                linkedInOptions.Value.ClientId,
-                linkedInOptions.Value.ClientSecret),
-            Platform.Twitter => (
-                "https://api.twitter.com/2/oauth2/token",
-                twitterOptions.Value.ClientId,
-                twitterOptions.Value.ClientSecret),
-            _ => throw new NotSupportedException($"Token refresh not supported for {credential.Platform}")
-        };
-
-        var parameters = new Dictionary<string, string>
-        {
-            ["grant_type"] = "refresh_token",
-            ["refresh_token"] = refreshToken,
-            ["client_id"] = clientId
-        };
-
-        if (credential.Platform != Platform.Twitter)
-            parameters["client_secret"] = clientSecret;
-
-        var client = httpClientFactory.CreateClient();
-        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
-        {
-            Content = new FormUrlEncodedContent(parameters)
-        };
-
-        if (credential.Platform == Platform.Twitter)
-        {
-            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
-            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
-        }
-
-        var response = await client.SendAsync(request, ct);
+        var provider = ResolveProvider(credential.Platform);
+        var refreshResult = await provider.RefreshAsync(credential, ct);
 
-        if (!response.IsSuccessStatusCode)
+        if (!refreshResult.IsSuccess)
         {
-            var errorBody = await response.Content.ReadAsStringAsync(ct);
-            logger.LogWarning("Token refresh failed for {Platform}: {Status} {Body}",
-                credential.Platform, response.StatusCode, errorBody);
             credential.IsActive = false;
             credential.UpdatedAt = DateTimeOffset.UtcNow;
             await db.SaveChangesAsync(ct);
-            return Result<string>.Fail($"Token refresh failed: {response.StatusCode}");
+            return Result<string>.Fail([.. refreshResult.Errors]);
         }
 
-        var json = await response.Content.ReadAsStringAsync(ct);
-        var tokenData = JsonSerializer.Deserialize<JsonElement>(json);
-
-        var newAccessToken = tokenData.GetProperty("access_token").GetString()!;
-        var expiresIn = tokenData.GetProperty("expires_in").GetInt32();
+        var tokens = refreshResult.Value!;
+        credential.EncryptedAccessToken = encryptor.Encrypt(tokens.AccessToken);
+        credential.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn);
 
-        credential.EncryptedAccessToken = encryptor.Encrypt(newAccessToken);
-        credential.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
-
-        if (tokenData.TryGetProperty("refresh_token", out var newRefreshProp))
-        {
-            var newRefreshToken = newRefreshProp.GetString()!;
-            credential.EncryptedRefreshToken = encryptor.Encrypt(newRefreshToken);
-        }
+        if (tokens.RefreshToken is not null)
+            credential.EncryptedRefreshToken = encryptor.Encrypt(tokens.RefreshToken);
 
         credential.UpdatedAt = DateTimeOffset.UtcNow;
         await db.SaveChangesAsync(ct);
 
-        return Result<string>.Success(newAccessToken);
-    }
-
-    private string BuildLinkedInAuthUrl(string state)
-    {
-        var li = linkedInOptions.Value;
-        var entry = new OAuthStateEntry(Platform.LinkedIn);
-        StateStore[state] = entry;
-
-        var qs = HttpUtility.ParseQueryString(string.Empty);
-        qs["response_type"] = "code";
-        qs["client_id"] = li.ClientId;
-        qs["redirect_uri"] = li.RedirectUri;
-        qs["scope"] = "openid profile w_member_social";
-        qs["state"] = state;
-
-        return $"https://www.linkedin.com/oauth/v2/authorization?{qs}";
-    }
-
-    private string BuildTwitterAuthUrl(string state)
-    {
-        var tw = twitterOptions.Value;
-        var codeVerifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
-            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
-        var codeChallenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
-
-        var entry = new OAuthStateEntry(Platform.Twitter, codeVerifier);
-        StateStore[state] = entry;
-
-        var qs = HttpUtility.ParseQueryString(string.Empty);
-        qs["response_type"] = "code";
-        qs["client_id"] = tw.ClientId;
-        qs["redirect_uri"] = tw.RedirectUri;
-        qs["scope"] = "tweet.read tweet.write users.read media.write offline.access";
-        qs["state"] = state;
-        qs["code_challenge"] = codeChallenge;
-        qs["code_challenge_method"] = "S256";
-
-        return $"https://twitter.com/i/oauth2/authorize?{qs}";
-    }
-
-    private async Task<OAuthTokenResponse> ExchangeLinkedInCodeAsync(string code, CancellationToken ct)
-    {
-        var li = linkedInOptions.Value;
-        var parameters = new Dictionary<string, string>
-        {
-            ["grant_type"] = "authorization_code",
-            ["code"] = code,
-            ["redirect_uri"] = li.RedirectUri,
-            ["client_id"] = li.ClientId,
-            ["client_secret"] = li.ClientSecret
-        };
-
-        var client = httpClientFactory.CreateClient();
-        var response = await client.PostAsync(
-            "https://www.linkedin.com/oauth/v2/accessToken",
-            new FormUrlEncodedContent(parameters), ct);
-
-        if (!response.IsSuccessStatusCode)
-        {
-            var errorBody = await response.Content.ReadAsStringAsync(ct);
-            logger.LogError("LinkedIn token exchange failed: {Status} {Body}", response.StatusCode, errorBody);
-            throw new InvalidOperationException($"LinkedIn token exchange failed: {response.StatusCode}");
-        }
-
-        var json = await response.Content.ReadAsStringAsync(ct);
-        var data = JsonSerializer.Deserialize<JsonElement>(json);
-
-        return new OAuthTokenResponse(
-            AccessToken: data.GetProperty("access_token").GetString()!,
-            RefreshToken: data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
-            ExpiresIn: data.GetProperty("expires_in").GetInt32(),
-            RefreshTokenExpiresIn: data.TryGetProperty("refresh_token_expires_in", out var rtExp)
-                ? rtExp.GetInt32() : null,
-            Scopes: "openid profile w_member_social");
+        return Result<string>.Success(tokens.AccessToken);
     }
 
-    private async Task<OAuthTokenResponse> ExchangeTwitterCodeAsync(
-        string code, string codeVerifier, CancellationToken ct)
-    {
-        var tw = twitterOptions.Value;
-        var parameters = new Dictionary<string, string>
-        {
-            ["grant_type"] = "authorization_code",
-            ["code"] = code,
-            ["redirect_uri"] = tw.RedirectUri,
-            ["client_id"] = tw.ClientId,
-            ["code_verifier"] = codeVerifier
-        };
-
-        var client = httpClientFactory.CreateClient();
-        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{tw.ClientId}:{tw.ClientSecret}"));
-        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.twitter.com/2/oauth2/token")
-        {
-            Content = new FormUrlEncodedContent(parameters)
-        };
-        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
-
-        var response = await client.SendAsync(request, ct);
-
-        if (!response.IsSuccessStatusCode)
-        {
-            var errorBody = await response.Content.ReadAsStringAsync(ct);
-            logger.LogError("Twitter token exchange failed: {Status} {Body}", response.StatusCode, errorBody);
-            throw new InvalidOperationException($"Twitter token exchange failed: {response.StatusCode}");
-        }
-
-        var json = await response.Content.ReadAsStringAsync(ct);
-        var data = JsonSerializer.Deserialize<JsonElement>(json);
-
-        return new OAuthTokenResponse(
-            AccessToken: data.GetProperty("access_token").GetString()!,
-            RefreshToken: data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
-            ExpiresIn: data.GetProperty("expires_in").GetInt32(),
-            RefreshTokenExpiresIn: null,
-            Scopes: "tweet.read tweet.write users.read media.write offline.access");
-    }
-
-    private static string Base64UrlEncode(byte[] input) =>
-        Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
+    private IOAuthProvider ResolveProvider(Platform platform) =>
+        serviceProvider.GetKeyedService<IOAuthProvider>(platform)
+        ?? throw new NotSupportedException($"OAuth is not supported for {platform}");
 
     private static void CleanExpiredStates()
     {
@@ -294,17 +120,4 @@ public sealed class OAuthService(
                 StateStore.TryRemove(kvp.Key, out _);
         }
     }
-
-    private sealed record OAuthStateEntry(Platform Platform, string? CodeVerifier = null)
-    {
-        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
-        public bool IsExpired => DateTimeOffset.UtcNow - CreatedAt > StateTtl;
-    }
-
-    private sealed record OAuthTokenResponse(
-        string AccessToken,
-        string? RefreshToken,
-        int ExpiresIn,
-        int? RefreshTokenExpiresIn,
-        string Scopes);
 }
diff --git a/src/PBA.Infrastructure/Security/OAuthStateEntry.cs b/src/PBA.Infrastructure/Security/OAuthStateEntry.cs
new file mode 100644
index 0000000..4bfc971
--- /dev/null
+++ b/src/PBA.Infrastructure/Security/OAuthStateEntry.cs
@@ -0,0 +1,14 @@
+using PBA.Domain.Enums;
+
+namespace PBA.Infrastructure.Security;
+
+// Promoted from a private record inside OAuthService so providers can receive it on ExchangeCodeAsync.
+// The coordinator still owns creating, storing, and expiring these entries.
+public sealed record OAuthStateEntry(Platform Platform, string? CodeVerifier = null)
+{
+    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
+
+    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
+
+    public bool IsExpired => DateTimeOffset.UtcNow - CreatedAt > Ttl;
+}
diff --git a/tests/PBA.Infrastructure.Tests/Security/OAuthProviderMapTests.cs b/tests/PBA.Infrastructure.Tests/Security/OAuthProviderMapTests.cs
new file mode 100644
index 0000000..5bb8fc2
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Security/OAuthProviderMapTests.cs
@@ -0,0 +1,49 @@
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging;
+using Moq;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+using PBA.Infrastructure.Security;
+using PBA.Infrastructure.Security.OAuthProviders;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Security;
+
+// Boundary test: proves exactly one IOAuthProvider resolves per platform via keyed DI, and that an
+// unregistered platform resolves to null (which the coordinator turns into NotSupportedException).
+public class OAuthProviderMapTests
+{
+    private static ServiceProvider BuildProviderMap()
+    {
+        var services = new ServiceCollection();
+
+        services.AddSingleton(Mock.Of<IHttpClientFactory>());
+        services.AddSingleton(Mock.Of<ITokenEncryptor>());
+        services.AddLogging();
+        services.Configure<LinkedInOptions>(_ => { });
+        services.Configure<TwitterOptions>(_ => { });
+
+        // The two registrations under test, identical to DependencyInjection.cs.
+        services.AddKeyedScoped<IOAuthProvider, LinkedInOAuthProvider>(Platform.LinkedIn);
+        services.AddKeyedScoped<IOAuthProvider, TwitterOAuthProvider>(Platform.Twitter);
+
+        return services.BuildServiceProvider();
+    }
+
+    [Fact]
+    public void ResolvesOneProviderPerPlatform_ViaKeyedDI()
+    {
+        using var provider = BuildProviderMap();
+
+        var linkedIn = provider.GetKeyedService<IOAuthProvider>(Platform.LinkedIn);
+        var twitter = provider.GetKeyedService<IOAuthProvider>(Platform.Twitter);
+        var unregistered = provider.GetKeyedService<IOAuthProvider>(Platform.Blog);
+
+        Assert.IsType<LinkedInOAuthProvider>(linkedIn);
+        Assert.IsType<TwitterOAuthProvider>(twitter);
+        Assert.Equal(Platform.LinkedIn, linkedIn!.Platform);
+        Assert.Equal(Platform.Twitter, twitter!.Platform);
+        Assert.Null(unregistered);
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/LinkedInOAuthProviderTests.cs b/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/LinkedInOAuthProviderTests.cs
new file mode 100644
index 0000000..2acbb13
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/LinkedInOAuthProviderTests.cs
@@ -0,0 +1,192 @@
+using System.Net;
+using System.Net.Http.Headers;
+using System.Text.Json;
+using System.Web;
+using Microsoft.Extensions.Logging.Abstractions;
+using Microsoft.Extensions.Options;
+using Moq;
+using Moq.Protected;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+using PBA.Infrastructure.Security;
+using PBA.Infrastructure.Security.OAuthProviders;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Security.OAuthProviders;
+
+public class LinkedInOAuthProviderTests : IDisposable
+{
+    private readonly Mock<ITokenEncryptor> _encryptor = new();
+    private readonly Mock<HttpMessageHandler> _httpHandler = new();
+    private readonly HttpClient _httpClient;
+    private readonly IHttpClientFactory _httpClientFactory;
+
+    private readonly LinkedInOptions _options = new()
+    {
+        Enabled = true,
+        ClientId = "linkedin-client-id",
+        ClientSecret = "linkedin-client-secret",
+        RedirectUri = "https://localhost:5001/api/auth/linkedin/callback"
+    };
+
+    public LinkedInOAuthProviderTests()
+    {
+        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>()))
+            .Returns((string s) => s.Replace("encrypted:", ""));
+
+        _httpClient = new HttpClient(_httpHandler.Object);
+        var factoryMock = new Mock<IHttpClientFactory>();
+        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);
+        _httpClientFactory = factoryMock.Object;
+    }
+
+    private LinkedInOAuthProvider CreateProvider() => new(
+        _httpClientFactory,
+        Options.Create(_options),
+        _encryptor.Object,
+        NullLogger<LinkedInOAuthProvider>.Instance);
+
+    // Captures the outgoing request and returns a canned JSON body.
+    private (Func<string?> Body, Func<AuthenticationHeaderValue?> AuthHeader) SetupCapture(string responseJson)
+    {
+        string? capturedBody = null;
+        AuthenticationHeaderValue? capturedAuth = null;
+
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
+            {
+                capturedBody = req.Content is not null ? await req.Content.ReadAsStringAsync() : null;
+                capturedAuth = req.Headers.Authorization;
+                return new HttpResponseMessage(HttpStatusCode.OK)
+                {
+                    Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
+                };
+            });
+
+        return (() => capturedBody, () => capturedAuth);
+    }
+
+    [Fact]
+    public void BuildAuthorization_ProducesUnchangedUrlScopesAndState()
+    {
+        var provider = CreateProvider();
+
+        var request = provider.BuildAuthorization("STATE123");
+
+        Assert.StartsWith("https://www.linkedin.com/oauth/v2/authorization", request.Url);
+        var query = HttpUtility.ParseQueryString(new Uri(request.Url).Query);
+        Assert.Equal("code", query["response_type"]);
+        Assert.Equal(_options.ClientId, query["client_id"]);
+        Assert.Equal(_options.RedirectUri, query["redirect_uri"]);
+        Assert.Equal("openid profile w_member_social", query["scope"]);
+        Assert.Equal("STATE123", query["state"]);
+        Assert.Null(request.Additions.CodeVerifier);
+    }
+
+    [Fact]
+    public async Task ExchangeCode_ReturnsTokenResult_Unchanged()
+    {
+        var provider = CreateProvider();
+        var capture = SetupCapture(JsonSerializer.Serialize(new
+        {
+            access_token = "li-access-token",
+            refresh_token = "li-refresh-token",
+            expires_in = 5184000,
+            refresh_token_expires_in = 31536000
+        }));
+
+        var result = await provider.ExchangeCodeAsync(
+            "auth-code", new OAuthStateEntry(Platform.LinkedIn), CancellationToken.None);
+
+        Assert.Equal("li-access-token", result.AccessToken);
+        Assert.Equal("li-refresh-token", result.RefreshToken);
+        Assert.Equal(5184000, result.ExpiresIn);
+        Assert.Equal(31536000, result.RefreshTokenExpiresIn);
+        Assert.Equal("openid profile w_member_social", result.Scopes);
+
+        // LinkedIn puts client_secret in the body, uses no Basic auth header.
+        Assert.Contains("client_secret=linkedin-client-secret", capture.Body());
+        Assert.Null(capture.AuthHeader());
+    }
+
+    [Fact]
+    public async Task RefreshAsync_RefreshesToken_BehaviorUnchanged()
+    {
+        var provider = CreateProvider();
+        var credential = new PlatformCredential
+        {
+            Platform = Platform.LinkedIn,
+            EncryptedRefreshToken = "encrypted:old-refresh",
+            IsActive = true
+        };
+        var capture = SetupCapture(JsonSerializer.Serialize(new
+        {
+            access_token = "new-access-token",
+            refresh_token = "new-refresh-token",
+            expires_in = 5184000
+        }));
+
+        var result = await provider.RefreshAsync(credential, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("new-access-token", result.Value!.AccessToken);
+        Assert.Equal("new-refresh-token", result.Value.RefreshToken);
+        Assert.Equal(5184000, result.Value.ExpiresIn);
+
+        var body = capture.Body();
+        Assert.Contains("grant_type=refresh_token", body);
+        Assert.Contains("refresh_token=old-refresh", body);
+        Assert.Contains("client_secret=linkedin-client-secret", body);
+        Assert.Null(capture.AuthHeader());
+    }
+
+    [Fact]
+    public async Task RefreshAsync_NonSuccessResponse_ReturnsFail()
+    {
+        var provider = CreateProvider();
+        var credential = new PlatformCredential
+        {
+            Platform = Platform.LinkedIn,
+            EncryptedRefreshToken = "encrypted:old-refresh"
+        };
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.BadRequest)
+            {
+                Content = new StringContent("{\"error\":\"invalid_grant\"}")
+            });
+
+        var result = await provider.RefreshAsync(credential, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+    }
+
+    [Fact]
+    public void NeedsRefresh_WithinFiveMinuteWindow_ReturnsTrue()
+    {
+        var provider = CreateProvider();
+        var now = DateTimeOffset.UtcNow;
+        var credential = new PlatformCredential
+        {
+            Platform = Platform.LinkedIn,
+            AccessTokenExpiresAt = now.AddMinutes(3)
+        };
+
+        Assert.True(provider.NeedsRefresh(credential, now));
+
+        credential.AccessTokenExpiresAt = now.AddMinutes(30);
+        Assert.False(provider.NeedsRefresh(credential, now));
+    }
+
+    public void Dispose()
+    {
+        _httpClient.Dispose();
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/TwitterOAuthProviderTests.cs b/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/TwitterOAuthProviderTests.cs
new file mode 100644
index 0000000..4025892
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/TwitterOAuthProviderTests.cs
@@ -0,0 +1,175 @@
+using System.Net;
+using System.Net.Http.Headers;
+using System.Text.Json;
+using System.Web;
+using Microsoft.Extensions.Logging.Abstractions;
+using Microsoft.Extensions.Options;
+using Moq;
+using Moq.Protected;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+using PBA.Infrastructure.Security;
+using PBA.Infrastructure.Security.OAuthProviders;
+using Xunit;
+
+namespace PBA.Infrastructure.Tests.Security.OAuthProviders;
+
+public class TwitterOAuthProviderTests : IDisposable
+{
+    private readonly Mock<ITokenEncryptor> _encryptor = new();
+    private readonly Mock<HttpMessageHandler> _httpHandler = new();
+    private readonly HttpClient _httpClient;
+    private readonly IHttpClientFactory _httpClientFactory;
+
+    private readonly TwitterOptions _options = new()
+    {
+        Enabled = true,
+        ClientId = "twitter-client-id",
+        ClientSecret = "twitter-client-secret",
+        RedirectUri = "https://localhost:5001/api/auth/twitter/callback"
+    };
+
+    public TwitterOAuthProviderTests()
+    {
+        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>()))
+            .Returns((string s) => s.Replace("encrypted:", ""));
+
+        _httpClient = new HttpClient(_httpHandler.Object);
+        var factoryMock = new Mock<IHttpClientFactory>();
+        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);
+        _httpClientFactory = factoryMock.Object;
+    }
+
+    private TwitterOAuthProvider CreateProvider() => new(
+        _httpClientFactory,
+        Options.Create(_options),
+        _encryptor.Object,
+        NullLogger<TwitterOAuthProvider>.Instance);
+
+    private (Func<string?> Body, Func<AuthenticationHeaderValue?> AuthHeader) SetupCapture(string responseJson)
+    {
+        string? capturedBody = null;
+        AuthenticationHeaderValue? capturedAuth = null;
+
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync",
+                ItExpr.IsAny<HttpRequestMessage>(),
+                ItExpr.IsAny<CancellationToken>())
+            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
+            {
+                capturedBody = req.Content is not null ? await req.Content.ReadAsStringAsync() : null;
+                capturedAuth = req.Headers.Authorization;
+                return new HttpResponseMessage(HttpStatusCode.OK)
+                {
+                    Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
+                };
+            });
+
+        return (() => capturedBody, () => capturedAuth);
+    }
+
+    [Fact]
+    public void BuildAuthorization_IncludesPkceS256ChallengeAndPersistsVerifier()
+    {
+        var provider = CreateProvider();
+
+        var request = provider.BuildAuthorization("STATE123");
+
+        Assert.StartsWith("https://twitter.com/i/oauth2/authorize", request.Url);
+        var query = HttpUtility.ParseQueryString(new Uri(request.Url).Query);
+        Assert.Equal("code", query["response_type"]);
+        Assert.Equal(_options.ClientId, query["client_id"]);
+        Assert.Equal("tweet.read tweet.write users.read media.write offline.access", query["scope"]);
+        Assert.Equal("STATE123", query["state"]);
+        Assert.Equal("S256", query["code_challenge_method"]);
+        Assert.False(string.IsNullOrEmpty(query["code_challenge"]));
+
+        // The provider hands the coordinator the PKCE verifier to persist in state.
+        Assert.False(string.IsNullOrEmpty(request.Additions.CodeVerifier));
+    }
+
+    [Fact]
+    public async Task ExchangeCode_UsesBasicAuthAndCodeVerifier_Unchanged()
+    {
+        var provider = CreateProvider();
+        var capture = SetupCapture(JsonSerializer.Serialize(new
+        {
+            access_token = "tw-access-token",
+            refresh_token = "tw-refresh-token",
+            expires_in = 7200
+        }));
+
+        var state = new OAuthStateEntry(Platform.Twitter, "the-code-verifier");
+        var result = await provider.ExchangeCodeAsync("auth-code", state, CancellationToken.None);
+
+        Assert.Equal("tw-access-token", result.AccessToken);
+        Assert.Equal("tw-refresh-token", result.RefreshToken);
+        Assert.Equal(7200, result.ExpiresIn);
+        Assert.Null(result.RefreshTokenExpiresIn);
+        Assert.Equal("tweet.read tweet.write users.read media.write offline.access", result.Scopes);
+
+        var body = capture.Body();
+        Assert.Contains("code_verifier=the-code-verifier", body);
+        // Twitter uses Basic auth; client_secret is in the header, not the body.
+        var auth = capture.AuthHeader();
+        Assert.NotNull(auth);
+        Assert.Equal("Basic", auth!.Scheme);
+        Assert.DoesNotContain("client_secret=", body);
+    }
+
+    [Fact]
+    public async Task RefreshAsync_UsesBasicAuth_BehaviorUnchanged()
+    {
+        var provider = CreateProvider();
+        var credential = new PlatformCredential
+        {
+            Platform = Platform.Twitter,
+            EncryptedRefreshToken = "encrypted:old-refresh",
+            IsActive = true
+        };
+        var capture = SetupCapture(JsonSerializer.Serialize(new
+        {
+            access_token = "new-access-token",
+            refresh_token = "new-refresh-token",
+            expires_in = 7200
+        }));
+
+        var result = await provider.RefreshAsync(credential, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("new-access-token", result.Value!.AccessToken);
+
+        var body = capture.Body();
+        Assert.Contains("grant_type=refresh_token", body);
+        Assert.Contains("refresh_token=old-refresh", body);
+        // Basic auth header carries the secret; body must NOT contain client_secret.
+        var auth = capture.AuthHeader();
+        Assert.NotNull(auth);
+        Assert.Equal("Basic", auth!.Scheme);
+        Assert.DoesNotContain("client_secret=", body);
+    }
+
+    [Fact]
+    public void NeedsRefresh_WithinTenMinuteWindow_ReturnsTrue()
+    {
+        var provider = CreateProvider();
+        var now = DateTimeOffset.UtcNow;
+        var credential = new PlatformCredential
+        {
+            Platform = Platform.Twitter,
+            AccessTokenExpiresAt = now.AddMinutes(8)
+        };
+
+        Assert.True(provider.NeedsRefresh(credential, now));
+
+        credential.AccessTokenExpiresAt = now.AddMinutes(30);
+        Assert.False(provider.NeedsRefresh(credential, now));
+    }
+
+    public void Dispose()
+    {
+        _httpClient.Dispose();
+    }
+}
diff --git a/tests/PBA.Infrastructure.Tests/Security/OAuthServiceTests.cs b/tests/PBA.Infrastructure.Tests/Security/OAuthServiceTests.cs
index 31fa478..fc92563 100644
--- a/tests/PBA.Infrastructure.Tests/Security/OAuthServiceTests.cs
+++ b/tests/PBA.Infrastructure.Tests/Security/OAuthServiceTests.cs
@@ -3,7 +3,9 @@ using System.Security.Cryptography;
 using System.Text.Json;
 using System.Web;
 using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Logging.Abstractions;
 using Microsoft.Extensions.Options;
 using Moq;
 using Moq.Protected;
@@ -12,6 +14,7 @@ using PBA.Domain.Enums;
 using PBA.Infrastructure.Configuration;
 using PBA.Infrastructure.Data;
 using PBA.Infrastructure.Security;
+using PBA.Infrastructure.Security.OAuthProviders;
 using Xunit;
 
 namespace PBA.Infrastructure.Tests.Security;
@@ -59,13 +62,21 @@ public class OAuthServiceTests : IDisposable
         _httpClientFactory = factoryMock.Object;
     }
 
-    private OAuthService CreateService() => new(
-        _httpClientFactory,
-        _encryptor.Object,
-        _dbContext,
-        Options.Create(_linkedInOptions),
-        Options.Create(_twitterOptions),
-        _logger.Object);
+    private OAuthService CreateService()
+    {
+        var services = new ServiceCollection();
+        services.AddSingleton(_httpClientFactory);
+        services.AddSingleton(_encryptor.Object);
+        services.AddSingleton(Options.Create(_linkedInOptions));
+        services.AddSingleton(Options.Create(_twitterOptions));
+        services.AddSingleton<ILogger<LinkedInOAuthProvider>>(NullLogger<LinkedInOAuthProvider>.Instance);
+        services.AddSingleton<ILogger<TwitterOAuthProvider>>(NullLogger<TwitterOAuthProvider>.Instance);
+        services.AddKeyedScoped<IOAuthProvider, LinkedInOAuthProvider>(Platform.LinkedIn);
+        services.AddKeyedScoped<IOAuthProvider, TwitterOAuthProvider>(Platform.Twitter);
+        var provider = services.BuildServiceProvider();
+
+        return new OAuthService(provider, _encryptor.Object, _dbContext, _logger.Object);
+    }
 
     private void SetupHttpResponse(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
     {
@@ -245,6 +256,98 @@ public class OAuthServiceTests : IDisposable
             service.GetAuthorizationUrlAsync(Platform.Blog, CancellationToken.None));
     }
 
+    [Fact]
+    public async Task OAuthService_GetAuthorizationUrl_ResolvesKeyedProviderAndPersistsStateAdditions()
+    {
+        var service = CreateService();
+
+        // Keyed resolution: the coordinator delegates URL construction to the LinkedIn provider.
+        var url = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CancellationToken.None);
+        Assert.StartsWith("https://www.linkedin.com/oauth/v2/authorization", url);
+
+        // State persisted by the coordinator: a follow-up exchange with that state succeeds.
+        var state = HttpUtility.ParseQueryString(new Uri(url).Query)["state"]!;
+        SetupHttpResponse(JsonSerializer.Serialize(new
+        {
+            access_token = "li-access-token",
+            refresh_token = "li-refresh-token",
+            expires_in = 5184000
+        }));
+
+        var credential = await service.ExchangeCodeAsync(Platform.LinkedIn, "auth-code", state, CancellationToken.None);
+        Assert.Equal(Platform.LinkedIn, credential.Platform);
+    }
+
+    [Fact]
+    public async Task OAuthService_PersistsTwitterCodeVerifier_FromProviderStateAdditions()
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
+        // The verifier the Twitter provider generated in BuildAuthorization must have been persisted
+        // by the coordinator and threaded back into the exchange — proving no leak of provider logic.
+        Assert.NotNull(capturedBody);
+        Assert.Contains("code_verifier=", capturedBody);
+    }
+
+    [Fact]
+    public async Task OAuthService_ExchangeCode_UnknownPlatform_ReturnsNotSupported()
+    {
+        var service = CreateService();
+
+        // Obtain a valid state (LinkedIn), then exchange against an unregistered platform: the state
+        // check passes, provider resolution returns null, and the coordinator throws NotSupported.
+        var url = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CancellationToken.None);
+        var state = HttpUtility.ParseQueryString(new Uri(url).Query)["state"]!;
+
+        await Assert.ThrowsAsync<NotSupportedException>(() =>
+            service.ExchangeCodeAsync(Platform.Blog, "code", state, CancellationToken.None));
+    }
+
+    [Fact]
+    public async Task OAuthService_ExchangeCode_DefaultsPurposeToPublishing()
+    {
+        // Purpose does not exist yet (section-02). This asserts the current default behavior: an exchanged
+        // credential is stored active with the platform's publishing scope, exactly as today.
+        var service = CreateService();
+        var authUrl = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CancellationToken.None);
+        var state = HttpUtility.ParseQueryString(new Uri(authUrl).Query)["state"]!;
+
+        SetupHttpResponse(JsonSerializer.Serialize(new
+        {
+            access_token = "li-access-token",
+            refresh_token = "li-refresh-token",
+            expires_in = 5184000
+        }));
+
+        var credential = await service.ExchangeCodeAsync(Platform.LinkedIn, "auth-code", state, CancellationToken.None);
+
+        Assert.True(credential.IsActive);
+        Assert.Equal("openid profile w_member_social", credential.Scopes);
+    }
+
     public void Dispose()
     {
         _httpClient.Dispose();
