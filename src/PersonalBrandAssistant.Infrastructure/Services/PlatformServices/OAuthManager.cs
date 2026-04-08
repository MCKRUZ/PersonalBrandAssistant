using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.OAuth;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices;

public sealed class OAuthManager : IOAuthManager
{
    private static readonly TimeSpan StateExpiry = TimeSpan.FromMinutes(10);

    private readonly IApplicationDbContext _dbContext;
    private readonly IEncryptionService _encryption;
    private readonly PlatformIntegrationOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OAuthManager> _logger;
    private readonly Dictionary<PlatformType, IOAuthPlatformStrategy> _strategies;

    public OAuthManager(
        IApplicationDbContext dbContext,
        IEncryptionService encryption,
        IOptions<PlatformIntegrationOptions> options,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<OAuthManager> logger)
    {
        _dbContext = dbContext;
        _encryption = encryption;
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
        _strategies = new Dictionary<PlatformType, IOAuthPlatformStrategy>
        {
            [PlatformType.TwitterX] = new TwitterOAuthStrategy(configuration),
            [PlatformType.LinkedIn] = new LinkedInOAuthStrategy(configuration),
            [PlatformType.Instagram] = new InstagramOAuthStrategy(configuration),
            [PlatformType.YouTube] = new YouTubeOAuthStrategy(configuration),
        };
    }

    public async Task<Result<OAuthAuthorizationUrl>> GenerateAuthUrlAsync(
        PlatformType platform, CancellationToken ct)
    {
        var state = GenerateRandomState();
        string? codeVerifier = null;
        string? codeChallenge = null;

        if (platform == PlatformType.TwitterX)
        {
            codeVerifier = GenerateCodeVerifier();
            codeChallenge = ComputeCodeChallenge(codeVerifier);
        }

        var oauthState = new OAuthState
        {
            State = state,
            Platform = platform,
            EncryptedCodeVerifier = codeVerifier != null ? _encryption.Encrypt(codeVerifier) : null,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.Add(StateExpiry),
        };

        _dbContext.OAuthStates.Add(oauthState);
        await _dbContext.SaveChangesAsync(ct);

        var strategy = _strategies[platform];
        var callbackUrl = GetCallbackUrl(platform);
        var url = strategy.BuildAuthUrl(state, codeChallenge, callbackUrl);

        _logger.LogInformation("Generated OAuth URL for {Platform}", platform);
        return Result.Success(new OAuthAuthorizationUrl(url, state));
    }

    public async Task<Result<OAuthTokens>> ExchangeCodeAsync(
        PlatformType platform, string code, string state, string? codeVerifier, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result.Failure<OAuthTokens>(ErrorCode.ValidationFailed, "Authorization code is required");
        }

        // Atomic state consumption: find and delete in a single operation to prevent TOCTOU race
        var oauthState = await _dbContext.OAuthStates
            .FirstOrDefaultAsync(s => s.State == state && s.Platform == platform, ct);

        if (oauthState is null)
        {
            _logger.LogWarning("OAuth state not found for {Platform}, possible CSRF", platform);
            return Result.Failure<OAuthTokens>(ErrorCode.ValidationFailed, "Invalid OAuth state");
        }

        if (oauthState.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _dbContext.OAuthStates.Remove(oauthState);
            await _dbContext.SaveChangesAsync(ct);
            return Result.Failure<OAuthTokens>(ErrorCode.ValidationFailed, "OAuth state expired");
        }

        // Decrypt stored code_verifier for PKCE (prefer server-stored over client-supplied)
        var storedVerifier = oauthState.EncryptedCodeVerifier != null
            ? _encryption.Decrypt(oauthState.EncryptedCodeVerifier)
            : codeVerifier;

        // Delete state entry immediately (single-use) to prevent replay
        _dbContext.OAuthStates.Remove(oauthState);
        await _dbContext.SaveChangesAsync(ct);

        try
        {
            var strategy = _strategies[platform];
            var client = _httpClientFactory.CreateClient("OAuth");
            var callbackUrl = GetCallbackUrl(platform);
            var tokenResponse = await strategy.ExchangeCodeAsync(client, code, storedVerifier, callbackUrl, ct);

            if (tokenResponse is null)
            {
                return Result.Failure<OAuthTokens>(ErrorCode.InternalError, "Token exchange failed");
            }

            var platformEntity = await _dbContext.Platforms
                .FirstOrDefaultAsync(p => p.Type == platform, ct);

            if (platformEntity is null)
            {
                return Result.NotFound<OAuthTokens>($"Platform '{platform}' not found");
            }

            platformEntity.EncryptedAccessToken = _encryption.Encrypt(tokenResponse.AccessToken);
            platformEntity.EncryptedRefreshToken = tokenResponse.RefreshToken != null
                ? _encryption.Encrypt(tokenResponse.RefreshToken)
                : null;
            platformEntity.TokenExpiresAt = tokenResponse.ExpiresAt;
            platformEntity.IsConnected = true;
            platformEntity.GrantedScopes = tokenResponse.GrantedScopes?.ToArray();

            await _dbContext.SaveChangesAsync(ct);

            _logger.LogInformation("OAuth token exchange completed for {Platform}", platform);
            return Result.Success(tokenResponse);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error during token exchange for {Platform}", platform);
            return Result.Failure<OAuthTokens>(ErrorCode.InternalError, "Token exchange failed due to an external service error");
        }
    }

    public async Task<Result<OAuthTokens>> RefreshTokenAsync(
        PlatformType platform, CancellationToken ct)
    {
        var platformEntity = await _dbContext.Platforms
            .FirstOrDefaultAsync(p => p.Type == platform, ct);

        if (platformEntity is null)
        {
            return Result.NotFound<OAuthTokens>($"Platform '{platform}' not found");
        }

        if (platformEntity.EncryptedRefreshToken is null)
        {
            // Reddit uses password grant — re-authenticate instead of refresh
            if (platform == PlatformType.Reddit)
                return await ReauthenticateRedditAsync(platformEntity, ct);

            return Result.Failure<OAuthTokens>(ErrorCode.ValidationFailed, "No refresh token available");
        }

        var refreshToken = _encryption.Decrypt(platformEntity.EncryptedRefreshToken);

        try
        {
            var strategy = _strategies[platform];
            var client = _httpClientFactory.CreateClient("OAuth");
            var tokenResponse = await strategy.RefreshTokenAsync(client, refreshToken, ct);

            if (tokenResponse is null)
            {
                platformEntity.IsConnected = false;
                platformEntity.EncryptedAccessToken = null;
                platformEntity.EncryptedRefreshToken = null;
                platformEntity.GrantedScopes = null;
                await _dbContext.SaveChangesAsync(ct);

                _logger.LogWarning("Token refresh failed for {Platform}, marking disconnected", platform);
                return Result.Failure<OAuthTokens>(ErrorCode.InternalError, "Token refresh failed, platform disconnected");
            }

            platformEntity.EncryptedAccessToken = _encryption.Encrypt(tokenResponse.AccessToken);
            if (tokenResponse.RefreshToken != null)
            {
                platformEntity.EncryptedRefreshToken = _encryption.Encrypt(tokenResponse.RefreshToken);
            }

            platformEntity.TokenExpiresAt = tokenResponse.ExpiresAt;
            await _dbContext.SaveChangesAsync(ct);

            _logger.LogInformation("Token refreshed for {Platform}", platform);
            return Result.Success(tokenResponse);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error during token refresh for {Platform}", platform);
            return Result.Failure<OAuthTokens>(ErrorCode.InternalError, "Token refresh failed due to an external service error");
        }
    }

    public async Task<Result<Unit>> RevokeTokenAsync(
        PlatformType platform, CancellationToken ct)
    {
        var platformEntity = await _dbContext.Platforms
            .FirstOrDefaultAsync(p => p.Type == platform, ct);

        if (platformEntity is null)
        {
            return Result.NotFound<Unit>($"Platform '{platform}' not found");
        }

        if (platformEntity.EncryptedAccessToken != null)
        {
            var accessToken = _encryption.Decrypt(platformEntity.EncryptedAccessToken);
            try
            {
                var strategy = _strategies[platform];
                var client = _httpClientFactory.CreateClient("OAuth");
                await strategy.RevokeTokenAsync(client, accessToken, ct);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Failed to revoke token at {Platform} endpoint, clearing locally", platform);
            }
        }

        platformEntity.EncryptedAccessToken = null;
        platformEntity.EncryptedRefreshToken = null;
        platformEntity.TokenExpiresAt = null;
        platformEntity.IsConnected = false;
        platformEntity.GrantedScopes = null;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Token revoked for {Platform}", platform);
        return Result.Success(Unit.Value);
    }

    private string GetCallbackUrl(PlatformType platform) => platform switch
    {
        PlatformType.TwitterX => _options.Twitter.CallbackUrl,
        PlatformType.LinkedIn => _options.LinkedIn.CallbackUrl,
        PlatformType.Instagram => _options.Instagram.CallbackUrl,
        PlatformType.YouTube => _options.YouTube.CallbackUrl,
        _ => throw new ArgumentOutOfRangeException(nameof(platform)),
    };

    private static string GenerateRandomState()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string ComputeCodeChallenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private async Task<Result<OAuthTokens>> ReauthenticateRedditAsync(
        Platform platformEntity, CancellationToken ct)
    {
        var clientId = _configuration["PlatformIntegrations:Reddit:ClientId"];
        var clientSecret = _configuration["PlatformIntegrations:Reddit:ClientSecret"];
        var username = _configuration["PlatformIntegrations:Reddit:Username"];
        var password = _configuration["PlatformIntegrations:Reddit:Password"];
        var userAgent = _configuration["PlatformIntegrations:Reddit:UserAgent"] ?? "pba/1.0";

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(username))
            return Result.Failure<OAuthTokens>(ErrorCode.ValidationFailed, "Reddit credentials not configured");

        using var http = _httpClientFactory.CreateClient("OAuth");
        http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

        var response = await http.PostAsync(
            "https://www.reddit.com/api/v1/access_token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = username!,
                ["password"] = password!,
                ["scope"] = "identity read submit privatemessages history",
            }), ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Reddit password grant refresh failed: {Status}", response.StatusCode);
            return Result.Failure<OAuthTokens>(ErrorCode.InternalError, "Reddit re-authentication failed");
        }

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
        if (!json.TryGetProperty("access_token", out var tokenProp))
            return Result.Failure<OAuthTokens>(ErrorCode.InternalError, "Reddit response missing access_token");

        var accessToken = tokenProp.GetString()!;
        var expiresIn = json.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;

        platformEntity.EncryptedAccessToken = _encryption.Encrypt(accessToken);
        platformEntity.TokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 300);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Reddit token refreshed via password grant");
        var scope = json.TryGetProperty("scope", out var s) ? s.GetString() : null;
        return Result.Success(new OAuthTokens(accessToken, null, DateTimeOffset.UtcNow.AddSeconds(expiresIn),
            scope?.Split(' ')));
    }
}
