using System.Collections.ObjectModel;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;

internal static class FormatterHelpers
{
    internal static IReadOnlyList<MediaFile> BuildMediaList(Content content)
    {
        if (string.IsNullOrWhiteSpace(content.ImageFileId))
            return Array.Empty<MediaFile>();

        var altText = content.Metadata.PlatformSpecificData
            .GetValueOrDefault("imageAltText");

        return [new MediaFile(content.ImageFileId, "image/png", altText)];
    }

    internal static string? ValidateImageRequirement(Content content)
    {
        if (content.ImageRequired && string.IsNullOrWhiteSpace(content.ImageFileId))
            return "Post requires an image but none is attached";

        return null;
    }

    /// <summary>
    /// Truncates text to maxLength, avoiding mid-surrogate splits.
    /// Appends "..." if truncation occurred.
    /// </summary>
    internal static string SafeTruncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;

        var cutoff = maxLength - 3; // room for "..."
        if (cutoff <= 0) return "...";

        // Avoid slicing mid-surrogate pair
        if (char.IsHighSurrogate(text[cutoff - 1]))
        {
            cutoff--;
        }

        return text[..cutoff] + "...";
    }

    internal static ReadOnlyDictionary<string, string> EmptyMetadata { get; } =
        new(new Dictionary<string, string>());

    internal static ReadOnlyDictionary<string, string> ToReadOnly(Dictionary<string, string> dict) =>
        new(dict);
}
