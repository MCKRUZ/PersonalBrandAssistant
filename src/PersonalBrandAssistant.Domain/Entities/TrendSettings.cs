using PersonalBrandAssistant.Domain.Common;

namespace PersonalBrandAssistant.Domain.Entities;

public class TrendSettings : AuditableEntityBase
{
    private TrendSettings()
    {
        Id = Guid.Empty;
    }

    public bool RelevanceFilterEnabled { get; set; } = true;
    public float RelevanceScoreThreshold { get; set; } = 0.6f;
    public int MaxSuggestionsPerCycle { get; set; } = 10;

    public static TrendSettings CreateDefault() => new();
}
