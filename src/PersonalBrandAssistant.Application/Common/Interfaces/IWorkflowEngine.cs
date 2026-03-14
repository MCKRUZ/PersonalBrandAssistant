using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IWorkflowEngine
{
    Task<Result<MediatR.Unit>> TransitionAsync(
        Guid contentId,
        ContentStatus targetStatus,
        string? reason = null,
        ActorType actor = ActorType.User,
        CancellationToken ct = default);

    Task<Result<ContentStatus[]>> GetAllowedTransitionsAsync(
        Guid contentId,
        CancellationToken ct = default);

    Task<bool> ShouldAutoApproveAsync(
        Guid contentId,
        CancellationToken ct = default);
}
