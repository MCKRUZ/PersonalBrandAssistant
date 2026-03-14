using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Models;

public record ContentPromptModel
{
    public required string? Title { get; init; }
    public required string Body { get; init; }
    public required ContentType ContentType { get; init; }
    public required ContentStatus Status { get; init; }
    public required PlatformType[] TargetPlatforms { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}
