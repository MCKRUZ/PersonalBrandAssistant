using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Entities;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Data;

namespace PBA.Infrastructure.Services.Radar;

/// <summary>
/// Turns ideas and active-profile pillar descriptions into stored <c>vector(1536)</c> embeddings, and
/// exposes the embedding brand-fit pre-filter the scoring sweep (section-07) consumes. Does no LLM
/// scoring or dedup. Never persists a zero/NaN vector (R-H2); a failed embedding batch leaves its items
/// <c>Embedding == null</c> for retry rather than aborting the whole pass.
/// </summary>
public sealed class IdeaEmbeddingService(
    ApplicationDbContext db,
    ISidecarClient sidecar,
    IOptionsMonitor<EmbeddingOptions> embeddingOptions,
    ILogger<IdeaEmbeddingService> logger)
{
    /// <summary>
    /// Embeds every idea with <c>Embedding == null</c> (title + description) and embeds active-profile
    /// pillar descriptions whose vector is still null. Idempotent: already-embedded ideas/pillars are
    /// not re-sent. Persists once per source after the batch loop.
    /// </summary>
    public async Task EmbedPendingAsync(CancellationToken ct)
    {
        var options = embeddingOptions.CurrentValue;
        await EmbedIdeasAsync(options, ct);
        await EmbedActivePillarsAsync(options, ct);
    }

    /// <summary>
    /// Embedding brand-fit pre-filter for one item over the given pillars. Delegates to the single shared
    /// <see cref="BrandFit.EmbeddingWeightedSum"/> so the sweep and this service never diverge.
    /// </summary>
    public double ComputeEmbeddingBrandFit(float[] itemVec, IReadOnlyList<BrandPillar> pillars) =>
        BrandFit.EmbeddingWeightedSum(
            itemVec, pillars.Select(p => (p.Weight, p.DescriptionEmbedding)).ToList());

    private async Task EmbedIdeasAsync(EmbeddingOptions options, CancellationToken ct)
    {
        var pending = await db.Ideas.Where(i => i.Embedding == null).ToListAsync(ct);
        if (pending.Count == 0) return;

        var now = DateTimeOffset.UtcNow;

        // Embed in safe chunks: EmbedAsync throws on a failing batch, so wrap each chunk so one failure
        // leaves only that chunk's items unembedded for retry (R-H2). EmbedAsync also drops empty inputs,
        // so build (idea, text) pairs filtered to non-empty text and align vectors by that filtered order
        // — never positional over the raw chunk. Persist after EACH chunk so a ~3,800-item backfill is
        // resumable and a crash mid-pass never re-spends embedding tokens on already-stored vectors.
        foreach (var chunk in pending.Chunk(options.BatchSize))
        {
            var items = chunk
                .Select(i => (idea: i, text: BuildText(i)))
                .Where(x => !string.IsNullOrWhiteSpace(x.text))
                .ToList();
            if (items.Count == 0) continue;

            IReadOnlyList<float[]> vectors;
            try
            {
                vectors = await sidecar.EmbedAsync(items.Select(x => x.text).ToList(), options.Model, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Idea embedding batch failed; {Count} ideas left for retry", items.Count);
                continue;
            }

            if (vectors.Count != items.Count)
            {
                logger.LogWarning("Idea embedding returned {Got} vectors for {Sent} inputs; skipping chunk",
                    vectors.Count, items.Count);
                continue;
            }

            var chunkChanged = false;
            for (var i = 0; i < items.Count; i++)
            {
                if (!IsUsable(vectors[i])) continue; // never persist a zero/NaN vector (R-H2)
                items[i].idea.Embedding = vectors[i];
                items[i].idea.EmbeddedAt = now;
                chunkChanged = true;
            }

            if (chunkChanged) await db.SaveChangesAsync(ct);
        }
    }

    private async Task EmbedActivePillarsAsync(EmbeddingOptions options, CancellationToken ct)
    {
        var profile = await db.BrandRankingProfiles
            .Include(p => p.Pillars)
            .FirstOrDefaultAsync(p => p.IsActive, ct);
        if (profile is null) return;

        // Stale = no vector yet. A pillar-definition edit (section-09) nulls the affected pillar's vector
        // and bumps Version, so a null vector is the single "needs (re)embedding" signal.
        var stale = profile.Pillars
            .Where(p => p.DescriptionEmbedding == null && !string.IsNullOrWhiteSpace(p.Description))
            .ToList();
        if (stale.Count == 0) return;

        IReadOnlyList<float[]> vectors;
        try
        {
            vectors = await sidecar.EmbedAsync(stale.Select(p => p.Description).ToList(), options.Model, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Pillar embedding batch failed; {Count} pillars left for retry", stale.Count);
            return;
        }

        if (vectors.Count != stale.Count)
        {
            logger.LogWarning("Pillar embedding returned {Got} vectors for {Sent} inputs; skipping",
                vectors.Count, stale.Count);
            return;
        }

        var changed = false;
        for (var i = 0; i < stale.Count; i++)
        {
            if (!IsUsable(vectors[i])) continue; // never persist a zero/NaN vector (R-H2)
            stale[i].DescriptionEmbedding = vectors[i];
            changed = true;
        }

        if (changed) await db.SaveChangesAsync(ct);
    }

    private static string BuildText(Idea idea) => $"{idea.Title} {idea.Description}".Trim();

    // Reject zero / NaN / Infinity vectors at the persist boundary (R-H2): cosine vs a zero vector is
    // undefined and corrupts dedup/pre-filter, so the item stays null for a later retry.
    private static bool IsUsable(float[]? vector)
    {
        if (vector is null || vector.Length == 0) return false;
        var allZero = true;
        foreach (var v in vector)
        {
            if (!float.IsFinite(v)) return false;
            if (v != 0f) allZero = false;
        }
        return !allZero;
    }
}
