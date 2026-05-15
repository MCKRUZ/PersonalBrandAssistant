using MediatR;
using Microsoft.Extensions.Logging;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Feed.Mappings;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Feed.Commands;

public static class CreateFeedItem
{
    public record Command(
        FeedItemType Type,
        string Title,
        string Summary,
        string? Data,
        string? ActionType,
        Guid? ActionTargetId,
        FeedItemPriority Priority = FeedItemPriority.Normal,
        DateTimeOffset? ExpiresAt = null) : IRequest<Result<Guid>>;

    public sealed class Handler(
        IAppDbContext db,
        IFeedNotifier feedNotifier,
        ILogger<Handler> logger) : IRequestHandler<Command, Result<Guid>>
    {
        public async Task<Result<Guid>> Handle(Command request, CancellationToken cancellationToken)
        {
            var item = new FeedItem
            {
                Type = request.Type,
                Title = request.Title,
                Summary = request.Summary,
                Data = request.Data,
                ActionType = request.ActionType,
                ActionTargetId = request.ActionTargetId,
                Priority = request.Priority,
                ExpiresAt = request.ExpiresAt
            };

            db.FeedItems.Add(item);
            await db.SaveChangesAsync(cancellationToken);

            try
            {
                await feedNotifier.NotifyNewItemAsync(item.ToDto());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to push feed item {FeedItemId} via SignalR", item.Id);
            }

            return item.Id;
        }
    }
}
