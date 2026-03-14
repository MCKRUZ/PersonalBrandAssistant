using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.ValueObjects;

public record PlatformOverride(PlatformType PlatformType, AutonomyLevel Level);
