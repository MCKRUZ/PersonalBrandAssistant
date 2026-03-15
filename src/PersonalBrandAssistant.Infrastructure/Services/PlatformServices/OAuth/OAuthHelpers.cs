using System.Net.Http.Json;
using System.Text.Json;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.OAuth;

internal static class OAuthHelpers
{
    internal static async Task<OAuthTokens?> SendTokenRequestAsync(
        HttpClient client, HttpRequestMessage request, CancellationToken ct)
    {
        var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        var accessToken = json.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        if (accessToken is null) return null;

        var refreshToken = json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var expiresIn = json.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : (int?)null;
        var scope = json.TryGetProperty("scope", out var sc) ? sc.GetString() : null;

        var expiresAt = expiresIn.HasValue ? DateTimeOffset.UtcNow.AddSeconds(expiresIn.Value) : (DateTimeOffset?)null;
        var scopes = scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList().AsReadOnly();

        return new OAuthTokens(accessToken, refreshToken, expiresAt, scopes);
    }
}
