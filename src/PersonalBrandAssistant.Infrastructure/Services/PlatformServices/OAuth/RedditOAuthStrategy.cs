using System.Text;
using Microsoft.Extensions.Configuration;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.OAuth;

internal sealed class RedditOAuthStrategy : IOAuthPlatformStrategy
{
    private readonly IConfiguration _configuration;

    public RedditOAuthStrategy(IConfiguration configuration) => _configuration = configuration;

    public string BuildAuthUrl(string state, string? codeChallenge, string callbackUrl)
    {
        var clientId = _configuration["PlatformIntegrations:Reddit:ClientId"];
        return $"https://www.reddit.com/api/v1/authorize?client_id={clientId}" +
               $"&response_type=code&state={state}" +
               $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
               $"&duration=permanent&scope=identity+read+submit+privatemessages+history";
    }

    public async Task<OAuthTokens?> ExchangeCodeAsync(
        HttpClient client, string code, string? codeVerifier, string callbackUrl, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://www.reddit.com/api/v1/access_token");
        request.Headers.Authorization = CreateBasicAuthHeader();
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = callbackUrl,
        });
        return await OAuthHelpers.SendTokenRequestAsync(client, request, ct);
    }

    public async Task<OAuthTokens?> RefreshTokenAsync(
        HttpClient client, string refreshToken, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://www.reddit.com/api/v1/access_token");
        request.Headers.Authorization = CreateBasicAuthHeader();
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        });
        return await OAuthHelpers.SendTokenRequestAsync(client, request, ct);
    }

    public async Task RevokeTokenAsync(HttpClient client, string accessToken, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://www.reddit.com/api/v1/revoke_token");
        request.Headers.Authorization = CreateBasicAuthHeader();
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = accessToken,
            ["token_type_hint"] = "access_token",
        });
        await client.SendAsync(request, ct);
    }

    private System.Net.Http.Headers.AuthenticationHeaderValue CreateBasicAuthHeader()
    {
        var clientId = _configuration["PlatformIntegrations:Reddit:ClientId"]!;
        var clientSecret = _configuration["PlatformIntegrations:Reddit:ClientSecret"]!;
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        return new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);
    }
}
