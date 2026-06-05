namespace PBA.Infrastructure.Configuration;

public sealed class DigestOptions
{
    public const string SectionName = "Digest";

    /// <summary>Local time of day (24h) to generate the digest, e.g. "07:00".</summary>
    public string RunAtLocalTime { get; init; } = "07:00";
    public int TopN { get; init; } = 8;
    public int LookbackHours { get; init; } = 24;

    /// <summary>Digest copy is Matt-facing brand prose; use the quality model.</summary>
    public string Model { get; init; } = "google/gemini-2.5-pro";
}
