namespace PersonalBrandAssistant.Application.Common.Models;

public class ContentEngineOptions
{
    public const string SectionName = "ContentEngine";

    public int MaxTreeDepth { get; set; } = 3;

    public int BrandVoiceScoreThreshold { get; set; } = 70;

    public int MaxAutoRegenerateAttempts { get; set; } = 3;
}
