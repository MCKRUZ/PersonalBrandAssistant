using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Publishing;

public sealed class ScheduledPublishReconciler(
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduledPublishReconciler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var overdueIds = await QueryOverdueContentAsync(db);
        await ReconcileAsync(overdueIds);
    }

    /// <summary>
    /// Overdue means the HAND-OVER moment has passed, not the go-live moment. For a post PBA
    /// publishes itself the two are the same instant. For one the platform releases later they are
    /// days apart, and sweeping on go-live would let a missed hand-over sit untouched until the
    /// moment it was supposed to be seen — by which time handing it over achieves nothing, because
    /// the platform is being asked to schedule something already in the past.
    ///
    /// Only records PBA is holding are in Scheduled at all: a post the platform already owns stays
    /// Approved precisely so this sweep cannot publish it a second time.
    /// </summary>
    internal static async Task<IReadOnlyList<Guid>> QueryOverdueContentAsync(IAppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.Contents
            .Where(c => c.Status == ContentStatus.Scheduled
                        && ((c.HandoverAt.HasValue && c.HandoverAt <= now)
                            || (!c.HandoverAt.HasValue && c.ScheduledAt.HasValue && c.ScheduledAt <= now)))
            .Select(c => c.Id)
            .ToListAsync();
    }

    internal async Task ReconcileAsync(IReadOnlyList<Guid> overdueIds)
    {
        if (overdueIds.Count == 0)
        {
            logger.LogDebug("No overdue scheduled content found on startup");
            return;
        }

        logger.LogInformation("Found {Count} overdue scheduled content items to publish", overdueIds.Count);

        foreach (var contentId in overdueIds)
        {
            try
            {
                await using var itemScope = scopeFactory.CreateAsyncScope();
                var publisher = itemScope.ServiceProvider.GetRequiredService<IContentPublisher>();
                await publisher.PublishAsync(contentId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish overdue content {ContentId}", contentId);
            }
        }
    }
}
