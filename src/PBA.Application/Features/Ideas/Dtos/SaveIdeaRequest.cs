namespace PBA.Application.Features.Ideas.Dtos;

public record SaveIdeaRequest
{
    public Guid IdeaId { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
}
