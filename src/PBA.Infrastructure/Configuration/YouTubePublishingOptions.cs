namespace PBA.Infrastructure.Configuration;

public sealed class YouTubePublishingOptions
{
    public const string SectionName = "Publishing:YouTubeUpload";

    public bool Enabled { get; init; }

    /// <summary>
    /// The channel uploads MUST land on, e.g. UCZ3-8txSHf0tsTbrm98p8_w (@matthewkruczek).
    ///
    /// A Google account can own more than one channel, and OAuth consent silently grants whichever
    /// was picked in Google's chooser. A credential for the wrong one is completely valid: the
    /// upload succeeds, returns a real video id, and the video is simply not on the channel anyone
    /// is looking at. That happened on the first test upload here — the second channel even carries
    /// the same display name, differing only by handle.
    ///
    /// Empty disables the check, which means trusting whatever was consented to. Set it.
    /// </summary>
    public string? ChannelId { get; init; }

    /// <summary>
    /// How many videos PBA will upload in one YouTube quota day. Each upload costs 1,600 units of
    /// 10,000, so six is the arithmetic ceiling — four leaves headroom for the analytics polling,
    /// thumbnail sets and list calls that share the same project allowance. Raise it only alongside
    /// a granted quota increase in the Google console, not to make a launch go out faster.
    /// </summary>
    public int DailyUploadBudget { get; init; } = 4;

    /// <summary>
    /// YouTube category for uploads. 28 = Science &amp; Technology, which is what this channel's
    /// clips have always been filed under.
    /// </summary>
    public string CategoryId { get; init; } = "28";

    /// <summary>
    /// Declared audience. False = not made for kids, which is what these are; getting this wrong
    /// disables comments and personalisation on every video it touches.
    /// </summary>
    public bool MadeForKids { get; init; }
}
