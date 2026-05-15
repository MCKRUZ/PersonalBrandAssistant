using MediatR;
using Microsoft.AspNetCore.Mvc;
using PBA.Api.Extensions;
using PBA.Application.Features.Feed.Commands;
using PBA.Application.Features.Feed.Dtos;
using PBA.Application.Features.Feed.Queries;
using PBA.Domain.Enums;

namespace PBA.Api.Endpoints;

public static class FeedEndpoints
{
    public static void MapFeedEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/feed").WithTags("Feed");

        group.MapGet("/", async (
            [AsParameters] ListFeedQueryParams p,
            ISender sender,
            CancellationToken ct) =>
        {
            var query = new ListFeedItems.Query
            {
                Page = p.Page ?? 1,
                PageSize = Math.Clamp(p.PageSize ?? 20, 1, 100),
                Type = p.Type,
                Priority = p.Priority,
                IsRead = p.IsRead,
                IncludeExpired = p.IncludeExpired,
                SortBy = p.SortBy ?? "CreatedAt",
                SortDirection = p.SortDirection ?? "desc"
            };
            var result = await sender.Send(query, ct);
            return result.ToApiResult();
        });

        group.MapGet("/summary", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetFeedSummary.Query(), ct);
            return result.ToApiResult();
        });

        group.MapGet("/trending", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetTrendingTopics.Query(), ct);
            return result.ToApiResult();
        });

        group.MapPut("/batch/read", async (BatchReadRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new BatchMarkRead.Command(body.Type), ct);
            return result.ToApiResult();
        });

        group.MapPut("/batch/dismiss", async (BatchDismissRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new BatchDismiss.Command(body.Type), ct);
            return result.ToApiResult();
        });

        group.MapPut("/batch/act", async (BatchActRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new BatchAct.Command(body.Ids, body.Action), ct);
            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}/read", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new MarkFeedItemRead.Command(id), ct);
            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}/act", async (Guid id, ActOnFeedItemRequest body, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ActOnFeedItem.Command(id, body.Action), ct);
            return result.ToApiResult();
        });
    }
}

public record ListFeedQueryParams
{
    public FeedItemType? Type { get; init; }
    public FeedItemPriority? Priority { get; init; }
    public bool? IsRead { get; init; }
    public bool IncludeExpired { get; init; } = false;
    public string? SortBy { get; init; }
    public string? SortDirection { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}
