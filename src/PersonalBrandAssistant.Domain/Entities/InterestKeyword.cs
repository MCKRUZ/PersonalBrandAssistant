using PersonalBrandAssistant.Domain.Common;

namespace PersonalBrandAssistant.Domain.Entities;

public class InterestKeyword : AuditableEntityBase
{
    public string Keyword { get; set; } = string.Empty;
    public double Weight { get; set; } = 1.0;
    public int MatchCount { get; set; }
}
