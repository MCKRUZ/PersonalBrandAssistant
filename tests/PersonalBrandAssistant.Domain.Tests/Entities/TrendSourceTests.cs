using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class TrendSourceTests
{
    [Fact]
    public void TrendSource_RequiredFields_AreSet()
    {
        var source = new TrendSource
        {
            Name = "HN Feed",
            Type = TrendSourceType.HackerNews,
            PollIntervalMinutes = 30,
        };

        Assert.Equal("HN Feed", source.Name);
        Assert.Equal(TrendSourceType.HackerNews, source.Type);
        Assert.Equal(30, source.PollIntervalMinutes);
    }

    [Fact]
    public void TrendSource_IsEnabled_DefaultsToTrue()
    {
        var source = new TrendSource();
        Assert.True(source.IsEnabled);
    }
}
