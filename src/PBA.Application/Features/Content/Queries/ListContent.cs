using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
using PBA.Application.Features.Content.Dtos;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Content.Queries;

public static class ListContent
{
    public record Query : IRequest<Result<PagedResult<ContentDto>>>
    {
        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 20;
        public ContentStatus? Status { get; init; }
        public Platform? Platform { get; init; }
        public ContentType? ContentType { get; init; }
        public DateTimeOffset? DateFrom { get; init; }
        public DateTimeOffset? DateTo { get; init; }
        public string? Search { get; init; }
    }

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<PagedResult<ContentDto>>>
    {
        public async Task<Result<PagedResult<ContentDto>>> Handle(Query request, CancellationToken cancellationToken)
        {
            var query = db.Contents.AsNoTracking().AsQueryable();

            query = query.Where(c => c.ParentContentId == null);

            if (request.Status.HasValue)
                query = query.Where(c => c.Status == request.Status.Value);

            if (request.Platform.HasValue)
                query = query.Where(c => c.PrimaryPlatform == request.Platform.Value);

            if (request.ContentType.HasValue)
                query = query.Where(c => c.ContentType == request.ContentType.Value);

            if (request.DateFrom.HasValue)
                query = query.Where(c => c.UpdatedAt >= request.DateFrom.Value);

            if (request.DateTo.HasValue)
                query = query.Where(c => c.UpdatedAt <= request.DateTo.Value);

            if (!string.IsNullOrWhiteSpace(request.Search))
            {
                var search = request.Search.ToLower();
                query = query.Where(c => c.Title.ToLower().Contains(search));
            }

            var totalCount = await query.CountAsync(cancellationToken);

            var items = await query
                .OrderByDescending(c => c.UpdatedAt)
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .Select(c => new ContentDto
                {
                    Id = c.Id,
                    Title = c.Title,
                    ContentType = c.ContentType,
                    Status = c.Status,
                    PrimaryPlatform = c.PrimaryPlatform,
                    VoiceScore = c.VoiceScore,
                    Tags = c.Tags,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                    ScheduledAt = c.ScheduledAt,
                    PublishedAt = c.PublishedAt
                })
                .ToListAsync(cancellationToken);

            return new PagedResult<ContentDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = request.Page,
                PageSize = request.PageSize
            };
        }
    }
}
