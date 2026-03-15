using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services.PlatformServices.Formatters;

public sealed class YouTubeContentFormatter : IPlatformContentFormatter
{
    private const int MaxTitleLength = 100;
    private const int MaxDescriptionLength = 5000;

    public PlatformType Platform => PlatformType.YouTube;

    public Result<PlatformContent> FormatAndValidate(Content content)
    {
        if (string.IsNullOrWhiteSpace(content.Title))
        {
            return Result.ValidationFailure<PlatformContent>(["YouTube video requires a title"]);
        }

        if (string.IsNullOrWhiteSpace(content.Body))
        {
            return Result.ValidationFailure<PlatformContent>(["YouTube video requires a description"]);
        }

        var title = FormatterHelpers.SafeTruncate(content.Title.Trim(), MaxTitleLength);
        var description = FormatterHelpers.SafeTruncate(content.Body.Trim(), MaxDescriptionLength);

        var metadata = new Dictionary<string, string>();

        if (content.Metadata.Tags.Count > 0)
        {
            metadata["tags"] = string.Join(",", content.Metadata.Tags);
        }

        return Result.Success(new PlatformContent(
            description, title, content.ContentType,
            Array.Empty<MediaFile>(), FormatterHelpers.ToReadOnly(metadata)));
    }
}
