using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Common;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IIntegrationMonitorService
{
    Task<Result<QueueStatusResponse>> GetQueueStatusAsync(CancellationToken ct);
    Task<Result<PipelineHealthResponse>> GetPipelineHealthAsync(CancellationToken ct);
    Task<Result<EngagementSummaryResponse>> GetEngagementSummaryAsync(CancellationToken ct);
    Task<Result<BriefingSummaryResponse>> GetBriefingSummaryAsync(CancellationToken ct);
}
