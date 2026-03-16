using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class ContentSeriesTests
{
    [Fact]
    public void ContentSeries_Inherits_AuditableEntityBase()
    {
        var series = new ContentSeries();
        Assert.NotEqual(Guid.Empty, series.Id);
    }

    [Fact]
    public void ContentSeries_DefaultValues_AreCorrect()
    {
        var series = new ContentSeries();
        Assert.Empty(series.TargetPlatforms);
        Assert.Empty(series.ThemeTags);
        Assert.False(series.IsActive);
    }

    [Fact]
    public void ContentSeries_StoresRecurrenceRule()
    {
        var rrule = "FREQ=WEEKLY;BYDAY=TU;BYHOUR=9;BYMINUTE=0";
        var series = new ContentSeries { RecurrenceRule = rrule };
        Assert.Equal(rrule, series.RecurrenceRule);
    }
}
