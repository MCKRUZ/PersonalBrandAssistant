using PBA.Domain.Enums;

namespace PBA.Application.Common.Interfaces;

/// <summary>
/// Decides WHEN PBA gives a clip to the platform, which is a different question from when the post
/// goes live. Each answer is a fact about the platform, not a preference:
///
/// <list type="bullet">
/// <item>Hand it over now — the platform holds it, for as long as needed (TikTok via Buffer, when
/// the slot is near).</item>
/// <item>Hand it over at the go-live moment — the platform cannot schedule, so PBA holds both the
/// post and the video until then (Instagram).</item>
/// <item>Hand it over early, paced — the platform schedules the release itself but charges a scarce
/// daily quota for accepting the video, so PBA holds the clips and feeds them in a few a day
/// (YouTube).</item>
/// <item>Hand it over later, once the slot is close enough — the platform schedules, but only
/// within a horizon, so PBA holds the clip until the go-live comes inside that window (TikTok via
/// Buffer, which refuses anything more than a week out).</item>
/// </list>
///
/// The last two constrain the same decision from opposite ends and compose rather than nest: the
/// pacer says the earliest PBA may hand over, the horizon the latest it must wait until, and a
/// platform could one day have both.
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
/// <param name="MediaNeededUntil">
/// The moment the staged video is finally read, which is NOT always the hand-over. A platform that
/// takes the bytes (YouTube) or that PBA posts itself (Instagram) is done with the staged object the
/// instant the hand-over happens. Buffer is not: it only ever fetches the video from a URL, and it
/// does so when the post fires, up to a full horizon after PBA handed it the link. The caller sizes
/// the staging window against this, so a clip cannot be reaped between being handed over and being
/// read. Null whenever PBA does not hold the clip at all.
/// </param>
public record HandoverPlan(
    bool PbaHolds,
    DateTimeOffset? HandoverAt,
    string? Refusal = null,
    DateTimeOffset? MediaNeededUntil = null)
{
    public static HandoverPlan Now() => new(false, null);

    /// <summary>Hold until <paramref name="at"/>, where the hand-over consumes the media.</summary>
    public static HandoverPlan Hold(DateTimeOffset at) => new(true, at, null, at);

    /// <summary>
    /// Hold until <paramref name="at"/>, where the platform reads the staged media later still.
    /// </summary>
    public static HandoverPlan Hold(DateTimeOffset at, DateTimeOffset mediaNeededUntil) =>
        new(true, at, null, mediaNeededUntil);

    public static HandoverPlan Refuse(string reason) => new(false, null, reason);
}
