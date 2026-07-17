using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Security;

// One provider per platform. The coordinator (OAuthService) resolves these by keyed DI and owns
// state storage, credential persistence, and encryption. Providers own the wire details.
public interface IOAuthProvider
{
    Platform Platform { get; }

    // Provider builds its own authorize URL AND any state it needs persisted (e.g. Twitter PKCE verifier).
    AuthorizationRequest BuildAuthorization(string state);

    Task<OAuthTokenResult> ExchangeCodeAsync(string code, OAuthStateEntry state, CancellationToken ct);

    // Takes the full credential (not a bare refresh-token string) so future providers with no separate
    // refresh token can implement their own refresh. Returns plaintext tokens for the coordinator to encrypt.
    Task<Result<OAuthTokenResult>> RefreshAsync(PlatformCredential credential, CancellationToken ct);

    // Provider-specific proactive refresh window. Declared for the poller (section-05); LinkedIn/Twitter
    // mirror the effective window their connectors use today.
    bool NeedsRefresh(PlatformCredential credential, DateTimeOffset now);
}
