using PBA.Domain.Enums;

namespace PBA.Application.Common.Interfaces;

/// <summary>
/// Answers what a platform's connector can do, without the caller resolving the connector itself.
///
/// Exists so a handler can decide WHO holds a post until its slot. Some platforms hold it for us
/// (TikTok, because Buffer schedules); others cannot at all (Instagram — Meta's Content Publishing
/// API has no scheduling concept), and for those PBA has to hold the post and the media itself.
/// Getting this wrong is expensive in one direction: treating a non-scheduling platform as if it
/// schedules publishes the clip immediately, days early, on a public account.
/// </summary>
public interface IPlatformCapabilityReader
{
    /// <summary>
    /// True when the platform will hold a post until a given time on our behalf. False both when it
    /// cannot and when no connector is registered — the safe reading, since it makes PBA keep
    /// responsibility for the timing rather than hand it to something that will ignore it.
    /// </summary>
    bool SupportsScheduling(Platform platform);

    /// <summary>
    /// True when handing the clip over is itself rationed and PBA must spread the handovers out,
    /// even though the platform can schedule the release (YouTube's upload quota). False when no
    /// connector is registered, which keeps an unknown platform on the ordinary path rather than
    /// inventing a pacing rule for it.
    /// </summary>
    bool RequiresPacedHandover(Platform platform);

    /// <summary>
    /// How far ahead the platform will accept a scheduled post, or null when it will accept any
    /// distance — which is also the answer when no connector is registered. Null is the safe reading
    /// here for the opposite reason to the flags above: inventing a horizon for an unknown platform
    /// would delay a hand-over PBA has no evidence needs delaying.
    /// </summary>
    TimeSpan? SchedulingHorizon(Platform platform);

    /// <summary>
    /// True when the platform fetches the video from our hosted URL when the post fires, rather than
    /// taking it at hand-over. Decides how long a staged object must outlive its own staging. False
    /// when no connector is registered — the shorter requirement, so an unknown platform is never
    /// promised a window PBA has no evidence it needs.
    /// </summary>
    bool FetchesHostedMediaAtPostTime(Platform platform);
}
