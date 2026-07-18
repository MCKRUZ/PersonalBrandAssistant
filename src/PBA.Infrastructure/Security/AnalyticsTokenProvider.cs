using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Security;

// On-demand token freshness for live analytics reads. Mirrors the poller's revoked-only-deactivation refresh
// (calls the keyed provider directly, re-persists rotated tokens), but returns the decrypted token for a
// caller that needs to hit the live API right now.
public sealed class AnalyticsTokenProvider(
    IServiceProvider serviceProvider,
    IAppDbContext db,
    ITokenEncryptor encryptor) : IAnalyticsTokenProvider
{
    public async Task<Result<string>> GetFreshAccessTokenAsync(Platform platform, CancellationToken ct)
    {
        var credential = await db.PlatformCredentials.FirstOrDefaultAsync(
            c => c.Platform == platform && c.Purpose == CredentialPurpose.Analytics && c.IsActive, ct);
        if (credential is null)
            return Result<string>.Fail($"{platform} analytics is not connected.");

        var provider = serviceProvider.GetRequiredKeyedService<IOAuthProvider>(platform);
        var now = DateTimeOffset.UtcNow;

        if (provider.NeedsRefresh(credential, now))
        {
            var refresh = await provider.RefreshAsync(credential, ct);
            if (!refresh.IsSuccess)
            {
                if (refresh.FailureReason == RefreshFailureReason.Revoked)
                {
                    credential.IsActive = false;
                    credential.UpdatedAt = now;
                    await db.SaveChangesAsync(ct);
                    return Result<string>.Fail($"{platform} analytics token revoked; reconnect required.");
                }

                // Transient — leave active; the caller degrades for this request only.
                return Result<string>.Fail($"{platform} analytics token refresh failed (transient).");
            }

            var tokens = refresh.Tokens!;
            credential.EncryptedAccessToken = encryptor.Encrypt(tokens.AccessToken);
            credential.AccessTokenExpiresAt = now.AddSeconds(tokens.ExpiresIn);
            if (tokens.RefreshToken is not null)
                credential.EncryptedRefreshToken = encryptor.Encrypt(tokens.RefreshToken);
            credential.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return Result<string>.Success(encryptor.Decrypt(credential.EncryptedAccessToken));
    }
}
