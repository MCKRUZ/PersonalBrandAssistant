namespace PBA.Application.Features.Ideas.Dtos;

public record IdeaConnectionDto
{
    public string Theme { get; init; } = string.Empty;
    public IReadOnlyList<Guid> RelatedIdeaIds { get; init; } = [];
    public string SuggestedAngle { get; init; } = string.Empty;
    public double Confidence { get; init; }
}
