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

    private void Capabilities(Platform platform, bool schedules, bool paced)
    {
        _capabilities.Setup(c => c.SupportsScheduling(platform)).Returns(schedules);
        _capabilities.Setup(c => c.RequiresPacedHandover(platform)).Returns(paced);
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
