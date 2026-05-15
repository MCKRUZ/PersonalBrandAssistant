using Microsoft.AspNetCore.SignalR;

namespace PBA.Api.Hubs;

public class FeedHub : Hub<IFeedHubClient>;
