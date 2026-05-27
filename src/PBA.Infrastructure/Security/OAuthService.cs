using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Security;

public sealed class OAuthService(
    IHttpClientFactory httpClientFactory,
    ITokenEncryptor encryptor,
    IAppDbContext db,
    IOptions<LinkedInOptions> linkedInOptions,
    IOptions<TwitterOptions> twitterOptions,
    ILogger<OAuthService> logger) : IOAuthService
{
    private static readonly ConcurrentDictionary<string, OAuthStateEntry> StateStore = new();
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);
    private const int MaxPendingStates = 1000;

    public Task<string> GetAuthorizationUrlAsync(Platform platform, CancellationToken ct)
    {
        CleanExpiredStates();

        if (StateStore.Count >= MaxPendingStates)
            throw new InvalidOperationException("Too many pending OAuth flows");

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        var url = platform switch
        {
            Platform.LinkedIn => BuildLinkedInAuthUrl(state),
            Platform.Twitter => BuildTwitterAuthUrl(state),
            _ => throw new NotSupportedException($"OAuth is not supported for {platform}")
        };

        return Task.FromResult(url);
    }

    public async Task<PlatformCredential> ExchangeCodeAsync(
        Platform platform, string code, string state, CancellationToken ct)
    {
        if (!StateStore.TryRemove(state, out var stateEntry) || stateEntry.IsExpired)
            throw new InvalidOperationException("Invalid or expired OAuth state");

        var tokenResponse = platform switch
        {
            Platform.LinkedIn => await ExchangeLinkedInCodeAsync(code, ct),
            Platform.Twitter => await ExchangeTwitterCodeAsync(code, stateEntry.CodeVerifier!, ct),
            _ => throw new NotSupportedException($"OAuth is not supported for {platform}")
        };

        var credential = await db.PlatformCredentials
            .FirstOrDefaultAsync(c => c.Platform == platform, ct);

        if (credential is null)
        {
            credential = new PlatformCredential { Platform = platform };
            db.PlatformCredentials.Add(credential);
        }

        credential.EncryptedAccessToken = encryptor.Encrypt(tokenResponse.AccessToken);
        credential.EncryptedRefreshToken = tokenResponse.RefreshToken is not null
            ? encryptor.Encrypt(tokenResponse.RefreshToken)
            : null;
        credential.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        credential.RefreshTokenExpiresAt = tokenResponse.RefreshTokenExpiresIn.HasValue
            ? DateTimeOffset.UtcNow.AddSeconds(tokenResponse.RefreshTokenExpiresIn.Value)
            : null;
        credential.Scopes = tokenResponse.Scopes;
        credential.IsActive = true;
        credential.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        logger.LogInformation("OAuth tokens stored for {Platform}", platform);
        return credential;
    }

    public async Task<Result<string>> RefreshTokenAsync(PlatformCredential credential, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(credential.EncryptedRefreshToken))
        {
            logger.LogWarning("No refresh token available for {Platform}", credential.Platform);
            credential.IsActive = false;
            credential.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Result<string>.Fail("No refresh token available");
        }

        var refreshToken = encryptor.Decrypt(credential.EncryptedRefreshToken);

        var (tokenEndpoint, clientId, clientSecret) = credential.Platform switch
        {
            Platform.LinkedIn => (
                "https://www.linkedin.com/oauth/v2/accessToken",
                linkedInOptions.Value.ClientId,
                linkedInOptions.Value.ClientSecret),
            Platform.Twitter => (
                "https://api.twitter.com/2/oauth2/token",
                twitterOptions.Value.ClientId,
                twitterOptions.Value.ClientSecret),
            _ => throw new NotSupportedException($"Token refresh not supported for {credential.Platform}")
        };

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId
        };

        if (credential.Platform != Platform.Twitter)
            parameters["client_secret"] = clientSecret;

        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(parameters)
        };

        if (credential.Platform == Platform.Twitter)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Token refresh failed for {Platform}: {Status} {Body}",
                credential.Platform, response.StatusCode, errorBody);
            credential.IsActive = false;
            credential.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Result<string>.Fail($"Token refresh failed: {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var tokenData = JsonSerializer.Deserialize<JsonElement>(json);

        var newAccessToken = tokenData.GetProperty("access_token").GetString()!;
        var expiresIn = tokenData.GetProperty("expires_in").GetInt32();

        credential.EncryptedAccessToken = encryptor.Encrypt(newAccessToken);
        credential.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);

        if (tokenData.TryGetProperty("refresh_token", out var newRefreshProp))
        {
            var newRefreshToken = newRefreshProp.GetString()!;
            credential.EncryptedRefreshToken = encryptor.Encrypt(newRefreshToken);
        }

        credential.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Result<string>.Success(newAccessToken);
    }

    private string BuildLinkedInAuthUrl(string state)
    {
        var li = linkedInOptions.Value;
        var entry = new OAuthStateEntry(Platform.LinkedIn);
        StateStore[state] = entry;

        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["response_type"] = "code";
        qs["client_id"] = li.ClientId;
        qs["redirect_uri"] = li.RedirectUri;
        qs["scope"] = "openid profile w_member_social";
        qs["state"] = state;

        return $"https://www.linkedin.com/oauth/v2/authorization?{qs}";
    }

    private string BuildTwitterAuthUrl(string state)
    {
        var tw = twitterOptions.Value;
        var codeVerifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var codeChallenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

        var entry = new OAuthStateEntry(Platform.Twitter, codeVerifier);
        StateStore[state] = entry;

        var qs = HttpUtility.ParseQueryString(string.Empty);
        qs["response_type"] = "code";
        qs["client_id"] = tw.ClientId;
        qs["redirect_uri"] = tw.RedirectUri;
        qs["scope"] = "tweet.read tweet.write users.read media.write offline.access";
        qs["state"] = state;
        qs["code_challenge"] = codeChallenge;
        qs["code_challenge_method"] = "S256";

        return $"https://twitter.com/i/oauth2/authorize?{qs}";
    }

    private async Task<OAuthTokenResponse> ExchangeLinkedInCodeAsync(string code, CancellationToken ct)
    {
        var li = linkedInOptions.Value;
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = li.RedirectUri,
            ["client_id"] = li.ClientId,
            ["client_secret"] = li.ClientSecret
        };

        var client = httpClientFactory.CreateClient();
        var response = await client.PostAsync(
            "https://www.linkedin.com/oauth/v2/accessToken",
            new FormUrlEncodedContent(parameters), ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("LinkedIn token exchange failed: {Status} {Body}", response.StatusCode, errorBody);
            throw new InvalidOperationException($"LinkedIn token exchange failed: {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var data = JsonSerializer.Deserialize<JsonElement>(json);

        return new OAuthTokenResponse(
            AccessToken: data.GetProperty("access_token").GetString()!,
            RefreshToken: data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            ExpiresIn: data.GetProperty("expires_in").GetInt32(),
            RefreshTokenExpiresIn: data.TryGetProperty("refresh_token_expires_in", out var rtExp)
                ? rtExp.GetInt32() : null,
            Scopes: "openid profile w_member_social");
    }

    private async Task<OAuthTokenResponse> ExchangeTwitterCodeAsync(
        string code, string codeVerifier, CancellationToken ct)
    {
        var tw = twitterOptions.Value;
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = tw.RedirectUri,
            ["client_id"] = tw.ClientId,
            ["code_verifier"] = codeVerifier
        };

        var client = httpClientFactory.CreateClient();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{tw.ClientId}:{tw.ClientSecret}"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.twitter.com/2/oauth2/token")
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

        return new OAuthTokenResponse(
            AccessToken: data.GetProperty("access_token").GetString()!,
            RefreshToken: data.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            ExpiresIn: data.GetProperty("expires_in").GetInt32(),
            RefreshTokenExpiresIn: null,
            Scopes: "tweet.read tweet.write users.read media.write offline.access");
    }

    private static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void CleanExpiredStates()
    {
        foreach (var kvp in StateStore)
        {
            if (kvp.Value.IsExpired)
                StateStore.TryRemove(kvp.Key, out _);
        }
    }

    private sealed record OAuthStateEntry(Platform Platform, string? CodeVerifier = null)
    {
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
        public bool IsExpired => DateTimeOffset.UtcNow - CreatedAt > StateTtl;
    }

    private sealed record OAuthTokenResponse(
        string AccessToken,
        string? RefreshToken,
        int ExpiresIn,
        int? RefreshTokenExpiresIn,
        string Scopes);
}
