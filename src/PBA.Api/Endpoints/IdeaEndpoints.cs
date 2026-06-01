using MediatR;
using Microsoft.AspNetCore.Mvc;
using PBA.Api.Extensions;
using PBA.Application.Features.Ideas.Commands;
using PBA.Application.Features.Ideas.Dtos;
using PBA.Application.Features.Ideas.Queries;
using PBA.Domain.Enums;

namespace PBA.Api.Endpoints;

public static class IdeaEndpoints
{
    public static void MapIdeaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ideas").WithTags("Ideas");

        group.MapGet("/", async (
            [AsParameters] ListIdeasQueryParams p,
            ISender sender,
            CancellationToken ct) =>
        {
            var query = new ListIdeas.Query
            {
                Page = p.Page ?? 1,
                PageSize = Math.Clamp(p.PageSize ?? 20, 1, 100),
                Status = p.Status,
                IdeaSourceId = p.IdeaSourceId,
                Category = p.Category,
                Tags = p.Tags,
                DateFrom = p.DateFrom,
                DateTo = p.DateTo,
                SearchText = p.SearchText,
                SortBy = p.SortBy ?? "detectedat",
                SortDirection = p.SortDirection ?? "desc"
            };
            var result = await sender.Send(query, ct);
            return result.ToApiResult();
        });

        group.MapGet("/connections", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetIdeaConnections.Query(), ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetIdea.Query(id), ct);
            return result.ToApiResult();
        });

        group.MapPost("/", async (CreateIdeaRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new CreateIdea.Command
            {
                Title = body.Title,
                Description = body.Description,
                Url = body.Url,
                Category = body.Category,
                Tags = body.Tags
            };
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/ideas/{result.Value}", result.Value)
                : result.ToApiResult();
        });

        group.MapPut("/{id:guid}/save", async (
            Guid id, SaveIdeaRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new SaveIdea.Command
            {
                IdeaId = id,
                Notes = body.Notes,
                Tags = body.Tags
            };
            var result = await sender.Send(command, ct);
            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}/dismiss", async (
            Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new DismissIdea.Command(id), ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/create-content", async (
            Guid id, CreateContentFromIdeaRequest? body, ISender sender, CancellationToken ct) =>
        {
            var command = new CreateContentFromIdea.Command(
                id,
                body?.ContentType ?? ContentType.Blog,
                body?.PrimaryPlatform ?? Platform.Blog);
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/content/{result.Value}", result.Value)
                : result.ToApiResult();
        });
    }
}

public record CreateContentFromIdeaRequest
{
    public ContentType ContentType { get; init; } = ContentType.Blog;
    public Platform PrimaryPlatform { get; init; } = Platform.Blog;
}

public record ListIdeasQueryParams
{
    public IdeaStatus? Status { get; init; }
    public Guid? IdeaSourceId { get; init; }
    public string? Category { get; init; }
    [FromQuery(Name = "tags")] public string[]? Tags { get; init; }
    public DateTimeOffset? DateFrom { get; init; }
    public DateTimeOffset? DateTo { get; init; }
    public string? SearchText { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
    public string? SortBy { get; init; }
    public string? SortDirection { get; init; }
}
