namespace PBA.Infrastructure.Security;

// What a provider returns from BuildAuthorization: the authorize URL plus any state the coordinator
// must persist on the provider's behalf (provider-specific logic never leaks into the coordinator).
public sealed record AuthorizationRequest(string Url, OAuthStateAdditions Additions);

// Extra state a provider needs persisted alongside its flow. Today only Twitter uses CodeVerifier (PKCE).
public sealed record OAuthStateAdditions(string? CodeVerifier = null);

// Provider token result. Replaces the private OAuthTokenResponse record; same shape.
public sealed record OAuthTokenResult(
    string AccessToken,
    string? RefreshToken,
    int ExpiresIn,
    int? RefreshTokenExpiresIn,
    string Scopes);

// RefreshAsync failure classification. Section-03 adds full revoked-vs-transient mapping per provider;
// LinkedIn/Twitter in this section preserve today's behavior (any non-success refresh deactivates).
public enum RefreshFailureReason
{
    Revoked,
    Transient
}
