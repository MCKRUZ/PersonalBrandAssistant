using MediatR;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;

namespace PBA.Application.Features.Feed.Commands;

public static class MarkFeedItemRead
{
    public record Command(Guid Id) : IRequest<Result>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Command, Result>
    {
        public async Task<Result> Handle(Command request, CancellationToken cancellationToken)
        {
            var item = await db.FeedItems.FindAsync([request.Id], cancellationToken);
            if (item is null)
                return Result.NotFound($"Feed item {request.Id} not found");

            item.IsRead = true;
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
    }
}
