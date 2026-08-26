using Microsoft.Extensions.DependencyInjection;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Enums;
using PBA.Infrastructure.Publishing;
using Xunit;

namespace PBA.Infrastructure.Tests.Publishing;

/// <summary>
/// The one place that answers "when does PBA hand the clip over", which is a different question
/// from when the post appears. Getting it wrong is expensive in one direction: treating a platform
/// as if it schedules publishes the clip immediately, days early, on a public account.
/// </summary>
public class HandoverPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GoLive = Now.AddDays(3);

    private readonly Mock<IPlatformCapabilityReader> _capabilities = new();
    private readonly ServiceCollection _services = new();

    private HandoverPlanner CreatePlanner() =>
        new(_capabilities.Object, _services.BuildServiceProvider(), new FixedClock(Now));

    private void Capabilities(
        Platform platform, bool schedules, bool paced,
        TimeSpan? horizon = null, bool fetchesMediaAtPostTime = false)
    {
        _capabilities.Setup(c => c.SupportsScheduling(platform)).Returns(schedules);
        _capabilities.Setup(c => c.RequiresPacedHandover(platform)).Returns(paced);
        _capabilities.Setup(c => c.SchedulingHorizon(platform)).Returns(horizon);
        _capabilities.Setup(c => c.FetchesHostedMediaAtPostTime(platform)).Returns(fetchesMediaAtPostTime);
    }

    private void RegisterPacer(Platform platform, DateTimeOffset? slot)
    {
        var pacer = new Mock<IUploadPacer>();
        pacer.Setup(p => p.NextHandoverSlotAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(slot);
        _services.AddKeyedSingleton(platform, pacer.Object);
    }

    // Instagram's shape: Meta's Content Publishing API has no scheduling concept, so PBA keeps the
    // post AND the video until the moment itself.
    [Fact]
    public async Task Plan_ForAPlatformThatCannotSchedule_HoldsUntilTheGoLiveMoment()
    {
        Capabilities(Platform.Instagram, schedules: false, paced: false);

        var plan = await CreatePlanner().PlanAsync(Platform.Instagram, GoLive, CancellationToken.None);

        Assert.True(plan.PbaHolds);
        Assert.Equal(GoLive, plan.HandoverAt);
        Assert.Null(plan.Refusal);
    }

    // TikTok's shape: Buffer takes it now and fires it later, so PBA keeps nothing.
    [Fact]
    public async Task Plan_ForAPlatformThatSchedules_HandsOverImmediately()
    {
        Capabilities(Platform.TikTok, schedules: true, paced: false);

        var plan = await CreatePlanner().PlanAsync(Platform.TikTok, GoLive, CancellationToken.None);

        Assert.False(plan.PbaHolds);
        Assert.Null(plan.HandoverAt);
    }

    // A horizon only bites when the go-live is beyond it. Inside the window Buffer will take the
    // post today, and holding it at PBA for no reason would put the clip's storage window at risk
    // for nothing.
    [Fact]
    public async Task Plan_WhenTheSlotIsInsideTheHorizon_HandsOverImmediately()
    {
        Capabilities(Platform.TikTok, schedules: true, paced: false, horizon: TimeSpan.FromDays(7));

        var plan = await CreatePlanner().PlanAsync(Platform.TikTok, Now.AddDays(3), CancellationToken.None);

        Assert.False(plan.PbaHolds);
        Assert.Null(plan.HandoverAt);
    }

    // The reported bug: a slot two weeks out used to be pushed at Buffer today and refused, leaving
    // a human to come back and run the hand-off by hand on the right day. PBA holds it instead.
    [Fact]
    public async Task Plan_WhenTheSlotIsBeyondTheHorizon_HoldsUntilItComesInsideTheWindow()
    {
        var horizon = TimeSpan.FromDays(7);
        var goLive = Now.AddDays(14);
        Capabilities(Platform.TikTok, schedules: true, paced: false, horizon: horizon);

        var plan = await CreatePlanner().PlanAsync(Platform.TikTok, goLive, CancellationToken.None);

        Assert.True(plan.PbaHolds);
        Assert.NotNull(plan.HandoverAt);

        // Strictly inside the window, not on its edge: the connector refuses a go-live further out
        // than the horizon, so a hand-off aimed exactly at the boundary is one clock-skew away from
        // being rejected. And still in the future, or holding it would have been pointless.
        Assert.True(goLive - plan.HandoverAt!.Value < horizon);
        Assert.True(plan.HandoverAt!.Value > Now);
    }

    // A platform could one day both ration uploads and refuse distant schedules. The pacer says the
    // earliest PBA may hand over; the horizon says the latest it must wait until. The answer is the
    // later of the two — obeying one while breaking the other is not a hand-off that works.
    [Fact]
    public async Task Plan_WhenPacedAndHorizonLimited_TakesTheLaterOfTheTwo()
    {
        var horizon = TimeSpan.FromDays(7);
        var goLive = Now.AddDays(14);
        Capabilities(Platform.YouTube, schedules: true, paced: true, horizon: horizon);
        RegisterPacer(Platform.YouTube, Now.AddDays(1));

        var plan = await CreatePlanner().PlanAsync(Platform.YouTube, goLive, CancellationToken.None);

        Assert.True(plan.PbaHolds);
        // The pacer had capacity tomorrow, but tomorrow is still 13 days ahead of the go-live and
        // the platform would refuse it. The horizon wins.
        Assert.True(plan.HandoverAt!.Value > Now.AddDays(1));
        Assert.True(goLive - plan.HandoverAt!.Value < horizon);
    }

    [Fact]
    public async Task Plan_WhenThePacerIsBusierThanTheHorizonRequires_TakesThePacerSlot()
    {
        var pacedSlot = Now.AddDays(10);
        Capabilities(Platform.YouTube, schedules: true, paced: true, horizon: TimeSpan.FromDays(7));
        RegisterPacer(Platform.YouTube, pacedSlot);

        var plan = await CreatePlanner().PlanAsync(Platform.YouTube, Now.AddDays(14), CancellationToken.None);

        Assert.True(plan.PbaHolds);
        Assert.Equal(pacedSlot, plan.HandoverAt);
    }

    // Every connector but Buffer declares no horizon, and none of them may change behaviour.
    [Fact]
    public async Task Plan_WithNoHorizonDeclared_IsUnchangedHoweverFarOutTheSlotIs()
    {
        Capabilities(Platform.LinkedIn, schedules: true, paced: false, horizon: null);

        var plan = await CreatePlanner().PlanAsync(Platform.LinkedIn, Now.AddDays(60), CancellationToken.None);

        Assert.False(plan.PbaHolds);
        Assert.Null(plan.HandoverAt);
    }

    // The staged clip's deadline is NOT the hand-over for Buffer: it takes the link and follows it
    // when the post fires. Reporting the hand-over instead would let a clip be staged that is reaped
    // in the gap, and nothing fails until Buffer fetches a dead URL days later.
    [Fact]
    public async Task Plan_ForAPlatformThatFetchesMediaAtPostTime_KeepsTheMediaAliveUntilGoLive()
    {
        var goLive = Now.AddDays(14);
        Capabilities(Platform.TikTok, schedules: true, paced: false,
            horizon: TimeSpan.FromDays(7), fetchesMediaAtPostTime: true);

        var plan = await CreatePlanner().PlanAsync(Platform.TikTok, goLive, CancellationToken.None);

        Assert.True(plan.PbaHolds);
        Assert.Equal(goLive, plan.MediaNeededUntil);
        Assert.True(plan.MediaNeededUntil > plan.HandoverAt);
    }

    // YouTube's shape by contrast: the bytes go INTO YouTube at hand-over and it keeps them from
    // there, so the staged object is dead weight the moment the upload completes.
    [Fact]
    public async Task Plan_ForAPlatformThatTakesTheBytes_NeedsTheMediaOnlyUntilTheHandover()
    {
        Capabilities(Platform.YouTube, schedules: true, paced: true);
        RegisterPacer(Platform.YouTube, Now.AddDays(1));

        var plan = await CreatePlanner().PlanAsync(Platform.YouTube, GoLive, CancellationToken.None);

        Assert.Equal(plan.HandoverAt, plan.MediaNeededUntil);
    }

    // A platform that cannot schedule at all has nothing to be inside of. PBA holds the post until
    // the moment itself regardless of what a horizon would have said.
    [Fact]
    public async Task Plan_WhenThePlatformCannotSchedule_TheHorizonIsIrrelevant()
    {
        Capabilities(Platform.Instagram, schedules: false, paced: false, horizon: TimeSpan.FromDays(7));

        var plan = await CreatePlanner().PlanAsync(Platform.Instagram, Now.AddDays(14), CancellationToken.None);

        Assert.True(plan.PbaHolds);
        Assert.Equal(Now.AddDays(14), plan.HandoverAt);
    }

    // YouTube's shape, and the reason this class exists: the platform schedules the release, but
    // will only accept so many videos a day, so the handover is early AND paced.
    [Fact]
    public async Task Plan_ForAPacedPlatform_HoldsUntilTheSlotThePacerChose()
    {
        var pacedSlot = Now.AddDays(1);
        Capabilities(Platform.YouTube, schedules: true, paced: true);
        RegisterPacer(Platform.YouTube, pacedSlot);

        var plan = await CreatePlanner().PlanAsync(Platform.YouTube, GoLive, CancellationToken.None);

        Assert.True(plan.PbaHolds);
        Assert.Equal(pacedSlot, plan.HandoverAt);
    }

    [Fact]
    public async Task Plan_ForAPacedPlatformWithCapacityNow_HandsOverImmediately()
    {
        Capabilities(Platform.YouTube, schedules: true, paced: true);
        RegisterPacer(Platform.YouTube, Now);

        var plan = await CreatePlanner().PlanAsync(Platform.YouTube, GoLive, CancellationToken.None);

        Assert.False(plan.PbaHolds);
        Assert.Null(plan.HandoverAt);
    }

    // Refusing is the point. Accepting a clip there is no capacity for means a failure days later,
    // unattended, against a quota nobody is watching.
    [Fact]
    public async Task Plan_WhenThePacerHasNoCapacity_Refuses()
    {
        Capabilities(Platform.YouTube, schedules: true, paced: true);
        RegisterPacer(Platform.YouTube, null);

        var plan = await CreatePlanner().PlanAsync(Platform.YouTube, GoLive, CancellationToken.None);

        Assert.NotNull(plan.Refusal);
        Assert.False(plan.PbaHolds);
    }

    // A connector asking to be paced with nothing registered to pace it is a wiring mistake. Saying
    // so beats guessing a rate — an unpaced batch burns the day's quota and everything after it
    // fails one by one with no explanation.
    [Fact]
    public async Task Plan_WhenAPacedPlatformHasNoPacerRegistered_RefusesRatherThanGuessing()
    {
        Capabilities(Platform.YouTube, schedules: true, paced: true);

        var plan = await CreatePlanner().PlanAsync(Platform.YouTube, GoLive, CancellationToken.None);

        Assert.NotNull(plan.Refusal);
        Assert.Contains("no upload pacer", plan.Refusal);
    }
}
