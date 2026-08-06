namespace PBA.Domain.Entities;

using PBA.Domain.Enums;

public class Content
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Title { get; set; }
    public string Body { get; set; } = string.Empty;
    public ContentType ContentType { get; set; }
    public ContentStatus Status { get; set; } = ContentStatus.Idea;
    public Platform PrimaryPlatform { get; set; }
    public decimal? VoiceScore { get; set; }
    public decimal? ViralityPrediction { get; set; }
    public Guid? SourceIdeaId { get; set; }
    public Guid? ParentContentId { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<Platform> TargetPlatforms { get; set; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? HangfireJobId { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Public URL of the clip staged on R2, held from the moment the content is accepted until it
    /// is published. Only set when PBA itself has to hold a post until its slot — i.e. the target
    /// platform cannot schedule (Instagram) — because the video bytes arrive with the request and
    /// would otherwise be gone by the time the slot came round.
    ///
    /// Not set for platforms that can hold the post themselves (TikTok via Buffer), which take the
    /// clip immediately and need nothing kept here.
    /// </summary>
    public string? StagedMediaUrl { get; set; }

    /// <summary>Storage key behind <see cref="StagedMediaUrl"/>, kept so the object can be removed
    /// once published rather than left for the bucket's lifecycle rule.</summary>
    public string? StagedMediaKey { get; set; }

    public Idea? SourceIdea { get; set; }
    public Content? ParentContent { get; set; }
    public List<Content> Children { get; set; } = [];
    public List<ContentPlatformPublish> CrossPosts { get; set; } = [];
}
