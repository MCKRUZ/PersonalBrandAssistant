namespace PBA.Application.Common.Models;

/// <summary>The item the analyzer scores: raw idea fields, untouched by the LLM until scored.</summary>
public sealed record IdeaAnalysisInput(string Title, string? Description, string? Url, string SourceName);

/// <summary>
/// One pillar's sub-score for an idea. <see cref="PillarId"/> is resolved from the snapshot by matching
/// the LLM-returned name (R-C3) so the score survives a pillar rename; <see cref="PillarName"/> is the
/// matched display name. <see cref="Score"/> is on the {0, .25, .5, .75, 1} scale.
/// </summary>
public sealed record PillarScore(Guid PillarId, string PillarName, double Score, string? Reason);

/// <summary>
/// Per-pillar analysis of an idea against the active profile snapshot. <see cref="Pillars"/> holds the
/// raw sub-scores (stored once; query-time weights are applied later in section-08, so re-weighting
/// never re-runs the LLM). The anti/authority flags only mark the item — the multipliers are applied at
/// read time, not here.
/// </summary>
public sealed record IdeaAnalysis(
    IReadOnlyList<PillarScore> Pillars,
    bool IsAntiTopic,
    bool IsAuthorityTopic,
    string Reason);
