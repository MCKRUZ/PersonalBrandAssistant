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

// TikTok analytics OAuth. Refresh ROTATES the refresh token — the new refresh token in the response must be
// persisted (carried on OAuthTokenResult.RefreshToken). TikTok returns errors in the JSON body (sometimes
// with a 200), so success is detected by the presence of access_token. access_token_invalid => revoked.
public sealed class TikTokOAuthProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<TikTokOAuthOptions> options,
    ITokenEncryptor encryptor,
    ILogger<TikTokOAuthProvider> logger) : IOAuthProvider
{
    private const string Scope = "user.info.stats,video.list";
    private const string AuthorizeBase = "https://www.tiktok.com/v2/auth/authorize/";
    private const string TokenEndpoint = "https://open.tiktokapis.com/v2/oauth/token/";
    private static readonly TimeSpan RefreshLead = TimeSpan.FromHours(1);

    public Platform Platform => Platform.TikTok;

    public AuthorizationRequest BuildAuthorization(string state, CredentialPurpose purpose)
    {
        var o = options.Value;
        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["client_key"] = o.ClientId;
        qs["scope"] = Scope;
        qs["response_type"] = "code";
        qs["redirect_uri"] = o.RedirectUri;
        qs["state"] = state;

        return new AuthorizationRequest($"{AuthorizeBase}?{qs}", new OAuthStateAdditions());
    }

    public async Task<OAuthTokenResult> ExchangeCodeAsync(string code, OAuthStateEntry state, CancellationToken ct)
    {
        var o = options.Value;
        var parameters = new Dictionary<string, string>
        {
            ["client_key"] = o.ClientId,
            ["client_secret"] = o.ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = o.RedirectUri
        };

        var client = httpClientFactory.CreateClient();
        var response = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(parameters), ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        var data = JsonSerializer.Deserialize<JsonElement>(body);
        if (!data.TryGetProperty("access_token", out var accessToken) || accessToken.ValueKind != JsonValueKind.String)
        {
            logger.LogError("TikTok token exchange failed: {Status} {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"TikTok token exchange failed: {response.StatusCode}");
        }

        return new OAuthTokenResult(
            AccessToken: accessToken.GetString()!,
            RefreshToken: data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            ExpiresIn: data.GetProperty("expires_in").GetInt32(),
            RefreshTokenExpiresIn: data.TryGetProperty("refresh_expires_in", out var re) && re.ValueKind == JsonValueKind.Number
                ? re.GetInt32() : null,
            Scopes: Scope);
    }

    public async Task<OAuthRefreshResult> RefreshAsync(PlatformCredential credential, CancellationToken ct)
    {
        var o = options.Value;
        var refreshToken = encryptor.Decrypt(credential.EncryptedRefreshToken!);

        var parameters = new Dictionary<string, string>
        {
            ["client_key"] = o.ClientId,
            ["client_secret"] = o.ClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
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

        if (IsTransientStatus(response.StatusCode))
            return OAuthRefreshResult.Fail(RefreshFailureReason.Transient, $"Transient refresh error: {response.StatusCode}");

        JsonElement data;
        try
        {
            data = JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch (JsonException)
        {
            return OAuthRefreshResult.Fail(RefreshFailureReason.Transient, "Unparseable refresh response");
        }

        if (data.TryGetProperty("access_token", out var accessToken) && accessToken.ValueKind == JsonValueKind.String)
        {
            if (!data.TryGetProperty("expires_in", out var expiresIn) || expiresIn.ValueKind != JsonValueKind.Number)
                return OAuthRefreshResult.Fail(RefreshFailureReason.Transient, "Refresh response missing expires_in");

            return OAuthRefreshResult.Success(new OAuthTokenResult(
                AccessToken: accessToken.GetString()!,
                // TikTok rotates the refresh token — persist the new one.
                RefreshToken: data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
                ExpiresIn: expiresIn.GetInt32(),
                RefreshTokenExpiresIn: data.TryGetProperty("refresh_expires_in", out var re) && re.ValueKind == JsonValueKind.Number
                    ? re.GetInt32() : null,
                Scopes: Scope));
        }

        var errorCode = data.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String
            ? errEl.GetString() : null;
        logger.LogWarning("TikTok token refresh failed: {Status} {Error} {Body}", response.StatusCode, errorCode, body);
        return errorCode == "access_token_invalid"
            ? OAuthRefreshResult.Fail(RefreshFailureReason.Revoked, errorCode)
            : OAuthRefreshResult.Fail(RefreshFailureReason.Transient, errorCode ?? "Refresh failed");
    }

    public bool NeedsRefresh(PlatformCredential credential, DateTimeOffset now) =>
        credential.AccessTokenExpiresAt.HasValue &&
        now >= credential.AccessTokenExpiresAt.Value - RefreshLead;

    private static bool IsTransientStatus(HttpStatusCode status) =>
        (int)status == 429 || (int)status >= 500;
}
