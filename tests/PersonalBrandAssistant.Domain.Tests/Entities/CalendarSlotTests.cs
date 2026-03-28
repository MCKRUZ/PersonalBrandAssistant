using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class CalendarSlotTests
{
    [Fact]
    public void CalendarSlot_DefaultStatus_IsOpen()
    {
        var slot = new CalendarSlot();
        Assert.Equal(CalendarSlotStatus.Open, slot.Status);
    }

    [Fact]
    public void CalendarSlot_WithOverride_StoresOverriddenOccurrence()
    {
        var original = DateTimeOffset.UtcNow;
        var slot = new CalendarSlot
        {
            IsOverride = true,
            OverriddenOccurrence = original,
        };

        Assert.True(slot.IsOverride);
        Assert.Equal(original, slot.OverriddenOccurrence);
    }

    [Fact]
    public void CalendarSlot_ContentId_IsNullable()
    {
        var slot = new CalendarSlot();
        Assert.Null(slot.ContentId);
    }
}
