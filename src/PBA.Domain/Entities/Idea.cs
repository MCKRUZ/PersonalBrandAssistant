namespace PBA.Domain.Entities;

using PBA.Domain.Enums;

public class Idea
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public required string SourceName { get; set; }
    public Guid? IdeaSourceId { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Category { get; set; }
    public string? Summary { get; set; }
    public string? AIConnections { get; set; }
    public IdeaStatus Status { get; set; }
    public List<string> Tags { get; set; } = [];
    public DateTimeOffset DetectedAt { get; set; }
    public required string DeduplicationKey { get; set; }
    public int? Score { get; set; }
    public string? ScoreReason { get; set; }
    public DateTimeOffset? ScoredAt { get; set; }
    public Guid? DuplicateOfId { get; set; }
    public DateTimeOffset? ClusteredAt { get; set; }
    public DateTimeOffset? AlertedAt { get; set; }

    // Brand-anchored ranking (feed-ranking redesign) -----------------------------------------
    public float[]? Embedding { get; set; }                          // vector(1536), null until embedded
    public DateTimeOffset? EmbeddedAt { get; set; }
    public IList<PillarSubScore> PillarSubScores { get; set; } = []; // jsonb, raw 0..1 per pillar (R-C3)
    public bool? IsAntiTopic { get; set; }
    public bool? IsAuthorityTopic { get; set; }
    public int? ScoredProfileVersion { get; set; }                   // BrandRankingProfile.Version used (R-H3)
    public int ScoreAttempts { get; set; }                           // sweep caps to avoid poison-item burn (R-M5)
    // Score (existing int? 0-10) is retained as a derived display value = round(brandFit*10).

    public IdeaSource? IdeaSource { get; set; }
    public SavedIdea? SavedDetails { get; set; }
}
