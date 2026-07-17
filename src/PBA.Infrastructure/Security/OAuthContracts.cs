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

// RefreshAsync failure classification. The poller (section-05) deactivates a credential only on Revoked,
// never on Transient (network / 5xx / 429).
public enum RefreshFailureReason
{
    Revoked,
    Transient
}

// Provider refresh outcome. The domain Result<T> can't carry a RefreshFailureReason, so refresh uses this
// dedicated envelope: success carries the rotated tokens; failure carries the reason the poller keys on.
public sealed record OAuthRefreshResult
{
    public bool IsSuccess { get; }
    public OAuthTokenResult? Tokens { get; }
    public RefreshFailureReason? FailureReason { get; }
    public string? Error { get; }

    private OAuthRefreshResult(bool isSuccess, OAuthTokenResult? tokens, RefreshFailureReason? reason, string? error)
    {
        IsSuccess = isSuccess;
        Tokens = tokens;
        FailureReason = reason;
        Error = error;
    }

    public static OAuthRefreshResult Success(OAuthTokenResult tokens) => new(true, tokens, null, null);

    public static OAuthRefreshResult Fail(RefreshFailureReason reason, string error) =>
        new(false, null, reason, error);
}
