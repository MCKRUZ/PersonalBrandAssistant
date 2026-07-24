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
}
