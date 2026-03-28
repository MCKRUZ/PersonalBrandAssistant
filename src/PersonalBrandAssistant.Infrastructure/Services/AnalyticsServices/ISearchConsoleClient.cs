using Google.Apis.SearchConsole.v1.Data;

namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

/// <summary>
/// Thin wrapper around SearchConsoleService.Searchanalytics.Query for testability.
/// </summary>
internal interface ISearchConsoleClient
{
    Task<SearchAnalyticsQueryResponse> QueryAsync(
        string siteUrl, SearchAnalyticsQueryRequest request, CancellationToken ct);
}
