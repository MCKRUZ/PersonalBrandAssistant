using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public class CalendarSlot : AuditableEntityBase
{
    public DateTimeOffset ScheduledAt { get; set; }
    public PlatformType Platform { get; set; }
    public Guid? ContentSeriesId { get; set; }
    public Guid? ContentId { get; set; }
    public CalendarSlotStatus Status { get; set; } = CalendarSlotStatus.Open;
    public bool IsOverride { get; set; }
    public DateTimeOffset? OverriddenOccurrence { get; set; }
}
