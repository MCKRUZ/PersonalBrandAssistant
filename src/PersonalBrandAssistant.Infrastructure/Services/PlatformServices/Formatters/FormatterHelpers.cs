using System.Collections.ObjectModel;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;

internal static class FormatterHelpers
{
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
