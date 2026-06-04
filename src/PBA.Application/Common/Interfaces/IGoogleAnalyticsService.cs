using PBA.Application.Features.Analytics.Dtos;
using PBA.Domain.Common;

namespace PBA.Application.Common.Interfaces;

public interface IGoogleAnalyticsService
{
    Task<Result<WebsiteOverview>> GetOverviewAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<Result<IReadOnlyList<PageViewEntry>>> GetTopPagesAsync(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);
    Task<Result<IReadOnlyList<TrafficSourceEntry>>> GetTrafficSourcesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<Result<IReadOnlyList<SearchQueryEntry>>> GetTopQueriesAsync(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);
}
