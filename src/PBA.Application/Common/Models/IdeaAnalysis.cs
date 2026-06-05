namespace PBA.Application.Common.Models;

public sealed record IdeaAnalysis(
    int Score,
    string Reason,
    string Summary,
    string? Category,
    IReadOnlyList<string> Tags);
