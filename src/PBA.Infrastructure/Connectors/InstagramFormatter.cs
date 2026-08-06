using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Connectors;

/// <summary>
/// Formats content into an Instagram Reels caption. Same shape as TikTok — plain text, hashtags
/// appended, 2200-char cap — because Instagram captions have the same constraints.
///
/// A formatter is not optional: ContentTransformer throws NotSupportedException for a platform with
/// none registered, so publishing would fail before the connector was ever reached.
/// </summary>
public sealed class InstagramFormatter : IPlatformFormatter
{
    private const int MaxCharacters = 2200;

    public Platform Platform => Platform.Instagram;

    public Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct) =>
        Task.FromResult(PlainTextCaption.Build(content.Body, content.Tags, MaxCharacters));
}
