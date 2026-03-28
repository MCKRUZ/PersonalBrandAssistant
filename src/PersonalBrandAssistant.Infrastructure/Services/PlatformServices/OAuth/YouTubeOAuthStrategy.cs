using Microsoft.Extensions.Configuration;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.OAuth;

internal sealed class YouTubeOAuthStrategy : IOAuthPlatformStrategy
{
    private readonly IConfiguration _configuration;

    public YouTubeOAuthStrategy(IConfiguration configuration) => _configuration = configuration;

    public string BuildAuthUrl(string state, string? codeChallenge, string callbackUrl)
    {
        var clientId = _configuration["PlatformIntegrations:YouTube:ClientId"];
        return $"https://accounts.google.com/o/oauth2/v2/auth?response_type=code&client_id={clientId}" +
               $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
               $"&scope=https://www.googleapis.com/auth/youtube%20https://www.googleapis.com/auth/youtube.upload" +
               $"&state={state}&access_type=offline&prompt=consent";
    }

    public async Task<OAuthTokens?> ExchangeCodeAsync(
        HttpClient client, string code, string? codeVerifier, string callbackUrl, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = callbackUrl,
            ["client_id"] = _configuration["PlatformIntegrations:YouTube:ClientId"]!,
            ["client_secret"] = _configuration["PlatformIntegrations:YouTube:ClientSecret"]!,
        });
        return await OAuthHelpers.SendTokenRequestAsync(client, request, ct);
    }

    public async Task<OAuthTokens?> RefreshTokenAsync(
        HttpClient client, string refreshToken, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = _configuration["PlatformIntegrations:YouTube:ClientId"]!,
            ["client_secret"] = _configuration["PlatformIntegrations:YouTube:ClientSecret"]!,
        });
        return await OAuthHelpers.SendTokenRequestAsync(client, request, ct);
    }

    public async Task RevokeTokenAsync(HttpClient client, string accessToken, CancellationToken ct)
    {
        // WARNING: Token in URL — Google OAuth API requirement. See DI logging config
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://oauth2.googleapis.com/revoke?token={accessToken}");
        request.Content = new StringContent(string.Empty);
        await client.SendAsync(request, ct);
    }
}
