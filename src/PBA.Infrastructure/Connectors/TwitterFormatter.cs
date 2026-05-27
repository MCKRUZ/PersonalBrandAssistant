using System.Text.Json;
using System.Text.RegularExpressions;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Domain.Enums;

namespace PBA.Infrastructure.Connectors;

public sealed partial class TwitterFormatter : IPlatformFormatter
{
    private const int MaxCharacters = 280;
    private const int TcoUrlLength = 23;

    public Platform Platform => Platform.Twitter;

    public Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct)
    {
        var body = StripMarkdown(content.Body);

        var effectiveUrlLength = !string.IsNullOrEmpty(content.CanonicalUrl) ? TcoUrlLength + 1 : 0;
        var totalLength = body.Length + effectiveUrlLength;

        if (totalLength <= MaxCharacters)
        {
            if (!string.IsNullOrEmpty(content.CanonicalUrl))
                body = $"{body}\n{content.CanonicalUrl}";
            return Task.FromResult(body);
        }

        var segments = SplitIntoThread(body, content.CanonicalUrl);
        var json = JsonSerializer.Serialize(segments);
        return Task.FromResult(json);
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

    private static List<string> SplitIntoThread(string text, string? canonicalUrl)
    {
        var segments = new List<string>();
        var estimatedCount = (int)Math.Ceiling((double)text.Length / (MaxCharacters - 10));
        var remaining = text;

        while (remaining.Length > 0)
        {
            var numberingSuffix = $" {segments.Count + 1}/{estimatedCount}";
            var budget = MaxCharacters - numberingSuffix.Length;

            if (remaining.Length <= budget)
            {
                segments.Add(remaining);
                break;
            }

            var splitIndex = FindSentenceBoundary(remaining, budget);
            var segment = remaining[..splitIndex].TrimEnd();
            segments.Add(segment);
            remaining = remaining[splitIndex..].TrimStart();
        }

        if (segments.Count > 1)
        {
            for (var i = 0; i < segments.Count; i++)
            {
                var suffix = $" {i + 1}/{segments.Count}";
                if (segments[i].Length + suffix.Length <= MaxCharacters)
                    segments[i] += suffix;
            }
        }

        if (!string.IsNullOrEmpty(canonicalUrl))
        {
            var lastIdx = segments.Count - 1;
            var urlAppend = $"\n{canonicalUrl}";
            var urlBudget = TcoUrlLength + 1;

            if (segments[lastIdx].Length + urlBudget <= MaxCharacters)
            {
                segments[lastIdx] += urlAppend;
            }
            else
            {
                segments.Add(canonicalUrl);
            }
        }

        return segments;
    }

    private static int FindSentenceBoundary(string text, int maxLength)
    {
        var searchRegion = text[..Math.Min(maxLength, text.Length)];

        var bestBreak = -1;
        for (var i = searchRegion.Length - 1; i >= maxLength / 2; i--)
        {
            if (IsSentenceEnd(searchRegion[i]) &&
                (i == searchRegion.Length - 1 || searchRegion[i + 1] == ' '))
            {
                bestBreak = i + 1;
                break;
            }
        }

        if (bestBreak > 0)
            return bestBreak;

        var lastSpace = searchRegion.LastIndexOf(' ');
        return lastSpace > maxLength / 2 ? lastSpace : maxLength;
    }

    private static bool IsSentenceEnd(char c) => c is '.' or '!' or '?';

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
