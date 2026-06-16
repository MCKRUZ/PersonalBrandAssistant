namespace PBA.Infrastructure.Configuration;

public sealed class ClusteringOptions
{
    public const string SectionName = "Clustering";

    public int IntervalMinutes { get; init; } = 30;
    public int LookbackHours { get; init; } = 48;
    public int MaxItemsPerSweep { get; init; } = 40;
    // MinScore + Model removed: dedup is embedding-cosine based (DedupThreshold from RankingOptions),
    // no LLM and no score gate.
}
