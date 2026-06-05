using PBA.Application.Common.Models;

namespace PBA.Application.Common.Interfaces;

public interface IIdeaAnalyzer
{
    /// <summary>
    /// Scores a single idea for brand content-worthiness (0-10) and returns an
    /// AI summary, category, and tags. Returns null if the model output cannot be parsed.
    /// </summary>
    Task<IdeaAnalysis?> AnalyzeAsync(
        string title,
        string? description,
        string? url,
        string sourceName,
        CancellationToken ct = default);
}
