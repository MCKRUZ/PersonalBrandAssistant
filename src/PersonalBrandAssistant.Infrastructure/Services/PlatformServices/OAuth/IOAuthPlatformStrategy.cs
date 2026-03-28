using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.OAuth;

public interface IOAuthPlatformStrategy
{
    string BuildAuthUrl(string state, string? codeChallenge, string callbackUrl);
    Task<OAuthTokens?> ExchangeCodeAsync(HttpClient client, string code, string? codeVerifier, string callbackUrl, CancellationToken ct);
    Task<OAuthTokens?> RefreshTokenAsync(HttpClient client, string refreshToken, CancellationToken ct);
    Task RevokeTokenAsync(HttpClient client, string accessToken, CancellationToken ct);
}
