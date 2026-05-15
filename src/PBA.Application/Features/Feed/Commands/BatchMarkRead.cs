using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Feed.Commands;

public static class BatchMarkRead
{
    public record Command(FeedItemType? Type = null) : IRequest<Result<int>>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Command, Result<int>>
    {
        public async Task<Result<int>> Handle(Command request, CancellationToken cancellationToken)
        {
            var query = db.FeedItems
                .Where(x => !x.IsRead)
                .Where(x => x.ExpiresAt == null || x.ExpiresAt > DateTimeOffset.UtcNow);

            if (request.Type.HasValue)
                query = query.Where(x => x.Type == request.Type.Value);

            var items = await query.ToListAsync(cancellationToken);

            foreach (var item in items)
                item.IsRead = true;

            await db.SaveChangesAsync(cancellationToken);
            return items.Count;
        }
    }
}
