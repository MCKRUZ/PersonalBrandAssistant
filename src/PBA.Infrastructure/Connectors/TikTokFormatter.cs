using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Connectors;

/// <summary>
/// Formats content into a TikTok caption: plain-text body (markdown stripped) followed by the tags
/// rendered as hashtags. TikTok captions are plain text — no markdown or HTML — capped at 2200 chars.
/// </summary>
public sealed class TikTokFormatter : IPlatformFormatter
{
    private const int MaxCharacters = 2200;

    public Platform Platform => Platform.TikTok;

    public Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct) =>
        Task.FromResult(PlainTextCaption.Build(content.Body, content.Tags, MaxCharacters));
}
