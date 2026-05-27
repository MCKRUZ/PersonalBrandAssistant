using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Application.Common.Interfaces;

public interface IOAuthService
{
    Task<string> GetAuthorizationUrlAsync(Platform platform, CancellationToken ct);
    Task<PlatformCredential> ExchangeCodeAsync(Platform platform, string code, string state, CancellationToken ct);
    Task<Result<string>> RefreshTokenAsync(PlatformCredential credential, CancellationToken ct);
}
