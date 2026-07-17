using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Security.OAuthProviders;

public sealed class LinkedInOAuthProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<LinkedInOptions> options,
    ITokenEncryptor encryptor,
    ILogger<LinkedInOAuthProvider> logger) : IOAuthProvider
{
    private const string Scope = "openid profile w_member_social";
    private const string AuthorizeBase = "https://www.linkedin.com/oauth/v2/authorization";
    private const string TokenEndpoint = "https://www.linkedin.com/oauth/v2/accessToken";

    // Mirrors LinkedInConnector.TokenRefreshWindow (5 min) — the effective refresh window today.
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(5);

    public Platform Platform => Platform.LinkedIn;

    public AuthorizationRequest BuildAuthorization(string state)
    {
        var li = options.Value;
        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["response_type"] = "code";
        qs["client_id"] = li.ClientId;
        qs["redirect_uri"] = li.RedirectUri;
        qs["scope"] = Scope;
        qs["state"] = state;

        return new AuthorizationRequest($"{AuthorizeBase}?{qs}", new OAuthStateAdditions());
    }

    public async Task<OAuthTokenResult> ExchangeCodeAsync(string code, OAuthStateEntry state, CancellationToken ct)
    {
        var li = options.Value;
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = li.RedirectUri,
            ["client_id"] = li.ClientId,
            ["client_secret"] = li.ClientSecret
        };

        var client = httpClientFactory.CreateClient();
        var response = await client.PostAsync(TokenEndpoint, new FormUrlEncodedContent(parameters), ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("LinkedIn token exchange failed: {Status} {Body}", response.StatusCode, errorBody);
            throw new InvalidOperationException($"LinkedIn token exchange failed: {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var data = JsonSerializer.Deserialize<JsonElement>(json);

        return new OAuthTokenResult(
            AccessToken: data.GetProperty("access_token").GetString()!,
            RefreshToken: data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            ExpiresIn: data.GetProperty("expires_in").GetInt32(),
            RefreshTokenExpiresIn: data.TryGetProperty("refresh_token_expires_in", out var rtExp)
                ? rtExp.GetInt32() : null,
            Scopes: Scope);
    }

    public async Task<OAuthRefreshResult> RefreshAsync(PlatformCredential credential, CancellationToken ct)
    {
        var li = options.Value;
        var refreshToken = encryptor.Decrypt(credential.EncryptedRefreshToken!);

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = li.ClientId,
            ["client_secret"] = li.ClientSecret
        };

        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(parameters)
        };

        var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Token refresh failed for {Platform}: {Status} {Body}",
                Platform, response.StatusCode, errorBody);
            // LinkedIn has no distinct revoked-vs-transient signal wired here; default to Transient.
            return OAuthRefreshResult.Fail(RefreshFailureReason.Transient,
                $"Token refresh failed: {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var tokenData = JsonSerializer.Deserialize<JsonElement>(json);

        return OAuthRefreshResult.Success(new OAuthTokenResult(
            AccessToken: tokenData.GetProperty("access_token").GetString()!,
            RefreshToken: tokenData.TryGetProperty("refresh_token", out var newRefreshProp)
                ? newRefreshProp.GetString() : null,
            ExpiresIn: tokenData.GetProperty("expires_in").GetInt32(),
            RefreshTokenExpiresIn: null,
            Scopes: Scope));
    }

    public bool NeedsRefresh(PlatformCredential credential, DateTimeOffset now) =>
        credential.AccessTokenExpiresAt.HasValue &&
        credential.AccessTokenExpiresAt.Value - now < RefreshWindow;
}
