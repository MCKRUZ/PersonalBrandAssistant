using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.ValueObjects;

public record ContentTypePlatformOverride(ContentType ContentType, PlatformType PlatformType, AutonomyLevel Level);
