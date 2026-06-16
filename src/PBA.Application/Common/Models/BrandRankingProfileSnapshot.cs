using PBA.Domain.Entities;

namespace PBA.Application.Common.Models;

/// <summary>
/// Immutable in-memory projection of one <see cref="BrandPillar"/>, carried by
/// <see cref="BrandRankingProfileSnapshot"/>. <see cref="DescriptionEmbedding"/> is the pillar vector
/// used by the embedding brand-fit pre-filter (section-06/07); it is null until the embedding service
/// has populated it.
/// </summary>
public sealed record BrandPillarSnapshot(
    Guid Id,
    string Name,
    string Description,
    double Weight,
    int Order,
    float[]? DescriptionEmbedding);

/// <summary>
/// Immutable in-memory projection of the active <see cref="BrandRankingProfile"/>. Built ONCE per
/// scoring sweep (R-H3) so every idea in a sweep is scored against the same profile and
/// <c>ScoredProfileVersion</c> is stamped from <see cref="Version"/>, never re-read mid-sweep. The
/// analyzer (section-05) reads positioning/audience/pillars/topics; the embedding pre-filter
/// (section-06/07) reads pillar weights + vectors; read-time ranking (section-08) reads
/// half-life / floor / multipliers. This is the single profile shape those layers share.
/// </summary>
public sealed record BrandRankingProfileSnapshot(
    int Version,
    string Positioning,
    string AudiencePrimary,
    string? AudienceSecondary,
    IReadOnlyList<BrandPillarSnapshot> Pillars,
    IReadOnlyList<string> AuthorityTopics,
    IReadOnlyList<string> AntiTopics,
    IReadOnlyList<string> VoiceMarkers,
    double HalfLifeDays,
    double DecayFloor,
    double AntiTopicMultiplier,
    double AuthorityBoost)
{
    /// <summary>
    /// Projects a loaded <see cref="BrandRankingProfile"/> (with its <c>Pillars</c> populated) into an
    /// immutable snapshot. Pillars are ordered by <see cref="BrandPillarSnapshot.Order"/> for stable
    /// prompt rendering. Call once per sweep against the active profile.
    /// </summary>
    public static BrandRankingProfileSnapshot FromProfile(BrandRankingProfile profile) => new(
        profile.Version,
        profile.Positioning,
        profile.AudiencePrimary,
        profile.AudienceSecondary,
        profile.Pillars
            .OrderBy(p => p.Order)
            .Select(p => new BrandPillarSnapshot(
                p.Id, p.Name, p.Description, p.Weight, p.Order, p.DescriptionEmbedding))
            .ToList(),
        profile.AuthorityTopics.ToList(),
        profile.AntiTopics.ToList(),
        profile.VoiceMarkers.ToList(),
        profile.HalfLifeDays,
        profile.DecayFloor,
        profile.AntiTopicMultiplier,
        profile.AuthorityBoost);
}
