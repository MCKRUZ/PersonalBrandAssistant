namespace PersonalBrandAssistant.Domain.ValueObjects;

public class ContentMetadata
{
    public List<string> Tags { get; set; } = [];
    public List<string> SeoKeywords { get; set; } = [];
    public Dictionary<string, string> PlatformSpecificData { get; set; } = new();
    public string? AiGenerationContext { get; set; }
    public int? TokensUsed { get; set; }
    public decimal? EstimatedCost { get; set; }
}
