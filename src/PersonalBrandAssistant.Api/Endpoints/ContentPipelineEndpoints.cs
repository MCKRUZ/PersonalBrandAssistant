using PersonalBrandAssistant.Api.Extensions;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
// IBrandVoiceService scoring is available via BrandVoiceEndpoints

namespace PersonalBrandAssistant.Api.Endpoints;

public static class ContentPipelineEndpoints
{
    public static void MapContentPipelineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content-pipeline").WithTags("ContentPipeline");

        group.MapPost("/create", CreateFromTopic);
        group.MapPost("/{id:guid}/outline", GenerateOutline);
        group.MapPost("/{id:guid}/draft", GenerateDraft);
        group.MapPost("/{id:guid}/submit", SubmitForReview);
    }

    private static async Task<IResult> CreateFromTopic(
        IContentPipeline pipeline,
        ContentCreationRequest request,
        CancellationToken ct)
    {
        var result = await pipeline.CreateFromTopicAsync(request, ct);
        return result.ToCreatedHttpResult("/api/content");
    }

    private static async Task<IResult> GenerateOutline(
        IContentPipeline pipeline,
        Guid id,
        CancellationToken ct)
    {
        var result = await pipeline.GenerateOutlineAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GenerateDraft(
        IContentPipeline pipeline,
        Guid id,
        CancellationToken ct)
    {
        var result = await pipeline.GenerateDraftAsync(id, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> SubmitForReview(
        IContentPipeline pipeline,
        Guid id,
        CancellationToken ct)
    {
        var result = await pipeline.SubmitForReviewAsync(id, ct);
        return result.ToHttpResult();
    }
}
