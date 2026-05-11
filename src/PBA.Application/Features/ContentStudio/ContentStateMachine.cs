using PBA.Domain.Enums;
using Stateless;

namespace PBA.Application.Features.ContentStudio;

public static class ContentStateMachine
{
    public static StateMachine<ContentStatus, ContentTrigger> Create(Domain.Entities.Content content)
    {
        var machine = new StateMachine<ContentStatus, ContentTrigger>(
            () => content.Status,
            s => content.Status = s);

        machine.Configure(ContentStatus.Idea)
            .Permit(ContentTrigger.StartDraft, ContentStatus.Draft);

        machine.Configure(ContentStatus.Draft)
            .OnEntryAsync(_ =>
            {
                content.ScheduledAt = null;
                content.HangfireJobId = null;
                content.UpdatedAt = DateTimeOffset.UtcNow;
                return Task.CompletedTask;
            })
            .PermitIf(ContentTrigger.SubmitForReview, ContentStatus.Review,
                () => !string.IsNullOrWhiteSpace(content.Body))
            .PermitIf(ContentTrigger.Approve, ContentStatus.Approved,
                () => !string.IsNullOrWhiteSpace(content.Body))
            .Permit(ContentTrigger.Archive, ContentStatus.Archived);

        machine.Configure(ContentStatus.Review)
            .Permit(ContentTrigger.Approve, ContentStatus.Approved)
            .Permit(ContentTrigger.RequestChanges, ContentStatus.Draft)
            .Permit(ContentTrigger.Archive, ContentStatus.Archived);

        machine.Configure(ContentStatus.Approved)
            .PermitIf(ContentTrigger.Schedule, ContentStatus.Scheduled,
                () => content.ScheduledAt.HasValue && content.ScheduledAt > DateTimeOffset.UtcNow)
            .Permit(ContentTrigger.PublishNow, ContentStatus.Published);

        machine.Configure(ContentStatus.Scheduled)
            .OnEntryAsync(_ =>
            {
                content.UpdatedAt = DateTimeOffset.UtcNow;
                return Task.CompletedTask;
            })
            .Permit(ContentTrigger.Publish, ContentStatus.Published)
            .Permit(ContentTrigger.Unschedule, ContentStatus.Approved);

        machine.Configure(ContentStatus.Published)
            .OnEntryAsync(_ =>
            {
                content.PublishedAt = DateTimeOffset.UtcNow;
                content.UpdatedAt = DateTimeOffset.UtcNow;
                return Task.CompletedTask;
            })
            .Permit(ContentTrigger.Archive, ContentStatus.Archived)
            .Permit(ContentTrigger.Unpublish, ContentStatus.Draft);

        machine.Configure(ContentStatus.Archived)
            .OnEntryAsync(_ =>
            {
                content.UpdatedAt = DateTimeOffset.UtcNow;
                return Task.CompletedTask;
            })
            .Permit(ContentTrigger.Restore, ContentStatus.Draft);

        return machine;
    }
}
