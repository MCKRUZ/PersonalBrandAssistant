using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Domain.Events;
using PersonalBrandAssistant.Domain.ValueObjects;

namespace PersonalBrandAssistant.Domain.Entities;

public class Content : AuditableEntityBase
{
    private static readonly Dictionary<ContentStatus, ContentStatus[]> _allowedTransitions = new()
    {
        [ContentStatus.Draft] = [ContentStatus.Review, ContentStatus.Archived],
        [ContentStatus.Review] = [ContentStatus.Draft, ContentStatus.Approved, ContentStatus.Archived],
        [ContentStatus.Approved] = [ContentStatus.Scheduled, ContentStatus.Draft, ContentStatus.Archived],
        [ContentStatus.Scheduled] = [ContentStatus.Publishing, ContentStatus.Approved, ContentStatus.Archived],
        [ContentStatus.Publishing] = [ContentStatus.Published, ContentStatus.Failed, ContentStatus.Scheduled],
        [ContentStatus.Published] = [ContentStatus.Archived],
        [ContentStatus.Failed] = [ContentStatus.Draft, ContentStatus.Archived, ContentStatus.Publishing],
        [ContentStatus.Archived] = [ContentStatus.Draft],
    };

    public static IReadOnlyDictionary<ContentStatus, ContentStatus[]> ValidTransitions => _allowedTransitions;

    public static ContentStatus[] GetAllowedTransitions(ContentStatus status) =>
        _allowedTransitions.TryGetValue(status, out var transitions) ? transitions : [];

    private Content() { }

    public ContentType ContentType { get; private init; }
    public string? Title { get; set; }
    public string Body { get; set; } = string.Empty;
    public ContentStatus Status { get; private set; } = ContentStatus.Draft;
    public ContentMetadata Metadata { get; set; } = new();
    public Guid? ParentContentId { get; set; }
    public PlatformType[] TargetPlatforms { get; set; } = [];
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public AutonomyLevel CapturedAutonomyLevel { get; private init; }
    public int RetryCount { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public DateTimeOffset? PublishingStartedAt { get; set; }
    public int TreeDepth { get; set; }
    public PlatformType? RepurposeSourcePlatform { get; set; }
    public uint Version { get; set; }
    public string? ImageFileId { get; set; }
    public bool ImageRequired { get; set; }
    public string? SubstackPostUrl { get; set; }
    public string? BlogPostUrl { get; private set; }
    public string? BlogDeployCommitSha { get; private set; }
    public TimeSpan? BlogDelayOverride { get; private set; }
    public bool BlogSkipped { get; private set; }
    public BlogPipelineStage CurrentBlogStage { get; private set; } = BlogPipelineStage.Draft;
    public List<BlogStageTransition> BlogStageHistory { get; private set; } = [];

    public void MarkBlogPublished(string url, string commitSha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(commitSha);

        BlogPostUrl = url;
        BlogDeployCommitSha = commitSha;
    }

    public void SkipBlog()
    {
        BlogSkipped = true;
    }

    public void SetBlogDelay(TimeSpan? delay)
    {
        if (delay is not null && (delay.Value < TimeSpan.Zero || delay.Value > TimeSpan.FromDays(365)))
            throw new ArgumentOutOfRangeException(nameof(delay), "Blog delay must be between 0 and 365 days.");

        BlogDelayOverride = delay;
    }

    public void SetBlogStage(BlogPipelineStage stage, string? note = null)
    {
        var from = CurrentBlogStage;
        CurrentBlogStage = stage;
        BlogStageHistory = [..BlogStageHistory, new BlogStageTransition(from, stage, DateTimeOffset.UtcNow, note)];
    }

    public void AdvanceBlogStage(string? note = null)
    {
        if (CurrentBlogStage >= BlogPipelineStage.Social)
            throw new InvalidOperationException("Blog is already at the final pipeline stage.");

        SetBlogStage(CurrentBlogStage + 1, note);
    }

    public static Content Create(
        ContentType type,
        string body,
        string? title = null,
        PlatformType[]? targetPlatforms = null,
        AutonomyLevel capturedAutonomyLevel = AutonomyLevel.Manual)
    {
        return new Content
        {
            ContentType = type,
            Body = body,
            Title = title,
            TargetPlatforms = targetPlatforms ?? [],
            CapturedAutonomyLevel = capturedAutonomyLevel,
        };
    }

    public void TransitionTo(ContentStatus newStatus)
    {
        if (!_allowedTransitions.TryGetValue(Status, out var allowed) ||
            !allowed.Contains(newStatus))
        {
            throw new InvalidOperationException(
                $"Cannot transition from {Status} to {newStatus}.");
        }

        var oldStatus = Status;
        Status = newStatus;
        AddDomainEvent(new ContentStateChangedEvent(Id, oldStatus, newStatus));
    }
}
