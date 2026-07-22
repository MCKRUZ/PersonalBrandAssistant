namespace PBA.Application.Common.Models;

/// <summary>
/// A transient media file attached to a publish request (e.g. a native video for LinkedIn).
/// Not persisted — the platform stores the media, we only stream it through at publish time.
/// Connectors that don't support media simply ignore it.
/// </summary>
public record MediaAttachment(
    byte[] Data,
    string FileName,
    string ContentType,
    string? Title = null
)
{
    public bool IsVideo =>
        ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
        || FileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
        || FileName.EndsWith(".mov", StringComparison.OrdinalIgnoreCase);

    public bool IsImage =>
        ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
        || FileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        || FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
        || FileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
        || FileName.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
}
