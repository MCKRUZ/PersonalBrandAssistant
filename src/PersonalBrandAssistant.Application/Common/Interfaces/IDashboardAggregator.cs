using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

/// <summary>Orchestrates all data sources into unified dashboard responses.</summary>
public interface IDashboardAggregator
{
    Task<Result<DashboardSummary>> GetSummaryAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    Task<Result<IReadOnlyList<DailyEngagement>>> GetTimelineAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    Task<Result<IReadOnlyList<PlatformSummary>>> GetPlatformSummariesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
