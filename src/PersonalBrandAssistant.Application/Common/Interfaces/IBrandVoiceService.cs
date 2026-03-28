using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IBrandVoiceService
{
    Task<Result<BrandVoiceScore>> ScoreContentAsync(Guid contentId, CancellationToken ct);

    Task<Result<MediatR.Unit>> ValidateAndGateAsync(Guid contentId, AutonomyLevel autonomy, CancellationToken ct);

    Result<IReadOnlyList<string>> RunRuleChecks(string text, BrandProfile profile);
}
