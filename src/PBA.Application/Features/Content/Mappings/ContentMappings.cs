using PBA.Application.Features.Content.Dtos;
using ContentEntity = PBA.Domain.Entities.Content;

namespace PBA.Application.Features.Content.Mappings;

public static class ContentMappings
{
    public static ContentDetailDto ToDetailDto(
        this ContentEntity content,
        IReadOnlyList<PlatformPublishDto>? platformPublishes = null,
        IReadOnlyList<ChildContentDto>? children = null)
    {
        return new ContentDetailDto
        {
            Id = content.Id,
            Title = content.Title,
            ContentType = content.ContentType,
            Status = content.Status,
            PrimaryPlatform = content.PrimaryPlatform,
            VoiceScore = content.VoiceScore,
            Tags = content.Tags,
            CreatedAt = content.CreatedAt,
            UpdatedAt = content.UpdatedAt,
            ScheduledAt = content.ScheduledAt,
            PublishedAt = content.PublishedAt,
            Body = content.Body,
            ViralityPrediction = content.ViralityPrediction,
            SourceIdeaId = content.SourceIdeaId,
            ParentContentId = content.ParentContentId,
            PlatformPublishes = platformPublishes ?? [],
            Children = children ?? [],
        };
    }
}
