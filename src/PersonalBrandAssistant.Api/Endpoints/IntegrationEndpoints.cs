using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Common.Interfaces;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class IntegrationEndpoints
{
    public static void MapIntegrationEndpoints(this IEndpointRouteBuilder app)
    {
        var contentGroup = app.MapGroup("/api/content").WithTags("Integration");
        contentGroup.MapGet("/queue-status", GetQueueStatus);
        contentGroup.MapGet("/pipeline-health", GetPipelineHealth);

        var analyticsGroup = app.MapGroup("/api/analytics").WithTags("Integration");
        analyticsGroup.MapGet("/engagement-summary", GetEngagementSummary);

        var briefingGroup = app.MapGroup("/api/briefing").WithTags("Integration");
        briefingGroup.MapGet("/summary", GetBriefingSummary);
    }

    private static async Task<IResult> GetQueueStatus(
        IIntegrationMonitorService service, CancellationToken ct)
    {
        var result = await service.GetQueueStatusAsync(ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetPipelineHealth(
        IIntegrationMonitorService service, CancellationToken ct)
    {
        var result = await service.GetPipelineHealthAsync(ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetEngagementSummary(
        IIntegrationMonitorService service, CancellationToken ct)
    {
        var result = await service.GetEngagementSummaryAsync(ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetBriefingSummary(
        IIntegrationMonitorService service, CancellationToken ct)
    {
        var result = await service.GetBriefingSummaryAsync(ct);
        return result.ToHttpResult();
    }
}
