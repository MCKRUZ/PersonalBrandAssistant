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

    /// <summary>
    /// When PBA hands the clip to the platform, which is NOT always when the post goes live.
    ///
    /// Three shapes exist. TikTok via Buffer: handed over at once, Buffer holds it — this stays null.
    /// Instagram: Meta cannot schedule, so handover IS the go-live moment and this equals
    /// <see cref="ScheduledAt"/>. YouTube: the upload is what costs a scarce daily quota, while
    /// YouTube itself releases the video at <see cref="ScheduledAt"/> — so PBA uploads as early as
    /// the budget allows and this lands days BEFORE the post appears.
    ///
    /// Recorded rather than inferred from the Hangfire job because the pacing decision needs to
    /// count what is already booked onto a given day, and reading that back out of a job store is
    /// both awkward and a second source of truth.
    /// </summary>
    public DateTimeOffset? HandoverAt { get; set; }

    /// <summary>
    /// Which frame of the video becomes the post's cover, in milliseconds from the start. Set when
    /// the caller chose one deliberately — the frames that read well are per-clip and a formula
    /// picks them badly, so a supplied value always wins over the connector's own guess.
    ///
    /// Persisted rather than kept in the request because a post PBA holds until its slot is
    /// published days later, by which time nothing but this record remembers the choice.
    /// </summary>
    public int? CoverFrameOffsetMs { get; set; }

    public Idea? SourceIdea { get; set; }
    public Content? ParentContent { get; set; }
    public List<Content> Children { get; set; } = [];
    public List<ContentPlatformPublish> CrossPosts { get; set; } = [];
}
