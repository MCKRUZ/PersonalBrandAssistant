namespace PBA.Application.Features.Ideas.Dtos;

public record CreateIdeaRequest
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Url { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}
