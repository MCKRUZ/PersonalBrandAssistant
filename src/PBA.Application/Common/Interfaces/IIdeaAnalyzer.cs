using PBA.Application.Common.Models;

namespace PBA.Application.Common.Interfaces;

public interface IIdeaAnalyzer
{
    /// <summary>
    /// Scores an item against the active profile snapshot. Returns per-pillar sub-scores (0..1 on the
    /// {0, .25, .5, .75, 1} scale, keyed by <see cref="PillarScore.PillarId"/>), the anti-topic /
    /// authority flags, and a one-line reason. Returns null on LLM/parse failure, on the central-collapse
    /// guard (all pillar scores identical, R-M5), or when no returned pillar name maps to the snapshot.
    /// </summary>
    Task<IdeaAnalysis?> AnalyzeAsync(
        IdeaAnalysisInput input, BrandRankingProfileSnapshot profile, CancellationToken ct = default);
}
