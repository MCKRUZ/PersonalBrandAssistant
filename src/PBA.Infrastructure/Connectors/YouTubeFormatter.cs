using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Connectors;

/// <summary>
/// Formats content into a YouTube video description: plain text, 5,000-character cap.
///
/// Unlike TikTok and Instagram this does NOT append the tags as hashtags. YouTube has a real tags
/// field on the video itself, and the connector fills it — appending them here as well would put
/// the same words in twice and eat description space that the first two lines need, since those are
/// all a viewer sees before "more".
///
/// A formatter is not optional: ContentTransformer throws for a platform with none registered, so
/// publishing would fail before the connector was ever reached.
/// </summary>
public sealed class YouTubeFormatter : IPlatformFormatter
{
    private const int MaxCharacters = 5000;

    public Platform Platform => Platform.YouTube;

    public Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct) =>
        Task.FromResult(PlainTextCaption.Build(content.Body, tags: [], MaxCharacters));
}
