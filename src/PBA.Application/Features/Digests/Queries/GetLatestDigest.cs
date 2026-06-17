using System.Linq.Expressions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Digests.Dtos;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Digests.Queries;

public static class GetLatestDigest
{
    public record Query(DigestKind Kind = DigestKind.Main) : IRequest<Result<DigestDto>>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<DigestDto>>
    {
        public async Task<Result<DigestDto>> Handle(Query request, CancellationToken ct)
        {
            var digest = await db.Digests
                .AsNoTracking()
                .Include(d => d.Items)
                    .ThenInclude(i => i.Idea)
                .Where(d => d.Kind == request.Kind)
                .OrderByDescending(d => d.Date)
                .FirstOrDefaultAsync(ct);

            if (digest is null)
                return Result<DigestDto>.NotFound("No digest available yet.");

            return ToDto(digest);
        }

        internal static DigestDto ToDto(Domain.Entities.Digest d) => new()
        {
            Id = d.Id,
            Date = d.Date,
            Title = d.Title,
            Intro = d.Intro,
            ItemCount = d.ItemCount,
            CreatedAt = d.CreatedAt,
            Items = d.Items
                .OrderBy(i => i.Rank)
                .Select(i => new DigestItemDto
                {
                    IdeaId = i.IdeaId,
                    Rank = i.Rank,
                    Score = i.Score,
                    WhyItMatters = i.WhyItMatters,
                    Title = i.Idea?.Title ?? string.Empty,
                    Url = i.Idea?.Url
                })
                .ToList()
        };
    }
}
