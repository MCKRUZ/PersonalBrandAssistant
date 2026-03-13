using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public class ContentCalendarSlot : AuditableEntityBase
{
    public DateOnly ScheduledDate { get; set; }
    public TimeOnly? ScheduledTime { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public string? Theme { get; set; }
    public ContentType ContentType { get; set; }
    public PlatformType TargetPlatform { get; set; }
    public Guid? ContentId { get; set; }
    public bool IsRecurring { get; set; }
    public string? RecurrencePattern { get; set; }
}
