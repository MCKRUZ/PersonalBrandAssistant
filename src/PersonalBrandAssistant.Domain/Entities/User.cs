using PersonalBrandAssistant.Domain.Common;
using PersonalBrandAssistant.Domain.ValueObjects;

namespace PersonalBrandAssistant.Domain.Entities;

public class User : AuditableEntityBase
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
    public UserSettings Settings { get; set; } = new();
}
