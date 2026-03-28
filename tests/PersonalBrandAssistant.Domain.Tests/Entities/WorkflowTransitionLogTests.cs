using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class WorkflowTransitionLogTests
{
    [Fact]
    public void Create_SetsTimestampToNonDefault()
    {
        var log = WorkflowTransitionLog.Create(
            Guid.NewGuid(), ContentStatus.Draft, ContentStatus.Review, ActorType.User);

        Assert.NotEqual(default, log.Timestamp);
    }

    [Fact]
    public void Create_PopulatesAllRequiredFields()
    {
        var contentId = Guid.NewGuid();
        var log = WorkflowTransitionLog.Create(
            contentId, ContentStatus.Review, ContentStatus.Approved, ActorType.System,
            actorId: "WorkflowEngine", reason: "Auto-approved");

        Assert.Equal(contentId, log.ContentId);
        Assert.Equal(ContentStatus.Review, log.FromStatus);
        Assert.Equal(ContentStatus.Approved, log.ToStatus);
        Assert.Equal(ActorType.System, log.ActorType);
        Assert.Equal("WorkflowEngine", log.ActorId);
        Assert.Equal("Auto-approved", log.Reason);
    }

    [Fact]
    public void Create_ReasonAndActorIdAreOptional()
    {
        var log = WorkflowTransitionLog.Create(
            Guid.NewGuid(), ContentStatus.Draft, ContentStatus.Review, ActorType.Agent);

        Assert.Null(log.Reason);
        Assert.Null(log.ActorId);
    }
}
