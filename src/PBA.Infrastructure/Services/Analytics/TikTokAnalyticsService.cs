using Microsoft.Extensions.Logging;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Services.Analytics;

// TikTok analytics facade over ITikTokDisplayClient. /v2/video/list/ caps at 20/page, so the facade loops on
// cursor/has_more to accumulate up to N videos. Ceiling: no reach/demographics/retention on TikTok — every
// TikTok trend is a snapshot delta only.
public sealed class TikTokAnalyticsService(
    ITikTokDisplayClient client,
    ITokenEncryptor encryptor,
    ILogger<TikTokAnalyticsService> logger) : IChannelAnalyticsService
{
    // Defensive cap: a misbehaving API returning has_more=true forever must not loop unbounded on the daily
    // poller. 50 pages x 20 items = 1,000 candidate videos, far beyond any N.
    private const int MaxPages = 50;

    public Platform Platform => Platform.TikTok;

    public async Task<Result<ChannelPollResult>> PollAsync(
        PlatformCredential credential, int recentVideoCount, CancellationToken ct)
    {
        try
        {
            var accessToken = encryptor.Decrypt(credential.EncryptedAccessToken);

            var stats = await client.GetUserStatsAsync(accessToken, ct);
            var account = new AccountMetrics(stats, Provisional: false);

            var videos = await AccumulateVideosAsync(accessToken, recentVideoCount, ct);

            return Result<ChannelPollResult>.Success(new ChannelPollResult(account, videos));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "TikTok poll failed for credential {CredentialId}", credential.Id);
            return Result<ChannelPollResult>.Fail($"TikTok poll failed: {ex.Message}");
        }
    }

    // Loop on cursor/has_more (20/page) until N videos are collected or there are no more pages.
    private async Task<IReadOnlyList<VideoMetrics>> AccumulateVideosAsync(
        string accessToken, int count, CancellationToken ct)
    {
        var videos = new List<VideoMetrics>();
        string? cursor = null;
        bool hasMore;
        var pages = 0;
        do
        {
            var page = await client.GetVideoPageAsync(accessToken, cursor, ct);
            foreach (var v in page.Videos)
            {
                videos.Add(new VideoMetrics(v.Id, v.Title, new Dictionary<string, long>
                {
                    ["views"] = v.Views,
                    ["likes"] = v.Likes,
                    ["comments"] = v.Comments,
                    ["shares"] = v.Shares
                }, Provisional: false));
            }
            cursor = page.NextCursor;
            hasMore = page.HasMore;
            pages++;
        }
        while (videos.Count < count && hasMore && cursor is not null && pages < MaxPages);

        return videos.Take(count).ToList();
    }
}
