using MediatR;
using Microsoft.AspNetCore.Mvc;
using PBA.Api.Authentication;
using PBA.Api.Extensions;
using PBA.Application.Features.Analytics.Queries;
using PBA.Application.Features.Content.Commands;
using PBA.Application.Features.Content.Dtos;
using PBA.Application.Features.Content.Queries;
using PBA.Application.Features.Digests.Queries;
using PBA.Application.Features.Feed.Queries;
using PBA.Application.Features.Ideas.Commands;
using PBA.Application.Features.Ideas.Queries;
using PBA.Domain.Enums;

namespace PBA.Api.Endpoints;

/// <summary>
/// Read-mostly API surface for trusted server-to-server consumers (project-avatar).
/// Every route is guarded by <see cref="ApiKeyEndpointFilter"/>. Reads reuse the same
/// MediatR queries as the internal UI endpoints; writes are bounded — inject an idea into
/// the Idea Bank, and trigger publication of an already-approved content item to its
/// target platform(s). Feed-mutation actions are intentionally not exposed here.
/// </summary>
public static class ExternalEndpoints
{
    public static void MapExternalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/external")
            .WithTags("External")
            .AddEndpointFilter<ApiKeyEndpointFilter>();

        // --- Reads ---

        group.MapGet("/feed", async (
            [AsParameters] ListFeedQueryParams p, ISender sender, CancellationToken ct) =>
        {
            var query = new ListFeedItems.Query
            {
                Page = p.Page ?? 1,
                PageSize = Math.Clamp(p.PageSize ?? 20, 1, 100),
                Type = p.Type,
                Priority = p.Priority,
                IsRead = p.IsRead,
                IncludeExpired = p.IncludeExpired ?? false,
                SortBy = p.SortBy ?? "CreatedAt",
                SortDirection = p.SortDirection ?? "desc"
            };
            return (await sender.Send(query, ct)).ToApiResult();
        });

        group.MapGet("/feed/trending", async (ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetTrendingTopics.Query(), ct)).ToApiResult());

        group.MapGet("/ideas", async (
            [AsParameters] ListIdeasQueryParams p, ISender sender, CancellationToken ct) =>
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
                SortBy = p.SortBy ?? "rank",
                SortDirection = p.SortDirection ?? "desc",
                MinScore = p.MinScore,
                IncludeDuplicates = p.IncludeDuplicates ?? false
            };
            return (await sender.Send(query, ct)).ToApiResult();
        });

        group.MapGet("/digests/latest", async (string? kind, ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetLatestDigest.Query(ParseKind(kind)), ct)).ToApiResult());

        group.MapGet("/content", async (
            [AsParameters] ListContentQueryParams p, ISender sender, CancellationToken ct) =>
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
            return (await sender.Send(query, ct)).ToApiResult();
        });

        group.MapGet("/analytics/website", async (string? period, ISender sender, CancellationToken ct) =>
        {
            var (from, to) = ResolveRange(period);
            return (await sender.Send(new GetWebsiteAnalytics.Query(from, to), ct)).ToApiResult();
        });

        // --- Bounded write ---

        group.MapPost("/ideas", async (CreateExternalIdeaRequest body, ISender sender, CancellationToken ct) =>
        {
            var command = new CreateIdea.Command
            {
                Title = body.Title,
                Description = body.Description,
                Url = body.Url,
                Category = body.Category,
                Tags = body.Tags ?? []
            };
            var result = await sender.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/external/ideas/{result.Value}", new { id = result.Value })
                : result.ToApiResult();
        });

        // Trigger publication of an already-approved content item to its target platform(s)
        // (e.g. LinkedIn). Reuses the same MediatR command as the internal UI publish route;
        // the item must already be in Approved/Scheduled status. Ad-hoc raw-text posting is not
        // exposed here by design — content is created and approved through the normal flow first.
        group.MapPost("/content/{id:guid}/publish", async (
            Guid id, PublishContentRequest? body, ISender sender, CancellationToken ct) =>
            (await sender.Send(new PublishContent.Command(id, body?.TargetPlatforms), ct)).ToApiResult());
    }

    private static DigestKind ParseKind(string? kind) =>
        string.Equals(kind, "microsoft", StringComparison.OrdinalIgnoreCase)
            ? DigestKind.Microsoft
            : DigestKind.Main;

    // Bot-friendly subset of the analytics range options (period only; defaults to 30 days).
    private static (DateTimeOffset From, DateTimeOffset To) ResolveRange(string? period)
    {
        var today = DateTimeOffset.UtcNow.Date;
        var days = period switch { "7d" => 7, "90d" => 90, _ => 30 };
        return (new DateTimeOffset(today.AddDays(-(days - 1)), TimeSpan.Zero),
                new DateTimeOffset(today, TimeSpan.Zero));
    }
}

public record CreateExternalIdeaRequest
{
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Url { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
}
