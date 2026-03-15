using Microsoft.Extensions.Configuration;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.OAuth;

internal sealed class LinkedInOAuthStrategy : IOAuthPlatformStrategy
{
    private readonly IConfiguration _configuration;

    public LinkedInOAuthStrategy(IConfiguration configuration) => _configuration = configuration;

    public string BuildAuthUrl(string state, string? codeChallenge, string callbackUrl)
    {
        var clientId = _configuration["PlatformIntegrations:LinkedIn:ClientId"];
        return $"https://www.linkedin.com/oauth/v2/authorization?response_type=code&client_id={clientId}" +
               $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
               $"&scope=w_member_social%20r_liteprofile&state={state}";
    }

    public async Task<OAuthTokens?> ExchangeCodeAsync(
        HttpClient client, string code, string? codeVerifier, string callbackUrl, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://www.linkedin.com/oauth/v2/accessToken");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = callbackUrl,
            ["client_id"] = _configuration["PlatformIntegrations:LinkedIn:ClientId"]!,
            ["client_secret"] = _configuration["PlatformIntegrations:LinkedIn:ClientSecret"]!,
        });
        return await OAuthHelpers.SendTokenRequestAsync(client, request, ct);
    }

    public async Task<OAuthTokens?> RefreshTokenAsync(
        HttpClient client, string refreshToken, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://www.linkedin.com/oauth/v2/accessToken");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = _configuration["PlatformIntegrations:LinkedIn:ClientId"]!,
            ["client_secret"] = _configuration["PlatformIntegrations:LinkedIn:ClientSecret"]!,
        });
        return await OAuthHelpers.SendTokenRequestAsync(client, request, ct);
    }

    public Task RevokeTokenAsync(HttpClient client, string accessToken, CancellationToken ct)
    {
        // LinkedIn has no revocation API — just clear local tokens
        return Task.CompletedTask;
    }
}
