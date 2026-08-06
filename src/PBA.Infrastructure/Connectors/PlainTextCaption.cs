using System.Text.RegularExpressions;

namespace PBA.Infrastructure.Connectors;

/// <summary>
/// Builds a plain-text social caption from markdown body text plus tags rendered as hashtags.
///
/// Shared by the short-video lanes (TikTok, Instagram Reels), which want the same thing: no
/// markdown, no HTML, hashtags appended, hard character cap. It lives here rather than in a base
/// class so a formatter can use part of it without inheriting a publishing identity it does not want.
/// </summary>
internal static partial class PlainTextCaption
{
    /// <summary>
    /// Strips markdown from <paramref name="body"/>, appends <paramref name="tags"/> as hashtags,
    /// and truncates to <paramref name="maxCharacters"/>.
    ///
    /// An empty tag list appends nothing — which is what makes an externally-authored caption
    /// publish verbatim, hashtags and all, instead of having a second set stapled on.
    /// </summary>
    internal static string Build(string body, IReadOnlyList<string> tags, int maxCharacters)
    {
        var text = StripMarkdown(body);

        var hashtags = tags
            .Select(ToHashtag)
            .Where(h => h.Length > 1)
            .ToList();

        var caption = hashtags.Count > 0
            ? $"{text}\n\n{string.Join(' ', hashtags)}"
            : text;

        return caption.Length > maxCharacters
            ? caption[..maxCharacters].TrimEnd()
            : caption;
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
