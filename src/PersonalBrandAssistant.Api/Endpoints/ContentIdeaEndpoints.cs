using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class ContentIdeaEndpoints
{
    public static void MapContentIdeaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content-ideas").WithTags("ContentIdeas");

        group.MapPost("/analyze-story", AnalyzeStory);
    }

    private static async Task<IResult> AnalyzeStory(
        IContentIdeaService service,
        AnalyzeStoryRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.StoryText))
            return Results.BadRequest("storyText is required");

        var result = await service.AnalyzeStoryAsync(request.StoryText, request.SourceUrl, ct);
        return result.ToHttpResult();
    }
}
