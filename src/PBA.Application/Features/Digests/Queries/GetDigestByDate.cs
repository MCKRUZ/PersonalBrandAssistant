using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Digests.Dtos;
using PBA.Domain.Common;
using PBA.Domain.Enums;

namespace PBA.Application.Features.Digests.Queries;

/// <summary>
/// Fetches a specific date's digest for a given kind. Used to load the Microsoft brief that sits beside
/// the selected Main brief (which the UI selects by date from the history spine).
/// </summary>
public static class GetDigestByDate
{
    public record Query(DateOnly Date, DigestKind Kind = DigestKind.Main) : IRequest<Result<DigestDto>>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<DigestDto>>
    {
        public async Task<Result<DigestDto>> Handle(Query request, CancellationToken ct)
        {
            var digest = await db.Digests
                .AsNoTracking()
                .Include(d => d.Items)
                    .ThenInclude(i => i.Idea)
                .Where(d => d.Date == request.Date && d.Kind == request.Kind)
                .FirstOrDefaultAsync(ct);

            if (digest is null)
                return Result<DigestDto>.NotFound("No digest for that date.");

            return GetLatestDigest.Handler.ToDto(digest);
        }
    }
}
