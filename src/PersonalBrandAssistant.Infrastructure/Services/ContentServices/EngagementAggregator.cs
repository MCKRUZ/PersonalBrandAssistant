using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;

public class EngagementAggregator : IEngagementAggregator
{
    private readonly IApplicationDbContext _db;
    private readonly IEnumerable<ISocialPlatform> _platforms;
    private readonly IRateLimiter _rateLimiter;
    private readonly ILogger<EngagementAggregator> _logger;
    private readonly ContentEngineOptions _options;

    public EngagementAggregator(
        IApplicationDbContext db,
        IEnumerable<ISocialPlatform> platforms,
        IRateLimiter rateLimiter,
        IOptions<ContentEngineOptions> options,
        ILogger<EngagementAggregator> logger)
    {
        _db = db;
        _platforms = platforms;
        _rateLimiter = rateLimiter;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<EngagementSnapshot>> FetchLatestAsync(
        Guid contentPlatformStatusId, CancellationToken ct)
    {
        var cps = await _db.ContentPlatformStatuses
            .FirstOrDefaultAsync(s => s.Id == contentPlatformStatusId, ct);

        if (cps is null)
            return Result<EngagementSnapshot>.NotFound(
                $"ContentPlatformStatus {contentPlatformStatusId} not found.");

        if (string.IsNullOrEmpty(cps.PlatformPostId))
            return Result<EngagementSnapshot>.ValidationFailure(
                ["Cannot fetch engagement for unpublished post (no PlatformPostId)."]);

        var platform = _platforms.FirstOrDefault(p => p.Type == cps.Platform);
        if (platform is null)
            return Result<EngagementSnapshot>.Failure(
                ErrorCode.InternalError, $"No adapter registered for platform {cps.Platform}.");

        var rateLimitCheck = await _rateLimiter.CanMakeRequestAsync(
            cps.Platform, "engagement", ct);

        if (!rateLimitCheck.IsSuccess)
            return Result<EngagementSnapshot>.Failure(rateLimitCheck.ErrorCode,
                rateLimitCheck.Errors.ToArray());

        if (!rateLimitCheck.Value!.Allowed)
            return Result<EngagementSnapshot>.Failure(ErrorCode.RateLimited,
                rateLimitCheck.Value.Reason ?? "Rate limited.");

        var engagementResult = await platform.GetEngagementAsync(cps.PlatformPostId, ct);
        if (!engagementResult.IsSuccess)
            return Result<EngagementSnapshot>.Failure(engagementResult.ErrorCode,
                engagementResult.Errors.ToArray());

        var stats = engagementResult.Value!;
        var snapshot = new EngagementSnapshot
        {
            ContentPlatformStatusId = contentPlatformStatusId,
            Likes = stats.Likes,
            Comments = stats.Comments,
            Shares = stats.Shares,
            Impressions = stats.Impressions,
            Clicks = stats.Clicks,
            FetchedAt = DateTimeOffset.UtcNow,
        };

        _db.EngagementSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);

        await _rateLimiter.RecordRequestAsync(cps.Platform, "engagement", 0, null, ct);

        _logger.LogInformation(
            "Fetched engagement for {Platform} post {PostId}: {Likes}L/{Comments}C/{Shares}S",
            cps.Platform, cps.PlatformPostId, stats.Likes, stats.Comments, stats.Shares);

        return Result<EngagementSnapshot>.Success(snapshot);
    }

    public async Task<Result<ContentPerformanceReport>> GetPerformanceAsync(
        Guid contentId, CancellationToken ct)
    {
        var publishedStatuses = await _db.ContentPlatformStatuses
            .Where(s => s.ContentId == contentId && s.Status == PlatformPublishStatus.Published)
            .ToListAsync(ct);

        var statusIds = publishedStatuses.Select(s => s.Id).ToHashSet();

        // Batch fetch all snapshots for published statuses (avoids N+1)
        var allSnapshots = await _db.EngagementSnapshots
            .Where(e => statusIds.Contains(e.ContentPlatformStatusId))
            .ToListAsync(ct);

        var statusPlatformLookup = publishedStatuses.ToDictionary(s => s.Id, s => s.Platform);

        var latestByPlatform = allSnapshots
            .GroupBy(s => s.ContentPlatformStatusId)
            .Select(g => g.OrderByDescending(s => s.FetchedAt).First())
            .ToDictionary(
                s => statusPlatformLookup[s.ContentPlatformStatusId],
                s => s)
            .AsReadOnly();

        var totalEngagement = latestByPlatform.Values
            .Sum(s => s.Likes + s.Comments + s.Shares);

        var completedExecutions = await _db.AgentExecutions
            .Where(e => e.ContentId == contentId && e.Status == AgentExecutionStatus.Completed)
            .ToListAsync(ct);

        decimal? llmCost = completedExecutions.Count > 0
            ? completedExecutions.Sum(e => e.Cost)
            : null;

        decimal? costPerEngagement = totalEngagement > 0 && llmCost is > 0
            ? llmCost.Value / totalEngagement
            : null;

        return Result<ContentPerformanceReport>.Success(new ContentPerformanceReport(
            contentId, latestByPlatform, totalEngagement, llmCost, costPerEngagement));
    }

    public async Task<Result<IReadOnlyList<TopPerformingContent>>> GetTopContentAsync(
        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct)
    {
        if (limit <= 0)
            return Result<IReadOnlyList<TopPerformingContent>>.ValidationFailure(
                ["Limit must be greater than zero."]);

        if (from >= to)
            return Result<IReadOnlyList<TopPerformingContent>>.ValidationFailure(
                ["'from' must be earlier than 'to'."]);

        var publishedStatuses = await _db.ContentPlatformStatuses
            .Where(s => s.Status == PlatformPublishStatus.Published
                        && s.PublishedAt >= from && s.PublishedAt <= to)
            .ToListAsync(ct);

        if (publishedStatuses.Count == 0)
            return Result<IReadOnlyList<TopPerformingContent>>.Success(
                Array.Empty<TopPerformingContent>());

        var statusIds = publishedStatuses.Select(s => s.Id).ToHashSet();

        var allSnapshots = await _db.EngagementSnapshots
            .Where(e => statusIds.Contains(e.ContentPlatformStatusId))
            .ToListAsync(ct);

        // Group snapshots by ContentPlatformStatusId, take latest per status
        var latestPerStatus = allSnapshots
            .GroupBy(s => s.ContentPlatformStatusId)
            .Select(g => g.OrderByDescending(s => s.FetchedAt).First())
            .ToList();

        // Map status ID -> ContentPlatformStatus for platform/content lookup
        var statusLookup = publishedStatuses.ToDictionary(s => s.Id);

        // Group latest snapshots by ContentId
        var byContent = latestPerStatus
            .Select(snap => new { Snapshot = snap, Status = statusLookup[snap.ContentPlatformStatusId] })
            .GroupBy(x => x.Status.ContentId)
            .Select(g =>
            {
                var totalEngagement = g.Sum(x => x.Snapshot.Likes + x.Snapshot.Comments + x.Snapshot.Shares);
                var engagementByPlatform = g.ToDictionary(
                    x => x.Status.Platform,
                    x => x.Snapshot.Likes + x.Snapshot.Comments + x.Snapshot.Shares)
                    .AsReadOnly();

                return new
                {
                    ContentId = g.Key,
                    TotalEngagement = totalEngagement,
                    EngagementByPlatform = (IReadOnlyDictionary<PlatformType, int>)engagementByPlatform,
                };
            })
            .OrderByDescending(x => x.TotalEngagement)
            .Take(limit)
            .ToList();

        // Load titles
        var contentIds = byContent.Select(x => x.ContentId).ToHashSet();
        var contentTitles = await _db.Contents
            .Where(c => contentIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Title ?? "(Untitled)", ct);

        var results = byContent
            .Select(x => new TopPerformingContent(
                x.ContentId,
                contentTitles.GetValueOrDefault(x.ContentId, "(Untitled)"),
                x.TotalEngagement,
                x.EngagementByPlatform))
            .ToList();

        return Result<IReadOnlyList<TopPerformingContent>>.Success(results);
    }

    public async Task<Result<int>> CleanupSnapshotsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var dailyCutoff = now.AddDays(-7);
        var deleteCutoff = now.AddDays(-_options.EngagementRetentionDays);

        // Bulk delete everything older than retention period
        var expiredCount = await _db.EngagementSnapshots
            .Where(s => s.FetchedAt < deleteCutoff)
            .ExecuteDeleteAsync(ct);

        // For 7-30 day range, keep only one snapshot per day per status
        var consolidationSnapshots = await _db.EngagementSnapshots
            .Where(s => s.FetchedAt >= deleteCutoff && s.FetchedAt < dailyCutoff)
            .ToListAsync(ct);

        var toRemove = consolidationSnapshots
            .GroupBy(s => new { s.ContentPlatformStatusId, Day = s.FetchedAt.Date })
            .SelectMany(g => g.OrderByDescending(s => s.FetchedAt).Skip(1))
            .ToList();

        if (toRemove.Count > 0)
        {
            _db.EngagementSnapshots.RemoveRange(toRemove);
            await _db.SaveChangesAsync(ct);
        }

        var totalRemoved = expiredCount + toRemove.Count;

        if (totalRemoved > 0)
        {
            _logger.LogInformation(
                "Cleaned up {Count} engagement snapshots ({Old} expired, {Consolidated} consolidated)",
                totalRemoved, expiredCount, toRemove.Count);
        }

        return Result<int>.Success(totalRemoved);
    }
}
