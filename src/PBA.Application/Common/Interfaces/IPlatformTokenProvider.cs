using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Common.Interfaces;

// Ensures a platform's active credential for a given PURPOSE has a FRESH access token right now,
// refreshing + persisting on demand. Analytics and Publishing are separate credentials with
// different scopes (a read-only insights token cannot post), so the purpose is part of the lookup
// rather than assumed. Returns the decrypted access token, or Fail on
// missing/inactive/revoked/transient. Implemented in Infrastructure so Application handlers and
// connectors never depend on the OAuth provider directly.
public interface IPlatformTokenProvider
{
    Task<Result<string>> GetFreshAccessTokenAsync(
        Platform platform, CredentialPurpose purpose, CancellationToken ct);
}
