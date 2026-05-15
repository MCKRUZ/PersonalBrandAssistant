using PBA.Domain.Enums;

namespace PBA.Application.Features.Feed.Dtos;

public record FeedItemDto
{
    public Guid Id { get; init; }
    public FeedItemType Type { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string? Data { get; init; }
    public string? ActionType { get; init; }
    public Guid? ActionTargetId { get; init; }
    public FeedItemPriority Priority { get; init; }
    public bool IsRead { get; init; }
    public bool IsActedOn { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}
