using PBA.Domain.Enums;

namespace PBA.Application.Features.Content.Dtos;

public record CrossPostRequest
{
    public Platform TargetPlatform { get; init; }
}
