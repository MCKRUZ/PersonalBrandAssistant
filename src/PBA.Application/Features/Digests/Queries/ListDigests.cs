using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Digests.Dtos;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Digests.Queries;

public static class ListDigests
{
    public record Query : IRequest<Result<IReadOnlyList<DigestSummaryDto>>>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<IReadOnlyList<DigestSummaryDto>>>
    {
        public async Task<Result<IReadOnlyList<DigestSummaryDto>>> Handle(Query request, CancellationToken ct)
        {
            // The history spine is the Main brief; the Microsoft brief is loaded by date alongside it.
            var items = await db.Digests
                .AsNoTracking()
                .Where(d => d.Kind == DigestKind.Main)
                .OrderByDescending(d => d.Date)
                .Select(d => new DigestSummaryDto
                {
                    Id = d.Id,
                    Date = d.Date,
                    Title = d.Title,
                    ItemCount = d.ItemCount,
                    CreatedAt = d.CreatedAt
                })
                .ToListAsync(ct);

            return Result<IReadOnlyList<DigestSummaryDto>>.Success(items);
        }
    }
}
