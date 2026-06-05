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

    /// <summary>When false, only ideas detected after service start are scored (no 3,831-item backfill).</summary>
    public bool BackfillEnabled { get; init; } = false;
}
