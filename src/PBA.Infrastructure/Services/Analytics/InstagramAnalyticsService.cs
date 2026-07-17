using Microsoft.Extensions.Logging;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Services.Analytics;

// Instagram analytics facade over IInstagramGraphClient. The requested metric lists are data-driven constants;
// the client silently drops any metric the API rejects as deprecated, so a dropped metric just doesn't appear
// in the bag (the poll still succeeds). `impressions` is intentionally absent — `views` replaces it.
public sealed class InstagramAnalyticsService(
    IInstagramGraphClient client,
    ITokenEncryptor encryptor,
    ILogger<InstagramAnalyticsService> logger) : IChannelAnalyticsService
{
    // "followers" is added by the client from follower_count; the rest are insight metric names.
    private static readonly string[] AccountInsightMetrics =
    [
        "reach", "views", "accounts_engaged", "total_interactions",
        "likes", "comments", "saves", "shares", "profile_links_taps"
    ];

    private static readonly string[] MediaMetrics =
        ["reach", "views", "likes", "comments", "saves", "shares"];

    public Platform Platform => Platform.Instagram;

    public async Task<Result<ChannelPollResult>> PollAsync(
        PlatformCredential credential, int recentVideoCount, CancellationToken ct)
    {
        try
        {
            var accessToken = encryptor.Decrypt(credential.EncryptedAccessToken);

            var accountMetrics = await client.GetAccountMetricsAsync(accessToken, AccountInsightMetrics, ct);
            var account = new AccountMetrics(accountMetrics, Provisional: false);

            var media = await client.GetRecentMediaAsync(accessToken, MediaMetrics, recentVideoCount, ct);
            var videos = media
                .Take(recentVideoCount)
                .Select(m => new VideoMetrics(m.MediaId, m.Caption, m.Metrics, Provisional: false))
                .ToList();

            return Result<ChannelPollResult>.Success(new ChannelPollResult(account, videos));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Instagram poll failed for credential {CredentialId}", credential.Id);
            return Result<ChannelPollResult>.Fail($"Instagram poll failed: {ex.Message}");
        }
    }
}
