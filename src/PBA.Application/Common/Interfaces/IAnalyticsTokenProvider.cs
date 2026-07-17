using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Common.Interfaces;

// Ensures a platform's active Analytics credential has a FRESH access token for a live read, refreshing +
// persisting on demand (the daily poller only keeps it fresh once/day, but access tokens expire in ~1h).
// Returns the decrypted access token, or Fail on missing/inactive/revoked/transient. Implemented in
// Infrastructure so the Application read handlers never depend on the OAuth provider directly.
public interface IAnalyticsTokenProvider
{
    Task<Result<string>> GetFreshAccessTokenAsync(Platform platform, CancellationToken ct);
}
