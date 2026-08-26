namespace PBA.Application.Common.Models;

/// <param name="SupportsScheduling">
/// True when the platform will hold a post until a given moment on our behalf. Buffer does this for
/// TikTok; YouTube does it natively (a video uploaded as private with a publish time releases
/// itself). False means PBA has to hold the post, because the platform's API has no such concept.
/// </param>
/// <param name="RequiresPacedHandover">
/// True when handing the clip over is itself expensive and must be spread out, even though the
/// platform can schedule the release. Only YouTube: an upload costs 1,600 of a 10,000-unit daily
/// quota shared with everything else on the same Google project, so a batch handed over at once
/// would fail partway through with the rest of the day's allowance already gone. PBA therefore
/// holds the clips and uploads a few a day, while the platform still controls when each goes live.
/// </param>
/// <param name="FetchesHostedMediaAtPostTime">
/// True when the platform does not take the video at hand-over but fetches it from our hosted URL
/// when the post fires. Buffer does exactly this. It matters because it decides how long a staged
/// object has to survive: a platform that takes the bytes (YouTube) or that PBA posts itself
/// (Instagram) is done with the URL at hand-over, while Buffer still needs it a whole horizon later.
/// Getting this wrong stages a clip that is reaped before anyone reads it — a dead link at 3am with
/// no failure until then.
/// </param>
/// <param name="SchedulingHorizon">
/// How far ahead the platform will accept a scheduled post, or null when there is no limit. Answers
/// a different question from <paramref name="SupportsScheduling"/>: not WHETHER it schedules but HOW
/// FAR OUT it will. Buffer is the first connector where the honest answer is "yes, but not that far"
/// — it fetches the video from a public URL at post time, and the object is reaped on a lifecycle
/// rule, so a slot beyond that window would post a dead link. Null for every other connector, which
/// is what keeps them on the hand-over-now path unchanged.
/// </param>
public record PlatformCapabilities(
    int MaxCharacters,
    bool SupportsMarkdown,
    bool SupportsHtml,
    bool SupportsImages,
    bool SupportsScheduling,
    bool SupportsThreads,
    IReadOnlyList<string> SupportedMediaTypes,
    bool RequiresPacedHandover = false,
    bool FetchesHostedMediaAtPostTime = false,
    TimeSpan? SchedulingHorizon = null
);
