using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.IntegrationServices;

public sealed class IntegrationMonitorService : IIntegrationMonitorService
{
    private static readonly ContentStatus[] TerminalStatuses =
        [ContentStatus.Published, ContentStatus.Archived];

    private readonly IApplicationDbContext _dbContext;
    private readonly IDateTimeProvider _dateTime;
    private readonly ILogger<IntegrationMonitorService> _logger;

    public IntegrationMonitorService(
        IApplicationDbContext dbContext,
        IDateTimeProvider dateTime,
        ILogger<IntegrationMonitorService> logger)
    {
        _dbContext = dbContext;
        _dateTime = dateTime;
        _logger = logger;
    }

    public async Task<Result<QueueStatusResponse>> GetQueueStatusAsync(CancellationToken ct)
    {
        var now = _dateTime.UtcNow;
        var last24h = now.AddHours(-24);

        var stageCounts = await _dbContext.Contents
            .AsNoTracking()
            .Where(c => !TerminalStatuses.Contains(c.Status))
            .GroupBy(c => c.Status)
            .Select(g => new { Stage = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var itemsByStage = stageCounts.ToDictionary(
            g => g.Stage.ToString(), g => g.Count) as IReadOnlyDictionary<string, int>;
        var queueDepth = stageCounts.Sum(g => g.Count);

        var nextScheduled = await NextScheduledPostQuery(now).FirstOrDefaultAsync(ct);

        var postsLast24h = await _dbContext.Contents
            .AsNoTracking()
            .CountAsync(c => c.PublishedAt != null && c.PublishedAt >= last24h, ct);

        return Result<QueueStatusResponse>.Success(new QueueStatusResponse(
            queueDepth, nextScheduled, postsLast24h, itemsByStage));
    }

    public async Task<Result<PipelineHealthResponse>> GetPipelineHealthAsync(CancellationToken ct)
    {
        var now = _dateTime.UtcNow;
        var stuckThreshold = now.AddHours(-2);
        var last24h = now.AddHours(-24);

        var stuckItems = await _dbContext.Contents
            .AsNoTracking()
            .Where(c => !TerminalStatuses.Contains(c.Status)
                     && c.Status != ContentStatus.Failed
                     && c.UpdatedAt < stuckThreshold)
            .Select(c => new StuckItemInfo(
                c.Id,
                c.Status.ToString(),
                c.UpdatedAt,
                (now - c.UpdatedAt).TotalHours))
            .ToListAsync(ct);

        var failedCount = await _dbContext.WorkflowTransitionLogs
            .AsNoTracking()
            .CountAsync(w => w.ToStatus == ContentStatus.Failed && w.Timestamp >= last24h, ct);

        var publishedCount = await _dbContext.WorkflowTransitionLogs
            .AsNoTracking()
            .CountAsync(w => w.ToStatus == ContentStatus.Published && w.Timestamp >= last24h, ct);

        var totalCompletions = failedCount + publishedCount;
        var errorRate = totalCompletions > 0 ? (double)failedCount / totalCompletions : 0.0;

        var activeCount = await _dbContext.Contents
            .AsNoTracking()
            .CountAsync(c => !TerminalStatuses.Contains(c.Status), ct);

        return Result<PipelineHealthResponse>.Success(new PipelineHealthResponse(
            stuckItems, failedCount, errorRate, activeCount));
    }

    public async Task<Result<EngagementSummaryResponse>> GetEngagementSummaryAsync(CancellationToken ct)
    {
        var now = _dateTime.UtcNow;
        var sevenDaysAgo = now.AddDays(-7);

        var projected = await _dbContext.EngagementSnapshots
            .AsNoTracking()
            .Where(s => s.FetchedAt >= sevenDaysAgo)
            .Join(
                _dbContext.ContentPlatformStatuses.AsNoTracking(),
                s => s.ContentPlatformStatusId,
                cps => cps.Id,
                (s, cps) => new
                {
                    s.Likes,
                    s.Comments,
                    s.Shares,
                    cps.ContentId,
                    cps.Platform
                })
            .ToListAsync(ct);

        var rolling7Day = projected.Sum(s => s.Likes + s.Comments + s.Shares);
        var averageDaily = rolling7Day / 7.0;

        var platformBreakdown = projected
            .GroupBy(s => s.Platform.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Likes + s.Comments + s.Shares))
            as IReadOnlyDictionary<string, int>;

        var anomalies = new List<EngagementAnomaly>();

        if (projected.Count > 0)
        {
            var perContent = projected
                .GroupBy(s => new { s.ContentId, s.Platform })
                .Select(g =>
                {
                    var likes = g.Sum(s => s.Likes);
                    var comments = g.Sum(s => s.Comments);
                    var shares = g.Sum(s => s.Shares);
                    var topMetric = likes >= comments && likes >= shares ? "likes"
                        : comments >= shares ? "comments" : "shares";
                    return new
                    {
                        g.Key.ContentId,
                        Platform = g.Key.Platform.ToString(),
                        TotalEngagement = likes + comments + shares,
                        TopMetric = topMetric,
                        TopValue = Math.Max(likes, Math.Max(comments, shares))
                    };
                })
                .ToList();

            var perPlatformAvg = perContent
                .GroupBy(p => p.Platform)
                .ToDictionary(g => g.Key, g => g.Average(p => (double)p.TotalEngagement));

            foreach (var item in perContent)
            {
                if (!perPlatformAvg.TryGetValue(item.Platform, out var platformAvg) || platformAvg <= 0)
                    continue;

                var multiplier = item.TotalEngagement / platformAvg;

                if (multiplier > 2.0)
                {
                    var confidence = Math.Min(1.0, Math.Abs(multiplier - 1.0) / 3.0);
                    anomalies.Add(new EngagementAnomaly(
                        item.ContentId, item.Platform, item.TopMetric,
                        item.TopValue, platformAvg, multiplier, "positive", confidence));
                }
                else if (multiplier < 0.5)
                {
                    var confidence = Math.Min(1.0, Math.Abs(multiplier - 1.0) / 3.0);
                    anomalies.Add(new EngagementAnomaly(
                        item.ContentId, item.Platform, item.TopMetric,
                        item.TopValue, platformAvg, multiplier, "negative", confidence));
                }
            }
        }

        return Result<EngagementSummaryResponse>.Success(new EngagementSummaryResponse(
            rolling7Day, averageDaily, anomalies, platformBreakdown));
    }

    public async Task<Result<BriefingSummaryResponse>> GetBriefingSummaryAsync(CancellationToken ct)
    {
        var now = _dateTime.UtcNow;
        var todayStart = new DateTimeOffset(now.Date, TimeSpan.Zero);
        var todayEnd = todayStart.AddDays(1);
        var last24h = now.AddHours(-24);

        var scheduledToday = await _dbContext.CalendarSlots
            .AsNoTracking()
            .Where(s => s.ScheduledAt >= todayStart && s.ScheduledAt < todayEnd && s.ContentId != null)
            .Join(
                _dbContext.Contents.AsNoTracking(),
                slot => slot.ContentId,
                content => content.Id,
                (slot, content) => new ScheduledContentInfo(
                    content.Id,
                    slot.Platform.ToString(),
                    slot.ScheduledAt,
                    content.Title))
            .OrderBy(s => s.Time)
            .ToListAsync(ct);

        var recentSnapshots = await _dbContext.EngagementSnapshots
            .AsNoTracking()
            .Where(s => s.FetchedAt >= last24h)
            .Join(
                _dbContext.ContentPlatformStatuses.AsNoTracking(),
                s => s.ContentPlatformStatusId,
                cps => cps.Id,
                (s, cps) => new
                {
                    cps.ContentId,
                    Platform = cps.Platform.ToString(),
                    Total = s.Likes + s.Comments + s.Shares,
                    TopMetric = s.Likes >= s.Comments && s.Likes >= s.Shares ? "likes"
                        : s.Comments >= s.Shares ? "comments" : "shares",
                    TopValue = Math.Max(s.Likes, Math.Max(s.Comments, s.Shares))
                })
            .OrderByDescending(s => s.Total)
            .Take(3)
            .ToListAsync(ct);

        var engagementHighlights = recentSnapshots
            .Select(s => new EngagementHighlight(s.ContentId, s.Platform, s.TopMetric, s.TopValue))
            .ToList();

        var trendingTopics = await _dbContext.TrendSuggestions
            .AsNoTracking()
            .Where(t => t.Status == TrendSuggestionStatus.Pending)
            .OrderByDescending(t => t.RelevanceScore)
            .Take(5)
            .Select(t => new TrendingTopicInfo(t.Topic, t.RelevanceScore, "Trends"))
            .ToListAsync(ct);

        var queueDepth = await _dbContext.Contents
            .AsNoTracking()
            .CountAsync(c => !TerminalStatuses.Contains(c.Status), ct);

        var nextPublish = await NextScheduledPostQuery(now).FirstOrDefaultAsync(ct);

        var pendingApprovals = await _dbContext.Contents
            .AsNoTracking()
            .CountAsync(c => c.Status == ContentStatus.Review || c.Status == ContentStatus.Approved, ct);

        return Result<BriefingSummaryResponse>.Success(new BriefingSummaryResponse(
            scheduledToday, engagementHighlights, trendingTopics,
            queueDepth, nextPublish, pendingApprovals));
    }

    private IQueryable<ScheduledPostInfo> NextScheduledPostQuery(DateTimeOffset now) =>
        _dbContext.Contents
            .AsNoTracking()
            .Where(c => c.Status == ContentStatus.Scheduled && c.ScheduledAt != null && c.ScheduledAt > now)
            .OrderBy(c => c.ScheduledAt)
            .Select(c => new ScheduledPostInfo(
                c.Id,
                c.TargetPlatforms.Length > 0 ? c.TargetPlatforms[0].ToString() : "Unknown",
                c.ScheduledAt!.Value));
}
