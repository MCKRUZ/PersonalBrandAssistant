using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IRepurposingService
{
    Task<Result<IReadOnlyList<Guid>>> RepurposeAsync(
        Guid sourceContentId, PlatformType[] targetPlatforms, CancellationToken ct);

    Task<Result<IReadOnlyList<RepurposingSuggestion>>> SuggestRepurposingAsync(
        Guid contentId, CancellationToken ct);

    Task<Result<IReadOnlyList<Content>>> GetContentTreeAsync(Guid rootId, CancellationToken ct);
}
