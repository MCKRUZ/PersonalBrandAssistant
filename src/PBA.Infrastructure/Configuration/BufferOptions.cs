namespace PBA.Infrastructure.Configuration;

public sealed class BufferOptions
{
    public const string SectionName = "Publishing:Buffer";

    public bool Enabled { get; init; }
    public required string ApiKey { get; init; }
    public string Endpoint { get; init; } = "https://api.buffer.com";

    /// <summary>
    /// When true, TikTok posts are marked as AI-generated via the channel-specific
    /// <c>metadata.tiktok.isAiGenerated</c> flag on Buffer's createPost mutation.
    /// </summary>
    public bool DiscloseAiGenerated { get; init; } = true;

    /// <summary>
    /// Where in the clip to grab the TikTok cover frame, as a fraction of the video's duration.
    /// The default 0.5 (mid-clip) avoids the black title-card that opens most clips — the frame the
    /// platform would otherwise pick at offset 0. Clamped to [0, 0.95]. Used only when the video's
    /// duration can be read; otherwise a fixed fallback offset applies.
    /// </summary>
    public double CoverFrameFraction { get; init; } = 0.5;
}
