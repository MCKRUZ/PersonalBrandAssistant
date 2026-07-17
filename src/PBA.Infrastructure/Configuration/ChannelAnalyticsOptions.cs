namespace PBA.Infrastructure.Configuration;

public sealed class ChannelAnalyticsOptions
{
    public const string SectionName = "ChannelAnalytics";

    public string RunAtLocalTime { get; init; } = "05:00";
    public int RecentVideoCount { get; init; } = 50;
    public bool YouTubeEnabled { get; init; }
    public bool InstagramEnabled { get; init; }
    public bool TikTokEnabled { get; init; }
}
