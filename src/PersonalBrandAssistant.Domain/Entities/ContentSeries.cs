using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Entities;

public class ContentSeries : AuditableEntityBase
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string RecurrenceRule { get; set; } = string.Empty;
    public PlatformType[] TargetPlatforms { get; set; } = [];
    public ContentType ContentType { get; set; }
    public List<string> ThemeTags { get; set; } = [];
    public string TimeZoneId { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
}
