using System.Text.RegularExpressions;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Connectors;

public sealed partial class LinkedInFormatter : IPlatformFormatter
{
    private const int MaxCharacters = 3000;

    public Platform Platform => Platform.LinkedIn;

    public Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct)
    {
        var body = content.Body;

        body = FencedCodeBlock().Replace(body, "$1");
        body = ImagePattern().Replace(body, "");
        body = LinkPattern().Replace(body, "$1");
        body = BoldPattern().Replace(body, "$1");
        body = ItalicPattern().Replace(body, "$1");
        body = InlineCodePattern().Replace(body, "$1");
        body = HeadingPattern().Replace(body, "$1");
        body = BlockquotePattern().Replace(body, "$1");
        body = HorizontalRulePattern().Replace(body, "");

        body = CollapseBlankLines().Replace(body, "\n\n");
        body = body.Trim();

        if (body.Length > MaxCharacters)
            body = Truncate(body, content.CanonicalUrl);

        return Task.FromResult(body);
    }

    private static string Truncate(string text, string? canonicalUrl)
    {
        string suffix;
        if (!string.IsNullOrEmpty(canonicalUrl))
            suffix = $"...\n\nRead more: {canonicalUrl}";
        else
            suffix = "...";

        var budget = MaxCharacters - suffix.Length;
        if (budget <= 0)
            return suffix[..MaxCharacters];

        var truncated = text[..budget];
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > budget / 2)
            truncated = truncated[..lastSpace];

        return truncated + suffix;
    }

    [GeneratedRegex(@"```[\w]*\n([\s\S]*?)```", RegexOptions.Multiline)]
    private static partial Regex FencedCodeBlock();

    [GeneratedRegex(@"!\[[^\]]*\]\([^\)]+\)\s*")]
    private static partial Regex ImagePattern();

    [GeneratedRegex(@"\[([^\]]+)\]\([^\)]+\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldPattern();

    [GeneratedRegex(@"\*(.+?)\*")]
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
