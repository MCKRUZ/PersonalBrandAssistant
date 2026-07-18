using Microsoft.Extensions.Logging;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Services.Analytics;

// YouTube analytics facade over IYouTubeApiClient. Owns orchestration: cumulative channel stats, recent-video
// discovery via the uploads playlist (never search), <=50-id videos.list batching, and the N cap.
public sealed class YouTubeAnalyticsService(
    IYouTubeApiClient client,
    ITokenEncryptor encryptor,
    ILogger<YouTubeAnalyticsService> logger) : IChannelAnalyticsService
{
    private const int MaxVideosPerBatch = 50;
    // Defensive cap: a misbehaving API returning empty pages with a non-null nextPageToken must not loop
    // unbounded on the daily poller. 50 pages x 50 items = 2,500 candidate videos, far beyond any N.
    private const int MaxUploadsPages = 50;

    public Platform Platform => Platform.YouTube;

    public async Task<Result<ChannelPollResult>> PollAsync(
        PlatformCredential credential, int recentVideoCount, CancellationToken ct)
    {
        try
        {
            var accessToken = encryptor.Decrypt(credential.EncryptedAccessToken);

            var channel = await client.GetChannelAsync(accessToken, ct);
            var account = new AccountMetrics(new Dictionary<string, long>
            {
                ["subscribers"] = channel.Subscribers,
                ["views"] = channel.Views,
                ["videos"] = channel.Videos
            }, Provisional: false);

            var recentIds = await DiscoverRecentVideoIdsAsync(channel.UploadsPlaylistId, recentVideoCount, accessToken, ct);

            var videos = new List<VideoMetrics>();
            foreach (var chunk in recentIds.Chunk(MaxVideosPerBatch))
            {
                var batch = await client.GetVideosBatchAsync(chunk, accessToken, ct);
                foreach (var v in batch)
                {
                    videos.Add(new VideoMetrics(v.VideoId, v.Title, new Dictionary<string, long>
                    {
                        ["views"] = v.Views,
                        ["likes"] = v.Likes,
                        ["comments"] = v.Comments
                    }, Provisional: false));
                }
            }

            return Result<ChannelPollResult>.Success(new ChannelPollResult(account, videos));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "YouTube poll failed for credential {CredentialId}", credential.Id);
            return Result<ChannelPollResult>.Fail($"YouTube poll failed: {ex.Message}");
        }
    }

    // Page the uploads playlist newest-first until we have N ids (or run out of pages), then take N.
    private async Task<IReadOnlyList<string>> DiscoverRecentVideoIdsAsync(
        string uploadsPlaylistId, int count, string accessToken, CancellationToken ct)
    {
        var ids = new List<string>();
        string? pageToken = null;
        var pages = 0;
        do
        {
            var page = await client.GetUploadsPageAsync(uploadsPlaylistId, pageToken, accessToken, ct);
            ids.AddRange(page.VideoIds);
            pageToken = page.NextPageToken;
            pages++;
        }
        while (ids.Count < count && pageToken is not null && pages < MaxUploadsPages);

        return ids.Take(count).ToList();
    }
}
