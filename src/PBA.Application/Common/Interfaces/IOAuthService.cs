using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Application.Common.Interfaces;

public interface IOAuthService
{
    // Purpose is captured here and persisted in OAuth state; the callback reads it back to stamp the
    // credential's CredentialPurpose. Defaults to Publishing (the pre-analytics behavior).
    Task<string> GetAuthorizationUrlAsync(Platform platform, CredentialPurpose purpose, CancellationToken ct);
    Task<PlatformCredential> ExchangeCodeAsync(Platform platform, string code, string state, CancellationToken ct);
    Task<Result<string>> RefreshTokenAsync(PlatformCredential credential, CancellationToken ct);
}
