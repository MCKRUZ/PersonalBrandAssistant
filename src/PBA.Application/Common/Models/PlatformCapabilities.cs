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
public record PlatformCapabilities(
    int MaxCharacters,
    bool SupportsMarkdown,
    bool SupportsHtml,
    bool SupportsImages,
    bool SupportsScheduling,
    bool SupportsThreads,
    IReadOnlyList<string> SupportedMediaTypes,
    bool RequiresPacedHandover = false
);
