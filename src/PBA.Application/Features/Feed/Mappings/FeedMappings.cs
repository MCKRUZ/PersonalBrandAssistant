using PBA.Application.Features.Feed.Dtos;
using PBA.Domain.Entities;

namespace PBA.Application.Features.Feed.Mappings;

public static class FeedMappings
{
    public static FeedItemDto ToDto(this FeedItem feedItem) => new()
    {
        Id = feedItem.Id,
        Type = feedItem.Type,
        Title = feedItem.Title,
        Summary = feedItem.Summary,
        Data = feedItem.Data,
        ActionType = feedItem.ActionType,
        ActionTargetId = feedItem.ActionTargetId,
        Priority = feedItem.Priority,
        IsRead = feedItem.IsRead,
        IsActedOn = feedItem.IsActedOn,
        CreatedAt = feedItem.CreatedAt,
        ExpiresAt = feedItem.ExpiresAt,
    };
}
