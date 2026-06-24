using MediatR;
using PBA.Api.Extensions;
using PBA.Application.Features.Digests.Queries;
using PBA.Domain.Enums;

namespace PBA.Api.Endpoints;

public static class DigestEndpoints
{
    public static void MapDigestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/digests").WithTags("Digests");

        // History spine — Main briefs only.
        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
            (await sender.Send(new ListDigests.Query(), ct)).ToApiResult());

        group.MapGet("/latest", async (string? kind, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetLatestDigest.Query(ParseKind(kind)), ct)).ToApiResult());

        group.MapGet("/by-date/{date:datetime}", async (
            DateTime date, string? kind, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetDigestByDate.Query(DateOnly.FromDateTime(date), ParseKind(kind)), ct)).ToApiResult());

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetDigest.Query(id), ct)).ToApiResult());
    }

    private static DigestKind ParseKind(string? kind) =>
        string.Equals(kind, "microsoft", StringComparison.OrdinalIgnoreCase)
            ? DigestKind.Microsoft
            : DigestKind.Main;
}
