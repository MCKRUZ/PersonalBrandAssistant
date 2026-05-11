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
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? HangfireJobId { get; set; }
    public bool IsDeleted { get; set; }

    public Idea? SourceIdea { get; set; }
    public Content? ParentContent { get; set; }
    public List<Content> Children { get; set; } = [];
    public List<ContentPlatformPublish> CrossPosts { get; set; } = [];
}
