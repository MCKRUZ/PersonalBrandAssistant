using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services;

public class WorkflowEngineStateMachineParityTests
{
    [Theory]
    [MemberData(nameof(AllContentStatuses))]
    public void StateMachine_And_DomainAllowedTransitions_AreInSync(ContentStatus status)
    {
        // Get domain-level allowed transitions
        var domainTransitions = Content.GetAllowedTransitions(status)
            .OrderBy(s => s)
            .ToArray();

        // Build a content in the target state and get Stateless-allowed transitions
        var content = CreateContentInState(status);
        var stateMachine = WorkflowEngine.BuildStateMachine(content);

#pragma warning disable CS0618 // using sync for test simplicity
        var triggers = stateMachine.GetPermittedTriggers();
#pragma warning restore CS0618

        var statelessTransitions = triggers
            .Select(t => MapTriggerToStatus(status, t))
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .OrderBy(s => s)
            .ToArray();

        Assert.Equal(domainTransitions, statelessTransitions);
    }

    public static IEnumerable<object[]> AllContentStatuses() =>
        Enum.GetValues<ContentStatus>().Select(s => new object[] { s });

    private static Content CreateContentInState(ContentStatus target)
    {
        var content = Content.Create(ContentType.BlogPost, "Test body");
        if (content.Status == target) return content;

        var path = target switch
        {
            ContentStatus.Draft => Array.Empty<ContentStatus>(),
            ContentStatus.Review => [ContentStatus.Review],
            ContentStatus.Approved => [ContentStatus.Review, ContentStatus.Approved],
            ContentStatus.Scheduled => [ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled],
            ContentStatus.Publishing => [ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled, ContentStatus.Publishing],
            ContentStatus.Published => [ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled, ContentStatus.Publishing, ContentStatus.Published],
            ContentStatus.Failed => [ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled, ContentStatus.Publishing, ContentStatus.Failed],
            ContentStatus.Archived => [ContentStatus.Review, ContentStatus.Approved, ContentStatus.Scheduled, ContentStatus.Publishing, ContentStatus.Published, ContentStatus.Archived],
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };

        foreach (var step in path)
            content.TransitionTo(step);

        return content;
    }

    private static ContentStatus? MapTriggerToStatus(ContentStatus from, ContentTrigger trigger) =>
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
            (ContentStatus.Publishing, ContentTrigger.Requeue) => ContentStatus.Scheduled,
            (ContentStatus.Published, ContentTrigger.Archive) => ContentStatus.Archived,
            (ContentStatus.Failed, ContentTrigger.ReturnToDraft) => ContentStatus.Draft,
            (ContentStatus.Failed, ContentTrigger.Archive) => ContentStatus.Archived,
            (ContentStatus.Failed, ContentTrigger.Retry) => ContentStatus.Publishing,
            (ContentStatus.Archived, ContentTrigger.Unarchive) => ContentStatus.Draft,
            _ => null,
        };
}
