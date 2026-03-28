using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class TrendItemTests
{
    [Fact]
    public void TrendItem_DeduplicationKey_StoresValue()
    {
        var key = "sha256-abc123";
        var item = new TrendItem { DeduplicationKey = key };
        Assert.Equal(key, item.DeduplicationKey);
    }

    [Fact]
    public void TrendItem_TrendSourceId_IsNullable()
    {
        var item = new TrendItem();
        Assert.Null(item.TrendSourceId);
    }

    [Fact]
    public void TrendItem_StoresAllFields()
    {
        var now = DateTimeOffset.UtcNow;
        var item = new TrendItem
        {
            Title = "Test Trend",
            Description = "A test trend item",
            Url = "https://example.com/trend",
            SourceName = "r/dotnet",
            SourceType = TrendSourceType.Reddit,
            DetectedAt = now,
        };

        Assert.Equal("Test Trend", item.Title);
        Assert.Equal("A test trend item", item.Description);
        Assert.Equal("https://example.com/trend", item.Url);
        Assert.Equal("r/dotnet", item.SourceName);
        Assert.Equal(TrendSourceType.Reddit, item.SourceType);
        Assert.Equal(now, item.DetectedAt);
    }
}
