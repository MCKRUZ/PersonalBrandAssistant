using Google.Apis.SearchConsole.v1.Data;
using Microsoft.Extensions.Logging;

namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

/// <summary>
/// No-op Search Console client used when credentials are not configured.
/// Returns empty responses so the dashboard degrades gracefully.
/// Logs a warning on first use so operators know Search Console data is unavailable.
/// </summary>
internal sealed class NullSearchConsoleClient : ISearchConsoleClient
{
    private readonly ILogger<NullSearchConsoleClient>? _logger;
    private bool _warned;

    public NullSearchConsoleClient() { }

    public NullSearchConsoleClient(ILogger<NullSearchConsoleClient> logger)
    {
        _logger = logger;
    }

    public Task<SearchAnalyticsQueryResponse> QueryAsync(
        string siteUrl, SearchAnalyticsQueryRequest request, CancellationToken ct)
    {
        if (!_warned)
        {
            _warned = true;
            _logger?.LogWarning(
                "Search Console credentials are not configured -- all query requests will return empty data. " +
                "Place a valid service account JSON at the path specified in GoogleAnalytics:CredentialsPath");
        }

        return Task.FromResult(new SearchAnalyticsQueryResponse());
    }
}
