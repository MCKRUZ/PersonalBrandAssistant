namespace PersonalBrandAssistant.Application.Common.Models;

public sealed record QueueStatusResponse(
    int QueueDepth,
    ScheduledPostInfo? NextScheduledPost,
    int PostsLast24h,
    IReadOnlyDictionary<string, int> ItemsByStage);

public sealed record ScheduledPostInfo(
    Guid ContentId,
    string Platform,
    DateTimeOffset ScheduledAt);

public sealed record PipelineHealthResponse(
    IReadOnlyList<StuckItemInfo> StuckItems,
    int FailedGenerations24h,
    double ErrorRate,
    int ActiveCount);

public sealed record StuckItemInfo(
    Guid ContentId,
    string Stage,
    DateTimeOffset StuckSince,
    double HoursStuck);

public sealed record EngagementSummaryResponse(
    int Rolling7DayEngagement,
    double AverageEngagement,
    IReadOnlyList<EngagementAnomaly> Anomalies,
    IReadOnlyDictionary<string, int> PlatformBreakdown);

public sealed record EngagementAnomaly(
    Guid ContentId,
    string Platform,
    string Metric,
    int Value,
    double Average,
    double Multiplier,
    string Direction,
    double Confidence);

public sealed record BriefingSummaryResponse(
    IReadOnlyList<ScheduledContentInfo> ScheduledToday,
    IReadOnlyList<EngagementHighlight> EngagementHighlights,
    IReadOnlyList<TrendingTopicInfo> TrendingTopics,
    int QueueDepth,
    ScheduledPostInfo? NextPublish,
    int PendingApprovals);

public sealed record ScheduledContentInfo(
    Guid ContentId,
    string Platform,
    DateTimeOffset Time,
    string? Title);

public sealed record EngagementHighlight(
    Guid ContentId,
    string Platform,
    string Metric,
    int Value);

public sealed record TrendingTopicInfo(
    string Topic,
    float RelevanceScore,
    string Source);
