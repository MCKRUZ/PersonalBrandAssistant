namespace PBA.Application.Features.Ideas.Dtos;

/// <summary>
/// One pillar's contribution to an idea's brand-fit, for display in the Ranked view. <see cref="Name"/>
/// is resolved from the ACTIVE pillar by <c>BrandPillarId</c> (R-C3), so renames reflect without
/// re-scoring; <see cref="Score"/> is the stored sub-score (0..1); <see cref="Reason"/> is the short
/// LLM rationale (display only).
/// </summary>
public record PillarBreakdownDto
{
    public string Name { get; init; } = string.Empty;
    public double Score { get; init; }
    public string? Reason { get; init; }
}
