using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IApprovalService
{
    Task<Result<MediatR.Unit>> ApproveAsync(Guid contentId, CancellationToken ct = default);
    Task<Result<MediatR.Unit>> RejectAsync(Guid contentId, string feedback, CancellationToken ct = default);
    Task<Result<int>> BatchApproveAsync(Guid[] contentIds, CancellationToken ct = default);
}
