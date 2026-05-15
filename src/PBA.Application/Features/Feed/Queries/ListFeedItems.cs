using System.Linq.Expressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Application.Features.Feed.Dtos;
using PBA.Application.Features.Feed.Mappings;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Feed.Queries;

public static class ListFeedItems
{
    public record Query : IRequest<Result<PagedResult<FeedItemDto>>>
    {
        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 20;
        public FeedItemType? Type { get; init; }
        public FeedItemPriority? Priority { get; init; }
        public bool? IsRead { get; init; }
        public bool IncludeExpired { get; init; } = false;
        public string SortBy { get; init; } = "CreatedAt";
        public string SortDirection { get; init; } = "desc";
    }

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<PagedResult<FeedItemDto>>>
    {
        public async Task<Result<PagedResult<FeedItemDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = db.FeedItems.AsNoTracking().AsQueryable();

            if (request.Type.HasValue)
                query = query.Where(f => f.Type == request.Type.Value);

            if (request.Priority.HasValue)
                query = query.Where(f => f.Priority == request.Priority.Value);

            if (request.IsRead.HasValue)
                query = query.Where(f => f.IsRead == request.IsRead.Value);

            if (!request.IncludeExpired)
                query = query.Where(f => f.ExpiresAt == null || f.ExpiresAt >= DateTimeOffset.UtcNow);

            var totalCount = await query.CountAsync(cancellationToken);

            query = ApplySort(query, request.SortBy, request.SortDirection);

            var items = await query
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<FeedItemDto>
            {
                Items = items.Select(f => f.ToDto()).ToList(),
                TotalCount = totalCount,
                Page = request.Page,
                PageSize = request.PageSize
            };
        }

        private static IQueryable<FeedItem> ApplySort(IQueryable<FeedItem> query, string sortBy, string direction)
        {
            var isDescending = direction.Equals("desc", StringComparison.OrdinalIgnoreCase);

            Expression<Func<FeedItem, object>> keySelector = sortBy.ToLower() switch
            {
                "title" => f => f.Title,
                "priority" => f => f.Priority,
                "type" => f => f.Type,
                _ => f => f.CreatedAt
            };

            return isDescending
                ? query.OrderByDescending(keySelector)
                : query.OrderBy(keySelector);
        }
    }
}
