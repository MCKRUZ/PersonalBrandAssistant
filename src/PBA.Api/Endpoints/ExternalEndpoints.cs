using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using PBA.Api.Authentication;
using PBA.Api.Extensions;
using PBA.Application.Common.Interfaces;
using PBA.Application.Common.Models;
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

        // Publish an approved content item WITH a native media attachment (multipart upload) —
        // an MP4/MOV video or a PNG/JPEG/GIF image. LinkedIn streams video to the Videos API /
        // image to the Images API and attaches the resulting URN; TikTok (via Buffer) hosts the
        // video on R2 and hands Buffer the public URL. Platforms whose connector ignores media
        // post text as usual. `title` becomes the video title or image alt-text. `platforms` is
        // an optional comma-separated list (e.g. "LinkedIn"); omit to use the content's own
        // target platforms. The default ~30 MB request-body limit caps the file size here.
        group.MapPost("/content/{id:guid}/publish-media", async (
            Guid id, IFormFile file, [FromForm] string? title, [FromForm] string? platforms,
            ISender sender, CancellationToken ct) =>
        {
            if (file.Length == 0)
                return Results.BadRequest(new { error = "Media file is empty." });

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            var media = new MediaAttachment(stream.ToArray(), file.FileName, file.ContentType, title);

            var command = new PublishContent.Command(id, ParsePlatformsCsv(platforms), media);
            return (await sender.Send(command, ct)).ToApiResult();
        }).DisableAntiforgery();

        // Publish a video clip authored OUTSIDE PBA in one call — ai-video-producer renders the
        // clip, owns the caption, and has already reviewed both. Creates the Content record,
        // walks it to Approved, and publishes it with the video attached; defaults to TikTok
        // (Buffer → R2). The caption is posted verbatim: the record is created with no tags, so
        // TikTokFormatter has nothing to append.
        //
        // Deliberately narrower than general content creation, which stays closed on this surface
        // — this accepts a finished clip, not an idea to be drafted. Larger body limit than
        // publish-media because rendered clips routinely exceed the 30 MB default.
        //
        // `scheduledAt` (optional, ISO-8601 WITH an offset, e.g. 2026-08-07T10:15:00-04:00) hands
        // the clip over to be posted later BY THE PLATFORM. For TikTok, Buffer holds and fires it,
        // so the caller does not need a machine awake at the slot — that is the whole reason this
        // field exists. Omit it to post immediately. A slot already in the past posts immediately.
        // `coverFrameOffsetMs` (optional) picks the cover frame, in milliseconds from the start of
        // the clip. Omit it and the connector guesses from the video's duration, which is fine for
        // an arbitrary clip and wrong for one whose good frame was chosen by hand.
        group.MapPost("/social-clip/publish", async (
            IFormFile file, [FromForm] string caption, [FromForm] string? title,
            [FromForm] string? platforms, [FromForm] string? scheduledAt,
            [FromForm] int? coverFrameOffsetMs,
            ISender sender, CancellationToken ct) =>
        {
            if (file.Length == 0)
                return Results.BadRequest(new { error = "Clip file is empty." });

            DateTimeOffset? when = null;
            if (!string.IsNullOrWhiteSpace(scheduledAt))
            {
                if (!TryParseOffsetAwareTimestamp(scheduledAt, out var parsed))
                    return Results.BadRequest(new
                    {
                        error = $"scheduledAt '{scheduledAt}' is not a valid ISO-8601 timestamp " +
                                "with a UTC offset. Example: 2026-08-07T10:15:00-04:00."
                    });
                when = parsed;
            }

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            var media = new MediaAttachment(stream.ToArray(), file.FileName, file.ContentType, title);

            var command = new PublishSocialClip.Command(
                title ?? string.Empty, caption, media, ParsePlatformsCsv(platforms), when,
                coverFrameOffsetMs);
            return (await sender.Send(command, ct)).ToApiResult();
        })
        .DisableAntiforgery()
        .WithMetadata(new RequestSizeLimitAttribute(MaxClipBytes));

        // Readiness probe for a publishing lane, for callers that schedule posts and need to know
        // BEFORE the slot arrives whether the lane can actually deliver. Runs the connector's own
        // credential check — for TikTok that means a live Buffer key AND a connected channel, not
        // merely a configured-looking credential row.
        group.MapGet("/platforms/{platform}/validate", async (
            string platform, IServiceProvider sp, CancellationToken ct) =>
        {
            if (!Enum.TryParse<Platform>(platform, ignoreCase: true, out var parsed))
                return Results.BadRequest(new { error = $"Unknown platform '{platform}'." });

            var connector = sp.GetKeyedService<IPlatformConnector>(parsed);
            if (connector is null)
                return Results.Ok(new { platform = parsed.ToString(), ready = false, reason = "No connector registered." });

            var ready = await connector.ValidateCredentialsAsync(ct);
            return Results.Ok(new
            {
                platform = parsed.ToString(),
                ready,
                reason = ready ? null : "Connector credential validation failed — see API logs."
            });
        });
    }

    // Rendered social clips run well past the 30 MB minimal-API default; 100 MB covers a
    // three-minute 1080p vertical cut with room to spare.
    private const long MaxClipBytes = 100L * 1024 * 1024;

    /// <summary>
    /// Parses a timestamp that MUST carry an explicit UTC offset (or a trailing Z).
    ///
    /// The offset is the whole point of the check. DateTimeOffset.TryParse happily accepts
    /// "2026-08-07T10:15:00" and resolves it against the SERVER's zone — the API container runs
    /// UTC while the campaign queues that feed this endpoint are written in America/New_York, so
    /// an offset-less value would schedule every post four or five hours off with nothing logged
    /// and no error to notice. Rejecting is the only safe reading: there is no correct zone to
    /// guess, and the mistake only becomes visible once the post is already public.
    /// </summary>
    private static bool TryParseOffsetAwareTimestamp(string value, out DateTimeOffset parsed)
    {
        parsed = default;

        // Kind survives parsing only when the text carried zone information; Unspecified means the
        // caller left it out, whatever DateTimeOffset.TryParse would have invented for it.
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var probe) ||
            probe.Kind == DateTimeKind.Unspecified)
            return false;

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out parsed);
    }

    // Parse an optional comma-separated platform list (e.g. "LinkedIn,Twitter") into enum values.
    // Unknown names are skipped; an empty/absent value yields null (fall back to content defaults).
    private static IReadOnlyList<Platform>? ParsePlatformsCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return null;

        var platforms = csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => Enum.TryParse<Platform>(name, ignoreCase: true, out var p) ? (Platform?)p : null)
            .Where(p => p is not null)
            .Select(p => p!.Value)
            .ToList();

        return platforms.Count > 0 ? platforms : null;
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
