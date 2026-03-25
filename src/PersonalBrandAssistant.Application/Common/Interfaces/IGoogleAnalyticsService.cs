using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

/// <summary>Abstracts GA4 Data API and Search Console API access.</summary>
public interface IGoogleAnalyticsService
{
    Task<Result<WebsiteOverview>> GetOverviewAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    Task<Result<IReadOnlyList<PageViewEntry>>> GetTopPagesAsync(
        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);

    Task<Result<IReadOnlyList<TrafficSourceEntry>>> GetTrafficSourcesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    Task<Result<IReadOnlyList<SearchQueryEntry>>> GetTopQueriesAsync(
        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);
}
