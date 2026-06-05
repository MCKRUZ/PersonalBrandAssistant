using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;

namespace PBA.Infrastructure.Services.Radar;

public sealed class IdeaClusteringService(
    IServiceScopeFactory scopeFactory,
    IOptions<ClusteringOptions> options,
    ILogger<IdeaClusteringService> logger) : BackgroundService
{
    private readonly ClusteringOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ClusterBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Idea clustering sweep failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(_options.IntervalMinutes), stoppingToken);
        }
    }

    internal async Task ClusterBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clusterer = scope.ServiceProvider.GetRequiredService<IIdeaClusterer>();

        var since = DateTimeOffset.UtcNow.AddHours(-_options.LookbackHours);
        var candidates = await db.Ideas
            .Where(i => i.ClusteredAt == null
                && i.ScoredAt != null
                && i.Score >= _options.MinScore
                && i.DuplicateOfId == null
                && i.DetectedAt >= since)
            .OrderByDescending(i => i.Score)
            .Take(_options.MaxItemsPerSweep)
            .ToListAsync(ct);

        if (candidates.Count < 2) return;

        var inputs = candidates
            .Select((idea, idx) => new ClusterInput(idx, idea.Title, idea.Summary))
            .ToList();

        var groups = await clusterer.ClusterAsync(inputs, ct);

        foreach (var group in groups)
        {
            if (group.Count < 2) continue;
            var primary = candidates[group[0]];
            foreach (var dupIdx in group.Skip(1))
            {
                if (dupIdx < 0 || dupIdx >= candidates.Count) continue;
                candidates[dupIdx].DuplicateOfId = primary.Id;
            }
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var idea in candidates) idea.ClusteredAt = now;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Clustered {Count} ideas into {Groups} groups", candidates.Count, groups.Count);
    }
}
