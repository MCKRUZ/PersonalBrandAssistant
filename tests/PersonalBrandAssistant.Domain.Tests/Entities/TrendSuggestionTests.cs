using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class TrendSuggestionTests
{
    [Fact]
    public void TrendSuggestion_DefaultStatus_IsPending()
    {
        var suggestion = new TrendSuggestion();
        Assert.Equal(TrendSuggestionStatus.Pending, suggestion.Status);
    }

    [Fact]
    public void TrendSuggestion_StoresRelevanceScore()
    {
        var suggestion = new TrendSuggestion { RelevanceScore = 0.85f };
        Assert.Equal(0.85f, suggestion.RelevanceScore);
    }

    [Fact]
    public void TrendSuggestion_RelatedTrends_IsInitialized()
    {
        var suggestion = new TrendSuggestion();
        Assert.NotNull(suggestion.RelatedTrends);
        Assert.Empty(suggestion.RelatedTrends);
    }
}
