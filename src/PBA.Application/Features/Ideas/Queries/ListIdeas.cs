using System.Linq.Expressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Application.Features.Ideas.Dtos;
using PBA.Domain.Common;
using PBA.Domain.Entities;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Ideas.Queries;

public static class ListIdeas
{
    public record Query : IRequest<Result<PagedResult<IdeaDto>>>
    {
        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 20;
        public IdeaStatus? Status { get; init; }
        public Guid? IdeaSourceId { get; init; }
        public string? Category { get; init; }
        public IReadOnlyList<string>? Tags { get; init; }
        public DateTimeOffset? DateFrom { get; init; }
        public DateTimeOffset? DateTo { get; init; }
        public string? SearchText { get; init; }
        public string SortBy { get; init; } = "DetectedAt";
        public string SortDirection { get; init; } = "desc";
    }

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<PagedResult<IdeaDto>>>
    {
        public async Task<Result<PagedResult<IdeaDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = db.Ideas.AsNoTracking().AsQueryable();

            if (request.Status.HasValue)
                query = query.Where(i => i.Status == request.Status.Value);

            if (request.IdeaSourceId.HasValue)
                query = query.Where(i => i.IdeaSourceId == request.IdeaSourceId.Value);

            if (!string.IsNullOrWhiteSpace(request.Category))
                query = query.Where(i => i.Category != null
                    && i.Category.ToLower().Contains(request.Category.ToLower()));

            if (request.Tags is { Count: > 0 })
                query = query.Where(i => i.Tags.Any(t => request.Tags.Contains(t)));

            if (request.DateFrom.HasValue)
                query = query.Where(i => i.DetectedAt >= request.DateFrom.Value);

            if (request.DateTo.HasValue)
                query = query.Where(i => i.DetectedAt <= request.DateTo.Value);

            if (!string.IsNullOrWhiteSpace(request.SearchText))
            {
                var search = request.SearchText.ToLower();
                query = query.Where(i =>
                    i.Title.ToLower().Contains(search)
                    || (i.Description != null && i.Description.ToLower().Contains(search))
                    || (i.Summary != null && i.Summary.ToLower().Contains(search)));
            }

            var totalCount = await query.CountAsync(cancellationToken);

            query = ApplySort(query, request.SortBy, request.SortDirection);

            var items = await query
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .Select(i => new IdeaDto
                {
                    Id = i.Id,
                    Title = i.Title,
                    SourceName = i.SourceName,
                    Category = i.Category,
                    Summary = i.Summary,
                    ThumbnailUrl = i.ThumbnailUrl,
                    Status = i.Status,
                    Tags = i.Tags,
                    DetectedAt = i.DetectedAt,
                    HasSavedDetails = i.SavedDetails != null
                })
                .ToListAsync(cancellationToken);

            return new PagedResult<IdeaDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = request.Page,
                PageSize = request.PageSize
            };
        }

        private static IQueryable<Idea> ApplySort(IQueryable<Idea> query, string sortBy, string direction)
        {
            var isDescending = direction.Equals("desc", StringComparison.OrdinalIgnoreCase);

            Expression<Func<Idea, object>> keySelector = sortBy.ToLower() switch
            {
                "title" => i => i.Title,
                "sourcename" => i => i.SourceName,
                "category" => i => i.Category!,
                "status" => i => i.Status,
                _ => i => i.DetectedAt
            };

            return isDescending
                ? query.OrderByDescending(keySelector)
                : query.OrderBy(keySelector);
        }
    }
}
