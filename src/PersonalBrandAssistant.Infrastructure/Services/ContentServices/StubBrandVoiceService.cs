using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;

/// <summary>
/// Placeholder for testing scenarios that don't need real brand voice validation.
/// Returns a perfect score so the pipeline can function end-to-end.
/// </summary>
public sealed class StubBrandVoiceService : IBrandVoiceService
{
    public Task<Result<BrandVoiceScore>> ScoreContentAsync(Guid contentId, CancellationToken ct)
    {
        var score = new BrandVoiceScore(100, 100, 100, 100, [], []);
        return Task.FromResult(Result<BrandVoiceScore>.Success(score));
    }

    public Task<Result<MediatR.Unit>> ValidateAndGateAsync(Guid contentId, AutonomyLevel autonomy, CancellationToken ct)
    {
        return Task.FromResult(Result<MediatR.Unit>.Success(MediatR.Unit.Value));
    }

    public Result<IReadOnlyList<string>> RunRuleChecks(string text, BrandProfile profile)
    {
        return Result<IReadOnlyList<string>>.Success([]);
    }
}
