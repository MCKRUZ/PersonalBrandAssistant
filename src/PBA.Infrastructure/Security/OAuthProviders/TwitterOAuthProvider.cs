using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Security.OAuthProviders;

public sealed class TwitterOAuthProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<TwitterOptions> options,
    ITokenEncryptor encryptor,
    ILogger<TwitterOAuthProvider> logger) : IOAuthProvider
{
    private const string Scope = "tweet.read tweet.write users.read media.write offline.access";
    private const string AuthorizeBase = "https://twitter.com/i/oauth2/authorize";
    private const string TokenEndpoint = "https://api.twitter.com/2/oauth2/token";

    // Mirrors TwitterConnector.TokenRefreshWindow (10 min) — the effective refresh window today.
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(10);

    public Platform Platform => Platform.Twitter;

    public AuthorizationRequest BuildAuthorization(string state, CredentialPurpose purpose)
    {
        var tw = options.Value;
        var codeVerifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var codeChallenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["response_type"] = "code";
        qs["client_id"] = tw.ClientId;
        qs["redirect_uri"] = tw.RedirectUri;
        qs["scope"] = Scope;
        qs["state"] = state;
        qs["code_challenge"] = codeChallenge;
        qs["code_challenge_method"] = "S256";

        return new AuthorizationRequest($"{AuthorizeBase}?{qs}", new OAuthStateAdditions(codeVerifier));
    }

    public async Task<OAuthTokenResult> ExchangeCodeAsync(string code, OAuthStateEntry state, CancellationToken ct)
    {
        var tw = options.Value;
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = tw.RedirectUri,
            ["client_id"] = tw.ClientId,
            ["code_verifier"] = state.CodeVerifier!
        };

        var client = httpClientFactory.CreateClient();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{tw.ClientId}:{tw.ClientSecret}"));
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(parameters)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Twitter token exchange failed: {Status} {Body}", response.StatusCode, errorBody);
            throw new InvalidOperationException($"Twitter token exchange failed: {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var data = JsonSerializer.Deserialize<JsonElement>(json);

        return new OAuthTokenResult(
            AccessToken: data.GetProperty("access_token").GetString()!,
            RefreshToken: data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            ExpiresIn: data.GetProperty("expires_in").GetInt32(),
            RefreshTokenExpiresIn: null,
            Scopes: Scope);
    }

    public async Task<OAuthRefreshResult> RefreshAsync(PlatformCredential credential, CancellationToken ct)
    {
        var tw = options.Value;
        var refreshToken = encryptor.Decrypt(credential.EncryptedRefreshToken!);

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = tw.ClientId
        };

        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(parameters)
        };
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{tw.ClientId}:{tw.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Token refresh failed for {Platform}: {Status} {Body}",
                Platform, response.StatusCode, errorBody);
            // Twitter has no distinct revoked-vs-transient signal wired here; default to Transient.
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

    private static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
