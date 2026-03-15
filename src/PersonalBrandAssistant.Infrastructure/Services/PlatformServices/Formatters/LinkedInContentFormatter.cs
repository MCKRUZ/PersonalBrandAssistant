using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;

public sealed class LinkedInContentFormatter : IPlatformContentFormatter
{
    private const int MaxLength = 3000;

    public PlatformType Platform => PlatformType.LinkedIn;

    public Result<PlatformContent> FormatAndValidate(Content content)
    {
        if (string.IsNullOrWhiteSpace(content.Body))
        {
            return Result.ValidationFailure<PlatformContent>(["LinkedIn post body cannot be empty"]);
        }

        var text = content.Body.Trim();

        // Append tags not already present inline
        // Normalize: strip leading # from tag before checking if #tag exists in text
        var tagsToAppend = content.Metadata.Tags
            .Select(tag => tag.TrimStart('#'))
            .Where(tag => !text.Contains($"#{tag}", StringComparison.OrdinalIgnoreCase))
            .Select(tag => $"#{tag}")
            .ToList();

        if (tagsToAppend.Count > 0)
        {
            text = $"{text}\n\n{string.Join(" ", tagsToAppend)}";
        }

        if (text.Length > MaxLength)
        {
            return Result.ValidationFailure<PlatformContent>(
                [$"LinkedIn post exceeds {MaxLength} character limit ({text.Length} chars)"]);
        }

        return Result.Success(new PlatformContent(
            text, content.Title, content.ContentType,
            Array.Empty<MediaFile>(), FormatterHelpers.EmptyMetadata));
    }
}
