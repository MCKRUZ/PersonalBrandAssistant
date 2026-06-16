using MediatR;
using Microsoft.EntityFrameworkCore;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.BrandRankingProfile.Dtos;
using PBA.Domain.Common;

namespace PBA.Application.Features.BrandRankingProfile.Queries;

public static class GetActiveBrandRankingProfile
{
    public record Query : IRequest<Result<BrandRankingProfileDto>>;

    public sealed class Handler(IAppDbContext db) : IRequestHandler<Query, Result<BrandRankingProfileDto>>
    {
        public async Task<Result<BrandRankingProfileDto>> Handle(Query request, CancellationToken cancellationToken)
        {
            var profile = await db.BrandRankingProfiles
                .AsNoTracking()
                .Include(p => p.Pillars)
                .FirstOrDefaultAsync(p => p.IsActive, cancellationToken);

            if (profile is null)
                return Result<BrandRankingProfileDto>.NotFound("No active brand ranking profile");

            return BrandRankingProfileMapping.ToDto(profile);
        }
    }
}
