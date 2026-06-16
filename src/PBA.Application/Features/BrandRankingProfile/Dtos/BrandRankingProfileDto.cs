using DomainBrandRankingProfile = PBA.Domain.Entities.BrandRankingProfile;

namespace PBA.Application.Features.BrandRankingProfile.Dtos;

public sealed record BrandRankingProfileDto
{
    public Guid Id { get; init; }
    public int Version { get; init; }
    public string Positioning { get; init; } = "";
    public string AudiencePrimary { get; init; } = "";
    public string? AudienceSecondary { get; init; }
    public double HalfLifeDays { get; init; }
    public double DecayFloor { get; init; }
    public double AntiTopicMultiplier { get; init; }
    public double AuthorityBoost { get; init; }
    public IReadOnlyList<BrandRankingPillarDto> Pillars { get; init; } = [];
    public IReadOnlyList<string> AuthorityTopics { get; init; } = [];
    public IReadOnlyList<string> AntiTopics { get; init; } = [];
    public IReadOnlyList<string> VoiceMarkers { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }

    // Optimistic-concurrency token (Npgsql xmin, a uint) round-tripped to PUT; serialized as a string.
    public string ConcurrencyToken { get; init; } = "";
}

public sealed record BrandRankingPillarDto
{
    public Guid Id { get; init; }   // identity (R-C3); survives renames. DescriptionEmbedding never exposed.
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public double Weight { get; init; }
    public int Order { get; init; }
}

public static class BrandRankingProfileMapping
{
    /// <summary>Projects the aggregate to its DTO, pillars ordered by <c>Order</c>, with the xmin token
    /// stringified into <see cref="BrandRankingProfileDto.ConcurrencyToken"/>. Never exposes pillar embeddings.</summary>
    public static BrandRankingProfileDto ToDto(DomainBrandRankingProfile profile) => new()
    {
        Id = profile.Id,
        Version = profile.Version,
        Positioning = profile.Positioning,
        AudiencePrimary = profile.AudiencePrimary,
        AudienceSecondary = profile.AudienceSecondary,
        HalfLifeDays = profile.HalfLifeDays,
        DecayFloor = profile.DecayFloor,
        AntiTopicMultiplier = profile.AntiTopicMultiplier,
        AuthorityBoost = profile.AuthorityBoost,
        Pillars = profile.Pillars
            .OrderBy(p => p.Order)
            .Select(p => new BrandRankingPillarDto
            {
                Id = p.Id, Name = p.Name, Description = p.Description, Weight = p.Weight, Order = p.Order
            })
            .ToList(),
        AuthorityTopics = profile.AuthorityTopics.ToList(),
        AntiTopics = profile.AntiTopics.ToList(),
        VoiceMarkers = profile.VoiceMarkers.ToList(),
        UpdatedAt = profile.UpdatedAt,
        ConcurrencyToken = profile.Xmin.ToString()
    };
}
