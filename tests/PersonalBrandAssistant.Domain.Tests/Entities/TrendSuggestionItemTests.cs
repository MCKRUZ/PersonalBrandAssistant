using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class TrendSuggestionItemTests
{
    [Fact]
    public void TrendSuggestionItem_MapsJoinRelationship()
    {
        var suggestionId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var join = new TrendSuggestionItem
        {
            TrendSuggestionId = suggestionId,
            TrendItemId = itemId,
            SimilarityScore = 0.92f,
        };

        Assert.Equal(suggestionId, join.TrendSuggestionId);
        Assert.Equal(itemId, join.TrendItemId);
        Assert.Equal(0.92f, join.SimilarityScore);
    }
}
