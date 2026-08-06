using System.Net;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Security.OAuthProviders;

// YouTube (Google) OAuth, for both the analytics credential and the upload credential — same account,
// different scopes. Standard refresh-token grant; Google signals a revoked token with invalid_grant.
// NeedsRefresh window ~1h before expiry.
public sealed class YouTubeOAuthProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<YouTubeOAuthOptions> options,
    ITokenEncryptor encryptor,
    ILogger<YouTubeOAuthProvider> logger) : IOAuthProvider
{
    // Scopes differ by what the credential is FOR, and deliberately do not overlap more than they
    // must. The analytics token reads; it has no business being able to put a video on the channel.
    // Uploading needs youtube.upload, and youtube (manage) on top of it because a scheduled upload
    // sets a publish time — that is a write to the video's status, not part of the upload itself.
    private const string AnalyticsScope =
        "https://www.googleapis.com/auth/yt-analytics.readonly https://www.googleapis.com/auth/youtube.readonly";
    private const string PublishingScope =
        "https://www.googleapis.com/auth/youtube.upload https://www.googleapis.com/auth/youtube";

    private const string AuthorizeBase = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private static readonly TimeSpan RefreshLead = TimeSpan.FromHours(1);

    private static string ScopeFor(CredentialPurpose purpose) =>
        purpose == CredentialPurpose.Publishing ? PublishingScope : AnalyticsScope;

    public Platform Platform => Platform.YouTube;

    public AuthorizationRequest BuildAuthorization(string state, CredentialPurpose purpose)
    {
        var o = options.Value;
        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["response_type"] = "code";
        qs["client_id"] = o.ClientId;
        qs["redirect_uri"] = o.RedirectUri;
        qs["scope"] = ScopeFor(purpose);
        qs["state"] = state;
        qs["access_type"] = "offline";   // request a refresh token
        qs["prompt"] = "consent";        // guarantee a refresh token is returned

        return new AuthorizationRequest($"{AuthorizeBase}?{qs}", new OAuthStateAdditions());
    }

    public async Task<OAuthTokenResult> ExchangeCodeAsync(string code, OAuthStateEntry state, CancellationToken ct)
    {
        var o = options.Value;
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = o.RedirectUri,
            ["client_id"] = o.ClientId,
            ["client_secret"] = o.ClientSecret
        };

        var client = httpClientFactory.CreateClient();
        var response = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(parameters), ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("YouTube token exchange failed: {Status} {Body}", response.StatusCode, errorBody);
            throw new InvalidOperationException($"YouTube token exchange failed: {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var data = JsonSerializer.Deserialize<JsonElement>(json);

        return new OAuthTokenResult(
            AccessToken: data.GetProperty("access_token").GetString()!,
            RefreshToken: data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            ExpiresIn: data.GetProperty("expires_in").GetInt32(),
            RefreshTokenExpiresIn: null,
            Scopes: ScopeFor(state.Purpose));
    }

    public async Task<OAuthRefreshResult> RefreshAsync(PlatformCredential credential, CancellationToken ct)
    {
        var o = options.Value;
        var refreshToken = encryptor.Decrypt(credential.EncryptedRefreshToken!);

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = o.ClientId,
            ["client_secret"] = o.ClientSecret
        };

        HttpResponseMessage response;
        string body;
        try
        {
            var client = httpClientFactory.CreateClient();
            response = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(parameters), ct);
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            return OAuthRefreshResult.Fail(RefreshFailureReason.Transient, ex.Message);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return OAuthRefreshResult.Fail(RefreshFailureReason.Transient, ex.Message);
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("YouTube token refresh failed: {Status} {Body}", response.StatusCode, body);
            if (IsTransientStatus(response.StatusCode))
                return OAuthRefreshResult.Fail(RefreshFailureReason.Transient, $"Transient refresh error: {response.StatusCode}");
            // Google marks a revoked/expired refresh token with invalid_grant.
            return body.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
                ? OAuthRefreshResult.Fail(RefreshFailureReason.Revoked, "invalid_grant")
                : OAuthRefreshResult.Fail(RefreshFailureReason.Transient, $"Refresh error: {response.StatusCode}");
        }

        try
        {
            var data = JsonSerializer.Deserialize<JsonElement>(body);
            return OAuthRefreshResult.Success(new OAuthTokenResult(
                AccessToken: data.GetProperty("access_token").GetString()!,
                RefreshToken: data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
                ExpiresIn: data.GetProperty("expires_in").GetInt32(),
                RefreshTokenExpiresIn: null,
                Scopes: ScopeFor(credential.Purpose)));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return OAuthRefreshResult.Fail(RefreshFailureReason.Transient, "Unparseable refresh response");
        }
    }

    public bool NeedsRefresh(PlatformCredential credential, DateTimeOffset now) =>
        credential.AccessTokenExpiresAt.HasValue &&
        now >= credential.AccessTokenExpiresAt.Value - RefreshLead;

    private static bool IsTransientStatus(HttpStatusCode status) =>
        (int)status == 429 || (int)status >= 500;
}
