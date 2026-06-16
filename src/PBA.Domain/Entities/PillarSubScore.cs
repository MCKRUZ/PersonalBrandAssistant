namespace PBA.Domain.Entities;

/// <summary>
/// One pillar's raw fit score for an idea, stored as part of the Idea's jsonb PillarSubScores document.
/// Keyed by <see cref="PillarId"/> (== BrandPillar.Id, R-C3) so it survives pillar renames;
/// <see cref="PillarName"/> is display-only and may go stale.
/// </summary>
public sealed class PillarSubScore
{
    public Guid PillarId { get; set; }
    public string PillarName { get; set; } = string.Empty;
    public double Score { get; set; }          // raw 0..1
    public string? Reason { get; set; }        // one-line LLM rationale
}
