namespace PBA.Infrastructure.Configuration;

/// <summary>
/// Runtime-tunable thresholds for the embedding/scoring pipeline. Consumed via IOptionsMonitor so
/// the scoring sweep can snapshot CurrentValue once per sweep (R-L2). Half-life / floor / multipliers
/// deliberately do NOT live here — those are part of the active BrandRankingProfile (DB), applied at
/// query time so weight/decay edits re-rank without an LLM call.
/// </summary>
public sealed class RankingOptions
{
    public const string SectionName = "Ranking";

    /// <summary>Brand-fit cutoff an idea must clear to earn an LLM per-pillar scoring call.</summary>
    public double PreFilterThreshold { get; init; } = 0.30;

    /// <summary>Pairwise cosine at/above which two ideas are treated as duplicates.</summary>
    public double DedupThreshold { get; init; } = 0.85;

    /// <summary>Rolling window (days) for LLM scoring; older items rank ~0 via recency decay.</summary>
    public int ScoringWindowDays { get; init; } = 30;
}
