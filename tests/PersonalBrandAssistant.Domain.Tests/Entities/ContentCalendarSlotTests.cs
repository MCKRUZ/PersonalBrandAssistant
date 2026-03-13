using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class ContentCalendarSlotTests
{
    [Fact]
    public void Slot_WithValidTimeZoneId_CreatesSuccessfully()
    {
        var slot = new ContentCalendarSlot
        {
            ScheduledDate = new DateOnly(2026, 3, 15),
            TimeZoneId = "America/New_York",
            ContentType = ContentType.BlogPost,
            TargetPlatform = PlatformType.LinkedIn,
        };

        Assert.Equal("America/New_York", slot.TimeZoneId);
        Assert.NotEqual(Guid.Empty, slot.Id);
    }

    [Fact]
    public void Slot_WithRecurrencePattern_StoresCronString()
    {
        var slot = new ContentCalendarSlot
        {
            ScheduledDate = new DateOnly(2026, 3, 15),
            TimeZoneId = "UTC",
            ContentType = ContentType.SocialPost,
            TargetPlatform = PlatformType.TwitterX,
            IsRecurring = true,
            RecurrencePattern = "0 9 * * 1-5",
        };

        Assert.True(slot.IsRecurring);
        Assert.Equal("0 9 * * 1-5", slot.RecurrencePattern);
    }

    [Fact]
    public void NonRecurringSlot_HasNullRecurrencePattern()
    {
        var slot = new ContentCalendarSlot
        {
            ScheduledDate = new DateOnly(2026, 3, 15),
            TimeZoneId = "UTC",
            ContentType = ContentType.SocialPost,
            TargetPlatform = PlatformType.TwitterX,
        };

        Assert.False(slot.IsRecurring);
        Assert.Null(slot.RecurrencePattern);
    }
}
