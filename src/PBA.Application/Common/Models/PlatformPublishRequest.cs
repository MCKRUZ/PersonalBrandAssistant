namespace PBA.Application.Common.Models;

using PBA.Domain.Entities;
using PBA.Domain.Enums;

/// <param name="HostedMediaUrl">
/// A public URL for media that has ALREADY been staged, for connectors whose transport fetches
/// media by URL (Buffer, Meta). Set when PBA held the post until its slot and therefore staged the
/// clip up front — by then the original bytes are long gone, so <paramref name="Media"/> is null and
/// this is the only handle on the video. A connector that gets this must use it rather than staging
/// again; staging twice would leave an orphaned object and pay to store the same clip twice.
/// </param>
public record PlatformPublishRequest(
    Content Content,
    string TransformedContent,
    IReadOnlyList<string> Tags,
    string? CanonicalUrl,
    PublishMode Mode,
    DateTimeOffset? ScheduledAt,
    MediaAttachment? Media = null,
    string? HostedMediaUrl = null
);
