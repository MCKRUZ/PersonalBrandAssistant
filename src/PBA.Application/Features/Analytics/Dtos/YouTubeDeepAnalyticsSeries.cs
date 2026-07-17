namespace PBA.Application.Features.Analytics.Dtos;

// One labeled time series per Analytics v2 metric column. Deep-path values may be fractional (e.g.
// averageViewDuration), so this is a double — the integer-only invariant applies only to snapshot bags.
public record YouTubeMetricSeries(string Metric, IReadOnlyList<YouTubeMetricPoint> Points);

public record YouTubeMetricPoint(string Day, double Value);
