namespace PBA.Infrastructure.Configuration;

public class SidecarOptions
{
    public const string SectionName = "Sidecar";

    public string CliPath { get; init; } = "claude";
    public int TimeoutMs { get; init; } = 60_000;
}
