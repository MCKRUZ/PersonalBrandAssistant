using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common;
using PBA.Domain.Entities;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;

namespace PBA.Infrastructure.Services.Radar;

/// <summary>
/// Marks near-duplicate ideas using in-memory cosine similarity over embeddings (replaces the old
/// LLM clusterer). Gated on full embedding coverage of the window (R-H1) so it never picks a wrong
/// primary mid-backfill; within a group the highest-brandFit idea is primary. No LLM/sidecar use.
/// </summary>
public sealed class IdeaDedupService(
    IServiceScopeFactory scopeFactory,
    IOptions<ClusteringOptions> options,
    IOptionsMonitor<RankingOptions> rankingOptions,
    ILogger<IdeaDedupService> logger) : BackgroundService
{
    private readonly ClusteringOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DedupBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Idea dedup sweep failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(_options.IntervalMinutes), stoppingToken);
        }
    }

    internal async Task DedupBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var dedupThreshold = rankingOptions.CurrentValue.DedupThreshold; // snapshot once per sweep

        var since = DateTimeOffset.UtcNow.AddHours(-_options.LookbackHours);

        // Gate (R-H1): if any in-window idea is still unembedded, dedup could pick a wrong primary; bail.
        if (await db.Ideas.AnyAsync(i => i.DetectedAt >= since && i.Embedding == null, ct))
        {
            logger.LogInformation("Dedup gated: in-window ideas still unembedded");
            return;
        }

        var candidates = await db.Ideas
            .Where(i => i.Embedding != null
                && i.DetectedAt >= since
                && i.DuplicateOfId == null
                && i.ClusteredAt == null)
            .OrderByDescending(i => i.Score)
            .Take(_options.MaxItemsPerSweep)
            .ToListAsync(ct);
        if (candidates.Count < 2) return;

        // In-memory pairwise cosine grouping via union-find (R-L3-dedup). O(n²) over ~MaxItemsPerSweep
        // (~40) is trivial; no pgvector query (EF InMemory can't run one).
        var parent = Enumerable.Range(0, candidates.Count).ToArray();
        int Find(int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            for (var j = i + 1; j < candidates.Count; j++)
            {
                if (VectorMath.CosineSimilarity(candidates[i].Embedding!, candidates[j].Embedding!) >= dedupThreshold)
                    parent[Find(i)] = Find(j);
            }
        }

        foreach (var group in candidates.Select((idea, idx) => (idea, root: Find(idx))).GroupBy(x => x.root))
        {
            var members = group.Select(x => x.idea).ToList();
            if (members.Count < 2) continue;

            // Primary = highest brandFit. The derived Score is round(brandFit×10), a faithful proxy within
            // a group (true near-duplicates share a pre-filter side, so all members use the same Score
            // formula) and avoids re-snapshotting the profile here. Tie-break deterministically on the
            // oldest DetectedAt (the original story is the canonical primary), then Id, so the primary
            // never depends on EF load order.
            var primary = members
                .OrderByDescending(m => m.Score ?? 0)
                .ThenBy(m => m.DetectedAt)
                .ThenBy(m => m.Id)
                .First();
            foreach (var dup in members.Where(m => m.Id != primary.Id))
                dup.DuplicateOfId = primary.Id;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var idea in candidates) idea.ClusteredAt = now;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Dedup sweep: processed {Count} in-window ideas", candidates.Count);
    }
}
