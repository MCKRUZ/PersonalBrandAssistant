namespace PBA.Application.Common.Interfaces;

// Thin seam over YouTube Data API v3 (poll path) + Analytics API v2 (live deep path). SDK types stay behind
// this seam so the facade maps plain records and tests feed canned responses without the SDK. The facade
// owns orchestration (uploads-playlist paging, <=50 videos.list batching, N cap); each method here is one call.
public interface IYouTubeApiClient
{
    // channels.list?part=statistics,contentDetails — cumulative stats + the uploads playlist id.
    Task<YouTubeChannelStats> GetChannelAsync(string accessToken, CancellationToken ct);

    // playlistItems.list on the uploads playlist (1 unit, newest-first). NOT search.list (100 units, unreliable).
    Task<YouTubePlaylistPage> GetUploadsPageAsync(string playlistId, string? pageToken, string accessToken, CancellationToken ct);

    // videos.list?part=statistics,snippet for a single batch of <=50 ids (the facade chunks).
    Task<IReadOnlyList<YouTubeVideoStat>> GetVideosBatchAsync(IReadOnlyList<string> ids, string accessToken, CancellationToken ct);

    // Analytics v2 reports.query — live deep path, NOT snapshotted. Returns raw column headers + rows.
    Task<YouTubeReportResult> RunAnalyticsReportAsync(YouTubeReportRequest request, string accessToken, CancellationToken ct);
}

public record YouTubeChannelStats(long Subscribers, long Views, long Videos, string UploadsPlaylistId);

public record YouTubePlaylistPage(IReadOnlyList<string> VideoIds, string? NextPageToken);

public record YouTubeVideoStat(string VideoId, string? Title, long Views, long Likes, long Comments);

public record YouTubeReportRequest(
    string Ids, string Dimensions, IReadOnlyList<string> Metrics, DateOnly StartDate, DateOnly EndDate);

// Columns carry their Analytics v2 ColumnType ("DIMENSION" | "METRIC") so the mapper labels by the actual
// dimension column (day, country, trafficSourceType, ...) rather than hardcoding "day".
public record YouTubeReportResult(
    IReadOnlyList<YouTubeReportColumn> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);

public record YouTubeReportColumn(string Name, string ColumnType);
