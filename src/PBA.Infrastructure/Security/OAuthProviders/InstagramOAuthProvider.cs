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

// Instagram-Login analytics OAuth. There is NO separate refresh token: the exchange trades the code for a
// short-lived token then immediately for a ~60-day long-lived token, and "refresh" extends that long-lived
// token via ig_refresh_token using the CURRENT access token. Meta codes 190/458/463 signal a revoked token.
public sealed class InstagramOAuthProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<InstagramOAuthOptions> options,
    ITokenEncryptor encryptor,
    ILogger<InstagramOAuthProvider> logger) : IOAuthProvider
{
    private const string Scope = "instagram_business_basic instagram_business_manage_insights";
    private const string AuthorizeBase = "https://www.instagram.com/oauth/authorize";
    private const string ShortTokenEndpoint = "https://api.instagram.com/oauth/access_token";
    private const string GraphBase = "https://graph.instagram.com";
    private static readonly TimeSpan RefreshLead = TimeSpan.FromDays(50);

    public Platform Platform => Platform.Instagram;

    public AuthorizationRequest BuildAuthorization(string state)
    {
        var o = options.Value;
        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["client_id"] = o.ClientId;
        qs["redirect_uri"] = o.RedirectUri;
        qs["scope"] = Scope;
        qs["response_type"] = "code";
        qs["state"] = state;

        return new AuthorizationRequest($"{AuthorizeBase}?{qs}", new OAuthStateAdditions());
    }

    public async Task<OAuthTokenResult> ExchangeCodeAsync(string code, OAuthStateEntry state, CancellationToken ct)
    {
        var o = options.Value;
        var client = httpClientFactory.CreateClient();

        // Step 1: code -> short-lived token.
        var shortParams = new Dictionary<string, string>
        {
            ["client_id"] = o.ClientId,
            ["client_secret"] = o.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = o.RedirectUri,
            ["code"] = code
        };
        var shortResponse = await client.PostAsync(ShortTokenEndpoint, new FormUrlEncodedContent(shortParams), ct);
        if (!shortResponse.IsSuccessStatusCode)
        {
            var errorBody = await shortResponse.Content.ReadAsStringAsync(ct);
            logger.LogError("Instagram short-token exchange failed: {Status} {Body}", shortResponse.StatusCode, errorBody);
            throw new InvalidOperationException($"Instagram token exchange failed: {shortResponse.StatusCode}");
        }
        var shortJson = await shortResponse.Content.ReadAsStringAsync(ct);
        var shortToken = JsonSerializer.Deserialize<JsonElement>(shortJson).GetProperty("access_token").GetString()!;

        // Step 2: short-lived -> long-lived (~60 day) token.
        var longUrl = $"{GraphBase}/access_token?grant_type=ig_exchange_token" +
                      $"&client_secret={Uri.EscapeDataString(o.ClientSecret)}" +
                      $"&access_token={Uri.EscapeDataString(shortToken)}";
        var longResponse = await client.GetAsync(longUrl, ct);
        if (!longResponse.IsSuccessStatusCode)
        {
            var errorBody = await longResponse.Content.ReadAsStringAsync(ct);
            logger.LogError("Instagram long-token exchange failed: {Status} {Body}", longResponse.StatusCode, errorBody);
            throw new InvalidOperationException($"Instagram long-lived token exchange failed: {longResponse.StatusCode}");
        }
        var longJson = await longResponse.Content.ReadAsStringAsync(ct);
        var longData = JsonSerializer.Deserialize<JsonElement>(longJson);

        return new OAuthTokenResult(
            AccessToken: longData.GetProperty("access_token").GetString()!,
            RefreshToken: null,   // Instagram-Login has no separate refresh token
            ExpiresIn: longData.GetProperty("expires_in").GetInt32(),
            RefreshTokenExpiresIn: null,
            Scopes: Scope);
    }

    public async Task<OAuthRefreshResult> RefreshAsync(PlatformCredential credential, CancellationToken ct)
    {
        // Refresh extends the long-lived token using the CURRENT access token (there is no refresh token).
        var accessToken = encryptor.Decrypt(credential.EncryptedAccessToken);
        var url = $"{GraphBase}/refresh_access_token?grant_type=ig_refresh_token" +
                  $"&access_token={Uri.EscapeDataString(accessToken)}";

        HttpResponseMessage response;
        string body;
        try
        {
            var client = httpClientFactory.CreateClient();
            response = await client.GetAsync(url, ct);
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
            logger.LogWarning("Instagram token refresh failed: {Status} {Body}", response.StatusCode, body);
            if (IsTransientStatus(response.StatusCode))
                return OAuthRefreshResult.Fail(RefreshFailureReason.Transient, $"Transient refresh error: {response.StatusCode}");
            return IsMetaRevoked(body)
                ? OAuthRefreshResult.Fail(RefreshFailureReason.Revoked, "Meta revoked token")
                : OAuthRefreshResult.Fail(RefreshFailureReason.Transient, $"Refresh error: {response.StatusCode}");
        }

        try
        {
            var data = JsonSerializer.Deserialize<JsonElement>(body);
            return OAuthRefreshResult.Success(new OAuthTokenResult(
                AccessToken: data.GetProperty("access_token").GetString()!,
                RefreshToken: null,
                ExpiresIn: data.GetProperty("expires_in").GetInt32(),
                RefreshTokenExpiresIn: null,
                Scopes: Scope));
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

    // Meta OAuthException code 190 / subcodes 458 / 463 indicate an invalidated (revoked/expired) token.
    private static bool IsMetaRevoked(string body)
    {
        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(body);
            if (!root.TryGetProperty("error", out var error))
                return false;

            return TryGetInt(error, "code") == 190
                || TryGetInt(error, "error_subcode") is 458 or 463;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Tolerates Meta serializing the code as either a JSON number or a numeric string.
    private static int? TryGetInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt32(),
            JsonValueKind.String when int.TryParse(value.GetString(), out var n) => n,
            _ => null
        };
    }
}
