using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IContentIdeaService
{
    Task<Result<ContentIdeaRecommendation>> AnalyzeStoryAsync(
        string storyText, string? sourceUrl, CancellationToken ct);
}
