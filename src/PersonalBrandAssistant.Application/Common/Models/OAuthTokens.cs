namespace PersonalBrandAssistant.Application.Common.Models;

public record OAuthAuthorizationUrl(string Url, string State);

public record OAuthTokens(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt, IReadOnlyList<string>? GrantedScopes)
{
    public override string ToString() =>
        $"OAuthTokens {{ ExpiresAt = {ExpiresAt}, GrantedScopes = [{string.Join(", ", GrantedScopes ?? [])}] }}";
}
