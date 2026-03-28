using System.Text.RegularExpressions;

namespace PersonalBrandAssistant.Infrastructure.Services.ContentServices;

public static partial class MarkdownSanitizer
{
    public static string StripHtml(string markdown)
    {
        return HtmlTagRegex().Replace(markdown, "");
    }

    public static string DemoteHeadings(string markdown)
    {
        return HeadingRegex().Replace(markdown, m =>
        {
            var hashes = m.Groups[1].Value;
            return hashes.Length < 6 ? $"#{hashes} {m.Groups[2].Value}" : m.Value;
        });
    }

    public static string ToPlainText(string markdown)
    {
        var text = StripHtml(markdown);
        text = MarkdownSyntaxRegex().Replace(text, "$1");
        text = HeadingPrefixRegex().Replace(text, "");
        text = MultipleNewlinesRegex().Replace(text, "\n\n");
        return text.Trim();
    }

    public static string TruncateAtWordBoundary(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;

        var truncated = text[..maxLength];
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > maxLength / 2)
            truncated = truncated[..lastSpace];

        return $"{truncated.TrimEnd()}...";
    }

    public static string ExtractFirstParagraph(string markdown)
    {
        var lines = markdown.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && !trimmed.StartsWith('#'))
                return trimmed;
        }
        return "";
    }

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"[*_]{1,2}([^*_]+)[*_]{1,2}")]
    private static partial Regex MarkdownSyntaxRegex();

    [GeneratedRegex(@"^#{1,6}\s+", RegexOptions.Multiline)]
    private static partial Regex HeadingPrefixRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleNewlinesRegex();
}
