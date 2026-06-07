namespace PBA.Infrastructure.Configuration;

public sealed class HackerNewsOptions
{
    public const string SectionName = "HackerNews";
    public int MinScore { get; init; } = 100;
    public int FetchTopStories { get; init; } = 30;
    public int TopComments { get; init; } = 5;
}
