using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using ContentEntity = PersonalBrandAssistant.Domain.Entities.Content;

namespace PersonalBrandAssistant.Application.Features.Content.Queries.ListContent;

public sealed class ListContentQueryHandler
    : IRequestHandler<ListContentQuery, Result<PagedResult<ContentEntity>>>
{
    private readonly IApplicationDbContext _dbContext;

    public ListContentQueryHandler(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PagedResult<ContentEntity>>> Handle(
        ListContentQuery request, CancellationToken cancellationToken)
    {
        var pageSize = Math.Min(request.PageSize, 50);
        var query = _dbContext.Contents.AsQueryable();

        if (request.ContentType.HasValue)
            query = query.Where(c => c.ContentType == request.ContentType.Value);

        if (request.Status.HasValue)
            query = query.Where(c => c.Status == request.Status.Value);

        var cursorData = PagedResult<ContentEntity>.DecodeCursor(request.Cursor);
        if (cursorData.HasValue)
        {
            var (cursorCreatedAt, cursorId) = cursorData.Value;
            query = query.Where(c =>
                c.CreatedAt < cursorCreatedAt ||
                (c.CreatedAt == cursorCreatedAt && c.Id.CompareTo(cursorId) < 0));
        }

        query = query
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id);

        var items = await query.Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = items.Count > pageSize;

        if (hasMore)
        {
            items = items.Take(pageSize).ToList();
        }

        string? nextCursor = null;
        if (hasMore && items.Count > 0)
        {
            var last = items[^1];
            nextCursor = PagedResult<ContentEntity>.EncodeCursor(last.CreatedAt, last.Id);
        }

        return Result<PagedResult<ContentEntity>>.Success(
            new PagedResult<ContentEntity>(items.AsReadOnly(), nextCursor, hasMore));
    }
}
