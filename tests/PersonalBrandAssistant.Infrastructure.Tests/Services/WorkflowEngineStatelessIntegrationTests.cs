using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services;

public class WorkflowEngineStatelessIntegrationTests
{
    [Fact]
    public void StateMachine_ConfiguresAllExpectedTriggers()
    {
        var content = Content.Create(ContentType.BlogPost, "Test body");
        var machine = WorkflowEngine.BuildStateMachine(content);

#pragma warning disable CS0618
        var triggers = machine.GetPermittedTriggers();
#pragma warning restore CS0618

        Assert.Contains(ContentTrigger.Submit, triggers);
        Assert.Contains(ContentTrigger.Archive, triggers);
    }

    [Fact]
    public void StateMachine_GuardClauses_AllowsTransitions()
    {
        var content = Content.Create(ContentType.BlogPost, "Test body",
            capturedAutonomyLevel: AutonomyLevel.Autonomous);
        var machine = WorkflowEngine.BuildStateMachine(content);

        // Draft -> Review should be allowed
        Assert.True(machine.CanFire(ContentTrigger.Submit));
        Assert.False(machine.CanFire(ContentTrigger.Approve)); // Not in Review state
    }

    [Fact]
    public void StateMachine_ExternalStateStorage_ReadsAndWritesStatus()
    {
        var content = Content.Create(ContentType.BlogPost, "Test body");
        var machine = WorkflowEngine.BuildStateMachine(content);

        Assert.Equal(ContentStatus.Draft, machine.State);

        machine.Fire(ContentTrigger.Submit);

        Assert.Equal(ContentStatus.Review, machine.State);
        Assert.Equal(ContentStatus.Review, content.Status);
    }

    [Fact]
    public void StateMachine_InvalidTrigger_ThrowsInvalidOperation()
    {
        var content = Content.Create(ContentType.BlogPost, "Test body");
        var machine = WorkflowEngine.BuildStateMachine(content);

        var ex = Assert.Throws<InvalidOperationException>(() => machine.Fire(ContentTrigger.Approve));
        Assert.Contains("Draft", ex.Message);
    }
}
