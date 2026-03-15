using System.Text;
using Microsoft.Extensions.Configuration;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.OAuth;

internal sealed class TwitterOAuthStrategy : IOAuthPlatformStrategy
{
    private readonly IConfiguration _configuration;

    public TwitterOAuthStrategy(IConfiguration configuration) => _configuration = configuration;

    public string BuildAuthUrl(string state, string? codeChallenge, string callbackUrl)
    {
        var clientId = _configuration["PlatformIntegrations:Twitter:ClientId"];
        return $"https://twitter.com/i/oauth2/authorize?response_type=code&client_id={clientId}" +
               $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
               $"&scope=tweet.read%20tweet.write%20users.read%20offline.access" +
               $"&state={state}&code_challenge={codeChallenge}&code_challenge_method=S256";
    }

    public async Task<OAuthTokens?> ExchangeCodeAsync(
        HttpClient client, string code, string? codeVerifier, string callbackUrl, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.com/2/oauth2/token");
        request.Headers.Authorization = CreateBasicAuthHeader();
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = callbackUrl,
            ["code_verifier"] = codeVerifier ?? string.Empty,
        });
        return await OAuthHelpers.SendTokenRequestAsync(client, request, ct);
    }

    public async Task<OAuthTokens?> RefreshTokenAsync(
        HttpClient client, string refreshToken, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.com/2/oauth2/token");
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
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.x.com/2/oauth2/revoke");
        request.Headers.Authorization = CreateBasicAuthHeader();
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = accessToken,
        });
        await client.SendAsync(request, ct);
    }

    private System.Net.Http.Headers.AuthenticationHeaderValue CreateBasicAuthHeader()
    {
        var clientId = _configuration["PlatformIntegrations:Twitter:ClientId"]!;
        var clientSecret = _configuration["PlatformIntegrations:Twitter:ClientSecret"]!;
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        return new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);
    }
}
