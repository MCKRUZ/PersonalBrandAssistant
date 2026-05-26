namespace PBA.Infrastructure.Configuration;

public class RssPollingOptions
{
    public const string SectionName = "RssPolling";

    public int PollIntervalMinutes { get; init; } = 30;
    public int MaxConsecutiveFailures { get; init; } = 5;
}
