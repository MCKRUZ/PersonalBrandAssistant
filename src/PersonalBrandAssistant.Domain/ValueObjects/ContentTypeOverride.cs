using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.ValueObjects;

public record ContentTypeOverride(ContentType ContentType, AutonomyLevel Level);
