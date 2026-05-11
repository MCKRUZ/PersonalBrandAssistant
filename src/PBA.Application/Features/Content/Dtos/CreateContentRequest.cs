using PBA.Domain.Enums;

namespace PBA.Application.Features.Content.Dtos;

public record CreateContentRequest
{
    public string Title { get; init; } = string.Empty;
    public ContentType ContentType { get; init; }
    public Platform PrimaryPlatform { get; init; }
    public Guid? SourceIdeaId { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}
