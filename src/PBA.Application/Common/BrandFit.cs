namespace PBA.Application.Common;

/// <summary>
/// The single, shared brand-fit formulas. Both the embedding service (section-06) and the scoring sweep
/// (section-07) call <see cref="EmbeddingWeightedSum"/> so the pre-filter math exists in exactly one
/// place; the scoring sweep and read-time ranking (section-08) call <see cref="RenormalizedSubScore"/>
/// for the query-time weighted brandFit. All in-memory; uses <see cref="VectorMath.CosineSimilarity"/>.
/// </summary>
public static class BrandFit
{
    /// <summary>
    /// Embedding pre-filter: weighted Σ over pillars of <c>weight × cosine(itemVec, pillarVec)</c>.
    /// Pillars whose embedding is null/empty are skipped (they contribute nothing and never NaN the sum).
    /// Not renormalized — it is a coarse gate, not the final brandFit.
    /// </summary>
    public static double EmbeddingWeightedSum(
        float[] itemVec, IReadOnlyList<(double Weight, float[]? Embedding)> pillars)
    {
        double sum = 0;
        foreach (var (weight, embedding) in pillars)
        {
            if (embedding is null || embedding.Length == 0) continue;
            sum += weight * VectorMath.CosineSimilarity(itemVec, embedding);
        }
        return sum;
    }

    /// <summary>
    /// Query-time brandFit: weight-renormalized average of per-pillar sub-scores over the pillars that
    /// actually have a sub-score (Σ weight×score / Σ weight). Renormalizing over present pillars keeps
    /// the value on a 0..1 scale regardless of how many pillars were scored. Returns 0 when no pillar in
    /// <paramref name="pillars"/> has a sub-score.
    /// </summary>
    public static double RenormalizedSubScore(
        IReadOnlyDictionary<Guid, double> subScoresByPillarId,
        IReadOnlyList<(Guid Id, double Weight)> pillars)
    {
        double weighted = 0, totalWeight = 0;
        foreach (var (id, weight) in pillars)
        {
            if (!subScoresByPillarId.TryGetValue(id, out var score)) continue;
            weighted += weight * score;
            totalWeight += weight;
        }
        return totalWeight > 0 ? weighted / totalWeight : 0;
    }
}
