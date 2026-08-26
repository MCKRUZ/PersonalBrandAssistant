using Microsoft.Extensions.DependencyInjection;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Publishing;

/// <summary>
/// Turns what a connector says about itself into a handover decision. Deliberately capability-driven
/// rather than a switch on Platform: the connector is the thing that knows, and a platform added
/// later gets the right treatment without anyone remembering to edit this file.
/// </summary>
public sealed class HandoverPlanner(
    IPlatformCapabilityReader capabilities,
    IServiceProvider serviceProvider,
    TimeProvider clock) : IHandoverPlanner
{
    /// <summary>
    /// How far inside its horizon a handover is aimed. The connector refuses a post whose go-live is
    /// further out than the horizon, so landing exactly on the edge would be refused by any clock
    /// skew or a job that fires a moment early. An hour is far beyond either, and firing LATE only
    /// moves the handover further inside the window, so the margin only ever needs to cover early.
    /// It is not free: the margin lengthens PBA's own hold by the same amount, and that hold is what
    /// the staged-media window has to cover.
    /// </summary>
    private static readonly TimeSpan HorizonMargin = TimeSpan.FromHours(1);

    public async Task<HandoverPlan> PlanAsync(Platform platform, DateTimeOffset goLiveAt, CancellationToken ct)
    {
        // The platform cannot schedule at all, so PBA holds the post and posts it at the moment.
        if (!capabilities.SupportsScheduling(platform))
            return HandoverPlan.Hold(goLiveAt);

        // Two independent reasons a handover cannot simply happen now, and a platform may have both.
        // They pull in opposite directions and are composed rather than nested: the horizon says how
        // LATE the handover must be for the platform to accept it, the pacer says how EARLY the
        // platform has capacity to take it. Neither is a special case of the other, and writing one
        // as a branch of the other would make the first platform with both unrepresentable.
        var notBefore = HorizonHandover(platform, goLiveAt);

        if (capabilities.RequiresPacedHandover(platform))
        {
            var pacer = serviceProvider.GetKeyedService<IUploadPacer>(platform);
            if (pacer is null)
            {
                // A connector asking to be paced with nothing registered to pace it is a wiring
                // mistake, and guessing a rate here would be worse than saying so — an unpaced batch
                // burns the day's quota and the rest of the queue fails one by one with no
                // explanation.
                return HandoverPlan.Refuse(
                    $"{platform} requires paced handover but no upload pacer is registered for it.");
            }

            var slot = await pacer.NextHandoverSlotAsync(goLiveAt, ct);
            if (slot is null)
                return HandoverPlan.Refuse(
                    $"{platform} has no upload capacity left before {goLiveAt:u}: every day until then " +
                    "is already fully booked. Move the date out or clear something already scheduled.");

            notBefore = Later(notBefore, slot.Value);
        }

        // Nothing to wait for, or the wait has already elapsed: capacity exists and the slot is close
        // enough for the platform to accept it.
        if (notBefore is null || notBefore.Value <= clock.GetUtcNow())
            return HandoverPlan.Now();

        // How long the staged clip has to live is a separate question from when it is handed over,
        // and only the connector knows it. Buffer takes a URL and reads it when the post fires, so
        // the object must survive to GO-LIVE. Everything else consumes the media at hand-over.
        return capabilities.FetchesHostedMediaAtPostTime(platform)
            ? HandoverPlan.Hold(notBefore.Value, goLiveAt)
            : HandoverPlan.Hold(notBefore.Value);
    }

    /// <summary>
    /// The earliest moment the platform would accept this post, or null when it would accept it now
    /// — either because the connector declares no horizon, or because the go-live is already inside
    /// one.
    /// </summary>
    private DateTimeOffset? HorizonHandover(Platform platform, DateTimeOffset goLiveAt)
    {
        if (capabilities.SchedulingHorizon(platform) is not { } horizon)
            return null;

        if (goLiveAt - clock.GetUtcNow() <= horizon)
            return null;

        // Clamped at the go-live moment: a horizon shorter than the margin would otherwise aim the
        // handover past the post it is for. Handing over at go-live is the worst honest answer here,
        // and it is still a hand-over rather than a silent no-op.
        var aimed = goLiveAt - horizon + HorizonMargin;
        return aimed > goLiveAt ? goLiveAt : aimed;
    }

    private static DateTimeOffset? Later(DateTimeOffset? a, DateTimeOffset b) =>
        a is null || b > a.Value ? b : a;
}
