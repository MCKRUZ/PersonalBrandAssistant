using PBA.Domain.Enums;

namespace PBA.Application.Common.Interfaces;

/// <summary>
/// Decides WHEN PBA gives a clip to the platform, which is a different question from when the post
/// goes live. Three answers exist and each is a fact about the platform, not a preference:
///
/// <list type="bullet">
/// <item>Hand it over now — the platform holds it (TikTok via Buffer).</item>
/// <item>Hand it over at the go-live moment — the platform cannot schedule, so PBA holds both the
/// post and the video until then (Instagram).</item>
/// <item>Hand it over early, paced — the platform schedules the release itself but charges a scarce
/// daily quota for accepting the video, so PBA holds the clips and feeds them in a few a day
/// (YouTube).</item>
/// </list>
///
/// Keeping this in one place is what stops the third case from being bolted onto the first two as a
/// special case at the call site, where it would be invisible to anyone reading the handler.
/// </summary>
public interface IHandoverPlanner
{
    Task<HandoverPlan> PlanAsync(Platform platform, DateTimeOffset goLiveAt, CancellationToken ct);
}

/// <param name="PbaHolds">
/// True when PBA keeps the clip and hands it over later, which means the video must be staged now —
/// the bytes arrive with the request and are gone by the time <paramref name="HandoverAt"/> comes.
/// </param>
/// <param name="HandoverAt">
/// When to hand over. Null together with <paramref name="PbaHolds"/> false means "now".
/// </param>
/// <param name="Refusal">
/// Set when no handover can be planned at all — e.g. every day between now and the go-live moment
/// already has its upload budget spoken for. Refusing here is deliberate: the alternative is
/// accepting a post that quietly fails days later against a quota nobody is watching.
/// </param>
public record HandoverPlan(bool PbaHolds, DateTimeOffset? HandoverAt, string? Refusal = null)
{
    public static HandoverPlan Now() => new(false, null);
    public static HandoverPlan Hold(DateTimeOffset at) => new(true, at);
    public static HandoverPlan Refuse(string reason) => new(false, null, reason);
}
