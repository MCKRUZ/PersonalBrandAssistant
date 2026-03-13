using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.ValueObjects;

public class UserSettings
{
    public AutonomyLevel DefaultAutonomyLevel { get; set; } = AutonomyLevel.Manual;
    public bool NotificationsEnabled { get; set; } = true;
    public string Theme { get; set; } = "light";
}
