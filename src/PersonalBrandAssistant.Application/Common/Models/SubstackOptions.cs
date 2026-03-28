namespace PersonalBrandAssistant.Application.Common.Models;

public class SubstackOptions
{
    public const string SectionName = "Substack";

    /// <summary>RSS feed URL for Substack newsletter.</summary>
    public string FeedUrl { get; set; } = "https://matthewkruczek.substack.com/feed";

    public int PollingIntervalMinutes { get; set; } = 60;
    public string MatchConfidenceThreshold { get; set; } = "Medium";
    public bool EnableConditionalGet { get; set; } = true;
}
