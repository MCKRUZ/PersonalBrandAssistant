using Microsoft.Extensions.Configuration;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.OAuth;

internal sealed class InstagramOAuthStrategy : IOAuthPlatformStrategy
{
    // NOTE: Facebook Graph API version should be updated periodically
    private const string FacebookApiVersion = "v19.0";

    private readonly IConfiguration _configuration;

    public InstagramOAuthStrategy(IConfiguration configuration) => _configuration = configuration;

    public string BuildAuthUrl(string state, string? codeChallenge, string callbackUrl)
    {
        var appId = _configuration["PlatformIntegrations:Instagram:AppId"];
        return $"https://www.facebook.com/{FacebookApiVersion}/dialog/oauth?client_id={appId}" +
               $"&redirect_uri={Uri.EscapeDataString(callbackUrl)}" +
               $"&scope=instagram_basic,instagram_content_publish,pages_show_list&state={state}";
    }

    public async Task<OAuthTokens?> ExchangeCodeAsync(
        HttpClient client, string code, string? codeVerifier, string callbackUrl, CancellationToken ct)
    {
        // Step 1: Exchange for short-lived token
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.instagram.com/oauth/access_token");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _configuration["PlatformIntegrations:Instagram:AppId"]!,
            ["client_secret"] = _configuration["PlatformIntegrations:Instagram:AppSecret"]!,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = callbackUrl,
            ["code"] = code,
        });

        var shortLived = await OAuthHelpers.SendTokenRequestAsync(client, request, ct);
        if (shortLived is null) return null;

        // Step 2: Exchange for long-lived token
        // WARNING: Instagram API requires access_token in URL query parameter — ensure HTTP client
        // logging is suppressed to prevent token leakage in logs (configured in DI section-12)
        var appSecret = _configuration["PlatformIntegrations:Instagram:AppSecret"]!;
        var longLivedUrl = $"https://graph.instagram.com/access_token?grant_type=ig_exchange_token" +
                           $"&client_secret={appSecret}&access_token={shortLived.AccessToken}";

        var longRequest = new HttpRequestMessage(HttpMethod.Get, longLivedUrl);
        return await OAuthHelpers.SendTokenRequestAsync(client, longRequest, ct) ?? shortLived;
    }

    public async Task<OAuthTokens?> RefreshTokenAsync(
        HttpClient client, string currentAccessToken, CancellationToken ct)
    {
        // WARNING: Instagram API requires access_token in URL — see DI logging config
        var url = $"https://graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token&access_token={currentAccessToken}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        return await OAuthHelpers.SendTokenRequestAsync(client, request, ct);
    }

    public async Task RevokeTokenAsync(HttpClient client, string accessToken, CancellationToken ct)
    {
        // WARNING: Token in URL — see DI logging config
        var request = new HttpRequestMessage(HttpMethod.Delete,
            $"https://graph.facebook.com/me/permissions?access_token={accessToken}");
        await client.SendAsync(request, ct);
    }
}
