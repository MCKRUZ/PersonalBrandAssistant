using Google.Analytics.Data.V1Beta;

namespace PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices;

/// <summary>
/// Production wrapper around BetaAnalyticsDataClient.
/// Registered as Singleton because BetaAnalyticsDataClient is thread-safe and reusable.
/// </summary>
internal sealed class Ga4ClientWrapper : IGa4Client
{
    private readonly BetaAnalyticsDataClient _client;

    public Ga4ClientWrapper(BetaAnalyticsDataClient client) => _client = client;

    public async Task<RunReportResponse> RunReportAsync(RunReportRequest request, CancellationToken ct)
        => await _client.RunReportAsync(request, ct);
}
