namespace PBA.Infrastructure.Configuration;

public class FreshRssOptions
{
    public const string SectionName = "FreshRss";

    public string BaseUrl { get; init; } = "";
    public string Username { get; init; } = "";
    public string ApiPassword { get; init; } = "";
    public int BatchSize { get; init; } = 200;
    public int PollIntervalMinutes { get; init; } = 15;
    public int MaxConsecutiveFailures { get; init; } = 5;
}
