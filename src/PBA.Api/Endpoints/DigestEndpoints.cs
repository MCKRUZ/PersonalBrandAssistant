using MediatR;
using PBA.Api.Extensions;
using PBA.Application.Features.Digests.Queries;

namespace PBA.Api.Endpoints;

public static class DigestEndpoints
{
    public static void MapDigestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/digests").WithTags("Digests");

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
            (await sender.Send(new ListDigests.Query(), ct)).ToApiResult());

        group.MapGet("/latest", async (ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetLatestDigest.Query(), ct)).ToApiResult());

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetDigest.Query(id), ct)).ToApiResult());
    }
}
