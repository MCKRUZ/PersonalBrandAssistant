namespace PBA.Application.Features.Feed.Dtos;

public record BatchActRequest
{
    public IReadOnlyList<Guid> Ids { get; init; } = [];
    public string Action { get; init; } = string.Empty;
}
