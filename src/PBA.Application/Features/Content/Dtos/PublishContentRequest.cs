using PBA.Domain.Enums;

namespace PBA.Application.Features.Content.Dtos;

public record PublishContentRequest
{
    public IReadOnlyList<Platform>? TargetPlatforms { get; init; }
}
