namespace PBA.Infrastructure.Configuration;

public sealed class YouTubePublishingOptions
{
    public const string SectionName = "Publishing:YouTubeUpload";

    public bool Enabled { get; init; }

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
