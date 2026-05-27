namespace PBA.Application.Features.Content.Dtos;

public record PublishStatusDto
{
    public Guid ContentId { get; init; }
    public IReadOnlyList<PlatformPublishDto> Platforms { get; init; } = [];
}
