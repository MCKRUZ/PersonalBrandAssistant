using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Common.Interfaces;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class BrandVoiceEndpoints
{
    public static void MapBrandVoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/brand-voice").WithTags("BrandVoice");

        group.MapGet("/score/{contentId:guid}", GetScore);
    }

    private static async Task<IResult> GetScore(
        IBrandVoiceService brandVoice,
        Guid contentId,
        CancellationToken ct)
    {
        var result = await brandVoice.ScoreContentAsync(contentId, ct);
        return result.ToHttpResult();
    }
}
