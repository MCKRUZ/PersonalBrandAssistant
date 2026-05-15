namespace PBA.Application.Features.Feed.Dtos;

public record FeedSummaryDto
{
    public int UnreadCount { get; init; }
    public int PendingApprovals { get; init; }
    public int TrendingCount { get; init; }
    public double EngagementDelta { get; init; }
}
