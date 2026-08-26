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
    /// Public URL of the clip on R2. Set at HAND-OVER, not when the content is accepted: while PBA
    /// is waiting, the video lives beside the record in <see cref="HeldMedia"/>, and this is the
    /// public copy made from it at the moment a platform needs a link to fetch.
    ///
    /// That ordering is the point. Public objects are reaped on a short lifecycle rule, so creating
    /// one at request time made the reachable campaign length a storage setting rather than a fact
    /// about the platform. Created at hand-over, the rule only has to outlast the platform reading
    /// it, which is what it was sized for.
    ///
    /// Null for a post that goes out immediately — the bytes travel with that request.
    /// </summary>
    public string? StagedMediaUrl { get; set; }

    /// <summary>
    /// Storage key behind <see cref="StagedMediaUrl"/>. Kept after publishing for a platform that
    /// fetches the video when the post fires (TikTok via Buffer) — deleting it then would hand
    /// Buffer a dead link on the day — and cleared once a platform that takes the bytes at
    /// hand-over (Instagram, YouTube) is finished with it.
    /// </summary>
    public string? StagedMediaKey { get; set; }

    /// <summary>
    /// When PBA hands the clip to the platform, which is NOT always when the post goes live.
    ///
    /// Four shapes exist. TikTok via Buffer with a near slot: handed over at once, Buffer holds it —
    /// this stays null. TikTok via Buffer with a distant slot: Buffer refuses anything more than a
    /// week ahead, so PBA keeps the clip and this lands just inside that week. Instagram: Meta
    /// cannot schedule, so handover IS the go-live moment and this equals <see cref="ScheduledAt"/>.
    /// YouTube: the upload is what costs a scarce daily quota, while YouTube itself releases the
    /// video at <see cref="ScheduledAt"/> — so PBA uploads as early as the budget allows and this
    /// lands days BEFORE the post appears.
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
