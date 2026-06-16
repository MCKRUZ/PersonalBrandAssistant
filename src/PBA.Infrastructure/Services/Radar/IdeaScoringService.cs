using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Entities;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;

namespace PBA.Infrastructure.Services.Radar;

/// <summary>
/// Brand-anchored scoring sweep. Each sweep snapshots the active profile + ranking options ONCE
/// (R-H3, R-L2), ensures pending ideas/pillars are embedded, builds an embedded + in-window +
/// not-current-version candidate set, computes an embedding brand-fit pre-filter, LLM per-pillar scores
/// the above-threshold items (bounded by BatchSize), and stamps embedding-only brandFit on the rest.
/// </summary>
public sealed class IdeaScoringService(
    IServiceScopeFactory scopeFactory,
    IOptions<IdeaScoringOptions> options,
    IOptionsMonitor<RankingOptions> rankingOptions,
    ILogger<IdeaScoringService> logger) : BackgroundService
{
    private readonly IdeaScoringOptions _options = options.Value;

    // A poison item (refusal / unparseable / central-collapse) must not burn an LLM call every sweep
    // forever; candidates at/above the cap are excluded (R-M5).
    private const int AttemptCap = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScoreSweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Idea scoring sweep failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(_options.IntervalMinutes), stoppingToken);
        }
    }

    internal async Task ScoreSweepAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var analyzer = scope.ServiceProvider.GetRequiredService<IIdeaAnalyzer>();
        var embedder = scope.ServiceProvider.GetRequiredService<IdeaEmbeddingService>();

        // Embed pending ideas + pillar descriptions first so the candidate set and pre-filter see vectors.
        await embedder.EmbedPendingAsync(ct);

        // Snapshot the active profile ONCE (R-H3): ScoredProfileVersion is stamped from this snapshot,
        // never re-read mid-sweep.
        var profileEntity = await db.BrandRankingProfiles
            .Include(p => p.Pillars)
            .FirstOrDefaultAsync(p => p.IsActive, ct);
        if (profileEntity is null)
        {
            logger.LogInformation("No active brand ranking profile; skipping scoring sweep");
            return;
        }

        var snapshot = BrandRankingProfileSnapshot.FromProfile(profileEntity);
        var ranking = rankingOptions.CurrentValue; // snapshot options ONCE per sweep (R-L2)

        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddDays(-ranking.ScoringWindowDays);

        var candidates = await db.Ideas
            .Where(i => i.Embedding != null
                && i.DetectedAt >= windowStart
                && (i.ScoredProfileVersion == null || i.ScoredProfileVersion < snapshot.Version)
                && i.ScoreAttempts < AttemptCap)
            .ToListAsync(ct);
        if (candidates.Count == 0) return;

        var pillarWeights = snapshot.Pillars.Select(p => (p.Id, p.Weight)).ToList();

        // Pre-filter (in memory) through the embedding service's helper — the single shared brand-fit
        // entry point (it delegates to BrandFit.EmbeddingWeightedSum, so there is one formula).
        var prefiltered = candidates
            .Select(i => (idea: i, fit: embedder.ComputeEmbeddingBrandFit(i.Embedding!, profileEntity.Pillars)))
            .ToList();

        var above = prefiltered
            .Where(x => x.fit >= ranking.PreFilterThreshold)
            .OrderByDescending(x => x.fit)
            .Take(_options.BatchSize) // BatchSize bounds the LLM-scored subset
            .ToList();
        var below = prefiltered.Where(x => x.fit < ranking.PreFilterThreshold).ToList();

        var changed = false;
        var llmScored = 0;

        foreach (var (idea, _) in above)
        {
            idea.ScoreAttempts += 1; // count every attempt so poison items are bounded (R-M5)
            changed = true;

            var analysis = await analyzer.AnalyzeAsync(
                new IdeaAnalysisInput(idea.Title, idea.Description, idea.Url, idea.SourceName), snapshot, ct);

            if (analysis is not null)
            {
                idea.PillarSubScores = analysis.Pillars
                    .Select(p => new PillarSubScore
                    {
                        PillarId = p.PillarId, PillarName = p.PillarName, Score = p.Score, Reason = p.Reason
                    })
                    .ToList();
                idea.IsAntiTopic = analysis.IsAntiTopic;
                idea.IsAuthorityTopic = analysis.IsAuthorityTopic;
                idea.ScoreReason = analysis.Reason;
                idea.ScoredProfileVersion = snapshot.Version;
                idea.ScoredAt = now;

                // Derived display badge (R-M1). Above-threshold items use the renormalized LLM sub-score
                // brandFit; below-threshold items use the raw embedding brandFit. The two sources are
                // intentionally different signals (the UI distinguishes score vs rank sorts); the
                // authoritative ranking is PillarSubScores + ComputeRank (section-08), not this badge.
                var subById = analysis.Pillars.ToDictionary(p => p.PillarId, p => p.Score);
                var llmBrandFit = BrandFit.RenormalizedSubScore(subById, pillarWeights);
                idea.Score = (int)Math.Round(Math.Clamp(llmBrandFit, 0, 1) * 10);
                llmScored++;
            }
            // analysis == null: leave sub-scores empty and do NOT stamp ScoredProfileVersion — it retries
            // next sweep, bounded by the already-incremented ScoreAttempts.

            if (_options.ThrottleMs > 0) await Task.Delay(_options.ThrottleMs, ct);
        }

        foreach (var (idea, fit) in below)
        {
            // No LLM call. Stamp the version so it is not reconsidered until the profile changes.
            idea.ScoredProfileVersion = snapshot.Version;
            idea.ScoredAt = now;
            idea.Score = (int)Math.Round(Math.Clamp(fit, 0, 1) * 10); // raw embedding brandFit badge (R-M1)
            changed = true;
        }

        if (changed) await db.SaveChangesAsync(ct);
        logger.LogInformation("Scoring sweep: {Llm} LLM-scored, {Below} embedding-only of {Total} candidates",
            llmScored, below.Count, candidates.Count);
    }
}
