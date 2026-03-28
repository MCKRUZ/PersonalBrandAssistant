using Google.Apis.SearchConsole.v1;
using Google.Apis.SearchConsole.v1.Data;

namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

/// <summary>
/// Production wrapper around SearchConsoleService.Searchanalytics.Query.
/// </summary>
internal sealed class SearchConsoleClientWrapper : ISearchConsoleClient
{
    private readonly SearchConsoleService _service;

    public SearchConsoleClientWrapper(SearchConsoleService service) => _service = service;

    public Task<SearchAnalyticsQueryResponse> QueryAsync(
        string siteUrl, SearchAnalyticsQueryRequest request, CancellationToken ct)
    {
        var query = _service.Searchanalytics.Query(request, siteUrl);
        return query.ExecuteAsync(ct);
    }
}
