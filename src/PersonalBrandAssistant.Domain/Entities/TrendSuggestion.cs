using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public class TrendSuggestion : AuditableEntityBase
{
    public string Topic { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public float RelevanceScore { get; set; }
    public ContentType SuggestedContentType { get; set; }
    public PlatformType[] SuggestedPlatforms { get; set; } = [];
    public TrendSuggestionStatus Status { get; set; } = TrendSuggestionStatus.Pending;
    public ICollection<TrendSuggestionItem> RelatedTrends { get; set; } = new List<TrendSuggestionItem>();
}
