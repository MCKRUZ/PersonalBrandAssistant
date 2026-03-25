using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Adapters;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class AnalyticsEndpoints
{
    private static readonly HashSet<string> ValidPeriods = ["1d", "7d", "14d", "30d", "90d"];

    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analytics").WithTags("Analytics");

        // Existing engagement routes
        group.MapGet("/content/{id:guid}", GetPerformance);
        group.MapGet("/top", GetTopContent);
        group.MapPost("/content/{id:guid}/refresh", RefreshEngagement);

        // Dashboard routes
        group.MapGet("/dashboard", GetDashboard);
        group.MapGet("/engagement-timeline", GetTimeline);
        group.MapGet("/platform-summary", GetPlatformSummaries);
        group.MapGet("/website", GetWebsiteAnalytics);
        group.MapGet("/substack", GetSubstackPosts);
        group.MapGet("/health", GetAnalyticsHealth);
        group.MapPost("/discover-posts", DiscoverPosts);
    }

    private static async Task<IResult> GetPerformance(
        IEngagementAggregator aggregator,
        Guid id,
        CancellationToken ct)
    {
        var result = await aggregator.GetPerformanceAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetTopContent(
        IEngagementAggregator aggregator,
        DateTimeOffset from,
        DateTimeOffset to,
        int limit = 10,
        CancellationToken ct = default)
    {
        var clampedLimit = Math.Clamp(limit, 1, 50);
        var result = await aggregator.GetTopContentAsync(from, to, clampedLimit, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RefreshEngagement(
        IEngagementAggregator aggregator,
        Guid id,
        CancellationToken ct)
    {
        var result = await aggregator.FetchLatestAsync(id, ct);
        if (result.IsSuccess)
            return Results.Accepted(value: result.Value);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetDashboard(
        IDashboardAggregator aggregator,
        IDashboardCacheInvalidator cacheInvalidator,
        IDateTimeProvider clock,
        string? period = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        bool refresh = false,
        CancellationToken ct = default)
    {
        var rangeResult = ParseDateRange(period, from, to, clock);
        if (!rangeResult.IsSuccess)
            return rangeResult.ToHttpResult();

        if (refresh)
            await cacheInvalidator.TryInvalidateAsync(ct);

        var (resolvedFrom, resolvedTo) = rangeResult.Value!;
        var result = await aggregator.GetSummaryAsync(resolvedFrom, resolvedTo, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetTimeline(
        IDashboardAggregator aggregator,
        IDashboardCacheInvalidator cacheInvalidator,
        IDateTimeProvider clock,
        string? period = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        bool refresh = false,
        CancellationToken ct = default)
    {
        var rangeResult = ParseDateRange(period, from, to, clock);
        if (!rangeResult.IsSuccess)
            return rangeResult.ToHttpResult();

        if (refresh)
            await cacheInvalidator.TryInvalidateAsync(ct);

        var (resolvedFrom, resolvedTo) = rangeResult.Value!;
        var result = await aggregator.GetTimelineAsync(resolvedFrom, resolvedTo, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetPlatformSummaries(
        IDashboardAggregator aggregator,
        IDashboardCacheInvalidator cacheInvalidator,
        IDateTimeProvider clock,
        string? period = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        bool refresh = false,
        CancellationToken ct = default)
    {
        var rangeResult = ParseDateRange(period, from, to, clock);
        if (!rangeResult.IsSuccess)
            return rangeResult.ToHttpResult();

        if (refresh)
            await cacheInvalidator.TryInvalidateAsync(ct);

        var (resolvedFrom, resolvedTo) = rangeResult.Value!;
        var result = await aggregator.GetPlatformSummariesAsync(resolvedFrom, resolvedTo, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetWebsiteAnalytics(
        IGoogleAnalyticsService gaService,
        IDateTimeProvider clock,
        string? period = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var rangeResult = ParseDateRange(period, from, to, clock);
        if (!rangeResult.IsSuccess)
            return rangeResult.ToHttpResult();

        var (resolvedFrom, resolvedTo) = rangeResult.Value!;

        // Run all four calls in parallel; capture failures individually
        var overviewTask = gaService.GetOverviewAsync(resolvedFrom, resolvedTo, ct);
        var topPagesTask = gaService.GetTopPagesAsync(resolvedFrom, resolvedTo, 20, ct);
        var trafficTask = gaService.GetTrafficSourcesAsync(resolvedFrom, resolvedTo, ct);
        var queriesTask = gaService.GetTopQueriesAsync(resolvedFrom, resolvedTo, 20, ct);

        await Task.WhenAll(overviewTask, topPagesTask, trafficTask, queriesTask);

        var overview = overviewTask.Result;
        var topPages = topPagesTask.Result;
        var traffic = trafficTask.Result;
        var queries = queriesTask.Result;

        var response = new WebsiteAnalyticsResponse(
            Overview: overview.IsSuccess ? overview.Value : null,
            TopPages: topPages.IsSuccess ? topPages.Value! : [],
            TrafficSources: traffic.IsSuccess ? traffic.Value! : [],
            SearchQueries: queries.IsSuccess ? queries.Value! : []);

        return Results.Ok(response);
    }

    private static async Task<IResult> GetSubstackPosts(
        ISubstackService substackService,
        int limit = 10,
        CancellationToken ct = default)
    {
        var clampedLimit = Math.Clamp(limit, 1, 50);
        var result = await substackService.GetRecentPostsAsync(clampedLimit, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetAnalyticsHealth(
        IGoogleAnalyticsService gaService,
        ISubstackService substackService,
        CancellationToken ct = default)
    {
        var yesterday = DateTimeOffset.UtcNow.AddDays(-1);
        var today = DateTimeOffset.UtcNow;

        bool ga4 = false, searchConsole = false, substack = false;

        // Probe GA4
        try
        {
            var gaResult = await gaService.GetOverviewAsync(yesterday, today, ct);
            ga4 = gaResult.IsSuccess;
        }
        catch { /* connectivity failed */ }

        // Probe Search Console independently
        try
        {
            var scResult = await gaService.GetTopQueriesAsync(yesterday, today, 1, ct);
            searchConsole = scResult.IsSuccess;
        }
        catch { /* connectivity failed */ }

        // Probe Substack
        try
        {
            var substackResult = await substackService.GetRecentPostsAsync(1, ct);
            substack = substackResult.IsSuccess;
        }
        catch { /* connectivity failed */ }

        return Results.Ok(new { ga4, searchConsole, substack });
    }

    private static async Task<IResult> DiscoverPosts(
        RedditPlatformAdapter redditAdapter,
        IApplicationDbContext db,
        int limit = 25,
        CancellationToken ct = default)
    {
        var clampedLimit = Math.Clamp(limit, 1, 100);
        var discoveryResult = await redditAdapter.DiscoverUserPostsAsync(clampedLimit, ct);

        if (!discoveryResult.IsSuccess)
            return discoveryResult.ToHttpResult();

        var discovered = discoveryResult.Value!;

        // Find existing platform post IDs to avoid duplicates
        var existingPostIds = await db.ContentPlatformStatuses
            .Where(cps => cps.Platform == PlatformType.Reddit)
            .Select(cps => cps.PlatformPostId)
            .ToHashSetAsync(ct);

        // Group by normalized title to collapse crossposts
        var grouped = discovered.GroupBy(p =>
            p.Title.Replace(" — open source", "").Replace(" - open source", "").Trim());

        var imported = 0;
        foreach (var group in grouped)
        {
            // Check if all posts in this group already exist
            var newPosts = group.Where(p => !existingPostIds.Contains(p.PlatformPostId)).ToList();
            if (newPosts.Count == 0) continue;

            // Find existing Content for this title group, or create one
            var firstPost = group.First();
            var existingContent = await db.Contents
                .Where(c => c.Title != null && c.Title.Contains(group.Key) && c.TargetPlatforms.Contains(PlatformType.Reddit))
                .FirstOrDefaultAsync(ct);

            Content content;
            if (existingContent is not null)
            {
                content = existingContent;
            }
            else
            {
                content = Content.Create(
                    ContentType.SocialPost,
                    firstPost.Body,
                    firstPost.Title,
                    [PlatformType.Reddit]);
                content.TransitionTo(ContentStatus.Review);
                content.TransitionTo(ContentStatus.Approved);
                content.TransitionTo(ContentStatus.Scheduled);
                content.TransitionTo(ContentStatus.Publishing);
                content.TransitionTo(ContentStatus.Published);
                content.PublishedAt = firstPost.PublishedAt;
                db.Contents.Add(content);
            }

            foreach (var post in newPosts)
            {
                db.ContentPlatformStatuses.Add(new ContentPlatformStatus
                {
                    ContentId = content.Id,
                    Platform = PlatformType.Reddit,
                    Status = PlatformPublishStatus.Published,
                    PlatformPostId = post.PlatformPostId,
                    PostUrl = post.Url,
                    PublishedAt = post.PublishedAt,
                });
                imported++;
            }
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            discovered = discovered.Count,
            imported,
            skippedDuplicates = discovered.Count - imported,
        });
    }

    private static Result<(DateTimeOffset From, DateTimeOffset To)> ParseDateRange(
        string? period, DateTimeOffset? from, DateTimeOffset? to, IDateTimeProvider clock)
    {
        var today = clock.UtcNow.Date;

        if (period is not null)
        {
            if (!ValidPeriods.Contains(period))
                return Result<(DateTimeOffset, DateTimeOffset)>.Failure(
                    ErrorCode.ValidationFailed,
                    $"Invalid period '{period}'. Valid values: {string.Join(", ", ValidPeriods)}");

            var days = int.Parse(period.TrimEnd('d'));
            var resolvedTo = new DateTimeOffset(today, TimeSpan.Zero)
                .AddDays(1).AddTicks(-1); // end of today
            var resolvedFrom = new DateTimeOffset(today.AddDays(-(days - 1)), TimeSpan.Zero);
            return Result.Success((resolvedFrom, resolvedTo));
        }

        if (from.HasValue && to.HasValue)
        {
            if (from.Value > to.Value)
                return Result<(DateTimeOffset, DateTimeOffset)>.Failure(
                    ErrorCode.ValidationFailed, "'from' must be before or equal to 'to'.");

            var maxRange = TimeSpan.FromDays(365);
            if (to.Value - from.Value > maxRange)
                return Result<(DateTimeOffset, DateTimeOffset)>.Failure(
                    ErrorCode.ValidationFailed, "Date range cannot exceed 365 days.");

            return Result.Success((from.Value, to.Value));
        }

        // Default to 30d
        {
            var resolvedTo = new DateTimeOffset(today, TimeSpan.Zero)
                .AddDays(1).AddTicks(-1);
            var resolvedFrom = new DateTimeOffset(today.AddDays(-29), TimeSpan.Zero);
            return Result.Success((resolvedFrom, resolvedTo));
        }
    }
}
