namespace PBA.Infrastructure.Configuration;

public sealed class InstagramPublishingOptions
{
    public const string SectionName = "Publishing:Instagram";

    public bool Enabled { get; init; }

    /// <summary>
    /// The Instagram professional account that owns the posts, e.g. @matthewkruczek.ai. This is the
    /// numeric IG user id, not the handle — Meta's publishing endpoints are all rooted at
    /// <c>/{ig-user-id}/…</c> and do not accept a username.
    /// </summary>
    public required string IgUserId { get; init; }

    public string GraphBase { get; init; } = "https://graph.instagram.com/v23.0";

    /// <summary>
    /// Reels appear in the main feed as well as the Reels tab when true. Matches what the lane did
    /// before it moved into PBA; turning it off narrows reach without changing anything else.
    /// </summary>
    public bool ShareToFeed { get; init; } = true;

    /// <summary>
    /// Where in the clip to grab the Reel cover frame, as a fraction of the video's duration.
    /// Instagram defaults to offset 0, and frame 0 of these clips is a near-black title card, so
    /// every cover would be a black square. Clamped to [0, 0.95].
    /// </summary>
    public double CoverFrameFraction { get; init; } = 0.5;

    /// <summary>
    /// Meta processes an uploaded Reel asynchronously; the container must reach FINISHED before it
    /// can be published. 60 polls at 10s is the ~10 minutes the previous lane allowed, and Reels
    /// processing genuinely does run into minutes.
    /// </summary>
    public int ContainerPollSeconds { get; init; } = 10;
    public int ContainerPollMaxAttempts { get; init; } = 60;
}
