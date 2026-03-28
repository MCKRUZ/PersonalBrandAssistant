using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface ISubstackPrepService
{
    Task<Result<SubstackPreparedContent>> PrepareAsync(Guid contentId, CancellationToken ct);
    Task<Result<SubstackPublishConfirmation>> MarkPublishedAsync(Guid contentId, string? substackUrl, CancellationToken ct);
}
