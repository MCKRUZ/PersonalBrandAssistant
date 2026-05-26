using Microsoft.AspNetCore.SignalR;
using PBA.Api.Hubs;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Feed.Dtos;

namespace PBA.Api.Services;

public class FeedNotifier(IHubContext<FeedHub, IFeedHubClient> hubContext) : IFeedNotifier
{
    public Task NotifyNewItemAsync(FeedItemDto item)
        => hubContext.Clients.All.ReceiveFeedItem(item);
}
