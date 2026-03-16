namespace PersonalBrandAssistant.Application.Common.Models;

public class TrendMonitoringOptions
{
    public const string SectionName = "TrendMonitoring";
    public int AggregationIntervalMinutes { get; set; } = 30;
    public string TrendRadarApiUrl { get; set; } = "http://trendradar:8000/api";
    public string FreshRssApiUrl { get; set; } = "http://freshrss:80/api";
    public string[] RedditSubreddits { get; set; } = ["programming", "dotnet", "webdev"];
    public string HackerNewsApiUrl { get; set; } = "https://hacker-news.firebaseio.com/v0";
    public float RelevanceScoreThreshold { get; set; } = 0.6f;
    public float TitleSimilarityThreshold { get; set; } = 0.85f;
    public int MaxSuggestionsPerCycle { get; set; } = 10;
    public int MaxAutoAcceptPerCycle { get; set; } = 1;
    public int HackerNewsTopN { get; set; } = 30;
    public int HackerNewsConcurrency { get; set; } = 5;
}
