using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IAutoPublishGateService
{
    Task<Result<AutoPublishGateResult>> EvaluateAsync(
        Guid contentId,
        AutonomyLevel level,
        CancellationToken ct = default);
}

public record AutoPublishGateResult(
    bool Approved,
    IReadOnlyList<string> FailureReasons);
