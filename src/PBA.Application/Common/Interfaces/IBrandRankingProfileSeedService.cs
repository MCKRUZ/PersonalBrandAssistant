namespace PBA.Application.Common.Interfaces;

/// <summary>
/// Seeds the v1 active <c>BrandRankingProfile</c>. Idempotent and race-safe across the two deploy
/// hosts: returns 0 if an active profile already exists, and tolerates a concurrent insert losing the
/// partial-unique-index race (R-L5).
/// </summary>
public interface IBrandRankingProfileSeedService
{
    Task<int> SeedAsync(CancellationToken cancellationToken = default);
}
