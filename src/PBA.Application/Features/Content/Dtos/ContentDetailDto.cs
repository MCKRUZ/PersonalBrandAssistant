using PBA.Domain.Enums;

namespace PBA.Application.Features.Content.Dtos;

public record ContentDetailDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public ContentType ContentType { get; init; }
    public ContentStatus Status { get; init; }
    public Platform PrimaryPlatform { get; init; }
    public decimal? VoiceScore { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? ScheduledAt { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public string Body { get; init; } = string.Empty;
    public decimal? ViralityPrediction { get; init; }
    public Guid? SourceIdeaId { get; init; }
    public Guid? ParentContentId { get; init; }
    public IReadOnlyList<PlatformPublishDto> PlatformPublishes { get; init; } = [];
    public IReadOnlyList<ChildContentDto> Children { get; init; } = [];
}
