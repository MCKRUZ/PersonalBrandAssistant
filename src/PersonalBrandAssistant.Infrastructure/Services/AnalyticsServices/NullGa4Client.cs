using Google.Analytics.Data.V1Beta;
using Microsoft.Extensions.Logging;

namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

/// <summary>
/// No-op GA4 client used when credentials are not configured.
/// Returns empty responses so the dashboard degrades gracefully.
/// Logs a warning on first use so operators know GA4 data is unavailable.
/// </summary>
internal sealed class NullGa4Client : IGa4Client
{
    private readonly ILogger<NullGa4Client>? _logger;
    private bool _warned;

    public NullGa4Client() { }

    public NullGa4Client(ILogger<NullGa4Client> logger)
    {
        _logger = logger;
    }

    public Task<RunReportResponse> RunReportAsync(RunReportRequest request, CancellationToken ct)
    {
        if (!_warned)
        {
            _warned = true;
            _logger?.LogWarning(
                "GA4 credentials are not configured -- all analytics requests will return empty data. " +
                "Place a valid service account JSON at the path specified in GoogleAnalytics:CredentialsPath");
        }

        return Task.FromResult(new RunReportResponse());
    }
}
