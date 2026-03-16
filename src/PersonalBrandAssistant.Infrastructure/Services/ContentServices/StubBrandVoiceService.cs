using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;

/// <summary>
/// Placeholder until section-07 (Brand Voice) is implemented.
/// Returns a perfect score so the pipeline can function end-to-end.
/// </summary>
public sealed class StubBrandVoiceService : IBrandVoiceService
{
    public Task<Result<BrandVoiceScore>> ScoreContentAsync(Guid contentId, CancellationToken ct)
    {
        var score = new BrandVoiceScore(100, 100, 100, 100, [], []);
        return Task.FromResult(Result<BrandVoiceScore>.Success(score));
    }
}
