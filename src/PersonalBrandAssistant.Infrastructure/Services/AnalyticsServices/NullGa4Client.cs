using Google.Analytics.Data.V1Beta;

namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

/// <summary>
/// No-op GA4 client used when credentials are not configured.
/// Returns empty responses so the dashboard degrades gracefully.
/// </summary>
internal sealed class NullGa4Client : IGa4Client
{
    public Task<RunReportResponse> RunReportAsync(RunReportRequest request, CancellationToken ct)
        => Task.FromResult(new RunReportResponse());
}
