namespace PBA.Domain.Entities;

using PBA.Domain.Enums;

// A daily cumulative capture of channel metrics. Stores only cumulative, as-of-capture integer counts;
// all trends (growth, gained/lost) are derived at read time (section-06) as deltas between snapshots.
public class ChannelMetricSnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Platform Platform { get; init; }

    // Host-LOCAL capture date (same clock as ChannelAnalytics:RunAtLocalTime in the poller).
    public DateOnly SnapshotDate { get; init; }

    public SnapshotScope Scope { get; init; }

    // NON-NULLABLE. Sentinel "" for Account scope so the (Platform, SnapshotDate, Scope, VideoId) unique
    // index + the poller's ON CONFLICT upsert work (PostgreSQL treats NULLs as distinct).
    public string VideoId { get; init; } = string.Empty;

    // Denormalized for display (Video scope only).
    public string? VideoTitle { get; init; }

    // metric name -> cumulative integer value (or whole seconds); stored as jsonb. No fractions ever.
    public IReadOnlyDictionary<string, long> Metrics { get; init; }
        = new Dictionary<string, long>();

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
}
