using MediatR;
using PBA.Api.Extensions;
using PBA.Application.Features.BrandRankingProfile.Commands;
using PBA.Application.Features.BrandRankingProfile.Queries;

namespace PBA.Api.Endpoints;

public static class BrandRankingProfileEndpoints
{
    public static void MapBrandRankingProfileEndpoints(this IEndpointRouteBuilder app)
    {
        // Distinct route from the existing voice BrandProfile; no endpoint auth in v2.
        var group = app.MapGroup("/api/brand-ranking-profile").WithTags("BrandRankingProfile");

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetActiveBrandRankingProfile.Query(), ct)).ToApiResult());

        // The command is the request body; validation + the server-enforced write mode run in the handler.
        // PUT is a FULL REPLACE (REST semantics): an omitted topics/pillars collection is treated as empty,
        // so clients must send the complete profile. A missing/invalid ConcurrencyToken is rejected (400).
        group.MapPut("/", async (UpdateBrandRankingProfile.Command body, ISender sender, CancellationToken ct) =>
            (await sender.Send(body, ct)).ToApiResult());
    }
}
