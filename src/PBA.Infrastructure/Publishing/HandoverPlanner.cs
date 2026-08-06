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
    public async Task<HandoverPlan> PlanAsync(Platform platform, DateTimeOffset goLiveAt, CancellationToken ct)
    {
        // The platform cannot schedule at all, so PBA holds the post and posts it at the moment.
        if (!capabilities.SupportsScheduling(platform))
            return HandoverPlan.Hold(goLiveAt);

        // It can schedule, and taking the clip costs it nothing scarce: hand it over and be done.
        if (!capabilities.RequiresPacedHandover(platform))
            return HandoverPlan.Now();

        var pacer = serviceProvider.GetKeyedService<IUploadPacer>(platform);
        if (pacer is null)
        {
            // A connector asking to be paced with nothing registered to pace it is a wiring mistake,
            // and guessing a rate here would be worse than saying so — an unpaced batch burns the
            // day's quota and the rest of the queue fails one by one with no explanation.
            return HandoverPlan.Refuse(
                $"{platform} requires paced handover but no upload pacer is registered for it.");
        }

        var slot = await pacer.NextHandoverSlotAsync(goLiveAt, ct);
        if (slot is null)
            return HandoverPlan.Refuse(
                $"{platform} has no upload capacity left before {goLiveAt:u}: every day until then " +
                "is already fully booked. Move the date out or clear something already scheduled.");

        // A slot in the past (or now) means capacity exists today and there is nothing to wait for.
        return slot.Value <= clock.GetUtcNow()
            ? HandoverPlan.Now()
            : HandoverPlan.Hold(slot.Value);
    }
}
