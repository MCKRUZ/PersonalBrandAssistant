namespace PersonalBrandAssistant.Domain.Entities;

public class TrendSuggestionItem
{
    public Guid TrendSuggestionId { get; set; }
    public Guid TrendItemId { get; set; }
    public float SimilarityScore { get; set; }
    public TrendItem? TrendItem { get; set; }
}
