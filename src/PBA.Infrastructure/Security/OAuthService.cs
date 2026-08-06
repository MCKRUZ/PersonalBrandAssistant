using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Security;

// Thin coordinator: owns OAuth state storage, credential upsert, and token encryption. Per-platform
// wire details (authorize URLs, code exchange, refresh) live in keyed IOAuthProvider implementations.
public sealed class OAuthService(
    IServiceProvider serviceProvider,
    ITokenEncryptor encryptor,
    IAppDbContext db,
    ILogger<OAuthService> logger) : IOAuthService
{
    private static readonly ConcurrentDictionary<string, OAuthStateEntry> StateStore = new();
    private const int MaxPendingStates = 1000;

    public Task<string> GetAuthorizationUrlAsync(Platform platform, CredentialPurpose purpose, CancellationToken ct)
    {
        CleanExpiredStates();

        if (StateStore.Count >= MaxPendingStates)
            throw new InvalidOperationException("Too many pending OAuth flows");

        var provider = ResolveProvider(platform);
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var authorization = provider.BuildAuthorization(state, purpose);

        StateStore[state] = new OAuthStateEntry(platform, authorization.Additions.CodeVerifier, purpose);

        return Task.FromResult(authorization.Url);
    }

    public async Task<PlatformCredential> ExchangeCodeAsync(
        Platform platform, string code, string state, CancellationToken ct)
    {
        if (!StateStore.TryRemove(state, out var stateEntry) || stateEntry.IsExpired)
            throw new InvalidOperationException("Invalid or expired OAuth state");

        var provider = ResolveProvider(platform);
        var tokenResult = await provider.ExchangeCodeAsync(code, stateEntry, ct);

        // Find/create by (Platform, Purpose): an Analytics flow must not overwrite the Publishing credential
        // (and vice versa). The purpose is the one captured at authorize time and carried in the state entry.
        var purpose = stateEntry.Purpose;
        var credential = await db.PlatformCredentials
            .FirstOrDefaultAsync(c => c.Platform == platform && c.Purpose == purpose, ct);

        if (credential is null)
        {
            credential = new PlatformCredential { Platform = platform, Purpose = purpose };
            db.PlatformCredentials.Add(credential);
        }

        credential.EncryptedAccessToken = encryptor.Encrypt(tokenResult.AccessToken);
        credential.EncryptedRefreshToken = tokenResult.RefreshToken is not null
            ? encryptor.Encrypt(tokenResult.RefreshToken)
            : null;
        credential.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokenResult.ExpiresIn);
        credential.RefreshTokenExpiresAt = tokenResult.RefreshTokenExpiresIn.HasValue
            ? DateTimeOffset.UtcNow.AddSeconds(tokenResult.RefreshTokenExpiresIn.Value)
            : null;
        credential.Scopes = tokenResult.Scopes;
        credential.IsActive = true;
        credential.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        logger.LogInformation("OAuth tokens stored for {Platform}", platform);
        return credential;
    }

    public async Task<Result<string>> RefreshTokenAsync(PlatformCredential credential, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(credential.EncryptedRefreshToken))
        {
            logger.LogWarning("No refresh token available for {Platform}", credential.Platform);
            credential.IsActive = false;
            credential.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Result<string>.Fail("No refresh token available");
        }

        var provider = ResolveProvider(credential.Platform);
        var refreshResult = await provider.RefreshAsync(credential, ct);

        if (!refreshResult.IsSuccess)
        {
            // Coordinator (publishing path) preserves deactivate-on-any-failure. The poller (section-05)
            // implements revoked-only deactivation by reading refreshResult.FailureReason directly.
            credential.IsActive = false;
            credential.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Result<string>.Fail(refreshResult.Error ?? "Token refresh failed");
        }

        var tokens = refreshResult.Tokens!;
        credential.EncryptedAccessToken = encryptor.Encrypt(tokens.AccessToken);
        credential.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn);

        if (tokens.RefreshToken is not null)
            credential.EncryptedRefreshToken = encryptor.Encrypt(tokens.RefreshToken);

        credential.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Result<string>.Success(tokens.AccessToken);
    }

    private IOAuthProvider ResolveProvider(Platform platform) =>
        serviceProvider.GetKeyedService<IOAuthProvider>(platform)
        ?? throw new NotSupportedException($"OAuth is not supported for {platform}");

    private static void CleanExpiredStates()
    {
        foreach (var kvp in StateStore)
        {
            if (kvp.Value.IsExpired)
                StateStore.TryRemove(kvp.Key, out _);
        }
    }
}
