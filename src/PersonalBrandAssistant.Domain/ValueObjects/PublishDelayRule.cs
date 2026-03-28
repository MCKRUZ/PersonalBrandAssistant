using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.ValueObjects;

public record PublishDelayRule(
    PlatformType SourcePlatform,
    PlatformType TargetPlatform,
    TimeSpan DefaultDelay,
    bool RequiresConfirmation);
