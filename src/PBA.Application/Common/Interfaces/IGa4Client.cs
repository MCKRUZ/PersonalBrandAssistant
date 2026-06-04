using Google.Analytics.Data.V1Beta;

namespace PBA.Application.Common.Interfaces;

/// <summary>
/// Thin seam over the GA4 Data API gRPC client so report mapping is unit-testable.
/// </summary>
public interface IGa4Client
{
    Task<RunReportResponse> RunReportAsync(RunReportRequest request, CancellationToken ct);
}
