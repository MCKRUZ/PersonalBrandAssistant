namespace PBA.Infrastructure.Configuration;

public class SourcePollingOptions
{
    public const string SectionName = "SourcePolling";

    public int PollIntervalMinutes { get; init; } = 30;
    public int MaxConsecutiveFailures { get; init; } = 5;
}
