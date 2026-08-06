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
}
