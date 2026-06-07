namespace PBA.Infrastructure.Configuration;

public sealed class GitHubScraperOptions
{
    public const string SectionName = "GitHubScraper";
    /// <summary>Optional PAT; bound from config/env. Raises rate limit. Empty = anonymous (60 req/hr).</summary>
    public string Token { get; init; } = "";
    public int MaxEventsPerSource { get; init; } = 30;
}
