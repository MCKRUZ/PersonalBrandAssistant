namespace PBA.Infrastructure.Configuration;

public sealed class ClusteringOptions
{
    public const string SectionName = "Clustering";

    public int IntervalMinutes { get; init; } = 30;
    public int MinScore { get; init; } = 6;
    public int LookbackHours { get; init; } = 48;
    public int MaxItemsPerSweep { get; init; } = 40;
    public string Model { get; init; } = "google/gemini-2.5-flash";
}
