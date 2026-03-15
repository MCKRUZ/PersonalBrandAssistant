using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;

public sealed class InstagramContentFormatter : IPlatformContentFormatter
{
    private const int MaxCaptionLength = 2200;
    private const int MaxHashtags = 30;
    private const int MaxCarouselItems = 10;

    public PlatformType Platform => PlatformType.Instagram;

    public Result<PlatformContent> FormatAndValidate(Content content)
    {
        if (string.IsNullOrWhiteSpace(content.Body))
        {
            return Result.ValidationFailure<PlatformContent>(["Instagram caption cannot be empty"]);
        }

        if (!HasMedia(content))
        {
            return Result.ValidationFailure<PlatformContent>(
                ["Instagram requires at least one media attachment"]);
        }

        if (content.Metadata.PlatformSpecificData.TryGetValue("carousel_count", out var carouselStr) &&
            int.TryParse(carouselStr, out var carouselCount) &&
            carouselCount > MaxCarouselItems)
        {
            return Result.ValidationFailure<PlatformContent>(
                [$"Instagram carousel is limited to {MaxCarouselItems} items"]);
        }

        var caption = content.Body.Trim();

        // Build hashtag string (limited to 30)
        var tags = content.Metadata.Tags
            .Take(MaxHashtags)
            .Select(t => t.StartsWith('#') ? t : $"#{t}")
            .ToList();

        if (tags.Count > 0)
        {
            caption = $"{caption}\n\n{string.Join(" ", tags)}";
        }

        if (caption.Length > MaxCaptionLength)
        {
            caption = FormatterHelpers.SafeTruncate(caption, MaxCaptionLength);
        }

        return Result.Success(new PlatformContent(
            caption, content.Title, content.ContentType,
            Array.Empty<MediaFile>(), FormatterHelpers.EmptyMetadata));
    }

    private static bool HasMedia(Content content) =>
        content.Metadata.PlatformSpecificData.TryGetValue("media_count", out var mc) &&
        int.TryParse(mc, out var count) && count > 0;
}
