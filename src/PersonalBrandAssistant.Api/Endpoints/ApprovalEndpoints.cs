using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class ApprovalEndpoints
{
    public record RejectRequest(string Feedback);
    public record BatchApproveRequest(Guid[] ContentIds);

    public static void MapApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/approval").WithTags("Approval");

        group.MapGet("/pending", GetPendingContent);
        group.MapPost("/{id:guid}/approve", ApproveContent);
        group.MapPost("/{id:guid}/reject", RejectContent);
        group.MapPost("/batch-approve", BatchApprove);
    }

    private static async Task<IResult> GetPendingContent(
        IApplicationDbContext dbContext,
        int pageSize = 20)
    {
        var pending = await dbContext.Contents
            .AsNoTracking()
            .Where(c => c.Status == ContentStatus.Review)
            .OrderByDescending(c => c.CreatedAt)
            .Take(Math.Clamp(pageSize, 1, 50))
            .ToListAsync();

        return Results.Ok(pending);
    }

    private static async Task<IResult> ApproveContent(
        IApprovalService approvalService,
        Guid id)
    {
        var result = await approvalService.ApproveAsync(id);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RejectContent(
        IApprovalService approvalService,
        Guid id,
        RejectRequest request)
    {
        var result = await approvalService.RejectAsync(id, request.Feedback);
        return result.ToHttpResult();
    }

    private static async Task<IResult> BatchApprove(
        IApprovalService approvalService,
        BatchApproveRequest request)
    {
        var result = await approvalService.BatchApproveAsync(request.ContentIds);
        return result.ToHttpResult();
    }
}
