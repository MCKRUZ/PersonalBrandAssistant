using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services;

public class ContentScheduler : IContentScheduler
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ContentScheduler(
        IApplicationDbContext dbContext,
        IWorkflowEngine workflowEngine,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _workflowEngine = workflowEngine;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<MediatR.Unit>> ScheduleAsync(
        Guid contentId, DateTimeOffset scheduledAt, CancellationToken ct = default)
    {
        var content = await _dbContext.Contents
            .FirstOrDefaultAsync(c => c.Id == contentId, ct);

        if (content is null)
            return Result<MediatR.Unit>.NotFound($"Content {contentId} not found.");

        if (content.Status != ContentStatus.Approved)
            return Result<MediatR.Unit>.Failure(ErrorCode.ValidationFailed,
                "Content must be in Approved status to schedule.");

        if (scheduledAt <= _dateTimeProvider.UtcNow)
            return Result<MediatR.Unit>.Failure(ErrorCode.ValidationFailed,
                "Scheduled time must be in the future.");

        content.ScheduledAt = scheduledAt;

        return await _workflowEngine.TransitionAsync(contentId, ContentStatus.Scheduled, ct: ct);
    }

    public async Task<Result<MediatR.Unit>> RescheduleAsync(
        Guid contentId, DateTimeOffset newScheduledAt, CancellationToken ct = default)
    {
        var content = await _dbContext.Contents
            .FirstOrDefaultAsync(c => c.Id == contentId, ct);

        if (content is null)
            return Result<MediatR.Unit>.NotFound($"Content {contentId} not found.");

        if (content.Status != ContentStatus.Scheduled)
            return Result<MediatR.Unit>.Failure(ErrorCode.ValidationFailed,
                "Content must be in Scheduled status to reschedule.");

        if (newScheduledAt <= _dateTimeProvider.UtcNow)
            return Result<MediatR.Unit>.Failure(ErrorCode.ValidationFailed,
                "Scheduled time must be in the future.");

        content.ScheduledAt = newScheduledAt;
        await _dbContext.SaveChangesAsync(ct);

        return Result<MediatR.Unit>.Success(MediatR.Unit.Value);
    }

    public async Task<Result<MediatR.Unit>> CancelAsync(
        Guid contentId, CancellationToken ct = default)
    {
        var content = await _dbContext.Contents
            .FirstOrDefaultAsync(c => c.Id == contentId, ct);

        if (content is null)
            return Result<MediatR.Unit>.NotFound($"Content {contentId} not found.");

        if (content.Status != ContentStatus.Scheduled)
            return Result<MediatR.Unit>.Failure(ErrorCode.ValidationFailed,
                "Content must be in Scheduled status to cancel.");

        content.ScheduledAt = null;

        return await _workflowEngine.TransitionAsync(
            contentId, ContentStatus.Approved, reason: "Schedule cancelled", ct: ct);
    }
}
