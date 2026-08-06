using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Security;

// On-demand token freshness for callers that must hit a live API right now. Mirrors the poller's
// revoked-only-deactivation refresh (calls the keyed provider directly, re-persists rotated tokens),
// but returns the decrypted token to the caller.
//
// Purpose is a parameter, not a constant: Analytics and Publishing are separate credential rows with
// different scopes. Instagram is the clearest case — its analytics token carries
// instagram_business_manage_insights and physically cannot post, so a publisher that grabbed "the
// Instagram token" would fail at the API with a permissions error rather than a missing-credential one.
public sealed class PlatformTokenProvider(
    IServiceProvider serviceProvider,
    IAppDbContext db,
    ITokenEncryptor encryptor) : IPlatformTokenProvider
{
    public async Task<Result<string>> GetFreshAccessTokenAsync(
        Platform platform, CredentialPurpose purpose, CancellationToken ct)
    {
        var credential = await db.PlatformCredentials.FirstOrDefaultAsync(
            c => c.Platform == platform && c.Purpose == purpose && c.IsActive, ct);
        if (credential is null)
            return Result<string>.Fail($"{platform} {purpose} is not connected.");

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
                    return Result<string>.Fail($"{platform} {purpose} token revoked; reconnect required.");
                }

                // Transient — leave active; the caller degrades for this request only.
                return Result<string>.Fail($"{platform} {purpose} token refresh failed (transient).");
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
