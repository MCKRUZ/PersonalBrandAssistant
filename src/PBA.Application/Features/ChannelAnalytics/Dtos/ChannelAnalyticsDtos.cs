using PBA.Application.Features.Analytics.Dtos;
using PBA.Domain.Enums;

namespace PBA.Application.Features.ChannelAnalytics.Dtos;

public enum ConnectionStatus
{
    NotConnected = 0,
    Connected = 1,
    ReconnectRequired = 2
}

// A single day's DELTA value (derived from cumulative snapshots), or a cumulative point for sparklines.
public record MetricPoint(DateOnly Date, long Value);

public record TrendSeries(string Metric, IReadOnlyList<MetricPoint> Points);

// Value = latest cumulative; DeltaPct = % change vs the prior in-range snapshot (null when no prior);
// Rate = engagement-rate fraction (null except on the engagement card).
public record KpiCard(string Key, string Label, long Value, double? DeltaPct, double? Rate);

public record RecentPost(string VideoId, string? Title, IReadOnlyDictionary<string, long> Metrics);

public record ChannelAnalyticsDto(
    Platform Platform,
    ConnectionStatus Status,
    DateOnly? AsOf,
    IReadOnlyList<KpiCard> Kpis,
    IReadOnlyList<TrendSeries> Trends,
    IReadOnlyList<RecentPost> RecentPosts);

public record OverviewChannel(
    Platform Platform,
    ConnectionStatus Status,
    long? Followers,
    IReadOnlyList<MetricPoint> FollowerSparkline);

public record OverviewDto(
    long TotalAudience,   // sum of followers/subscribers across connected channels (APPROXIMATE — YouTube rounds)
    IReadOnlyList<KpiCard> CombinedKpis,
    IReadOnlyList<OverviewChannel> Channels);

// Live YouTube Analytics v2 deep path — flat labeled series (reuses section-04's YouTubeMetricSeries) so the
// frontend charts them directly. Not snapshotted.
public record YouTubeDeepAnalyticsDto(
    IReadOnlyList<YouTubeMetricSeries> DaySeries,
    IReadOnlyList<YouTubeMetricSeries> TrafficSources,
    IReadOnlyList<YouTubeMetricSeries> Geography,
    IReadOnlyList<YouTubeMetricSeries> Demographics);
