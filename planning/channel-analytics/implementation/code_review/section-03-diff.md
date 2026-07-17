diff --git a/src/PBA.Api/Endpoints/OAuthEndpoints.cs b/src/PBA.Api/Endpoints/OAuthEndpoints.cs
index 04ff79b..e1bb74e 100644
--- a/src/PBA.Api/Endpoints/OAuthEndpoints.cs
+++ b/src/PBA.Api/Endpoints/OAuthEndpoints.cs
@@ -7,7 +7,8 @@ namespace PBA.Api.Endpoints;
 
 public static class OAuthEndpoints
 {
-    private static readonly HashSet<Platform> OAuthPlatforms = [Platform.LinkedIn, Platform.Twitter];
+    private static readonly HashSet<Platform> OAuthPlatforms =
+        [Platform.LinkedIn, Platform.Twitter, Platform.YouTube, Platform.Instagram, Platform.TikTok];
 
     public static void MapOAuthEndpoints(this IEndpointRouteBuilder app)
     {
@@ -15,6 +16,7 @@ public static class OAuthEndpoints
 
         group.MapGet("/{platform}/authorize", async (
             string platform,
+            string? purpose,
             IOAuthService oauthService,
             CancellationToken ct) =>
         {
@@ -24,7 +26,10 @@ public static class OAuthEndpoints
             if (!OAuthPlatforms.Contains(p))
                 return Results.BadRequest($"{p} does not support OAuth. Use credential storage instead.");
 
-            var authUrl = await oauthService.GetAuthorizationUrlAsync(p, ct);
+            if (!TryParsePurpose(purpose, out var credentialPurpose))
+                return Results.BadRequest($"Invalid purpose '{purpose}'. Use 'publishing' or 'analytics'.");
+
+            var authUrl = await oauthService.GetAuthorizationUrlAsync(p, credentialPurpose, ct);
             return Results.Redirect(authUrl);
         });
 
@@ -101,4 +106,18 @@ public static class OAuthEndpoints
             return Results.Ok();
         });
     }
+
+    // Absent/empty -> Publishing (the pre-analytics default). "analytics"/"publishing" (any case) map
+    // to their enum; anything else is rejected so a typo never silently stores a Publishing credential.
+    private static bool TryParsePurpose(string? purpose, out CredentialPurpose result)
+    {
+        if (string.IsNullOrWhiteSpace(purpose))
+        {
+            result = CredentialPurpose.Publishing;
+            return true;
+        }
+
+        return Enum.TryParse(purpose, ignoreCase: true, out result)
+            && Enum.IsDefined(result);
+    }
 }
diff --git a/src/PBA.Api/appsettings.json b/src/PBA.Api/appsettings.json
index 3fe40ea..8a38f90 100644
--- a/src/PBA.Api/appsettings.json
+++ b/src/PBA.Api/appsettings.json
@@ -37,6 +37,15 @@
     },
     "Twitter": {
       "Enabled": false
+    },
+    "YouTube": {
+      "Enabled": false
+    },
+    "Instagram": {
+      "Enabled": false
+    },
+    "TikTok": {
+      "Enabled": false
     }
   },
   "GoogleAnalytics": {
diff --git a/src/PBA.Application/Common/Interfaces/IOAuthService.cs b/src/PBA.Application/Common/Interfaces/IOAuthService.cs
index 3d5e726..c960faf 100644
--- a/src/PBA.Application/Common/Interfaces/IOAuthService.cs
+++ b/src/PBA.Application/Common/Interfaces/IOAuthService.cs
@@ -6,7 +6,9 @@ namespace PBA.Application.Common.Interfaces;
 
 public interface IOAuthService
 {
-    Task<string> GetAuthorizationUrlAsync(Platform platform, CancellationToken ct);
+    // Purpose is captured here and persisted in OAuth state; the callback reads it back to stamp the
+    // credential's CredentialPurpose. Defaults to Publishing (the pre-analytics behavior).
+    Task<string> GetAuthorizationUrlAsync(Platform platform, CredentialPurpose purpose, CancellationToken ct);
     Task<PlatformCredential> ExchangeCodeAsync(Platform platform, string code, string state, CancellationToken ct);
     Task<Result<string>> RefreshTokenAsync(PlatformCredential credential, CancellationToken ct);
 }
diff --git a/src/PBA.Infrastructure/Configuration/InstagramOAuthOptions.cs b/src/PBA.Infrastructure/Configuration/InstagramOAuthOptions.cs
new file mode 100644
index 0000000..bc2b1d5
--- /dev/null
+++ b/src/PBA.Infrastructure/Configuration/InstagramOAuthOptions.cs
@@ -0,0 +1,11 @@
+namespace PBA.Infrastructure.Configuration;
+
+public sealed class InstagramOAuthOptions
+{
+    public const string SectionName = "Publishing:Instagram";
+
+    public bool Enabled { get; init; }
+    public required string ClientId { get; init; }
+    public required string ClientSecret { get; init; }
+    public required string RedirectUri { get; init; }
+}
diff --git a/src/PBA.Infrastructure/Configuration/TikTokOAuthOptions.cs b/src/PBA.Infrastructure/Configuration/TikTokOAuthOptions.cs
new file mode 100644
index 0000000..15ecad0
--- /dev/null
+++ b/src/PBA.Infrastructure/Configuration/TikTokOAuthOptions.cs
@@ -0,0 +1,11 @@
+namespace PBA.Infrastructure.Configuration;
+
+public sealed class TikTokOAuthOptions
+{
+    public const string SectionName = "Publishing:TikTok";
+
+    public bool Enabled { get; init; }
+    public required string ClientId { get; init; }
+    public required string ClientSecret { get; init; }
+    public required string RedirectUri { get; init; }
+}
diff --git a/src/PBA.Infrastructure/Configuration/YouTubeOAuthOptions.cs b/src/PBA.Infrastructure/Configuration/YouTubeOAuthOptions.cs
new file mode 100644
index 0000000..9c2764e
--- /dev/null
+++ b/src/PBA.Infrastructure/Configuration/YouTubeOAuthOptions.cs
@@ -0,0 +1,13 @@
+namespace PBA.Infrastructure.Configuration;
+
+// SectionName keeps the Publishing: prefix (it names the app registration, not the token purpose).
+public sealed class YouTubeOAuthOptions
+{
+    public const string SectionName = "Publishing:YouTube";
+
+    public bool Enabled { get; init; }
+    public required string ClientId { get; init; }
+    public required string ClientSecret { get; init; }
+    public required string RedirectUri { get; init; }
+    public string? ApiKey { get; init; }
+}
diff --git a/src/PBA.Infrastructure/DependencyInjection.cs b/src/PBA.Infrastructure/DependencyInjection.cs
index 2bc8817..bb34c82 100644
--- a/src/PBA.Infrastructure/DependencyInjection.cs
+++ b/src/PBA.Infrastructure/DependencyInjection.cs
@@ -137,6 +137,9 @@ public static class DependencyInjection
         services.Configure<SubstackOptions>(configuration.GetSection(SubstackOptions.SectionName));
         services.Configure<LinkedInOptions>(configuration.GetSection(LinkedInOptions.SectionName));
         services.Configure<TwitterOptions>(configuration.GetSection(TwitterOptions.SectionName));
+        services.Configure<YouTubeOAuthOptions>(configuration.GetSection(YouTubeOAuthOptions.SectionName));
+        services.Configure<InstagramOAuthOptions>(configuration.GetSection(InstagramOAuthOptions.SectionName));
+        services.Configure<TikTokOAuthOptions>(configuration.GetSection(TikTokOAuthOptions.SectionName));
         services.Configure<TransformerOptions>(configuration.GetSection(TransformerOptions.SectionName));
         services.Configure<ComfyUiOptions>(configuration.GetSection(ComfyUiOptions.SectionName));
 
@@ -147,6 +150,9 @@ public static class DependencyInjection
         // Keyed OAuth providers (resolved by the OAuthService coordinator)
         services.AddKeyedScoped<IOAuthProvider, LinkedInOAuthProvider>(Platform.LinkedIn);
         services.AddKeyedScoped<IOAuthProvider, TwitterOAuthProvider>(Platform.Twitter);
+        services.AddKeyedScoped<IOAuthProvider, YouTubeOAuthProvider>(Platform.YouTube);
+        services.AddKeyedScoped<IOAuthProvider, InstagramOAuthProvider>(Platform.Instagram);
+        services.AddKeyedScoped<IOAuthProvider, TikTokOAuthProvider>(Platform.TikTok);
 
         // Content transformation
         services.AddScoped<IContentTransformer, ContentTransformer>();
diff --git a/src/PBA.Infrastructure/Security/IOAuthProvider.cs b/src/PBA.Infrastructure/Security/IOAuthProvider.cs
index 0c6f003..0a77564 100644
--- a/src/PBA.Infrastructure/Security/IOAuthProvider.cs
+++ b/src/PBA.Infrastructure/Security/IOAuthProvider.cs
@@ -15,9 +15,10 @@ public interface IOAuthProvider
 
     Task<OAuthTokenResult> ExchangeCodeAsync(string code, OAuthStateEntry state, CancellationToken ct);
 
-    // Takes the full credential (not a bare refresh-token string) so future providers with no separate
-    // refresh token can implement their own refresh. Returns plaintext tokens for the coordinator to encrypt.
-    Task<Result<OAuthTokenResult>> RefreshAsync(PlatformCredential credential, CancellationToken ct);
+    // Takes the full credential (not a bare refresh-token string) so providers with no separate refresh
+    // token (e.g. Instagram) can implement their own refresh. Returns plaintext tokens for the coordinator
+    // to encrypt on success, or a Revoked/Transient reason on failure (the poller keys on that reason).
+    Task<OAuthRefreshResult> RefreshAsync(PlatformCredential credential, CancellationToken ct);
 
     // Provider-specific proactive refresh window. Declared for the poller (section-05); LinkedIn/Twitter
     // mirror the effective window their connectors use today.
diff --git a/src/PBA.Infrastructure/Security/OAuthContracts.cs b/src/PBA.Infrastructure/Security/OAuthContracts.cs
index f247ab8..e4e790c 100644
--- a/src/PBA.Infrastructure/Security/OAuthContracts.cs
+++ b/src/PBA.Infrastructure/Security/OAuthContracts.cs
@@ -15,10 +15,33 @@ public sealed record OAuthTokenResult(
     int? RefreshTokenExpiresIn,
     string Scopes);
 
-// RefreshAsync failure classification. Section-03 adds full revoked-vs-transient mapping per provider;
-// LinkedIn/Twitter in this section preserve today's behavior (any non-success refresh deactivates).
+// RefreshAsync failure classification. The poller (section-05) deactivates a credential only on Revoked,
+// never on Transient (network / 5xx / 429).
 public enum RefreshFailureReason
 {
     Revoked,
     Transient
 }
+
+// Provider refresh outcome. The domain Result<T> can't carry a RefreshFailureReason, so refresh uses this
+// dedicated envelope: success carries the rotated tokens; failure carries the reason the poller keys on.
+public sealed record OAuthRefreshResult
+{
+    public bool IsSuccess { get; }
+    public OAuthTokenResult? Tokens { get; }
+    public RefreshFailureReason? FailureReason { get; }
+    public string? Error { get; }
+
+    private OAuthRefreshResult(bool isSuccess, OAuthTokenResult? tokens, RefreshFailureReason? reason, string? error)
+    {
+        IsSuccess = isSuccess;
+        Tokens = tokens;
+        FailureReason = reason;
+        Error = error;
+    }
+
+    public static OAuthRefreshResult Success(OAuthTokenResult tokens) => new(true, tokens, null, null);
+
+    public static OAuthRefreshResult Fail(RefreshFailureReason reason, string error) =>
+        new(false, null, reason, error);
+}
diff --git a/src/PBA.Infrastructure/Security/OAuthProviders/InstagramOAuthProvider.cs b/src/PBA.Infrastructure/Security/OAuthProviders/InstagramOAuthProvider.cs
new file mode 100644
index 0000000..4117910
--- /dev/null
+++ b/src/PBA.Infrastructure/Security/OAuthProviders/InstagramOAuthProvider.cs
@@ -0,0 +1,160 @@
+using System.Net;
+using System.Text.Json;
+using System.Web;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+
+namespace PBA.Infrastructure.Security.OAuthProviders;
+
+// Instagram-Login analytics OAuth. There is NO separate refresh token: the exchange trades the code for a
+// short-lived token then immediately for a ~60-day long-lived token, and "refresh" extends that long-lived
+// token via ig_refresh_token using the CURRENT access token. Meta codes 190/458/463 signal a revoked token.
+public sealed class InstagramOAuthProvider(
+    IHttpClientFactory httpClientFactory,
+    IOptions<InstagramOAuthOptions> options,
+    ITokenEncryptor encryptor,
+    ILogger<InstagramOAuthProvider> logger) : IOAuthProvider
+{
+    private const string Scope = "instagram_business_basic instagram_business_manage_insights";
+    private const string AuthorizeBase = "https://www.instagram.com/oauth/authorize";
+    private const string ShortTokenEndpoint = "https://api.instagram.com/oauth/access_token";
+    private const string GraphBase = "https://graph.instagram.com";
+    private static readonly TimeSpan RefreshLead = TimeSpan.FromDays(50);
+
+    public Platform Platform => Platform.Instagram;
+
+    public AuthorizationRequest BuildAuthorization(string state)
+    {
+        var o = options.Value;
+        var qs = HttpUtility.ParseQueryString(string.Empty);
+        qs["client_id"] = o.ClientId;
+        qs["redirect_uri"] = o.RedirectUri;
+        qs["scope"] = Scope;
+        qs["response_type"] = "code";
+        qs["state"] = state;
+
+        return new AuthorizationRequest($"{AuthorizeBase}?{qs}", new OAuthStateAdditions());
+    }
+
+    public async Task<OAuthTokenResult> ExchangeCodeAsync(string code, OAuthStateEntry state, CancellationToken ct)
+    {
+        var o = options.Value;
+        var client = httpClientFactory.CreateClient();
+
+        // Step 1: code -> short-lived token.
+        var shortParams = new Dictionary<string, string>
+        {
+            ["client_id"] = o.ClientId,
+            ["client_secret"] = o.ClientSecret,
+            ["grant_type"] = "authorization_code",
+            ["redirect_uri"] = o.RedirectUri,
+            ["code"] = code
+        };
+        var shortResponse = await client.PostAsync(ShortTokenEndpoint, new FormUrlEncodedContent(shortParams), ct);
+        if (!shortResponse.IsSuccessStatusCode)
+        {
+            var errorBody = await shortResponse.Content.ReadAsStringAsync(ct);
+            logger.LogError("Instagram short-token exchange failed: {Status} {Body}", shortResponse.StatusCode, errorBody);
+            throw new InvalidOperationException($"Instagram token exchange failed: {shortResponse.StatusCode}");
+        }
+        var shortJson = await shortResponse.Content.ReadAsStringAsync(ct);
+        var shortToken = JsonSerializer.Deserialize<JsonElement>(shortJson).GetProperty("access_token").GetString()!;
+
+        // Step 2: short-lived -> long-lived (~60 day) token.
+        var longUrl = $"{GraphBase}/access_token?grant_type=ig_exchange_token" +
+                      $"&client_secret={Uri.EscapeDataString(o.ClientSecret)}" +
+                      $"&access_token={Uri.EscapeDataString(shortToken)}";
+        var longResponse = await client.GetAsync(longUrl, ct);
+        if (!longResponse.IsSuccessStatusCode)
+        {
+            var errorBody = await longResponse.Content.ReadAsStringAsync(ct);
+            logger.LogError("Instagram long-token exchange failed: {Status} {Body}", longResponse.StatusCode, errorBody);
+            throw new InvalidOperationException($"Instagram long-lived token exchange failed: {longResponse.StatusCode}");
+        }
+        var longJson = await longResponse.Content.ReadAsStringAsync(ct);
+        var longData = JsonSerializer.Deserialize<JsonElement>(longJson);
+
+        return new OAuthTokenResult(
+            AccessToken: longData.GetProperty("access_token").GetString()!,
+            RefreshToken: null,   // Instagram-Login has no separate refresh token
+            ExpiresIn: longData.GetProperty("expires_in").GetInt32(),
+            RefreshTokenExpiresIn: null,
+            Scopes: Scope);
+    }
+
+    public async Task<OAuthRefreshResult> RefreshAsync(PlatformCredential credential, CancellationToken ct)
+    {
+        // Refresh extends the long-lived token using the CURRENT access token (there is no refresh token).
+        var accessToken = encryptor.Decrypt(credential.EncryptedAccessToken);
+        var url = $"{GraphBase}/refresh_access_token?grant_type=ig_refresh_token" +
+                  $"&access_token={Uri.EscapeDataString(accessToken)}";
+
+        HttpResponseMessage response;
+        string body;
+        try
+        {
+            var client = httpClientFactory.CreateClient();
+            response = await client.GetAsync(url, ct);
+            body = await response.Content.ReadAsStringAsync(ct);
+        }
+        catch (HttpRequestException ex)
+        {
+            return OAuthRefreshResult.Fail(RefreshFailureReason.Transient, ex.Message);
+        }
+        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
+        {
+            return OAuthRefreshResult.Fail(RefreshFailureReason.Transient, ex.Message);
+        }
+
+        if (!response.IsSuccessStatusCode)
+        {
+            logger.LogWarning("Instagram token refresh failed: {Status} {Body}", response.StatusCode, body);
+            if (IsTransientStatus(response.StatusCode))
+                return OAuthRefreshResult.Fail(RefreshFailureReason.Transient, $"Transient refresh error: {response.StatusCode}");
+            return IsMetaRevoked(body)
+                ? OAuthRefreshResult.Fail(RefreshFailureReason.Revoked, "Meta revoked token")
+                : OAuthRefreshResult.Fail(RefreshFailureReason.Transient, $"Refresh error: {response.StatusCode}");
+        }
+
+        var data = JsonSerializer.Deserialize<JsonElement>(body);
+        return OAuthRefreshResult.Success(new OAuthTokenResult(
+            AccessToken: data.GetProperty("access_token").GetString()!,
+            RefreshToken: null,
+            ExpiresIn: data.GetProperty("expires_in").GetInt32(),
+            RefreshTokenExpiresIn: null,
+            Scopes: Scope));
+    }
+
+    public bool NeedsRefresh(PlatformCredential credential, DateTimeOffset now) =>
+        credential.AccessTokenExpiresAt.HasValue &&
+        now >= credential.AccessTokenExpiresAt.Value - RefreshLead;
+
+    private static bool IsTransientStatus(HttpStatusCode status) =>
+        (int)status == 429 || (int)status >= 500;
+
+    // Meta OAuthException code 190 / subcodes 458 / 463 indicate an invalidated (revoked/expired) token.
+    private static bool IsMetaRevoked(string body)
+    {
+        try
+        {
+            var root = JsonSerializer.Deserialize<JsonElement>(body);
+            if (!root.TryGetProperty("error", out var error))
+                return false;
+
+            if (error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number
+                && code.GetInt32() == 190)
+                return true;
+
+            return error.TryGetProperty("error_subcode", out var subcode) && subcode.ValueKind == JsonValueKind.Number
+                && subcode.GetInt32() is 458 or 463;
+        }
+        catch (JsonException)
+        {
+            return false;
+        }
+    }
+}
diff --git a/src/PBA.Infrastructure/Security/OAuthProviders/LinkedInOAuthProvider.cs b/src/PBA.Infrastructure/Security/OAuthProviders/LinkedInOAuthProvider.cs
index 11845fa..569fd41 100644
--- a/src/PBA.Infrastructure/Security/OAuthProviders/LinkedInOAuthProvider.cs
+++ b/src/PBA.Infrastructure/Security/OAuthProviders/LinkedInOAuthProvider.cs
@@ -3,7 +3,6 @@ using System.Web;
 using Microsoft.Extensions.Logging;
 using Microsoft.Extensions.Options;
 using PBA.Application.Common.Interfaces;
-using PBA.Domain.Common;
 using PBA.Domain.Entities;
 using PBA.Domain.Enums;
 using PBA.Infrastructure.Configuration;
@@ -72,7 +71,7 @@ public sealed class LinkedInOAuthProvider(
             Scopes: Scope);
     }
 
-    public async Task<Result<OAuthTokenResult>> RefreshAsync(PlatformCredential credential, CancellationToken ct)
+    public async Task<OAuthRefreshResult> RefreshAsync(PlatformCredential credential, CancellationToken ct)
     {
         var li = options.Value;
         var refreshToken = encryptor.Decrypt(credential.EncryptedRefreshToken!);
@@ -98,13 +97,15 @@ public sealed class LinkedInOAuthProvider(
             var errorBody = await response.Content.ReadAsStringAsync(ct);
             logger.LogWarning("Token refresh failed for {Platform}: {Status} {Body}",
                 Platform, response.StatusCode, errorBody);
-            return Result<OAuthTokenResult>.Fail($"Token refresh failed: {response.StatusCode}");
+            // LinkedIn has no distinct revoked-vs-transient signal wired here; default to Transient.
+            return OAuthRefreshResult.Fail(RefreshFailureReason.Transient,
+                $"Token refresh failed: {response.StatusCode}");
         }
 
         var json = await response.Content.ReadAsStringAsync(ct);
         var tokenData = JsonSerializer.Deserialize<JsonElement>(json);
 
-        return Result<OAuthTokenResult>.Success(new OAuthTokenResult(
+        return OAuthRefreshResult.Success(new OAuthTokenResult(
             AccessToken: tokenData.GetProperty("access_token").GetString()!,
             RefreshToken: tokenData.TryGetProperty("refresh_token", out var newRefreshProp)
                 ? newRefreshProp.GetString() : null,
diff --git a/src/PBA.Infrastructure/Security/OAuthProviders/TikTokOAuthProvider.cs b/src/PBA.Infrastructure/Security/OAuthProviders/TikTokOAuthProvider.cs
new file mode 100644
index 0000000..a2453c3
--- /dev/null
+++ b/src/PBA.Infrastructure/Security/OAuthProviders/TikTokOAuthProvider.cs
@@ -0,0 +1,143 @@
+using System.Net;
+using System.Text.Json;
+using System.Web;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+
+namespace PBA.Infrastructure.Security.OAuthProviders;
+
+// TikTok analytics OAuth. Refresh ROTATES the refresh token — the new refresh token in the response must be
+// persisted (carried on OAuthTokenResult.RefreshToken). TikTok returns errors in the JSON body (sometimes
+// with a 200), so success is detected by the presence of access_token. access_token_invalid => revoked.
+public sealed class TikTokOAuthProvider(
+    IHttpClientFactory httpClientFactory,
+    IOptions<TikTokOAuthOptions> options,
+    ITokenEncryptor encryptor,
+    ILogger<TikTokOAuthProvider> logger) : IOAuthProvider
+{
+    private const string Scope = "user.info.stats,video.list";
+    private const string AuthorizeBase = "https://www.tiktok.com/v2/auth/authorize/";
+    private const string TokenEndpoint = "https://open.tiktokapis.com/v2/oauth/token/";
+    private static readonly TimeSpan RefreshLead = TimeSpan.FromHours(1);
+
+    public Platform Platform => Platform.TikTok;
+
+    public AuthorizationRequest BuildAuthorization(string state)
+    {
+        var o = options.Value;
+        var qs = HttpUtility.ParseQueryString(string.Empty);
+        qs["client_key"] = o.ClientId;
+        qs["scope"] = Scope;
+        qs["response_type"] = "code";
+        qs["redirect_uri"] = o.RedirectUri;
+        qs["state"] = state;
+
+        return new AuthorizationRequest($"{AuthorizeBase}?{qs}", new OAuthStateAdditions());
+    }
+
+    public async Task<OAuthTokenResult> ExchangeCodeAsync(string code, OAuthStateEntry state, CancellationToken ct)
+    {
+        var o = options.Value;
+        var parameters = new Dictionary<string, string>
+        {
+            ["client_key"] = o.ClientId,
+            ["client_secret"] = o.ClientSecret,
+            ["code"] = code,
+            ["grant_type"] = "authorization_code",
+            ["redirect_uri"] = o.RedirectUri
+        };
+
+        var client = httpClientFactory.CreateClient();
+        var response = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(parameters), ct);
+        var body = await response.Content.ReadAsStringAsync(ct);
+
+        var data = JsonSerializer.Deserialize<JsonElement>(body);
+        if (!data.TryGetProperty("access_token", out var accessToken) || accessToken.ValueKind != JsonValueKind.String)
+        {
+            logger.LogError("TikTok token exchange failed: {Status} {Body}", response.StatusCode, body);
+            throw new InvalidOperationException($"TikTok token exchange failed: {response.StatusCode}");
+        }
+
+        return new OAuthTokenResult(
+            AccessToken: accessToken.GetString()!,
+            RefreshToken: data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
+            ExpiresIn: data.GetProperty("expires_in").GetInt32(),
+            RefreshTokenExpiresIn: data.TryGetProperty("refresh_expires_in", out var re) && re.ValueKind == JsonValueKind.Number
+                ? re.GetInt32() : null,
+            Scopes: Scope);
+    }
+
+    public async Task<OAuthRefreshResult> RefreshAsync(PlatformCredential credential, CancellationToken ct)
+    {
+        var o = options.Value;
+        var refreshToken = encryptor.Decrypt(credential.EncryptedRefreshToken!);
+
+        var parameters = new Dictionary<string, string>
+        {
+            ["client_key"] = o.ClientId,
+            ["client_secret"] = o.ClientSecret,
+            ["grant_type"] = "refresh_token",
+            ["refresh_token"] = refreshToken
+        };
+
+        HttpResponseMessage response;
+        string body;
+        try
+        {
+            var client = httpClientFactory.CreateClient();
+            response = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(parameters), ct);
+            body = await response.Content.ReadAsStringAsync(ct);
+        }
+        catch (HttpRequestException ex)
+        {
+            return OAuthRefreshResult.Fail(RefreshFailureReason.Transient, ex.Message);
+        }
+        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
+        {
+            return OAuthRefreshResult.Fail(RefreshFailureReason.Transient, ex.Message);
+        }
+
+        if (IsTransientStatus(response.StatusCode))
+            return OAuthRefreshResult.Fail(RefreshFailureReason.Transient, $"Transient refresh error: {response.StatusCode}");
+
+        JsonElement data;
+        try
+        {
+            data = JsonSerializer.Deserialize<JsonElement>(body);
+        }
+        catch (JsonException)
+        {
+            return OAuthRefreshResult.Fail(RefreshFailureReason.Transient, "Unparseable refresh response");
+        }
+
+        if (data.TryGetProperty("access_token", out var accessToken) && accessToken.ValueKind == JsonValueKind.String)
+        {
+            return OAuthRefreshResult.Success(new OAuthTokenResult(
+                AccessToken: accessToken.GetString()!,
+                // TikTok rotates the refresh token — persist the new one.
+                RefreshToken: data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
+                ExpiresIn: data.GetProperty("expires_in").GetInt32(),
+                RefreshTokenExpiresIn: data.TryGetProperty("refresh_expires_in", out var re) && re.ValueKind == JsonValueKind.Number
+                    ? re.GetInt32() : null,
+                Scopes: Scope));
+        }
+
+        var errorCode = data.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String
+            ? errEl.GetString() : null;
+        logger.LogWarning("TikTok token refresh failed: {Status} {Error} {Body}", response.StatusCode, errorCode, body);
+        return errorCode == "access_token_invalid"
+            ? OAuthRefreshResult.Fail(RefreshFailureReason.Revoked, errorCode)
+            : OAuthRefreshResult.Fail(RefreshFailureReason.Transient, errorCode ?? "Refresh failed");
+    }
+
+    public bool NeedsRefresh(PlatformCredential credential, DateTimeOffset now) =>
+        credential.AccessTokenExpiresAt.HasValue &&
+        now >= credential.AccessTokenExpiresAt.Value - RefreshLead;
+
+    private static bool IsTransientStatus(HttpStatusCode status) =>
+        (int)status == 429 || (int)status >= 500;
+}
diff --git a/src/PBA.Infrastructure/Security/OAuthProviders/TwitterOAuthProvider.cs b/src/PBA.Infrastructure/Security/OAuthProviders/TwitterOAuthProvider.cs
index 1c27b8e..b1915ce 100644
--- a/src/PBA.Infrastructure/Security/OAuthProviders/TwitterOAuthProvider.cs
+++ b/src/PBA.Infrastructure/Security/OAuthProviders/TwitterOAuthProvider.cs
@@ -6,7 +6,6 @@ using System.Web;
 using Microsoft.Extensions.Logging;
 using Microsoft.Extensions.Options;
 using PBA.Application.Common.Interfaces;
-using PBA.Domain.Common;
 using PBA.Domain.Entities;
 using PBA.Domain.Enums;
 using PBA.Infrastructure.Configuration;
@@ -87,7 +86,7 @@ public sealed class TwitterOAuthProvider(
             Scopes: Scope);
     }
 
-    public async Task<Result<OAuthTokenResult>> RefreshAsync(PlatformCredential credential, CancellationToken ct)
+    public async Task<OAuthRefreshResult> RefreshAsync(PlatformCredential credential, CancellationToken ct)
     {
         var tw = options.Value;
         var refreshToken = encryptor.Decrypt(credential.EncryptedRefreshToken!);
@@ -114,13 +113,15 @@ public sealed class TwitterOAuthProvider(
             var errorBody = await response.Content.ReadAsStringAsync(ct);
             logger.LogWarning("Token refresh failed for {Platform}: {Status} {Body}",
                 Platform, response.StatusCode, errorBody);
-            return Result<OAuthTokenResult>.Fail($"Token refresh failed: {response.StatusCode}");
+            // Twitter has no distinct revoked-vs-transient signal wired here; default to Transient.
+            return OAuthRefreshResult.Fail(RefreshFailureReason.Transient,
+                $"Token refresh failed: {response.StatusCode}");
         }
 
         var json = await response.Content.ReadAsStringAsync(ct);
         var tokenData = JsonSerializer.Deserialize<JsonElement>(json);
 
-        return Result<OAuthTokenResult>.Success(new OAuthTokenResult(
+        return OAuthRefreshResult.Success(new OAuthTokenResult(
             AccessToken: tokenData.GetProperty("access_token").GetString()!,
             RefreshToken: tokenData.TryGetProperty("refresh_token", out var newRefreshProp)
                 ? newRefreshProp.GetString() : null,
diff --git a/src/PBA.Infrastructure/Security/OAuthProviders/YouTubeOAuthProvider.cs b/src/PBA.Infrastructure/Security/OAuthProviders/YouTubeOAuthProvider.cs
new file mode 100644
index 0000000..786011a
--- /dev/null
+++ b/src/PBA.Infrastructure/Security/OAuthProviders/YouTubeOAuthProvider.cs
@@ -0,0 +1,133 @@
+using System.Net;
+using System.Text.Json;
+using System.Web;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PBA.Application.Common.Interfaces;
+using PBA.Domain.Entities;
+using PBA.Domain.Enums;
+using PBA.Infrastructure.Configuration;
+
+namespace PBA.Infrastructure.Security.OAuthProviders;
+
+// YouTube (Google) analytics OAuth. Standard refresh-token grant; Google signals a revoked token with
+// invalid_grant. NeedsRefresh window ~1h before expiry.
+public sealed class YouTubeOAuthProvider(
+    IHttpClientFactory httpClientFactory,
+    IOptions<YouTubeOAuthOptions> options,
+    ITokenEncryptor encryptor,
+    ILogger<YouTubeOAuthProvider> logger) : IOAuthProvider
+{
+    private const string Scope =
+        "https://www.googleapis.com/auth/yt-analytics.readonly https://www.googleapis.com/auth/youtube.readonly";
+    private const string AuthorizeBase = "https://accounts.google.com/o/oauth2/v2/auth";
+    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
+    private static readonly TimeSpan RefreshLead = TimeSpan.FromHours(1);
+
+    public Platform Platform => Platform.YouTube;
+
+    public AuthorizationRequest BuildAuthorization(string state)
+    {
+        var o = options.Value;
+        var qs = HttpUtility.ParseQueryString(string.Empty);
+        qs["response_type"] = "code";
+        qs["client_id"] = o.ClientId;
+        qs["redirect_uri"] = o.RedirectUri;
+        qs["scope"] = Scope;
+        qs["state"] = state;
+        qs["access_type"] = "offline";   // request a refresh token
+        qs["prompt"] = "consent";        // guarantee a refresh token is returned
+
+        return new AuthorizationRequest($"{AuthorizeBase}?{qs}", new OAuthStateAdditions());
+    }
+
+    public async Task<OAuthTokenResult> ExchangeCodeAsync(string code, OAuthStateEntry state, CancellationToken ct)
+    {
+        var o = options.Value;
+        var parameters = new Dictionary<string, string>
+        {
+            ["grant_type"] = "authorization_code",
+            ["code"] = code,
+            ["redirect_uri"] = o.RedirectUri,
+            ["client_id"] = o.ClientId,
+            ["client_secret"] = o.ClientSecret
+        };
+
+        var client = httpClientFactory.CreateClient();
+        var response = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(parameters), ct);
+
+        if (!response.IsSuccessStatusCode)
+        {
+            var errorBody = await response.Content.ReadAsStringAsync(ct);
+            logger.LogError("YouTube token exchange failed: {Status} {Body}", response.StatusCode, errorBody);
+            throw new InvalidOperationException($"YouTube token exchange failed: {response.StatusCode}");
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
+    public async Task<OAuthRefreshResult> RefreshAsync(PlatformCredential credential, CancellationToken ct)
+    {
+        var o = options.Value;
+        var refreshToken = encryptor.Decrypt(credential.EncryptedRefreshToken!);
+
+        var parameters = new Dictionary<string, string>
+        {
+            ["grant_type"] = "refresh_token",
+            ["refresh_token"] = refreshToken,
+            ["client_id"] = o.ClientId,
+            ["client_secret"] = o.ClientSecret
+        };
+
+        HttpResponseMessage response;
+        string body;
+        try
+        {
+            var client = httpClientFactory.CreateClient();
+            response = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(parameters), ct);
+            body = await response.Content.ReadAsStringAsync(ct);
+        }
+        catch (HttpRequestException ex)
+        {
+            return OAuthRefreshResult.Fail(RefreshFailureReason.Transient, ex.Message);
+        }
+        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
+        {
+            return OAuthRefreshResult.Fail(RefreshFailureReason.Transient, ex.Message);
+        }
+
+        if (!response.IsSuccessStatusCode)
+        {
+            logger.LogWarning("YouTube token refresh failed: {Status} {Body}", response.StatusCode, body);
+            if (IsTransientStatus(response.StatusCode))
+                return OAuthRefreshResult.Fail(RefreshFailureReason.Transient, $"Transient refresh error: {response.StatusCode}");
+            // Google marks a revoked/expired refresh token with invalid_grant.
+            return body.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
+                ? OAuthRefreshResult.Fail(RefreshFailureReason.Revoked, "invalid_grant")
+                : OAuthRefreshResult.Fail(RefreshFailureReason.Transient, $"Refresh error: {response.StatusCode}");
+        }
+
+        var data = JsonSerializer.Deserialize<JsonElement>(body);
+        return OAuthRefreshResult.Success(new OAuthTokenResult(
+            AccessToken: data.GetProperty("access_token").GetString()!,
+            RefreshToken: data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
+            ExpiresIn: data.GetProperty("expires_in").GetInt32(),
+            RefreshTokenExpiresIn: null,
+            Scopes: Scope));
+    }
+
+    public bool NeedsRefresh(PlatformCredential credential, DateTimeOffset now) =>
+        credential.AccessTokenExpiresAt.HasValue &&
+        now >= credential.AccessTokenExpiresAt.Value - RefreshLead;
+
+    private static bool IsTransientStatus(HttpStatusCode status) =>
+        (int)status == 429 || (int)status >= 500;
+}
diff --git a/src/PBA.Infrastructure/Security/OAuthService.cs b/src/PBA.Infrastructure/Security/OAuthService.cs
index e32dc0a..44dffc6 100644
--- a/src/PBA.Infrastructure/Security/OAuthService.cs
+++ b/src/PBA.Infrastructure/Security/OAuthService.cs
@@ -21,7 +21,7 @@ public sealed class OAuthService(
     private static readonly ConcurrentDictionary<string, OAuthStateEntry> StateStore = new();
     private const int MaxPendingStates = 1000;
 
-    public Task<string> GetAuthorizationUrlAsync(Platform platform, CancellationToken ct)
+    public Task<string> GetAuthorizationUrlAsync(Platform platform, CredentialPurpose purpose, CancellationToken ct)
     {
         CleanExpiredStates();
 
@@ -32,7 +32,7 @@ public sealed class OAuthService(
         var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
         var authorization = provider.BuildAuthorization(state);
 
-        StateStore[state] = new OAuthStateEntry(platform, authorization.Additions.CodeVerifier);
+        StateStore[state] = new OAuthStateEntry(platform, authorization.Additions.CodeVerifier, purpose);
 
         return Task.FromResult(authorization.Url);
     }
@@ -46,12 +46,15 @@ public sealed class OAuthService(
         var provider = ResolveProvider(platform);
         var tokenResult = await provider.ExchangeCodeAsync(code, stateEntry, ct);
 
+        // Find/create by (Platform, Purpose): an Analytics flow must not overwrite the Publishing credential
+        // (and vice versa). The purpose is the one captured at authorize time and carried in the state entry.
+        var purpose = stateEntry.Purpose;
         var credential = await db.PlatformCredentials
-            .FirstOrDefaultAsync(c => c.Platform == platform, ct);
+            .FirstOrDefaultAsync(c => c.Platform == platform && c.Purpose == purpose, ct);
 
         if (credential is null)
         {
-            credential = new PlatformCredential { Platform = platform };
+            credential = new PlatformCredential { Platform = platform, Purpose = purpose };
             db.PlatformCredentials.Add(credential);
         }
 
@@ -89,13 +92,15 @@ public sealed class OAuthService(
 
         if (!refreshResult.IsSuccess)
         {
+            // Coordinator (publishing path) preserves deactivate-on-any-failure. The poller (section-05)
+            // implements revoked-only deactivation by reading refreshResult.FailureReason directly.
             credential.IsActive = false;
             credential.UpdatedAt = DateTimeOffset.UtcNow;
             await db.SaveChangesAsync(ct);
-            return Result<string>.Fail([.. refreshResult.Errors]);
+            return Result<string>.Fail(refreshResult.Error ?? "Token refresh failed");
         }
 
-        var tokens = refreshResult.Value!;
+        var tokens = refreshResult.Tokens!;
         credential.EncryptedAccessToken = encryptor.Encrypt(tokens.AccessToken);
         credential.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn);
 
diff --git a/src/PBA.Infrastructure/Security/OAuthStateEntry.cs b/src/PBA.Infrastructure/Security/OAuthStateEntry.cs
index 4bfc971..7c9c538 100644
--- a/src/PBA.Infrastructure/Security/OAuthStateEntry.cs
+++ b/src/PBA.Infrastructure/Security/OAuthStateEntry.cs
@@ -3,8 +3,12 @@ using PBA.Domain.Enums;
 namespace PBA.Infrastructure.Security;
 
 // Promoted from a private record inside OAuthService so providers can receive it on ExchangeCodeAsync.
-// The coordinator still owns creating, storing, and expiring these entries.
-public sealed record OAuthStateEntry(Platform Platform, string? CodeVerifier = null)
+// The coordinator still owns creating, storing, and expiring these entries. Purpose is captured at
+// authorize time and read back at callback time so the credential is stamped Publishing vs Analytics.
+public sealed record OAuthStateEntry(
+    Platform Platform,
+    string? CodeVerifier = null,
+    CredentialPurpose Purpose = CredentialPurpose.Publishing)
 {
     private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
 
diff --git a/tests/PBA.Api.Tests/Endpoints/OAuthEndpointsTests.cs b/tests/PBA.Api.Tests/Endpoints/OAuthEndpointsTests.cs
index 7b48c33..6f69180 100644
--- a/tests/PBA.Api.Tests/Endpoints/OAuthEndpointsTests.cs
+++ b/tests/PBA.Api.Tests/Endpoints/OAuthEndpointsTests.cs
@@ -3,6 +3,7 @@ using System.Net.Http.Json;
 using PBA.Domain.Entities;
 using PBA.Domain.Enums;
 using Microsoft.Extensions.DependencyInjection;
+using Moq;
 using PBA.Infrastructure.Data;
 using Xunit;
 
@@ -39,6 +40,36 @@ public class OAuthEndpointsTests : IClassFixture<TestWebApplicationFactory>
         Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
     }
 
+    [Theory]
+    [InlineData("YouTube")]
+    [InlineData("Instagram")]
+    [InlineData("TikTok")]
+    public async Task Authorize_YouTubeInstagramTikTok_Returns302(string platform)
+    {
+        var response = await _client.GetAsync($"/api/auth/{platform}/authorize");
+
+        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
+    }
+
+    [Fact]
+    public async Task Authorize_WithPurposeAnalytics_PassesAnalyticsPurpose()
+    {
+        var response = await _client.GetAsync("/api/auth/TikTok/authorize?purpose=analytics");
+
+        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
+        // Only this test uses TikTok+analytics, so the verify is not coupled to other tests.
+        _factory.OAuthServiceMock.Verify(x => x.GetAuthorizationUrlAsync(
+            Platform.TikTok, CredentialPurpose.Analytics, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
+    }
+
+    [Fact]
+    public async Task Authorize_InvalidPurpose_Returns400()
+    {
+        var response = await _client.GetAsync("/api/auth/YouTube/authorize?purpose=bogus");
+
+        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
+    }
+
     [Fact]
     public async Task Callback_ValidCode_Returns302RedirectToFrontend()
     {
diff --git a/tests/PBA.Api.Tests/TestWebApplicationFactory.cs b/tests/PBA.Api.Tests/TestWebApplicationFactory.cs
index 8b61021..a944b7a 100644
--- a/tests/PBA.Api.Tests/TestWebApplicationFactory.cs
+++ b/tests/PBA.Api.Tests/TestWebApplicationFactory.cs
@@ -14,6 +14,9 @@ public class TestWebApplicationFactory : WebApplicationFactory<Program>
 {
     private readonly string _dbName = "TestDb_" + Guid.NewGuid();
 
+    // Exposed so endpoint tests can verify what the OAuth endpoints pass to the service (e.g. the purpose).
+    public Mock<IOAuthService> OAuthServiceMock { get; } = new();
+
     protected override void ConfigureWebHost(IWebHostBuilder builder)
     {
         builder.ConfigureServices(services =>
@@ -60,12 +63,11 @@ public class TestWebApplicationFactory : WebApplicationFactory<Program>
                 .ReturnsAsync((PBA.Domain.Entities.Content c, PBA.Domain.Enums.Platform _, CancellationToken _) => c.Body);
             services.AddSingleton<IContentTransformer>(transformerMock.Object);
 
-            var oauthMock = new Mock<IOAuthService>();
-            oauthMock.Setup(x => x.GetAuthorizationUrlAsync(It.IsAny<PBA.Domain.Enums.Platform>(), It.IsAny<CancellationToken>()))
+            OAuthServiceMock.Setup(x => x.GetAuthorizationUrlAsync(It.IsAny<PBA.Domain.Enums.Platform>(), It.IsAny<PBA.Domain.Enums.CredentialPurpose>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync("https://oauth.test/authorize?state=test");
-            oauthMock.Setup(x => x.ExchangeCodeAsync(It.IsAny<PBA.Domain.Enums.Platform>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
+            OAuthServiceMock.Setup(x => x.ExchangeCodeAsync(It.IsAny<PBA.Domain.Enums.Platform>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new PBA.Domain.Entities.PlatformCredential { Platform = PBA.Domain.Enums.Platform.LinkedIn, IsActive = true, EncryptedAccessToken = "encrypted" });
-            services.AddSingleton(oauthMock.Object);
+            services.AddSingleton(OAuthServiceMock.Object);
 
             var encryptorMock = new Mock<ITokenEncryptor>();
             encryptorMock.Setup(x => x.Encrypt(It.IsAny<string>())).Returns<string>(s => $"enc:{s}");
diff --git a/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/InstagramOAuthProviderTests.cs b/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/InstagramOAuthProviderTests.cs
new file mode 100644
index 0000000..e1d86bf
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/InstagramOAuthProviderTests.cs
@@ -0,0 +1,153 @@
+using System.Net;
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
+public class InstagramOAuthProviderTests : IDisposable
+{
+    private readonly Mock<ITokenEncryptor> _encryptor = new();
+    private readonly Mock<HttpMessageHandler> _httpHandler = new();
+    private readonly HttpClient _httpClient;
+    private readonly IHttpClientFactory _httpClientFactory;
+
+    private readonly InstagramOAuthOptions _options = new()
+    {
+        Enabled = true,
+        ClientId = "ig-client-id",
+        ClientSecret = "ig-client-secret",
+        RedirectUri = "https://localhost:5001/api/auth/instagram/callback"
+    };
+
+    public InstagramOAuthProviderTests()
+    {
+        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string s) => s.Replace("encrypted:", ""));
+        _httpClient = new HttpClient(_httpHandler.Object);
+        var factory = new Mock<IHttpClientFactory>();
+        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);
+        _httpClientFactory = factory.Object;
+    }
+
+    private InstagramOAuthProvider CreateProvider() => new(
+        _httpClientFactory, Options.Create(_options), _encryptor.Object, NullLogger<InstagramOAuthProvider>.Instance);
+
+    // Routes each request to a response by inspecting its URL; records the last request URI for assertions.
+    private Func<Uri?> SetupRouter(Func<HttpRequestMessage, HttpResponseMessage> route)
+    {
+        Uri? lastUri = null;
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
+            .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
+            {
+                lastUri = req.RequestUri;
+                return Task.FromResult(route(req));
+            });
+        return () => lastUri;
+    }
+
+    private static HttpResponseMessage Json(object payload, HttpStatusCode status = HttpStatusCode.OK) =>
+        new(status) { Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json") };
+
+    private void SetupThrow() =>
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
+            .ThrowsAsync(new HttpRequestException("network down"));
+
+    [Fact]
+    public void BuildAuthorization_IncludesCorrectScopesAndRedirectUri()
+    {
+        var request = CreateProvider().BuildAuthorization("STATE1");
+
+        Assert.StartsWith("https://www.instagram.com/oauth/authorize", request.Url);
+        var q = HttpUtility.ParseQueryString(new Uri(request.Url).Query);
+        Assert.Contains("instagram_business_basic", q["scope"]);
+        Assert.Contains("instagram_business_manage_insights", q["scope"]);
+        Assert.Equal(_options.RedirectUri, q["redirect_uri"]);
+        Assert.Equal("STATE1", q["state"]);
+    }
+
+    [Fact]
+    public async Task ExchangeCode_MapsTokenResponseToOAuthTokenResult()
+    {
+        // Two-step: short-lived token (POST api.instagram.com) then long-lived (GET graph.instagram.com).
+        SetupRouter(req => req.RequestUri!.Host == "api.instagram.com"
+            ? Json(new { access_token = "ig-short", user_id = 123 })
+            : Json(new { access_token = "ig-long", token_type = "bearer", expires_in = 5184000 }));
+
+        var result = await CreateProvider().ExchangeCodeAsync("code", new OAuthStateEntry(Platform.Instagram), CancellationToken.None);
+
+        Assert.Equal("ig-long", result.AccessToken);
+        Assert.Null(result.RefreshToken);                 // no separate refresh token
+        Assert.Equal(5184000, result.ExpiresIn);
+    }
+
+    [Fact]
+    public async Task RefreshAsync_ExtendsLongLivedAccessToken_NoRefreshToken()
+    {
+        // Credential has NO refresh token — refresh must use the current access token and still succeed.
+        var lastUri = SetupRouter(_ => Json(new { access_token = "ig-extended", token_type = "bearer", expires_in = 5184000 }));
+        var credential = new PlatformCredential
+        {
+            Platform = Platform.Instagram,
+            EncryptedAccessToken = "encrypted:ig-current",
+            EncryptedRefreshToken = null
+        };
+
+        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("ig-extended", result.Tokens!.AccessToken);
+        Assert.Null(result.Tokens.RefreshToken);
+        // Refresh used the current access token, not a refresh token.
+        Assert.Contains("grant_type=ig_refresh_token", lastUri()!.Query);
+        Assert.Contains("access_token=ig-current", lastUri()!.Query);
+    }
+
+    [Fact]
+    public async Task RefreshAsync_MapsRevokedSignal_ToRefreshFailureReasonRevoked()
+    {
+        SetupRouter(_ => Json(new { error = new { code = 190, error_subcode = 463, message = "expired" } }, HttpStatusCode.BadRequest));
+        var credential = new PlatformCredential { Platform = Platform.Instagram, EncryptedAccessToken = "encrypted:ig-current" };
+
+        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(RefreshFailureReason.Revoked, result.FailureReason);
+    }
+
+    [Fact]
+    public async Task RefreshAsync_MapsNetworkError_ToRefreshFailureReasonTransient()
+    {
+        SetupThrow();
+        var credential = new PlatformCredential { Platform = Platform.Instagram, EncryptedAccessToken = "encrypted:ig-current" };
+
+        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);
+
+        Assert.Equal(RefreshFailureReason.Transient, result.FailureReason);
+    }
+
+    [Fact]
+    public void NeedsRefresh_ReturnsTrue_WithinProviderLeadTime()
+    {
+        var provider = CreateProvider();
+        var now = DateTimeOffset.UtcNow;
+        var credential = new PlatformCredential { Platform = Platform.Instagram, AccessTokenExpiresAt = now.AddDays(40) };
+
+        Assert.True(provider.NeedsRefresh(credential, now));   // within 50-day lead
+        credential.AccessTokenExpiresAt = now.AddDays(59);
+        Assert.False(provider.NeedsRefresh(credential, now));
+    }
+
+    public void Dispose() => _httpClient.Dispose();
+}
diff --git a/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/LinkedInOAuthProviderTests.cs b/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/LinkedInOAuthProviderTests.cs
index cb03dae..1403a6a 100644
--- a/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/LinkedInOAuthProviderTests.cs
+++ b/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/LinkedInOAuthProviderTests.cs
@@ -146,9 +146,9 @@ public class LinkedInOAuthProviderTests : IDisposable
         var result = await provider.RefreshAsync(credential, CancellationToken.None);
 
         Assert.True(result.IsSuccess);
-        Assert.Equal("new-access-token", result.Value!.AccessToken);
-        Assert.Equal("new-refresh-token", result.Value.RefreshToken);
-        Assert.Equal(5184000, result.Value.ExpiresIn);
+        Assert.Equal("new-access-token", result.Tokens!.AccessToken);
+        Assert.Equal("new-refresh-token", result.Tokens.RefreshToken);
+        Assert.Equal(5184000, result.Tokens.ExpiresIn);
 
         var body = capture.Body();
         Assert.Contains("grant_type=refresh_token", body);
diff --git a/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/TikTokOAuthProviderTests.cs b/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/TikTokOAuthProviderTests.cs
new file mode 100644
index 0000000..b7f2afc
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/TikTokOAuthProviderTests.cs
@@ -0,0 +1,163 @@
+using System.Net;
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
+public class TikTokOAuthProviderTests : IDisposable
+{
+    private readonly Mock<ITokenEncryptor> _encryptor = new();
+    private readonly Mock<HttpMessageHandler> _httpHandler = new();
+    private readonly HttpClient _httpClient;
+    private readonly IHttpClientFactory _httpClientFactory;
+
+    private readonly TikTokOAuthOptions _options = new()
+    {
+        Enabled = true,
+        ClientId = "tt-client-key",
+        ClientSecret = "tt-client-secret",
+        RedirectUri = "https://localhost:5001/api/auth/tiktok/callback"
+    };
+
+    public TikTokOAuthProviderTests()
+    {
+        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string s) => s.Replace("encrypted:", ""));
+        _httpClient = new HttpClient(_httpHandler.Object);
+        var factory = new Mock<IHttpClientFactory>();
+        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);
+        _httpClientFactory = factory.Object;
+    }
+
+    private TikTokOAuthProvider CreateProvider() => new(
+        _httpClientFactory, Options.Create(_options), _encryptor.Object, NullLogger<TikTokOAuthProvider>.Instance);
+
+    private Func<string?> SetupJson(string json, HttpStatusCode status = HttpStatusCode.OK)
+    {
+        string? capturedBody = null;
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
+            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
+            {
+                capturedBody = req.Content is not null ? await req.Content.ReadAsStringAsync() : null;
+                return new HttpResponseMessage(status)
+                {
+                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
+                };
+            });
+        return () => capturedBody;
+    }
+
+    private void SetupThrow() =>
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
+            .ThrowsAsync(new HttpRequestException("network down"));
+
+    [Fact]
+    public void BuildAuthorization_IncludesCorrectScopesAndRedirectUri()
+    {
+        var request = CreateProvider().BuildAuthorization("STATE1");
+
+        Assert.StartsWith("https://www.tiktok.com/v2/auth/authorize/", request.Url);
+        var q = HttpUtility.ParseQueryString(new Uri(request.Url).Query);
+        Assert.Equal(_options.ClientId, q["client_key"]);
+        Assert.Equal("user.info.stats,video.list", q["scope"]);
+        Assert.Equal(_options.RedirectUri, q["redirect_uri"]);
+        Assert.Equal("STATE1", q["state"]);
+    }
+
+    [Fact]
+    public async Task ExchangeCode_MapsTokenResponseToOAuthTokenResult()
+    {
+        SetupJson(JsonSerializer.Serialize(new
+        {
+            access_token = "tt-access", refresh_token = "tt-refresh", expires_in = 86400,
+            refresh_expires_in = 31536000, open_id = "open-1"
+        }));
+
+        var result = await CreateProvider().ExchangeCodeAsync("code", new OAuthStateEntry(Platform.TikTok), CancellationToken.None);
+
+        Assert.Equal("tt-access", result.AccessToken);
+        Assert.Equal("tt-refresh", result.RefreshToken);
+        Assert.Equal(86400, result.ExpiresIn);
+        Assert.Equal(31536000, result.RefreshTokenExpiresIn);
+    }
+
+    [Fact]
+    public async Task RefreshAsync_PersistsRotatedRefreshToken()
+    {
+        var body = SetupJson(JsonSerializer.Serialize(new
+        {
+            access_token = "tt-new-access", refresh_token = "tt-rotated-refresh", expires_in = 86400
+        }));
+        var credential = new PlatformCredential { Platform = Platform.TikTok, EncryptedRefreshToken = "encrypted:tt-old-refresh" };
+
+        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("tt-new-access", result.Tokens!.AccessToken);
+        // TikTok rotates the refresh token — the new one must be surfaced for persistence.
+        Assert.Equal("tt-rotated-refresh", result.Tokens.RefreshToken);
+        Assert.Contains("grant_type=refresh_token", body());
+        Assert.Contains("refresh_token=tt-old-refresh", body());
+    }
+
+    [Fact]
+    public async Task RefreshAsync_MapsRevokedSignal_ToRefreshFailureReasonRevoked()
+    {
+        SetupJson(JsonSerializer.Serialize(new { error = "access_token_invalid", error_description = "invalid", log_id = "x" }),
+            HttpStatusCode.BadRequest);
+        var credential = new PlatformCredential { Platform = Platform.TikTok, EncryptedRefreshToken = "encrypted:tt-old-refresh" };
+
+        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(RefreshFailureReason.Revoked, result.FailureReason);
+    }
+
+    [Fact]
+    public async Task RefreshAsync_MapsNetworkError_ToRefreshFailureReasonTransient()
+    {
+        SetupThrow();
+        var credential = new PlatformCredential { Platform = Platform.TikTok, EncryptedRefreshToken = "encrypted:tt-old-refresh" };
+
+        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);
+
+        Assert.Equal(RefreshFailureReason.Transient, result.FailureReason);
+    }
+
+    [Fact]
+    public async Task RefreshAsync_MapsServerError_ToRefreshFailureReasonTransient()
+    {
+        SetupJson("{}", HttpStatusCode.ServiceUnavailable);
+        var credential = new PlatformCredential { Platform = Platform.TikTok, EncryptedRefreshToken = "encrypted:tt-old-refresh" };
+
+        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);
+
+        Assert.Equal(RefreshFailureReason.Transient, result.FailureReason);
+    }
+
+    [Fact]
+    public void NeedsRefresh_ReturnsTrue_WithinProviderLeadTime()
+    {
+        var provider = CreateProvider();
+        var now = DateTimeOffset.UtcNow;
+        var credential = new PlatformCredential { Platform = Platform.TikTok, AccessTokenExpiresAt = now.AddMinutes(30) };
+
+        Assert.True(provider.NeedsRefresh(credential, now));   // within 1h lead
+        credential.AccessTokenExpiresAt = now.AddHours(5);
+        Assert.False(provider.NeedsRefresh(credential, now));
+    }
+
+    public void Dispose() => _httpClient.Dispose();
+}
diff --git a/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/TwitterOAuthProviderTests.cs b/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/TwitterOAuthProviderTests.cs
index 6b1976d..bb6b34c 100644
--- a/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/TwitterOAuthProviderTests.cs
+++ b/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/TwitterOAuthProviderTests.cs
@@ -151,7 +151,7 @@ public class TwitterOAuthProviderTests : IDisposable
         var result = await provider.RefreshAsync(credential, CancellationToken.None);
 
         Assert.True(result.IsSuccess);
-        Assert.Equal("new-access-token", result.Value!.AccessToken);
+        Assert.Equal("new-access-token", result.Tokens!.AccessToken);
 
         var body = capture.Body();
         Assert.Contains("grant_type=refresh_token", body);
diff --git a/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/YouTubeOAuthProviderTests.cs b/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/YouTubeOAuthProviderTests.cs
new file mode 100644
index 0000000..8592807
--- /dev/null
+++ b/tests/PBA.Infrastructure.Tests/Security/OAuthProviders/YouTubeOAuthProviderTests.cs
@@ -0,0 +1,160 @@
+using System.Net;
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
+public class YouTubeOAuthProviderTests : IDisposable
+{
+    private readonly Mock<ITokenEncryptor> _encryptor = new();
+    private readonly Mock<HttpMessageHandler> _httpHandler = new();
+    private readonly HttpClient _httpClient;
+    private readonly IHttpClientFactory _httpClientFactory;
+
+    private readonly YouTubeOAuthOptions _options = new()
+    {
+        Enabled = true,
+        ClientId = "yt-client-id",
+        ClientSecret = "yt-client-secret",
+        RedirectUri = "https://localhost:5001/api/auth/youtube/callback",
+        ApiKey = "yt-api-key"
+    };
+
+    public YouTubeOAuthProviderTests()
+    {
+        _encryptor.Setup(e => e.Decrypt(It.IsAny<string>())).Returns((string s) => s.Replace("encrypted:", ""));
+        _httpClient = new HttpClient(_httpHandler.Object);
+        var factory = new Mock<IHttpClientFactory>();
+        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);
+        _httpClientFactory = factory.Object;
+    }
+
+    private YouTubeOAuthProvider CreateProvider() => new(
+        _httpClientFactory, Options.Create(_options), _encryptor.Object, NullLogger<YouTubeOAuthProvider>.Instance);
+
+    private Func<string?> SetupJson(string json, HttpStatusCode status = HttpStatusCode.OK)
+    {
+        string? capturedBody = null;
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
+            .Returns<HttpRequestMessage, CancellationToken>(async (req, _) =>
+            {
+                capturedBody = req.Content is not null ? await req.Content.ReadAsStringAsync() : null;
+                return new HttpResponseMessage(status)
+                {
+                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
+                };
+            });
+        return () => capturedBody;
+    }
+
+    private void SetupThrow() =>
+        _httpHandler.Protected()
+            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
+            .ThrowsAsync(new HttpRequestException("network down"));
+
+    [Fact]
+    public void BuildAuthorization_IncludesCorrectScopesAndRedirectUri()
+    {
+        var request = CreateProvider().BuildAuthorization("STATE1");
+
+        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth", request.Url);
+        var q = HttpUtility.ParseQueryString(new Uri(request.Url).Query);
+        Assert.Contains("yt-analytics.readonly", q["scope"]);
+        Assert.Contains("youtube.readonly", q["scope"]);
+        Assert.Equal(_options.RedirectUri, q["redirect_uri"]);
+        Assert.Equal("offline", q["access_type"]);
+        Assert.Equal("consent", q["prompt"]);
+        Assert.Equal("STATE1", q["state"]);
+        Assert.Null(request.Additions.CodeVerifier);
+    }
+
+    [Fact]
+    public async Task ExchangeCode_MapsTokenResponseToOAuthTokenResult()
+    {
+        SetupJson(JsonSerializer.Serialize(new
+        {
+            access_token = "yt-access", refresh_token = "yt-refresh", expires_in = 3600
+        }));
+
+        var result = await CreateProvider().ExchangeCodeAsync("code", new OAuthStateEntry(Platform.YouTube), CancellationToken.None);
+
+        Assert.Equal("yt-access", result.AccessToken);
+        Assert.Equal("yt-refresh", result.RefreshToken);
+        Assert.Equal(3600, result.ExpiresIn);
+    }
+
+    [Fact]
+    public async Task RefreshAsync_UsesRefreshToken()
+    {
+        var body = SetupJson(JsonSerializer.Serialize(new { access_token = "yt-new-access", expires_in = 3600 }));
+        var credential = new PlatformCredential { Platform = Platform.YouTube, EncryptedRefreshToken = "encrypted:yt-refresh" };
+
+        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal("yt-new-access", result.Tokens!.AccessToken);
+        Assert.Contains("grant_type=refresh_token", body());
+        Assert.Contains("refresh_token=yt-refresh", body());
+    }
+
+    [Fact]
+    public async Task RefreshAsync_MapsRevokedSignal_ToRefreshFailureReasonRevoked()
+    {
+        SetupJson("{\"error\":\"invalid_grant\"}", HttpStatusCode.BadRequest);
+        var credential = new PlatformCredential { Platform = Platform.YouTube, EncryptedRefreshToken = "encrypted:yt-refresh" };
+
+        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(RefreshFailureReason.Revoked, result.FailureReason);
+    }
+
+    [Fact]
+    public async Task RefreshAsync_MapsNetworkError_ToRefreshFailureReasonTransient()
+    {
+        SetupThrow();
+        var credential = new PlatformCredential { Platform = Platform.YouTube, EncryptedRefreshToken = "encrypted:yt-refresh" };
+
+        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(RefreshFailureReason.Transient, result.FailureReason);
+    }
+
+    [Fact]
+    public async Task RefreshAsync_MapsServerError_ToRefreshFailureReasonTransient()
+    {
+        SetupJson("{\"error\":\"backend\"}", HttpStatusCode.ServiceUnavailable);
+        var credential = new PlatformCredential { Platform = Platform.YouTube, EncryptedRefreshToken = "encrypted:yt-refresh" };
+
+        var result = await CreateProvider().RefreshAsync(credential, CancellationToken.None);
+
+        Assert.Equal(RefreshFailureReason.Transient, result.FailureReason);
+    }
+
+    [Fact]
+    public void NeedsRefresh_ReturnsTrue_WithinProviderLeadTime()
+    {
+        var provider = CreateProvider();
+        var now = DateTimeOffset.UtcNow;
+        var credential = new PlatformCredential { Platform = Platform.YouTube, AccessTokenExpiresAt = now.AddMinutes(30) };
+
+        Assert.True(provider.NeedsRefresh(credential, now));   // within 1h lead
+        credential.AccessTokenExpiresAt = now.AddHours(5);
+        Assert.False(provider.NeedsRefresh(credential, now));
+    }
+
+    public void Dispose() => _httpClient.Dispose();
+}
diff --git a/tests/PBA.Infrastructure.Tests/Security/OAuthServiceTests.cs b/tests/PBA.Infrastructure.Tests/Security/OAuthServiceTests.cs
index 3136f4a..da942db 100644
--- a/tests/PBA.Infrastructure.Tests/Security/OAuthServiceTests.cs
+++ b/tests/PBA.Infrastructure.Tests/Security/OAuthServiceTests.cs
@@ -94,7 +94,7 @@ public class OAuthServiceTests : IDisposable
     public async Task GetAuthorizationUrl_LinkedIn_ReturnsCorrectUrlWithScopes()
     {
         var service = CreateService();
-        var url = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CancellationToken.None);
+        var url = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CredentialPurpose.Publishing, CancellationToken.None);
 
         Assert.StartsWith("https://www.linkedin.com/oauth/v2/authorization", url);
         var query = HttpUtility.ParseQueryString(new Uri(url).Query);
@@ -108,7 +108,7 @@ public class OAuthServiceTests : IDisposable
     public async Task GetAuthorizationUrl_Twitter_IncludesPKCECodeChallenge()
     {
         var service = CreateService();
-        var url = await service.GetAuthorizationUrlAsync(Platform.Twitter, CancellationToken.None);
+        var url = await service.GetAuthorizationUrlAsync(Platform.Twitter, CredentialPurpose.Publishing, CancellationToken.None);
 
         Assert.StartsWith("https://twitter.com/i/oauth2/authorize", url);
         Assert.Contains("code_challenge=", url);
@@ -119,7 +119,7 @@ public class OAuthServiceTests : IDisposable
     public async Task GetAuthorizationUrl_IncludesStateParameter()
     {
         var service = CreateService();
-        var url = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CancellationToken.None);
+        var url = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CredentialPurpose.Publishing, CancellationToken.None);
 
         var uri = new Uri(url);
         var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
@@ -133,7 +133,7 @@ public class OAuthServiceTests : IDisposable
     public async Task ExchangeCodeAsync_LinkedIn_StoresEncryptedTokens()
     {
         var service = CreateService();
-        var authUrl = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CancellationToken.None);
+        var authUrl = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CredentialPurpose.Publishing, CancellationToken.None);
         var state = System.Web.HttpUtility.ParseQueryString(new Uri(authUrl).Query)["state"]!;
 
         SetupHttpResponse(JsonSerializer.Serialize(new
@@ -159,7 +159,7 @@ public class OAuthServiceTests : IDisposable
     public async Task ExchangeCodeAsync_Twitter_UsesCodeVerifierForPKCE()
     {
         var service = CreateService();
-        var authUrl = await service.GetAuthorizationUrlAsync(Platform.Twitter, CancellationToken.None);
+        var authUrl = await service.GetAuthorizationUrlAsync(Platform.Twitter, CredentialPurpose.Publishing, CancellationToken.None);
         var state = HttpUtility.ParseQueryString(new Uri(authUrl).Query)["state"]!;
 
         string? capturedBody = null;
@@ -285,7 +285,7 @@ public class OAuthServiceTests : IDisposable
         var service = CreateService();
 
         await Assert.ThrowsAsync<NotSupportedException>(() =>
-            service.GetAuthorizationUrlAsync(Platform.Blog, CancellationToken.None));
+            service.GetAuthorizationUrlAsync(Platform.Blog, CredentialPurpose.Publishing, CancellationToken.None));
     }
 
     [Fact]
@@ -294,7 +294,7 @@ public class OAuthServiceTests : IDisposable
         var service = CreateService();
 
         // Keyed resolution: the coordinator delegates URL construction to the LinkedIn provider.
-        var url = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CancellationToken.None);
+        var url = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CredentialPurpose.Publishing, CancellationToken.None);
         Assert.StartsWith("https://www.linkedin.com/oauth/v2/authorization", url);
 
         // State persisted by the coordinator: a follow-up exchange with that state succeeds.
@@ -314,7 +314,7 @@ public class OAuthServiceTests : IDisposable
     public async Task OAuthService_PersistsTwitterCodeVerifier_FromProviderStateAdditions()
     {
         var service = CreateService();
-        var authUrl = await service.GetAuthorizationUrlAsync(Platform.Twitter, CancellationToken.None);
+        var authUrl = await service.GetAuthorizationUrlAsync(Platform.Twitter, CredentialPurpose.Publishing, CancellationToken.None);
         var state = HttpUtility.ParseQueryString(new Uri(authUrl).Query)["state"]!;
 
         string? capturedBody = null;
@@ -351,7 +351,7 @@ public class OAuthServiceTests : IDisposable
 
         // Obtain a valid state (LinkedIn), then exchange against an unregistered platform: the state
         // check passes, provider resolution returns null, and the coordinator throws NotSupported.
-        var url = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CancellationToken.None);
+        var url = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CredentialPurpose.Publishing, CancellationToken.None);
         var state = HttpUtility.ParseQueryString(new Uri(url).Query)["state"]!;
 
         await Assert.ThrowsAsync<NotSupportedException>(() =>
@@ -364,7 +364,7 @@ public class OAuthServiceTests : IDisposable
         // Purpose does not exist yet (section-02). This asserts the current default behavior: an exchanged
         // credential is stored active with the platform's publishing scope, exactly as today.
         var service = CreateService();
-        var authUrl = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CancellationToken.None);
+        var authUrl = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CredentialPurpose.Publishing, CancellationToken.None);
         var state = HttpUtility.ParseQueryString(new Uri(authUrl).Query)["state"]!;
 
         SetupHttpResponse(JsonSerializer.Serialize(new
@@ -378,6 +378,51 @@ public class OAuthServiceTests : IDisposable
 
         Assert.True(credential.IsActive);
         Assert.Equal("openid profile w_member_social", credential.Scopes);
+        Assert.Equal(CredentialPurpose.Publishing, credential.Purpose);
+    }
+
+    [Fact]
+    public async Task OAuthService_ExchangeCode_AnalyticsPurposeFromState_StampsCredentialAnalytics()
+    {
+        var service = CreateService();
+
+        // Purpose captured at authorize time flows through OAuth state to the callback's exchange.
+        var authUrl = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CredentialPurpose.Analytics, CancellationToken.None);
+        var state = HttpUtility.ParseQueryString(new Uri(authUrl).Query)["state"]!;
+        SetupHttpResponse(JsonSerializer.Serialize(new
+        {
+            access_token = "li-access-token", refresh_token = "li-refresh-token", expires_in = 5184000
+        }));
+
+        var credential = await service.ExchangeCodeAsync(Platform.LinkedIn, "auth-code", state, CancellationToken.None);
+
+        Assert.Equal(CredentialPurpose.Analytics, credential.Purpose);
+    }
+
+    [Fact]
+    public async Task OAuthService_ExchangeCode_PublishingAndAnalyticsSamePlatform_CreateDistinctCredentials()
+    {
+        var service = CreateService();
+        SetupHttpResponse(JsonSerializer.Serialize(new
+        {
+            access_token = "li-access-token", refresh_token = "li-refresh-token", expires_in = 5184000
+        }));
+
+        var pubUrl = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CredentialPurpose.Publishing, CancellationToken.None);
+        var pubState = HttpUtility.ParseQueryString(new Uri(pubUrl).Query)["state"]!;
+        await service.ExchangeCodeAsync(Platform.LinkedIn, "code", pubState, CancellationToken.None);
+
+        var anaUrl = await service.GetAuthorizationUrlAsync(Platform.LinkedIn, CredentialPurpose.Analytics, CancellationToken.None);
+        var anaState = HttpUtility.ParseQueryString(new Uri(anaUrl).Query)["state"]!;
+        await service.ExchangeCodeAsync(Platform.LinkedIn, "code", anaState, CancellationToken.None);
+
+        // Find/create is keyed on (Platform, Purpose): the Analytics exchange must NOT overwrite the
+        // Publishing credential. Without that, only one row would exist.
+        var creds = await _dbContext.PlatformCredentials
+            .Where(c => c.Platform == Platform.LinkedIn).ToListAsync();
+        Assert.Equal(2, creds.Count);
+        Assert.Contains(creds, c => c.Purpose == CredentialPurpose.Publishing);
+        Assert.Contains(creds, c => c.Purpose == CredentialPurpose.Analytics);
     }
 
     public void Dispose()
