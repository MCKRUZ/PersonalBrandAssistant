namespace PBA.Application.Common.Interfaces;

// Thin seam over graph.instagram.com (Instagram-Login). The facade defines the canonical metric list and
// passes it in; the client requests those metrics and silently drops any the API rejects as unknown/
// deprecated (maps by returned name), so a missing metric just doesn't appear in the returned bag.
public interface IInstagramGraphClient
{
    // Account insights + follower_count merged into one metric-name -> value map.
    Task<IReadOnlyDictionary<string, long>> GetAccountMetricsAsync(
        string accessToken, IReadOnlyList<string> metrics, CancellationToken ct);

    // Recent media with per-media insights, newest-first, up to max.
    Task<IReadOnlyList<InstagramMediaMetrics>> GetRecentMediaAsync(
        string accessToken, IReadOnlyList<string> mediaMetrics, int max, CancellationToken ct);
}

public record InstagramMediaMetrics(string MediaId, string? Caption, IReadOnlyDictionary<string, long> Metrics);
