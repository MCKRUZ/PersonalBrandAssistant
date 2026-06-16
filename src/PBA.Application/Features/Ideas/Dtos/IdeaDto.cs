using PBA.Domain.Enums;

namespace PBA.Application.Features.Ideas.Dtos;

public record IdeaDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Url { get; init; }
    public string SourceName { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string? Summary { get; init; }
    public string? ThumbnailUrl { get; init; }
    public IdeaStatus Status { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public DateTimeOffset DetectedAt { get; init; }
    public bool HasSavedDetails { get; init; }
    public int? Score { get; init; }
    public string? ScoreReason { get; init; }
    public bool IsDuplicate { get; init; }

    // Brand-anchored composite ranking (section-08), computed at read time from stored sub-scores +
    // the active profile. Default 0 when no active profile exists (graceful degrade).
    public double Rank { get; init; }
    public double BrandFit { get; init; }
    public double RecencyFactor { get; init; }
    public IReadOnlyList<PillarBreakdownDto> PillarBreakdown { get; init; } = [];
    public bool? IsAntiTopic { get; init; }
    public bool? IsAuthorityTopic { get; init; }
    public bool Stale { get; init; }
}
