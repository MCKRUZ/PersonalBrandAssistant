using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class WorkflowEndpoints
{
    public record TransitionRequest(ContentStatus TargetStatus, string? Reason = null);

    public static void MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workflow").WithTags("Workflow");

        group.MapPost("/{id:guid}/transition", TransitionContent);
        group.MapGet("/{id:guid}/transitions", GetAllowedTransitions);
        group.MapGet("/audit", GetAuditLog);
    }

    private static async Task<IResult> TransitionContent(
        IWorkflowEngine workflowEngine,
        Guid id,
        TransitionRequest request)
    {
        var result = await workflowEngine.TransitionAsync(id, request.TargetStatus, request.Reason);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetAllowedTransitions(
        IWorkflowEngine workflowEngine,
        Guid id)
    {
        var result = await workflowEngine.GetAllowedTransitionsAsync(id);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetAuditLog(
        IApplicationDbContext dbContext,
        Guid? contentId = null,
        int pageSize = 20)
    {
        var query = dbContext.WorkflowTransitionLogs.AsNoTracking();

        if (contentId.HasValue)
            query = query.Where(l => l.ContentId == contentId.Value);

        var entries = await query
            .OrderByDescending(l => l.Timestamp)
            .Take(Math.Clamp(pageSize, 1, 50))
            .ToListAsync();

        return Results.Ok(entries);
    }
}
