using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Feed.Commands;

public static class BatchDismiss
{
    public record Command(FeedItemType Type) : IRequest<Result<int>>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Command, Result<int>>
    {
        public async Task<Result<int>> Handle(Command request, CancellationToken cancellationToken)
        {
            var items = await db.FeedItems
                .Where(x => x.Type == request.Type)
                .Where(x => x.ExpiresAt == null || x.ExpiresAt > DateTimeOffset.UtcNow)
                .Where(x => !x.IsActedOn)
                .ToListAsync(cancellationToken);

            foreach (var item in items)
            {
                item.IsRead = true;
                item.IsActedOn = true;
            }

            await db.SaveChangesAsync(cancellationToken);
            return items.Count;
        }
    }
}
