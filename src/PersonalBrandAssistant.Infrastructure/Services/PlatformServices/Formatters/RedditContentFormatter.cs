using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;

public sealed class RedditContentFormatter : IPlatformContentFormatter
{
    private const int MaxTitleLength = 300;

    public PlatformType Platform => PlatformType.Reddit;

    public Result<PlatformContent> FormatAndValidate(Content content)
    {
        if (string.IsNullOrWhiteSpace(content.Body))
        {
            return Result.ValidationFailure<PlatformContent>(["Post body cannot be empty"]);
        }

        var imageError = FormatterHelpers.ValidateImageRequirement(content);
        if (imageError is not null)
        {
            return Result.ValidationFailure<PlatformContent>([imageError]);
        }

        var title = content.Title?.Trim();
        if (title is not null && title.Length > MaxTitleLength)
        {
            title = title[..MaxTitleLength];
        }

        var text = content.Body.Trim();
        var hashtags = content.Metadata.Tags;
        if (hashtags.Count > 0)
        {
            var tagLine = string.Join(" ", hashtags.Select(t => t.StartsWith('#') ? t : $"#{t}"));
            text = $"{text}\n\n---\n{tagLine}";
        }

        return Result.Success(new PlatformContent(
            text, title, content.ContentType,
            FormatterHelpers.BuildMediaList(content), FormatterHelpers.EmptyMetadata));
    }
}
