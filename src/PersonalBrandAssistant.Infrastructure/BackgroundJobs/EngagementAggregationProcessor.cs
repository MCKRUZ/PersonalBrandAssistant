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
    private readonly ILogger<EngagementAggregationProcessor> _logger;

    public EngagementAggregationProcessor(
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider,
        IOptions<ContentEngineOptions> options,
        ILogger<EngagementAggregationProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(Math.Max(1, _options.EngagementAggregationIntervalHours));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during engagement aggregation processing");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    internal async Task ProcessAsync(CancellationToken ct)
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
            "Engagement aggregation complete: {Success} fetched, {Skipped} skipped out of {Total}",
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
