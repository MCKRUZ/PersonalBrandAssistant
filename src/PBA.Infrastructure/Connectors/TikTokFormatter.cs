using System.Text;
using System.Text.RegularExpressions;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Connectors;

/// <summary>
/// Formats content into a TikTok caption: plain-text body (markdown stripped) followed by the tags
/// rendered as hashtags. TikTok captions are plain text — no markdown or HTML — capped at 2200 chars.
/// </summary>
public sealed partial class TikTokFormatter : IPlatformFormatter
{
    private const int MaxCharacters = 2200;

    public Platform Platform => Platform.TikTok;

    public Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct)
    {
        var body = StripMarkdown(content.Body);

        var hashtags = content.Tags
            .Select(ToHashtag)
            .Where(h => h.Length > 1)
            .ToList();

        var caption = hashtags.Count > 0
            ? $"{body}\n\n{string.Join(' ', hashtags)}"
            : body;

        if (caption.Length > MaxCharacters)
            caption = caption[..MaxCharacters].TrimEnd();

        return Task.FromResult(caption);
    }

    private static string ToHashtag(string tag)
    {
        var cleaned = NonAlphanumeric().Replace(tag, "");
        return string.IsNullOrEmpty(cleaned) ? string.Empty : $"#{cleaned}";
    }

    private static string StripMarkdown(string text)
    {
        text = FencedCodeBlock().Replace(text, "$1");
        text = ImagePattern().Replace(text, "");
        text = LinkPattern().Replace(text, "$1");
        text = BoldPattern().Replace(text, "$1");
        text = ItalicPattern().Replace(text, "$1");
        text = InlineCodePattern().Replace(text, "$1");
        text = HeadingPattern().Replace(text, "$1");
        text = BlockquotePattern().Replace(text, "$1");
        text = HorizontalRulePattern().Replace(text, "");
        text = CollapseBlankLines().Replace(text, "\n\n");
        return text.Trim();
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex(@"```[\w]*\n([\s\S]*?)```", RegexOptions.Multiline)]
    private static partial Regex FencedCodeBlock();

    [GeneratedRegex(@"!\[[^\]]*\]\([^\)]+\)\s*")]
    private static partial Regex ImagePattern();

    [GeneratedRegex(@"\[([^\]]+)\]\([^\)]+\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldPattern();

    [GeneratedRegex(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)")]
    private static partial Regex ItalicPattern();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCodePattern();

    [GeneratedRegex(@"^#{1,6}\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^>\s?(.*)$", RegexOptions.Multiline)]
    private static partial Regex BlockquotePattern();

    [GeneratedRegex(@"^---+\s*$", RegexOptions.Multiline)]
    private static partial Regex HorizontalRulePattern();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex CollapseBlankLines();
}
