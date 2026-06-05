using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Digests.Dtos;
using PBA.Domain.Common;

namespace PBA.Application.Features.Digests.Queries;

public static class GetDigest
{
    public record Query(Guid Id) : IRequest<Result<DigestDto>>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<DigestDto>>
    {
        public async Task<Result<DigestDto>> Handle(Query request, CancellationToken ct)
        {
            var digest = await db.Digests
                .AsNoTracking()
                .Include(d => d.Items)
                    .ThenInclude(i => i.Idea)
                .Where(d => d.Id == request.Id)
                .FirstOrDefaultAsync(ct);

            if (digest is null)
                return Result<DigestDto>.NotFound("Digest not found.");

            return GetLatestDigest.Handler.ToDto(digest);
        }
    }
}
