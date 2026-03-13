using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services;

public class ApprovalService : IApprovalService
{
    private readonly IWorkflowEngine _workflowEngine;
    private readonly INotificationService _notificationService;
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<ApprovalService> _logger;

    public ApprovalService(
        IWorkflowEngine workflowEngine,
        INotificationService notificationService,
        IApplicationDbContext dbContext,
        ILogger<ApprovalService> logger)
    {
        _workflowEngine = workflowEngine;
        _notificationService = notificationService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<Result<MediatR.Unit>> ApproveAsync(Guid contentId, CancellationToken ct = default)
    {
        var content = await _dbContext.Contents
            .FirstOrDefaultAsync(c => c.Id == contentId, ct);

        if (content is null)
            return Result<MediatR.Unit>.NotFound($"Content {contentId} not found.");

        var result = await _workflowEngine.TransitionAsync(contentId, ContentStatus.Approved, ct: ct);
        if (!result.IsSuccess)
            return result;

        // Chain to Scheduled if ScheduledAt is set
        if (content.ScheduledAt is not null && content.ScheduledAt > DateTimeOffset.UtcNow)
        {
            var scheduleResult = await _workflowEngine.TransitionAsync(
                contentId, ContentStatus.Scheduled, ct: ct);
            if (!scheduleResult.IsSuccess)
                _logger.LogWarning("Content {ContentId} approved but schedule chain failed: {Errors}",
                    contentId, string.Join(", ", scheduleResult.Errors));
        }

        return Result<MediatR.Unit>.Success(MediatR.Unit.Value);
    }

    public async Task<Result<MediatR.Unit>> RejectAsync(Guid contentId, string feedback, CancellationToken ct = default)
    {
        var result = await _workflowEngine.TransitionAsync(
            contentId, ContentStatus.Draft, reason: feedback, ct: ct);

        if (!result.IsSuccess)
            return result;

        try
        {
            await _notificationService.SendAsync(
                NotificationType.ContentRejected,
                $"Content rejected: {contentId}",
                feedback,
                contentId,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send rejection notification for content {ContentId}", contentId);
        }

        return Result<MediatR.Unit>.Success(MediatR.Unit.Value);
    }

    public async Task<Result<int>> BatchApproveAsync(Guid[] contentIds, CancellationToken ct = default)
    {
        var successCount = 0;

        foreach (var contentId in contentIds)
        {
            var result = await ApproveAsync(contentId, ct);
            if (result.IsSuccess)
                successCount++;
            else
                _logger.LogWarning("Batch approve failed for content {ContentId}: {Errors}",
                    contentId, string.Join(", ", result.Errors));
        }

        return Result<int>.Success(successCount);
    }
}
