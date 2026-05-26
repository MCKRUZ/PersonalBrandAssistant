namespace PBA.Domain.Entities;

using PBA.Domain.Enums;

public class FeedItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public FeedItemType Type { get; set; }
    public required string Title { get; set; }
    public required string Summary { get; set; }
    public string? Data { get; set; }
    public string? ActionType { get; set; }
    public Guid? ActionTargetId { get; set; }
    public FeedItemPriority Priority { get; set; }
    public bool IsRead { get; set; }
    public bool IsActedOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}
