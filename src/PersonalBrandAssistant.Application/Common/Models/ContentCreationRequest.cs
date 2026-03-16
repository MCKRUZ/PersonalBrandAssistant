using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Models;

public record ContentCreationRequest(
    ContentType Type,
    string Topic,
    string? Outline,
    PlatformType[]? TargetPlatforms,
    Guid? ParentContentId,
    Dictionary<string, string>? Parameters);
