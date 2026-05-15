using PBA.Application.Features.Feed.Dtos;

namespace PBA.Application.Common.Interfaces;

public interface IFeedNotifier
{
    Task NotifyNewItemAsync(FeedItemDto item);
}
