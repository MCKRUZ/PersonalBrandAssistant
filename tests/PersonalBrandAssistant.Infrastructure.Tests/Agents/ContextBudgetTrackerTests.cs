using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Models.Skills;
using PersonalBrandAssistant.Infrastructure.Agents;

namespace PersonalBrandAssistant.Infrastructure.Tests.Agents;

public class ContextBudgetTrackerTests
{
    private static ContextBudgetTracker CreateTracker(
        int nudge = 80_000, int stop = 180_000, int hard = 200_000)
    {
        var options = new ContextBudgetOptions
        {
            NudgeThreshold = nudge,
            StopThreshold = stop,
            HardMaxTokens = hard
        };
        return new ContextBudgetTracker(Options.Create(options));
    }

    // ── RecordTokens guards ───────────────────────────────────────────────

    [Fact]
    public void RecordTokens_NegativeTokens_Throws()
    {
        var tracker = CreateTracker();
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.RecordTokens("system", -1));
    }

    [Fact]
    public void RecordTokens_NullComponent_Throws()
    {
        var tracker = CreateTracker();
        Assert.Throws<ArgumentNullException>(() => tracker.RecordTokens(null!, 100));
    }

    [Fact]
    public void RecordTokens_EmptyComponent_Throws()
    {
        var tracker = CreateTracker();
        Assert.Throws<ArgumentException>(() => tracker.RecordTokens("", 100));
    }

    // ── RecordTokens accumulation ─────────────────────────────────────────

    [Fact]
    public void RecordTokens_SingleComponent_TotalReflectsCount()
    {
        var tracker = CreateTracker();
        tracker.RecordTokens("system", 500);
        Assert.Equal(500, tracker.TotalTokens);
    }

    [Fact]
    public void RecordTokens_MultipleComponents_TotalSumsAll()
    {
        var tracker = CreateTracker();
        tracker.RecordTokens("system", 1_000);
        tracker.RecordTokens("user", 2_000);
        tracker.RecordTokens("assistant", 3_000);
        Assert.Equal(6_000, tracker.TotalTokens);
    }

    [Fact]
    public void RecordTokens_SameComponentTwice_Accumulates()
    {
        var tracker = CreateTracker();
        tracker.RecordTokens("system", 300);
        tracker.RecordTokens("system", 700);
        Assert.Equal(1_000, tracker.TotalTokens);
    }

    // ── AssessContinuation threshold boundaries ───────────────────────────

    [Fact]
    public void AssessContinuation_Below80k_ReturnsContinue()
    {
        var tracker = CreateTracker(nudge: 80_000, stop: 180_000, hard: 200_000);
        tracker.RecordTokens("ctx", 79_999);
        var result = tracker.AssessContinuation();
        Assert.Equal(BudgetDecision.Continue, result.Decision);
    }

    [Fact]
    public void AssessContinuation_At80k_ReturnsNudge()
    {
        var tracker = CreateTracker(nudge: 80_000, stop: 180_000, hard: 200_000);
        tracker.RecordTokens("ctx", 80_000);
        var result = tracker.AssessContinuation();
        Assert.Equal(BudgetDecision.Nudge, result.Decision);
    }

    [Fact]
    public void AssessContinuation_Between80kAnd180k_ReturnsNudge()
    {
        var tracker = CreateTracker(nudge: 80_000, stop: 180_000, hard: 200_000);
        tracker.RecordTokens("ctx", 120_000);
        var result = tracker.AssessContinuation();
        Assert.Equal(BudgetDecision.Nudge, result.Decision);
    }

    [Fact]
    public void AssessContinuation_At180k_ReturnsStop()
    {
        var tracker = CreateTracker(nudge: 80_000, stop: 180_000, hard: 200_000);
        tracker.RecordTokens("ctx", 180_000);
        var result = tracker.AssessContinuation();
        Assert.Equal(BudgetDecision.Stop, result.Decision);
    }

    [Fact]
    public void AssessContinuation_Above180k_ReturnsStop()
    {
        var tracker = CreateTracker(nudge: 80_000, stop: 180_000, hard: 200_000);
        tracker.RecordTokens("ctx", 195_000);
        var result = tracker.AssessContinuation();
        Assert.Equal(BudgetDecision.Stop, result.Decision);
    }

    // ── Configurable thresholds ───────────────────────────────────────────

    [Fact]
    public void AssessContinuation_CustomNudgeThreshold_HonorsConfig()
    {
        var tracker = CreateTracker(nudge: 50_000, stop: 180_000, hard: 200_000);
        tracker.RecordTokens("ctx", 50_000);
        var result = tracker.AssessContinuation();
        Assert.Equal(BudgetDecision.Nudge, result.Decision);
    }

    [Fact]
    public void AssessContinuation_CustomStopThreshold_HonorsConfig()
    {
        var tracker = CreateTracker(nudge: 80_000, stop: 100_000, hard: 200_000);
        tracker.RecordTokens("ctx", 100_000);
        var result = tracker.AssessContinuation();
        Assert.Equal(BudgetDecision.Stop, result.Decision);
    }

    // ── BudgetAssessment field correctness ────────────────────────────────

    [Fact]
    public void AssessContinuation_Continue_TokensUsedAndRemainingCorrect()
    {
        var tracker = CreateTracker(nudge: 80_000, stop: 180_000, hard: 200_000);
        tracker.RecordTokens("ctx", 10_000);
        var result = tracker.AssessContinuation();
        Assert.Equal(10_000, result.TokensUsed);
        Assert.Equal(190_000, result.TokensRemaining);
    }

    [Fact]
    public void AssessContinuation_Nudge_ReasonIsNonEmpty()
    {
        var tracker = CreateTracker(nudge: 80_000, stop: 180_000, hard: 200_000);
        tracker.RecordTokens("ctx", 80_000);
        var result = tracker.AssessContinuation();
        Assert.NotEmpty(result.Reason);
    }

    [Fact]
    public void AssessContinuation_Stop_TokensRemainingIsNegativeOrZero()
    {
        var tracker = CreateTracker(nudge: 80_000, stop: 180_000, hard: 200_000);
        tracker.RecordTokens("ctx", 210_000);
        var result = tracker.AssessContinuation();
        Assert.True(result.TokensRemaining <= 0);
    }
}
