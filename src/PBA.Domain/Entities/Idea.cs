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

    public IdeaSource? IdeaSource { get; set; }
    public SavedIdea? SavedDetails { get; set; }
}
