using PBA.Domain.Enums;

namespace PBA.Application.Features.Feed.Dtos;

public record BatchReadRequest
{
    public FeedItemType? Type { get; init; }
    public bool? IsRead { get; init; }
    public IReadOnlyList<Guid>? Ids { get; init; }
}
