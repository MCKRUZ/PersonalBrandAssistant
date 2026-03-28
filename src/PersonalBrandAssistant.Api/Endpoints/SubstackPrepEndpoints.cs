using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class SubstackPrepEndpoints
{
    public static void MapSubstackPrepEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content/{contentId:guid}").WithTags("SubstackPrep");
        group.MapGet("/substack-prep", GetSubstackPrep);
        group.MapPost("/substack-published", MarkSubstackPublished);
    }

    private static async Task<IResult> GetSubstackPrep(
        Guid contentId, ISubstackPrepService service, CancellationToken ct)
    {
        var result = await service.PrepareAsync(contentId, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.ErrorCode == ErrorCode.NotFound
                ? Results.NotFound(new { error = result.Errors.FirstOrDefault() })
                : Results.BadRequest(new { error = result.Errors.FirstOrDefault() });
    }

    private static async Task<IResult> MarkSubstackPublished(
        Guid contentId, SubstackPublishRequest? request, ISubstackPrepService service, CancellationToken ct)
    {
        var result = await service.MarkPublishedAsync(contentId, request?.SubstackUrl, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.ErrorCode == ErrorCode.NotFound
                ? Results.NotFound(new { error = result.Errors.FirstOrDefault() })
                : Results.BadRequest(new { error = result.Errors.FirstOrDefault() });
    }
}

public record SubstackPublishRequest(string? SubstackUrl);
