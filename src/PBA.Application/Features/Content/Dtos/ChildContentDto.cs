using PBA.Domain.Enums;

namespace PBA.Application.Features.Content.Dtos;

public record ChildContentDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public ContentType ContentType { get; init; }
    public Platform PrimaryPlatform { get; init; }
    public ContentStatus Status { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
