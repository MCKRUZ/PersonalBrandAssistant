using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IEngagementAggregator
{
    Task<Result<EngagementSnapshot>> FetchLatestAsync(Guid contentPlatformStatusId, CancellationToken ct);

    Task<Result<ContentPerformanceReport>> GetPerformanceAsync(Guid contentId, CancellationToken ct);

    Task<Result<IReadOnlyList<TopPerformingContent>>> GetTopContentAsync(
        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);

    Task<Result<int>> CleanupSnapshotsAsync(CancellationToken ct);
}
