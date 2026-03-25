using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

internal sealed class DashboardAggregator(
    IApplicationDbContext db,
    IGoogleAnalyticsService ga,
    ILogger<DashboardAggregator> logger) : IDashboardAggregator
{
    // Platforms that do not have full engagement data available
    private static readonly PlatformType[] UnavailablePlatforms = [PlatformType.LinkedIn];

    public async Task<Result<DashboardSummary>> GetSummaryAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        try
        {
            var periodLength = to.Date - from.Date + TimeSpan.FromDays(1);
            var previousTo = from.AddDays(-1);
            var previousFrom = previousTo - periodLength + TimeSpan.FromDays(1);

            // Current period engagement
            var (currentEngagement, currentSocialImpressions) = await GetEngagementForPeriodAsync(from, to, ct);

            // Previous period engagement
            var (previousEngagement, previousSocialImpressions) = await GetEngagementForPeriodAsync(previousFrom, previousTo, ct);

            // Content published counts
            var currentContentCount = await db.Contents
                .Where(c => c.Status == ContentStatus.Published
                    && c.PublishedAt >= from && c.PublishedAt <= to)
                .CountAsync(ct);

            var previousContentCount = await db.Contents
                .Where(c => c.Status == ContentStatus.Published
                    && c.PublishedAt >= previousFrom && c.PublishedAt <= previousTo)
                .CountAsync(ct);

            // GA4 data (partial failure tolerant)
            var (currentGaUsers, currentGaPageViews) = await GetGaMetricsAsync(from, to, ct);
            var (previousGaUsers, previousGaPageViews) = await GetGaMetricsAsync(previousFrom, previousTo, ct);

            var currentTotalImpressions = currentSocialImpressions + currentGaPageViews;
            var previousTotalImpressions = previousSocialImpressions + previousGaPageViews;

            var currentEngagementRate = currentTotalImpressions > 0
                ? (decimal)currentEngagement / currentTotalImpressions * 100
                : 0m;
            var previousEngagementRate = previousTotalImpressions > 0
                ? (decimal)previousEngagement / previousTotalImpressions * 100
                : 0m;

            // Cost per engagement
            var currentCost = await GetCostForPeriodAsync(from, to, ct);
            var previousCost = await GetCostForPeriodAsync(previousFrom, previousTo, ct);
            var currentCpe = currentEngagement > 0 ? currentCost / currentEngagement : 0m;
            var previousCpe = previousEngagement > 0 ? previousCost / previousEngagement : 0m;

            return Result.Success(new DashboardSummary(
                TotalEngagement: currentEngagement,
                PreviousEngagement: previousEngagement,
                TotalImpressions: currentTotalImpressions,
                PreviousImpressions: previousTotalImpressions,
                EngagementRate: currentEngagementRate,
                PreviousEngagementRate: previousEngagementRate,
                ContentPublished: currentContentCount,
                PreviousContentPublished: previousContentCount,
                CostPerEngagement: currentCpe,
                PreviousCostPerEngagement: previousCpe,
                WebsiteUsers: currentGaUsers,
                PreviousWebsiteUsers: previousGaUsers,
                GeneratedAt: DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate dashboard summary");
            return Result.Failure<DashboardSummary>(ErrorCode.InternalError, "Failed to generate dashboard summary");
        }
    }

    public async Task<Result<IReadOnlyList<DailyEngagement>>> GetTimelineAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        try
        {
            var fromDate = DateOnly.FromDateTime(from.UtcDateTime);
            var toDate = DateOnly.FromDateTime(to.UtcDateTime);

            // Get all snapshots in range with their platform info
            var snapshots = await db.EngagementSnapshots
                .Where(s => s.FetchedAt >= from && s.FetchedAt <= to)
                .Join(db.ContentPlatformStatuses,
                    s => s.ContentPlatformStatusId,
                    cps => cps.Id,
                    (s, cps) => new { s.Likes, s.Comments, s.Shares, cps.Platform, s.FetchedAt })
                .ToListAsync(ct);

            // Group by date and platform
            var grouped = snapshots
                .GroupBy(s => new { Date = DateOnly.FromDateTime(s.FetchedAt.UtcDateTime), s.Platform })
                .ToDictionary(
                    g => g.Key,
                    g => new { Likes = g.Sum(x => x.Likes), Comments = g.Sum(x => x.Comments), Shares = g.Sum(x => x.Shares) });

            // Determine which platforms have any data in the range
            var activePlatforms = snapshots.Select(s => s.Platform).Distinct().ToList();

            // Build gap-filled timeline
            var timeline = new List<DailyEngagement>();
            for (var date = fromDate; date <= toDate; date = date.AddDays(1))
            {
                var platforms = activePlatforms.Select(platform =>
                {
                    var key = new { Date = date, Platform = platform };
                    if (grouped.TryGetValue(key, out var data))
                    {
                        return new PlatformDailyMetrics(platform, data.Likes, data.Comments, data.Shares,
                            data.Likes + data.Comments + data.Shares);
                    }
                    return new PlatformDailyMetrics(platform, 0, 0, 0, 0);
                }).ToList();

                var total = platforms.Sum(p => p.Total);
                timeline.Add(new DailyEngagement(date, platforms, total));
            }

            return Result.Success<IReadOnlyList<DailyEngagement>>(timeline);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate timeline");
            return Result.Failure<IReadOnlyList<DailyEngagement>>(ErrorCode.InternalError, "Failed to generate timeline");
        }
    }

    public async Task<Result<IReadOnlyList<PlatformSummary>>> GetPlatformSummariesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        try
        {
            // Get all published statuses in range
            var statuses = await db.ContentPlatformStatuses
                .Where(cps => cps.Status == PlatformPublishStatus.Published
                    && cps.PublishedAt >= from && cps.PublishedAt <= to)
                .ToListAsync(ct);

            // Get all engagement snapshots for these statuses
            var statusIds = statuses.Select(s => s.Id).ToHashSet();
            var snapshots = await db.EngagementSnapshots
                .Where(s => statusIds.Contains(s.ContentPlatformStatusId))
                .ToListAsync(ct);

            // Get content titles for top post identification
            var contentIds = statuses.Select(s => s.ContentId).Distinct().ToHashSet();
            var contents = await db.Contents
                .Where(c => contentIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Title, ct);

            // Group statuses by platform
            var platformGroups = statuses.GroupBy(s => s.Platform);

            var summaries = new List<PlatformSummary>();
            foreach (var group in platformGroups)
            {
                var platform = group.Key;
                var postCount = group.Count();

                // Calculate engagement per status (latest snapshot per status)
                var engagementByStatus = group.Select(cps =>
                {
                    var latestSnap = snapshots
                        .Where(s => s.ContentPlatformStatusId == cps.Id)
                        .OrderByDescending(s => s.FetchedAt)
                        .FirstOrDefault();
                    var engagement = latestSnap is not null
                        ? latestSnap.Likes + latestSnap.Comments + latestSnap.Shares
                        : 0;
                    return new { cps.ContentId, cps.PostUrl, Engagement = engagement };
                }).ToList();

                var totalEngagement = engagementByStatus.Sum(e => e.Engagement);
                var avgEngagement = postCount > 0 ? (double)totalEngagement / postCount : 0;

                var topPost = engagementByStatus.OrderByDescending(e => e.Engagement).First();
                contents.TryGetValue(topPost.ContentId, out var topPostTitle);

                summaries.Add(new PlatformSummary(
                    Platform: platform,
                    FollowerCount: null, // Follower data comes from platform adapters at API layer
                    PostCount: postCount,
                    AvgEngagement: avgEngagement,
                    TopPostTitle: topPostTitle,
                    TopPostUrl: topPost.PostUrl,
                    IsAvailable: !UnavailablePlatforms.Contains(platform)));
            }

            // Add unavailable platforms that have no data
            foreach (var platform in UnavailablePlatforms)
            {
                if (summaries.All(s => s.Platform != platform))
                {
                    summaries.Add(new PlatformSummary(platform, null, 0, 0, null, null, false));
                }
            }

            return Result.Success<IReadOnlyList<PlatformSummary>>(summaries);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate platform summaries");
            return Result.Failure<IReadOnlyList<PlatformSummary>>(ErrorCode.InternalError, "Failed to generate platform summaries");
        }
    }

    private async Task<(int engagement, int impressions)> GetEngagementForPeriodAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var snapshots = await db.EngagementSnapshots
            .Where(s => s.FetchedAt >= from && s.FetchedAt <= to)
            .ToListAsync(ct);

        // Group by ContentPlatformStatusId, take latest per status
        var latestSnapshots = snapshots
            .GroupBy(s => s.ContentPlatformStatusId)
            .Select(g => g.OrderByDescending(s => s.FetchedAt).First())
            .ToList();

        var totalEngagement = latestSnapshots.Sum(s => s.Likes + s.Comments + s.Shares);
        var totalImpressions = latestSnapshots.Sum(s => s.Impressions ?? 0);

        return (totalEngagement, totalImpressions);
    }

    private async Task<(int users, int pageViews)> GetGaMetricsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        try
        {
            var gaResult = await ga.GetOverviewAsync(from, to, ct);
            if (gaResult.IsSuccess)
                return (gaResult.Value!.ActiveUsers, gaResult.Value.PageViews);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GA4 call failed, continuing with zeros");
        }

        return (0, 0);
    }

    private async Task<decimal> GetCostForPeriodAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var contentIds = await db.Contents
            .Where(c => c.Status == ContentStatus.Published
                && c.PublishedAt >= from && c.PublishedAt <= to)
            .Select(c => c.Id)
            .ToListAsync(ct);

        if (contentIds.Count == 0) return 0m;

        return await db.AgentExecutions
            .Where(e => e.ContentId != null
                && contentIds.Contains(e.ContentId.Value)
                && e.Status == AgentExecutionStatus.Completed)
            .SumAsync(e => e.Cost, ct);
    }
}
