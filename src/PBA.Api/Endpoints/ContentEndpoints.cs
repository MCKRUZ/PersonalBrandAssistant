using MediatR;
using Microsoft.AspNetCore.Mvc;
using PBA.Api.Extensions;
using PBA.Application.Features.Content.Commands;
using PBA.Application.Features.Content.Dtos;
using PBA.Application.Features.Content.Queries;
using PBA.Domain.Enums;

namespace PBA.Api.Endpoints;

public static class ContentEndpoints
{
    public static void MapContentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content").WithTags("Content");

        group.MapGet("/", async (
            [AsParameters] ListContentQueryParams p,
            ISender sender,
            CancellationToken ct) =>
        {
            var query = new ListContent.Query
            {
                Page = p.Page ?? 1,
                PageSize = Math.Clamp(p.PageSize ?? 20, 1, 100),
                Status = p.Status,
                Platform = p.Platform,
                ContentType = p.ContentType,
                DateFrom = p.DateFrom,
                DateTo = p.DateTo,
                Search = p.Search
            };
            var result = await sender.Send(query, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetContent.Query(id), ct);
            return result.ToApiResult();
        });

        group.MapPost("/", async (CreateContentRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new CreateContent.Command(
                body.Title,
                body.ContentType,
                body.PrimaryPlatform,
                body.SourceIdeaId,
                body.Tags);
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/content/{result.Value}", result.Value)
                : result.ToApiResult();
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateContentRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new UpdateContent.Command(
                id,
                body.Title,
                body.Body,
                body.Tags,
                body.ContentType,
                body.PrimaryPlatform,
                body.LastUpdatedAt);
            var result = await sender.Send(command, ct);
            return result.ToApiResult();
        });

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DeleteContent.Command(id), ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/draft", async (Guid id, DraftContentRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new DraftContent.Command(id, body.Action, body.Instructions, body.ToneName);
            var result = await sender.Send(command, ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/cross-post", async (Guid id, CrossPostRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new GenerateCrossPost.Command(id, body.TargetPlatform);
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/content/{result.Value}", result.Value)
                : result.ToApiResult();
        });

        group.MapPut("/{id:guid}/approve", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ApproveContent.Command(id), ct);
            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}/submit-review", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new SubmitForReviewContent.Command(id), ct);
            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}/request-changes", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RequestChangesContent.Command(id), ct);
            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}/schedule", async (Guid id, ScheduleContentRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new ScheduleContent.Command(id, body.ScheduledAt, body.TargetPlatforms);
            var result = await sender.Send(command, ct);
            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}/unschedule", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new UnscheduleContent.Command(id), ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/publish", async (Guid id, PublishContentRequest? body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new PublishContent.Command(id, body?.TargetPlatforms), ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/publish-status", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetPublishStatus.Query(id), ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/retry/{platform}", async (Guid id, string platform, ISender sender, CancellationToken ct) =>
        {
            if (!Enum.TryParse<Platform>(platform, ignoreCase: true, out var targetPlatform))
                return Results.BadRequest("Invalid platform");

            var result = await sender.Send(new RetryPlatformPublish.Command(id, targetPlatform), ct);
            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}/unpublish", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new UnpublishContent.Command(id), ct);
            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}/restore", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RestoreContent.Command(id), ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/voice-check", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CheckVoice.Query(id), ct);
            return result.ToApiResult();
        });
    }
}

public record ListContentQueryParams
{
    public ContentStatus? Status { get; init; }
    public Platform? Platform { get; init; }
    public ContentType? ContentType { get; init; }
    public DateTimeOffset? DateFrom { get; init; }
    public DateTimeOffset? DateTo { get; init; }
    public string? Search { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}
