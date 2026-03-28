using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Domain.Tests.Entities;

public class AgentExecutionTests
{
    private static AgentExecution CreatePending() =>
        AgentExecution.Create(AgentCapabilityType.Writer, ModelTier.Standard);

    private static AgentExecution CreateRunning()
    {
        var execution = CreatePending();
        execution.MarkRunning();
        return execution;
    }

    [Fact]
    public void Create_SetsIdAsNonEmptyGuid()
    {
        var execution = CreatePending();
        Assert.NotEqual(Guid.Empty, execution.Id);
    }

    [Fact]
    public void Create_SetsStatusToPending()
    {
        var execution = CreatePending();
        Assert.Equal(AgentExecutionStatus.Pending, execution.Status);
    }

    [Fact]
    public void Create_SetsStartedAtToCurrentTime()
    {
        var before = DateTimeOffset.UtcNow;
        var execution = CreatePending();
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(execution.StartedAt, before, after);
    }

    [Fact]
    public void Create_SetsAgentTypeFromParameter()
    {
        var execution = AgentExecution.Create(AgentCapabilityType.Social, ModelTier.Fast);
        Assert.Equal(AgentCapabilityType.Social, execution.AgentType);
    }

    [Fact]
    public void Create_SetsModelUsedFromParameter()
    {
        var execution = AgentExecution.Create(AgentCapabilityType.Writer, ModelTier.Advanced);
        Assert.Equal(ModelTier.Advanced, execution.ModelUsed);
    }

    [Fact]
    public void Create_WithNullContentId_IsValid()
    {
        var execution = AgentExecution.Create(AgentCapabilityType.Analytics, ModelTier.Fast);
        Assert.Null(execution.ContentId);
    }

    [Fact]
    public void Create_WithContentId_StoresCorrectly()
    {
        var contentId = Guid.NewGuid();
        var execution = AgentExecution.Create(AgentCapabilityType.Writer, ModelTier.Standard, contentId);
        Assert.Equal(contentId, execution.ContentId);
    }

    [Fact]
    public void MarkRunning_SetsStatusToRunning()
    {
        var execution = CreatePending();
        execution.MarkRunning();
        Assert.Equal(AgentExecutionStatus.Running, execution.Status);
    }

    [Fact]
    public void MarkRunning_ThrowsWhenStatusIsNotPending()
    {
        var execution = CreateRunning();
        Assert.Throws<InvalidOperationException>(() => execution.MarkRunning());
    }

    [Fact]
    public void Complete_SetsStatusToCompleted()
    {
        var execution = CreateRunning();
        execution.Complete();
        Assert.Equal(AgentExecutionStatus.Completed, execution.Status);
    }

    [Fact]
    public void Complete_SetsCompletedAtAndDuration()
    {
        var execution = CreateRunning();
        execution.Complete();

        Assert.NotNull(execution.CompletedAt);
        Assert.NotNull(execution.Duration);
        Assert.True(execution.Duration.Value >= TimeSpan.Zero);
    }

    [Fact]
    public void Complete_WithOutputSummary_StoresIt()
    {
        var execution = CreateRunning();
        execution.Complete("Generated blog post about AI");
        Assert.Equal("Generated blog post about AI", execution.OutputSummary);
    }

    [Fact]
    public void Complete_ThrowsWhenStatusIsNotRunning()
    {
        var execution = CreatePending();
        Assert.Throws<InvalidOperationException>(() => execution.Complete());
    }

    [Fact]
    public void Fail_SetsStatusToFailed()
    {
        var execution = CreateRunning();
        execution.Fail("API rate limit exceeded");
        Assert.Equal(AgentExecutionStatus.Failed, execution.Status);
    }

    [Fact]
    public void Fail_SetsErrorAndCompletedAt()
    {
        var execution = CreateRunning();
        execution.Fail("Timeout");

        Assert.Equal("Timeout", execution.Error);
        Assert.NotNull(execution.CompletedAt);
        Assert.NotNull(execution.Duration);
    }

    [Fact]
    public void Fail_FromPending_Succeeds()
    {
        var execution = CreatePending();
        execution.Fail("Budget exceeded before start");
        Assert.Equal(AgentExecutionStatus.Failed, execution.Status);
    }

    [Fact]
    public void Fail_ThrowsWhenAlreadyCompleted()
    {
        var execution = CreateRunning();
        execution.Complete();
        Assert.Throws<InvalidOperationException>(() => execution.Fail("Too late"));
    }

    [Fact]
    public void Cancel_SetsStatusToCancelled()
    {
        var execution = CreateRunning();
        execution.Cancel();
        Assert.Equal(AgentExecutionStatus.Cancelled, execution.Status);
    }

    [Fact]
    public void Cancel_SetsCompletedAtAndDuration()
    {
        var execution = CreateRunning();
        execution.Cancel();

        Assert.NotNull(execution.CompletedAt);
        Assert.NotNull(execution.Duration);
    }

    [Fact]
    public void Cancel_FromPending_Succeeds()
    {
        var execution = CreatePending();
        execution.Cancel();
        Assert.Equal(AgentExecutionStatus.Cancelled, execution.Status);
    }

    [Fact]
    public void Cancel_ThrowsWhenAlreadyCompleted()
    {
        var execution = CreateRunning();
        execution.Complete();
        Assert.Throws<InvalidOperationException>(() => execution.Cancel());
    }

    [Fact]
    public void Cancel_ThrowsWhenAlreadyFailed()
    {
        var execution = CreateRunning();
        execution.Fail("Error");
        Assert.Throws<InvalidOperationException>(() => execution.Cancel());
    }

    [Fact]
    public void RecordUsage_SetsAllTokenAndCostFields()
    {
        var execution = CreateRunning();
        execution.RecordUsage("claude-sonnet-4-5-20250929", 1000, 500, 200, 100, 0.0105m);

        Assert.Equal("claude-sonnet-4-5-20250929", execution.ModelId);
        Assert.Equal(1000, execution.InputTokens);
        Assert.Equal(500, execution.OutputTokens);
        Assert.Equal(200, execution.CacheReadTokens);
        Assert.Equal(100, execution.CacheCreationTokens);
        Assert.Equal(0.0105m, execution.Cost);
    }

    [Fact]
    public void RecordUsage_CanBeCalledOnRunningExecution()
    {
        var execution = CreateRunning();
        var exception = Record.Exception(() =>
            execution.RecordUsage("claude-haiku-4-5", 100, 50, 0, 0, 0.001m));
        Assert.Null(exception);
    }

    [Fact]
    public void RecordUsage_ThrowsOnNullModelId()
    {
        var execution = CreateRunning();
        Assert.Throws<ArgumentNullException>(() =>
            execution.RecordUsage(null!, 100, 50, 0, 0, 0.001m));
    }

    [Fact]
    public void RecordUsage_ThrowsOnNegativeTokens()
    {
        var execution = CreateRunning();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            execution.RecordUsage("claude-haiku-4-5", -1, 50, 0, 0, 0.001m));
    }

    [Fact]
    public void RecordUsage_ThrowsOnNegativeCost()
    {
        var execution = CreateRunning();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            execution.RecordUsage("claude-haiku-4-5", 100, 50, 0, 0, -1m));
    }

    [Fact]
    public void Fail_TruncatesErrorLongerThan4000Chars()
    {
        var execution = CreateRunning();
        var longError = new string('e', 5000);
        execution.Fail(longError);
        Assert.Equal(4000, execution.Error!.Length);
    }

    [Theory]
    [InlineData(AgentExecutionStatus.Pending, AgentExecutionStatus.Running, true)]
    [InlineData(AgentExecutionStatus.Pending, AgentExecutionStatus.Failed, true)]
    [InlineData(AgentExecutionStatus.Pending, AgentExecutionStatus.Cancelled, true)]
    [InlineData(AgentExecutionStatus.Running, AgentExecutionStatus.Completed, true)]
    [InlineData(AgentExecutionStatus.Running, AgentExecutionStatus.Failed, true)]
    [InlineData(AgentExecutionStatus.Running, AgentExecutionStatus.Cancelled, true)]
    [InlineData(AgentExecutionStatus.Completed, AgentExecutionStatus.Failed, false)]
    [InlineData(AgentExecutionStatus.Completed, AgentExecutionStatus.Cancelled, false)]
    [InlineData(AgentExecutionStatus.Failed, AgentExecutionStatus.Running, false)]
    [InlineData(AgentExecutionStatus.Cancelled, AgentExecutionStatus.Running, false)]
    public void StatusTransitions_OnlyValidOnesAllowed(
        AgentExecutionStatus from, AgentExecutionStatus to, bool shouldSucceed)
    {
        var execution = CreatePending();
        TransitionToState(execution, from);

        if (shouldSucceed)
        {
            TransitionTo(execution, to);
            Assert.Equal(to, execution.Status);
        }
        else
        {
            Assert.ThrowsAny<InvalidOperationException>(() => TransitionTo(execution, to));
        }
    }

    private static void TransitionToState(AgentExecution execution, AgentExecutionStatus target)
    {
        if (execution.Status == target) return;

        switch (target)
        {
            case AgentExecutionStatus.Pending:
                break;
            case AgentExecutionStatus.Running:
                execution.MarkRunning();
                break;
            case AgentExecutionStatus.Completed:
                execution.MarkRunning();
                execution.Complete();
                break;
            case AgentExecutionStatus.Failed:
                execution.MarkRunning();
                execution.Fail("Test failure");
                break;
            case AgentExecutionStatus.Cancelled:
                execution.Cancel();
                break;
        }
    }

    private static void TransitionTo(AgentExecution execution, AgentExecutionStatus target)
    {
        switch (target)
        {
            case AgentExecutionStatus.Running:
                execution.MarkRunning();
                break;
            case AgentExecutionStatus.Completed:
                execution.Complete();
                break;
            case AgentExecutionStatus.Failed:
                execution.Fail("Test error");
                break;
            case AgentExecutionStatus.Cancelled:
                execution.Cancel();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }
}
