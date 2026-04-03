namespace PersonalBrandAssistant.Application.Common.Models;

public class ContentEngineOptions
{
    public const string SectionName = "ContentEngine";

    public int MaxTreeDepth { get; set; } = 3;

    public int BrandVoiceScoreThreshold { get; set; } = 70;

    public int MaxAutoRegenerateAttempts { get; set; } = 3;

    public int EngagementRetentionDays { get; set; } = 30;

    public int EngagementAggregationIntervalHours { get; set; } = 4;

    /// <summary>
    /// How often (in minutes) to check for newly published posts that need
    /// their initial engagement data seeded. Defaults to 15 minutes.
    /// </summary>
    public int NewPostEngagementCheckMinutes { get; set; } = 15;

    public int SlotMaterializationDays { get; set; } = 7;
}
