using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Util;
using Google.Apis.YouTube.v3;
using PBA.Application.Common.Interfaces;
using YouTubeAnalyticsSdk = Google.Apis.YouTubeAnalytics.v2;

namespace PBA.Infrastructure.Services.Analytics;

// Thin seam over the YouTube Data v3 + Analytics v2 SDKs, authorized per-call with the user's OAuth access
// token. SDK types never escape this class — every method returns the plain records the facade maps. Untested
// by design (the facade mocks IYouTubeApiClient); needs a real-credential smoke test before prod.
public sealed class YouTubeApiClient : IYouTubeApiClient
{
    private const string AppName = "PersonalBrandAssistant";

    public async Task<YouTubeChannelStats> GetChannelAsync(string accessToken, CancellationToken ct)
    {
        using var service = BuildDataService(accessToken);
        var request = service.Channels.List("statistics,contentDetails");
        request.Mine = true;

        var response = await request.ExecuteAsync(ct);
        var channel = response.Items?.FirstOrDefault();
        if (channel is null)
            return new YouTubeChannelStats(0, 0, 0, string.Empty);

        return new YouTubeChannelStats(
            Subscribers: (long)(channel.Statistics?.SubscriberCount ?? 0),
            Views: (long)(channel.Statistics?.ViewCount ?? 0),
            Videos: (long)(channel.Statistics?.VideoCount ?? 0),
            UploadsPlaylistId: channel.ContentDetails?.RelatedPlaylists?.Uploads ?? string.Empty);
    }

    public async Task<YouTubePlaylistPage> GetUploadsPageAsync(
        string playlistId, string? pageToken, string accessToken, CancellationToken ct)
    {
        using var service = BuildDataService(accessToken);
        var request = service.PlaylistItems.List("contentDetails");
        request.PlaylistId = playlistId;
        request.MaxResults = 50;
        request.PageToken = pageToken;

        var response = await request.ExecuteAsync(ct);
        var ids = (response.Items ?? [])
            .Select(i => i.ContentDetails?.VideoId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToList();

        return new YouTubePlaylistPage(ids, response.NextPageToken);
    }

    public async Task<IReadOnlyList<YouTubeVideoStat>> GetVideosBatchAsync(
        IReadOnlyList<string> ids, string accessToken, CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];

        using var service = BuildDataService(accessToken);
        var request = service.Videos.List("statistics,snippet");
        request.Id = new Repeatable<string>(ids);
        request.MaxResults = 50;

        var response = await request.ExecuteAsync(ct);
        return (response.Items ?? [])
            .Select(v => new YouTubeVideoStat(
                VideoId: v.Id,
                Title: v.Snippet?.Title,
                Views: (long)(v.Statistics?.ViewCount ?? 0),
                Likes: (long)(v.Statistics?.LikeCount ?? 0),
                Comments: (long)(v.Statistics?.CommentCount ?? 0)))
            .ToList();
    }

    public async Task<YouTubeReportResult> RunAnalyticsReportAsync(
        YouTubeReportRequest request, string accessToken, CancellationToken ct)
    {
        using var service = BuildAnalyticsService(accessToken);
        var query = service.Reports.Query();
        query.Ids = request.Ids;
        query.Dimensions = request.Dimensions;
        query.Metrics = string.Join(",", request.Metrics);
        query.StartDate = request.StartDate.ToString("yyyy-MM-dd");
        query.EndDate = request.EndDate.ToString("yyyy-MM-dd");

        var response = await query.ExecuteAsync(ct);

        var columns = (response.ColumnHeaders ?? [])
            .Select(h => new YouTubeReportColumn(h.Name ?? string.Empty, h.ColumnType ?? string.Empty))
            .ToList();
        var rows = (response.Rows ?? [])
            .Select(row => (IReadOnlyList<string>)row
                .Select(cell => cell?.ToString() ?? string.Empty)
                .ToList())
            .ToList();

        return new YouTubeReportResult(columns, rows);
    }

    private static YouTubeService BuildDataService(string accessToken) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = GoogleCredential.FromAccessToken(accessToken),
            ApplicationName = AppName
        });

    private static YouTubeAnalyticsSdk.YouTubeAnalyticsService BuildAnalyticsService(string accessToken) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = GoogleCredential.FromAccessToken(accessToken),
            ApplicationName = AppName
        });
}
