using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class RepurposingEndpoints
{
    public record RepurposeRequest(PlatformType[] TargetPlatforms);

    public static void MapRepurposingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/repurposing").WithTags("Repurposing");

        group.MapPost("/{id:guid}/repurpose", Repurpose);
        group.MapGet("/{id:guid}/repurpose-suggestions", GetSuggestions);
        group.MapGet("/{id:guid}/tree", GetContentTree);
    }

    private static async Task<IResult> Repurpose(
        IRepurposingService service,
        Guid id,
        RepurposeRequest request,
        CancellationToken ct)
    {
        var result = await service.RepurposeAsync(id, request.TargetPlatforms, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetSuggestions(
        IRepurposingService service,
        Guid id,
        CancellationToken ct)
    {
        var result = await service.SuggestRepurposingAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetContentTree(
        IRepurposingService service,
        Guid id,
        CancellationToken ct)
    {
        var result = await service.GetContentTreeAsync(id, ct);
        return result.ToHttpResult();
    }
}
