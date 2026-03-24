using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;

public sealed class TwitterContentFormatter : IPlatformContentFormatter
{
    private const int MaxTweetLength = 280;

    public PlatformType Platform => PlatformType.TwitterX;

    public Result<PlatformContent> FormatAndValidate(Content content)
    {
        if (string.IsNullOrWhiteSpace(content.Body))
        {
            return Result.ValidationFailure<PlatformContent>(["Tweet body cannot be empty"]);
        }

        var imageError = FormatterHelpers.ValidateImageRequirement(content);
        if (imageError is not null)
        {
            return Result.ValidationFailure<PlatformContent>([imageError]);
        }

        var text = content.Body.Trim();
        var hashtags = FormatHashtags(content.Metadata.Tags);

        if (FitsInSingleTweet(text, hashtags))
        {
            var single = AppendHashtags(text, hashtags);
            return Result.Success(new PlatformContent(
                single, content.Title, content.ContentType,
                FormatterHelpers.BuildMediaList(content), FormatterHelpers.EmptyMetadata));
        }

        return BuildThread(text, hashtags, content);
    }

    private static bool FitsInSingleTweet(string text, string hashtags)
    {
        var total = hashtags.Length > 0 ? text.Length + 1 + hashtags.Length : text.Length;
        return total <= MaxTweetLength;
    }

    private static string AppendHashtags(string text, string hashtags)
    {
        if (hashtags.Length == 0) return text;
        var combined = $"{text} {hashtags}";
        return combined.Length <= MaxTweetLength ? combined : text;
    }

    private static Result<PlatformContent> BuildThread(
        string text, string hashtags, Content content)
    {
        var sentences = SplitIntoSentences(text);

        // Two-pass: first pass estimates parts to compute numbering width,
        // second pass builds final parts with correct suffix reservation.
        var estimatedParts = EstimatePartCount(sentences);
        var suffixLength = ComputeSuffixLength(estimatedParts);

        var parts = new List<string>();
        var current = "";
        var maxContent = MaxTweetLength - suffixLength;

        foreach (var sentence in sentences)
        {
            var testLength = current.Length == 0
                ? sentence.Length
                : current.Length + 1 + sentence.Length;

            if (testLength <= maxContent)
            {
                current = current.Length == 0 ? sentence : $"{current} {sentence}";
            }
            else
            {
                if (current.Length > 0) parts.Add(current);
                current = sentence.Length <= maxContent
                    ? sentence
                    : FormatterHelpers.SafeTruncate(sentence, maxContent);
            }
        }

        if (current.Length > 0) parts.Add(current);

        if (parts.Count == 0)
        {
            parts.Add(FormatterHelpers.SafeTruncate(text, maxContent));
        }

        // Recompute suffix if actual count differs from estimate
        if (parts.Count != estimatedParts)
        {
            suffixLength = ComputeSuffixLength(parts.Count);
        }

        // Try to append hashtags to last part
        if (hashtags.Length > 0)
        {
            var last = parts[^1];
            var withTags = $"{last} {hashtags}";
            if (withTags.Length + suffixLength <= MaxTweetLength)
            {
                parts[^1] = withTags;
            }
        }

        // Add numbering
        var total = parts.Count;
        for (var i = 0; i < parts.Count; i++)
        {
            parts[i] = $"{parts[i]} {i + 1}/{total}";
        }

        var metadata = new Dictionary<string, string>();
        for (var i = 1; i < parts.Count; i++)
        {
            metadata[$"thread:{i}"] = parts[i];
        }

        return Result.Success(new PlatformContent(
            parts[0], content.Title, content.ContentType,
            FormatterHelpers.BuildMediaList(content), FormatterHelpers.ToReadOnly(metadata)));
    }

    private static int EstimatePartCount(List<string> sentences)
    {
        // Rough estimate: assume 6-char suffix initially
        var count = 1;
        var current = 0;
        var maxContent = MaxTweetLength - 6;

        foreach (var sentence in sentences)
        {
            var testLength = current == 0 ? sentence.Length : current + 1 + sentence.Length;
            if (testLength <= maxContent)
            {
                current = testLength;
            }
            else
            {
                count++;
                current = Math.Min(sentence.Length, maxContent);
            }
        }

        return count;
    }

    /// <summary>
    /// Computes the length of " N/M" suffix given the total part count.
    /// </summary>
    private static int ComputeSuffixLength(int totalParts)
    {
        var digits = totalParts.ToString().Length;
        // " " + maxDigits + "/" + digits = 1 + digits + 1 + digits
        return 2 + digits * 2;
    }

    private static List<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if ((text[i] == '.' || text[i] == '!' || text[i] == '?') &&
                (i + 1 >= text.Length || text[i + 1] == ' '))
            {
                sentences.Add(text[start..(i + 1)].Trim());
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            var remaining = text[start..].Trim();
            if (remaining.Length > 0) sentences.Add(remaining);
        }

        return sentences;
    }

    private static string FormatHashtags(List<string> tags) =>
        tags.Count == 0 ? "" : string.Join(" ", tags.Select(t => t.StartsWith('#') ? t : $"#{t}"));
}
