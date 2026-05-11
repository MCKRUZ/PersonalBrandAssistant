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

    internal static async Task<IReadOnlyList<Guid>> QueryOverdueContentAsync(IAppDbContext db)
    {
        return await db.Contents
            .Where(c => c.Status == ContentStatus.Scheduled
                        && c.ScheduledAt.HasValue
                        && c.ScheduledAt <= DateTimeOffset.UtcNow)
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
