using Google.Analytics.Data.V1Beta;

namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

/// <summary>
/// Thin wrapper around BetaAnalyticsDataClient.RunReportAsync for testability.
/// </summary>
internal interface IGa4Client
{
    Task<RunReportResponse> RunReportAsync(RunReportRequest request, CancellationToken ct);
}
