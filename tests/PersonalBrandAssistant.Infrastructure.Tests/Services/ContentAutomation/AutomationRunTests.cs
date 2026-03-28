using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services.ContentAutomation;

public class AutomationRunTests
{
    [Fact]
    public void Create_SetsRunningStatus_AndTriggeredAt()
    {
        var run = AutomationRun.Create();

        Assert.Equal(AutomationRunStatus.Running, run.Status);
        Assert.True(run.TriggeredAt <= DateTimeOffset.UtcNow);
        Assert.True(run.TriggeredAt > DateTimeOffset.UtcNow.AddSeconds(-5));
        Assert.Null(run.CompletedAt);
    }

    [Fact]
    public void Complete_SetsCompletedStatus_AndDuration()
    {
        var run = AutomationRun.Create();

        run.Complete(5000);

        Assert.Equal(AutomationRunStatus.Completed, run.Status);
        Assert.NotNull(run.CompletedAt);
        Assert.Equal(5000, run.DurationMs);
    }

    [Fact]
    public void Fail_SetsFailedStatus_WithErrorDetails()
    {
        var run = AutomationRun.Create();

        run.Fail("ComfyUI unreachable", 1500);

        Assert.Equal(AutomationRunStatus.Failed, run.Status);
        Assert.Equal("ComfyUI unreachable", run.ErrorDetails);
        Assert.NotNull(run.CompletedAt);
        Assert.Equal(1500, run.DurationMs);
    }

    [Fact]
    public void PartialFailure_SetsPartialFailureStatus()
    {
        var run = AutomationRun.Create();

        run.PartialFailure("LinkedIn failed, Twitter succeeded", 3000);

        Assert.Equal(AutomationRunStatus.PartialFailure, run.Status);
        Assert.Equal("LinkedIn failed, Twitter succeeded", run.ErrorDetails);
        Assert.NotNull(run.CompletedAt);
    }
}
