using PBA.Domain.Enums;

namespace PBA.Application.Features.Content.Dtos;

public record UpdateContentRequest
{
    public string? Title { get; init; }
    public string? Body { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public ContentType? ContentType { get; init; }
    public Platform? PrimaryPlatform { get; init; }
    public DateTimeOffset LastUpdatedAt { get; init; }
    public IReadOnlyList<Platform>? TargetPlatforms { get; init; }
}
