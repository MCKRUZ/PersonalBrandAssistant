namespace PBA.Application.Common.Interfaces;

// The result of one poll: the current cumulative account snapshot plus recent-video snapshots. Every value
// in a Metrics bag is an integer count (or whole seconds) — ratios/rates are computed at read time, never here.
public record ChannelPollResult(AccountMetrics Account, IReadOnlyList<VideoMetrics> RecentVideos);

// Provisional is always false for these platforms (cumulative counts are final at capture); it exists for
// the poller's sentinel logic and is not persisted.
public record AccountMetrics(IReadOnlyDictionary<string, long> Metrics, bool Provisional);

public record VideoMetrics(string VideoId, string? Title, IReadOnlyDictionary<string, long> Metrics, bool Provisional);
