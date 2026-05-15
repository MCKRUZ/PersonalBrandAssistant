using PBA.Application.Features.Feed.Dtos;

namespace PBA.Api.Hubs;

public interface IFeedHubClient
{
    Task ReceiveFeedItem(FeedItemDto feedItem);
    Task FeedSummaryUpdated(FeedSummaryDto summary);
}
