using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public record AnalysisResult(string Summary, string? ImageUrl);

public interface IArticleAnalyzer
{
    Task<Result<AnalysisResult>> AnalyzeAsync(Guid trendItemId, CancellationToken ct);
}
