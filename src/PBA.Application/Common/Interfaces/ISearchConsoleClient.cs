using Google.Apis.SearchConsole.v1.Data;

namespace PBA.Application.Common.Interfaces;

/// <summary>
/// Thin seam over the Search Console Search Analytics API so query mapping is unit-testable.
/// </summary>
public interface ISearchConsoleClient
{
    Task<SearchAnalyticsQueryResponse> QueryAsync(
        string siteUrl, SearchAnalyticsQueryRequest request, CancellationToken ct);
}
