using Google.Apis.SearchConsole.v1.Data;

namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

/// <summary>
/// No-op Search Console client used when credentials are not configured.
/// Returns empty responses so the dashboard degrades gracefully.
/// </summary>
internal sealed class NullSearchConsoleClient : ISearchConsoleClient
{
    public Task<SearchAnalyticsQueryResponse> QueryAsync(
        string siteUrl, SearchAnalyticsQueryRequest request, CancellationToken ct)
        => Task.FromResult(new SearchAnalyticsQueryResponse());
}
