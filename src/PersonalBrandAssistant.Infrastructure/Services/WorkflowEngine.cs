using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using MediatR;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Domain.Events;
using Stateless;

namespace PersonalBrandAssistant.Infrastructure.Services;

public class WorkflowEngine : IWorkflowEngine
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<WorkflowEngine> _logger;

    public WorkflowEngine(
        IApplicationDbContext dbContext,
        IDateTimeProvider dateTimeProvider,
        ILogger<WorkflowEngine> logger)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public async Task<Result<MediatR.Unit>> TransitionAsync(
        Guid contentId,
        ContentStatus targetStatus,
        string? reason = null,
        ActorType actor = ActorType.User,
        CancellationToken ct = default)
    {
        var content = await _dbContext.Contents
            .FirstOrDefaultAsync(c => c.Id == contentId, ct);

        if (content is null)
            return Result<MediatR.Unit>.NotFound($"Content {contentId} not found.");

        var previousStatus = content.Status;

        var trigger = MapToTrigger(previousStatus, targetStatus);
        if (trigger is null)
            return Result<MediatR.Unit>.Failure(ErrorCode.ValidationFailed,
                $"No valid trigger from {previousStatus} to {targetStatus}.");

        var stateMachine = BuildStateMachine(content);

        if (!stateMachine.CanFire(trigger.Value))
            return Result<MediatR.Unit>.Failure(ErrorCode.ValidationFailed,
                $"Cannot transition from {previousStatus} to {targetStatus}.");

        try
        {
            stateMachine.Fire(trigger.Value);
        }
        catch (InvalidOperationException ex)
        {
            return Result<MediatR.Unit>.Failure(ErrorCode.ValidationFailed, ex.Message);
        }

        if (content.Status == ContentStatus.Publishing)
            content.PublishingStartedAt = _dateTimeProvider.UtcNow;
        else if (previousStatus == ContentStatus.Publishing)
            content.PublishingStartedAt = null;

        _dbContext.WorkflowTransitionLogs.Add(
            WorkflowTransitionLog.Create(
                content.Id,
                previousStatus,
                content.Status,
                actor,
                actor.ToString(),
                reason));

        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<MediatR.Unit>.Conflict(
                $"Content {contentId} was modified concurrently. Please retry.");
        }

        RaisePostCommitEvents(content, previousStatus, reason);

        // Auto-approval chaining: if we just moved to Review, check if we should auto-approve
        if (content.Status == ContentStatus.Review && await ShouldAutoApproveAsync(contentId, ct))
        {
            _logger.LogInformation("Auto-approving content {ContentId} (CapturedAutonomyLevel: {Level})",
                contentId, content.CapturedAutonomyLevel);
            return await TransitionAsync(contentId, ContentStatus.Approved, "Auto-approved", ActorType.System, ct);
        }

        return Result<MediatR.Unit>.Success(MediatR.Unit.Value);
    }

    public async Task<Result<ContentStatus[]>> GetAllowedTransitionsAsync(
        Guid contentId,
        CancellationToken ct = default)
    {
        var content = await _dbContext.Contents
            .FirstOrDefaultAsync(c => c.Id == contentId, ct);

        if (content is null)
            return Result<ContentStatus[]>.NotFound($"Content {contentId} not found.");

        var stateMachine = BuildStateMachine(content);
        var triggers = await stateMachine.GetPermittedTriggersAsync();
        var statuses = triggers
            .Select(t => MapTriggerToTargetStatus(content.Status, t))
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .ToArray();

        return Result<ContentStatus[]>.Success(statuses);
    }

    public async Task<bool> ShouldAutoApproveAsync(
        Guid contentId,
        CancellationToken ct = default)
    {
        var content = await _dbContext.Contents
            .FirstOrDefaultAsync(c => c.Id == contentId, ct);

        if (content is null) return false;

        return content.CapturedAutonomyLevel switch
        {
            AutonomyLevel.Autonomous => true,
            AutonomyLevel.SemiAuto when content.ParentContentId is not null =>
                await IsParentPublishedOrApproved(content.ParentContentId.Value, ct),
            _ => false,
        };
    }

    private async Task<bool> IsParentPublishedOrApproved(Guid parentId, CancellationToken ct)
    {
        var parent = await _dbContext.Contents
            .FirstOrDefaultAsync(c => c.Id == parentId, ct);

        return parent is not null &&
               parent.Status is ContentStatus.Published or ContentStatus.Approved;
    }

    internal static StateMachine<ContentStatus, ContentTrigger> BuildStateMachine(Content content)
    {
        var machine = new StateMachine<ContentStatus, ContentTrigger>(
            () => content.Status,
            s => content.TransitionTo(s));

        machine.Configure(ContentStatus.Draft)
            .Permit(ContentTrigger.Submit, ContentStatus.Review)
            .Permit(ContentTrigger.Archive, ContentStatus.Archived);

        machine.Configure(ContentStatus.Review)
            .Permit(ContentTrigger.Approve, ContentStatus.Approved)
            .Permit(ContentTrigger.Reject, ContentStatus.Draft)
            .Permit(ContentTrigger.Archive, ContentStatus.Archived);

        machine.Configure(ContentStatus.Approved)
            .Permit(ContentTrigger.Schedule, ContentStatus.Scheduled)
            .Permit(ContentTrigger.ReturnToDraft, ContentStatus.Draft)
            .Permit(ContentTrigger.Archive, ContentStatus.Archived);

        machine.Configure(ContentStatus.Scheduled)
            .Permit(ContentTrigger.Publish, ContentStatus.Publishing)
            .Permit(ContentTrigger.Unschedule, ContentStatus.Approved)
            .Permit(ContentTrigger.Archive, ContentStatus.Archived);

        machine.Configure(ContentStatus.Publishing)
            .Permit(ContentTrigger.Complete, ContentStatus.Published)
            .Permit(ContentTrigger.Fail, ContentStatus.Failed);

        machine.Configure(ContentStatus.Published)
            .Permit(ContentTrigger.Archive, ContentStatus.Archived);

        machine.Configure(ContentStatus.Failed)
            .Permit(ContentTrigger.ReturnToDraft, ContentStatus.Draft)
            .Permit(ContentTrigger.Archive, ContentStatus.Archived);

        machine.Configure(ContentStatus.Archived)
            .Permit(ContentTrigger.Unarchive, ContentStatus.Draft);

        return machine;
    }

    private static ContentTrigger? MapToTrigger(ContentStatus from, ContentStatus to) =>
        (from, to) switch
        {
            (ContentStatus.Draft, ContentStatus.Review) => ContentTrigger.Submit,
            (ContentStatus.Draft, ContentStatus.Archived) => ContentTrigger.Archive,
            (ContentStatus.Review, ContentStatus.Approved) => ContentTrigger.Approve,
            (ContentStatus.Review, ContentStatus.Draft) => ContentTrigger.Reject,
            (ContentStatus.Review, ContentStatus.Archived) => ContentTrigger.Archive,
            (ContentStatus.Approved, ContentStatus.Scheduled) => ContentTrigger.Schedule,
            (ContentStatus.Approved, ContentStatus.Draft) => ContentTrigger.ReturnToDraft,
            (ContentStatus.Approved, ContentStatus.Archived) => ContentTrigger.Archive,
            (ContentStatus.Scheduled, ContentStatus.Publishing) => ContentTrigger.Publish,
            (ContentStatus.Scheduled, ContentStatus.Approved) => ContentTrigger.Unschedule,
            (ContentStatus.Scheduled, ContentStatus.Archived) => ContentTrigger.Archive,
            (ContentStatus.Publishing, ContentStatus.Published) => ContentTrigger.Complete,
            (ContentStatus.Publishing, ContentStatus.Failed) => ContentTrigger.Fail,
            (ContentStatus.Published, ContentStatus.Archived) => ContentTrigger.Archive,
            (ContentStatus.Failed, ContentStatus.Draft) => ContentTrigger.ReturnToDraft,
            (ContentStatus.Failed, ContentStatus.Archived) => ContentTrigger.Archive,
            (ContentStatus.Archived, ContentStatus.Draft) => ContentTrigger.Unarchive,
            _ => null,
        };

    private static ContentStatus? MapTriggerToTargetStatus(ContentStatus from, ContentTrigger trigger) =>
        (from, trigger) switch
        {
            (ContentStatus.Draft, ContentTrigger.Submit) => ContentStatus.Review,
            (ContentStatus.Draft, ContentTrigger.Archive) => ContentStatus.Archived,
            (ContentStatus.Review, ContentTrigger.Approve) => ContentStatus.Approved,
            (ContentStatus.Review, ContentTrigger.Reject) => ContentStatus.Draft,
            (ContentStatus.Review, ContentTrigger.Archive) => ContentStatus.Archived,
            (ContentStatus.Approved, ContentTrigger.Schedule) => ContentStatus.Scheduled,
            (ContentStatus.Approved, ContentTrigger.ReturnToDraft) => ContentStatus.Draft,
            (ContentStatus.Approved, ContentTrigger.Archive) => ContentStatus.Archived,
            (ContentStatus.Scheduled, ContentTrigger.Publish) => ContentStatus.Publishing,
            (ContentStatus.Scheduled, ContentTrigger.Unschedule) => ContentStatus.Approved,
            (ContentStatus.Scheduled, ContentTrigger.Archive) => ContentStatus.Archived,
            (ContentStatus.Publishing, ContentTrigger.Complete) => ContentStatus.Published,
            (ContentStatus.Publishing, ContentTrigger.Fail) => ContentStatus.Failed,
            (ContentStatus.Published, ContentTrigger.Archive) => ContentStatus.Archived,
            (ContentStatus.Failed, ContentTrigger.ReturnToDraft) => ContentStatus.Draft,
            (ContentStatus.Failed, ContentTrigger.Archive) => ContentStatus.Archived,
            (ContentStatus.Archived, ContentTrigger.Unarchive) => ContentStatus.Draft,
            _ => null,
        };

    private static void RaisePostCommitEvents(Content content, ContentStatus previousStatus, string? reason)
    {
        switch ((previousStatus, content.Status))
        {
            case (ContentStatus.Review, ContentStatus.Approved):
                content.ClearDomainEvents();
                content.AddDomainEvent(new ContentApprovedEvent(content.Id));
                break;
            case (ContentStatus.Review, ContentStatus.Draft):
                content.ClearDomainEvents();
                content.AddDomainEvent(new ContentRejectedEvent(content.Id, reason ?? string.Empty));
                break;
            case (ContentStatus.Approved, ContentStatus.Scheduled):
                content.ClearDomainEvents();
                content.AddDomainEvent(new ContentScheduledEvent(content.Id, content.ScheduledAt ?? DateTimeOffset.UtcNow));
                break;
            case (ContentStatus.Publishing, ContentStatus.Published):
                content.ClearDomainEvents();
                content.AddDomainEvent(new ContentPublishedEvent(content.Id, content.TargetPlatforms));
                break;
        }
    }
}
