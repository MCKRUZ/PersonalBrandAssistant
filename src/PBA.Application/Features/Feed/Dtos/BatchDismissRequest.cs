using PBA.Domain.Enums;

namespace PBA.Application.Features.Feed.Dtos;

public record BatchDismissRequest
{
    public FeedItemType Type { get; init; }
}
