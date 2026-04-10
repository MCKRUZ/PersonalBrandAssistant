using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;
namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;

public class EngagementAggregationProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ContentEngineOptions _options;
    private readonly BackgroundJobsOptions _jobsOptions;
    private readonly ILogger<EngagementAggregationProcessor> _logger;

    /// <summary>
    /// Maximum number of snapshots a post can have before it graduates from
    /// the fast "new post" loop to the standard aggregation loop.
    /// </summary>
    private const int NewPostSnapshotThreshold = 3;

    public EngagementAggregationProcessor(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        IOptions<ContentEngineOptions> options,
        IOptions<BackgroundJobsOptions> jobsOptions,
        ILogger<EngagementAggregationProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _options = options.Value;
        _jobsOptions = jobsOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_jobsOptions.EngagementAggregationEnabled)
        {
            _logger.LogInformation("EngagementAggregation processor is disabled");
            return;
        }

        var bulkInterval = TimeSpan.FromHours(Math.Max(1, _options.EngagementAggregationIntervalHours));
        var newPostInterval = TimeSpan.FromMinutes(Math.Max(5, _options.NewPostEngagementCheckMinutes));

        // Run both loops concurrently
        await Task.WhenAll(
            RunNewPostLoopAsync(newPostInterval, stoppingToken),
            RunBulkAggregationLoopAsync(bulkInterval, stoppingToken));
    }

    /// <summary>
    /// Fast loop (default 15 min) that seeds initial engagement data for
    /// recently published posts that have no or very few snapshots yet.
    /// </summary>
    private async Task RunNewPostLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);

        // Run immediately on startup, then on interval
        await ProcessNewPostsAsync(ct);

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await ProcessNewPostsAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during new-post engagement seeding");
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
            }
        }
    }

    /// <summary>
    /// Standard loop (default 4 hours) that refreshes engagement data
    /// for all published content within the retention window.
    /// </summary>
    private async Task RunBulkAggregationLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);

        // Run immediately on startup so imported posts get snapshots without waiting for the interval
        await ProcessBulkAsync(ct);

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await ProcessBulkAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during bulk engagement aggregation");
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
            }
        }
    }

    /// <summary>
    /// Finds published posts from the last 24 hours that have a PlatformPostId
    /// but fewer than <see cref="NewPostSnapshotThreshold"/> engagement snapshots,
    /// and fetches their engagement data.
    /// </summary>
    internal async Task ProcessNewPostsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var aggregator = scope.ServiceProvider.GetRequiredService<IEngagementAggregator>();
        var now = _dateTimeProvider.UtcNow;
        var recentCutoff = now.AddHours(-24);

        var recentStatuses = await context.ContentPlatformStatuses
            .Where(s => s.Status == PlatformPublishStatus.Published
                        && s.PublishedAt >= recentCutoff
                        && s.PlatformPostId != null)
            .Select(s => new { s.Id, s.Platform, s.PlatformPostId })
            .ToListAsync(ct);

        if (recentStatuses.Count == 0)
            return;

        var statusIds = recentStatuses.Select(s => s.Id).ToHashSet();

        // Count existing snapshots per status in one query
        var snapshotCounts = await context.EngagementSnapshots
            .Where(e => statusIds.Contains(e.ContentPlatformStatusId))
            .GroupBy(e => e.ContentPlatformStatusId)
            .Select(g => new { StatusId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StatusId, x => x.Count, ct);

        var needsFetch = recentStatuses
            .Where(s => !snapshotCounts.TryGetValue(s.Id, out var count)
                        || count < NewPostSnapshotThreshold)
            .ToList();

        if (needsFetch.Count == 0)
            return;

        _logger.LogInformation(
            "New-post engagement seeder found {Count} recently published posts needing engagement data",
            needsFetch.Count);

        var successCount = 0;
        var skipCount = 0;

        foreach (var entry in needsFetch)
        {
            try
            {
                var result = await aggregator.FetchLatestAsync(entry.Id, ct);

                if (result.IsSuccess)
                {
                    successCount++;
                }
                else
                {
                    skipCount++;
                    _logger.LogDebug(
                        "Skipped new-post engagement fetch for {Platform} post {PostId}: {Errors}",
                        entry.Platform, entry.PlatformPostId, string.Join(", ", result.Errors));
                }
            }
            catch (Exception ex)
            {
                skipCount++;
                _logger.LogError(ex,
                    "Error fetching engagement for new {Platform} post {PostId}",
                    entry.Platform, entry.PlatformPostId);
            }
        }

        _logger.LogInformation(
            "New-post engagement seeding complete: {Success} fetched, {Skipped} skipped out of {Total}",
            successCount, skipCount, needsFetch.Count);
    }

    /// <summary>
    /// Refreshes engagement data for all published content within the retention window.
    /// This is the original bulk aggregation logic.
    /// </summary>
    internal async Task ProcessBulkAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var aggregator = scope.ServiceProvider.GetRequiredService<IEngagementAggregator>();
        var now = _dateTimeProvider.UtcNow;
        var retentionStart = now.AddDays(-_options.EngagementRetentionDays);

        var publishedStatuses = await context.ContentPlatformStatuses
            .Where(s => s.Status == PlatformPublishStatus.Published
                        && s.PublishedAt >= retentionStart)
            .ToListAsync(ct);

        var successCount = 0;
        var skipCount = 0;

        foreach (var entry in publishedStatuses)
        {
            try
            {
                var result = await aggregator.FetchLatestAsync(entry.Id, ct);

                if (result.IsSuccess)
                {
                    successCount++;
                }
                else
                {
                    skipCount++;
                    _logger.LogDebug(
                        "Skipped engagement fetch for {Platform} post {PostId}: {Errors}",
                        entry.Platform, entry.PlatformPostId, string.Join(", ", result.Errors));
                }
            }
            catch (Exception ex)
            {
                skipCount++;
                _logger.LogError(ex,
                    "Error fetching engagement for {Platform} post {PostId}",
                    entry.Platform, entry.PlatformPostId);
            }
        }

        _logger.LogInformation(
            "Bulk engagement aggregation complete: {Success} fetched, {Skipped} skipped out of {Total}",
            successCount, skipCount, publishedStatuses.Count);

        // Run retention cleanup
        try
        {
            var cleanupResult = await aggregator.CleanupSnapshotsAsync(ct);

            if (cleanupResult.IsSuccess && cleanupResult.Value > 0)
            {
                _logger.LogInformation("Cleaned up {Count} old engagement snapshots", cleanupResult.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during engagement snapshot cleanup");
        }
    }
}
