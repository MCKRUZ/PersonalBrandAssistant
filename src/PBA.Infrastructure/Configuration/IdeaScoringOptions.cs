namespace PBA.Infrastructure.Configuration;

public sealed class IdeaScoringOptions
{
    public const string SectionName = "IdeaScoring";

    /// <summary>How often the scoring sweep runs.</summary>
    public int IntervalMinutes { get; init; } = 10;

    /// <summary>Ideas scored per sweep.</summary>
    public int BatchSize { get; init; } = 20;

    /// <summary>Delay between per-idea LLM calls, to respect rate limits.</summary>
    public int ThrottleMs { get; init; } = 1000;

    /// <summary>Cheap, fast model for per-idea scoring. Defaults independent of the drafting model.</summary>
    public string Model { get; init; } = "google/gemini-2.5-flash";

    /// <summary>
    /// Gates the irreversible, token-spending LLM scoring step (replaces the old BackfillEnabled). Default
    /// FALSE so the first production LLM-scoring run never fires automatically on deploy — embedding and the
    /// below-threshold embedding-only brandFit still run (cheap, reversible). A human flips this to true on
    /// the target host after confirming embedding backfill completed and the cost is understood (R: section-12 gate).
    /// </summary>
    public bool ScoringEnabled { get; init; } = false;
}
