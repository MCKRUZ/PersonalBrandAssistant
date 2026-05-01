using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class ImportEndpoints
{
    public static void MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/import").WithTags("Import").RequireAuthorization();

        group.MapPost("/social-post", ImportSocialPost);
        group.MapPost("/social-posts/bulk", ImportSocialPostsBulk);
    }

    private static async Task<IResult> ImportSocialPost(
        IApplicationDbContext db,
        IEngagementAggregator aggregator,
        ILoggerFactory loggerFactory,
        ImportSocialPostRequest request,
        CancellationToken ct)
    {
        var result = await ImportSingle(db, aggregator, request, ct);
        if (result.Error is not null)
            return Results.Problem(statusCode: 400, detail: result.Error);

        await db.SaveChangesAsync(ct);

        if (result.CpsId is not null)
            ScheduleBackgroundAggregation(aggregator, result.CpsId.Value, loggerFactory.CreateLogger("ImportEndpoints"));

        return Results.Created($"/api/content/{result.ContentId}", new
        {
            result.ContentId,
            ContentPlatformStatusId = result.CpsId,
        });
    }

    private static async Task<IResult> ImportSocialPostsBulk(
        IApplicationDbContext db,
        IEngagementAggregator aggregator,
        ILoggerFactory loggerFactory,
        ImportSocialPostRequest[] requests,
        CancellationToken ct)
    {
        if (requests.Length == 0)
            return Results.Problem(statusCode: 400, detail: "No posts to import.");
        if (requests.Length > 50)
            return Results.Problem(statusCode: 400, detail: "Maximum 50 posts per batch.");

        var importResults = new List<ImportBulkEntry>();
        var cpsIds = new List<Guid>();

        foreach (var request in requests)
        {
            var result = await ImportSingle(db, aggregator, request, ct);
            if (result.Error is not null)
            {
                importResults.Add(new ImportBulkEntry(null, null, result.Error));
                continue;
            }
            importResults.Add(new ImportBulkEntry(result.ContentId, result.CpsId, null));
            if (result.CpsId is not null)
                cpsIds.Add(result.CpsId.Value);
        }

        await db.SaveChangesAsync(ct);

        foreach (var cpsId in cpsIds)
            ScheduleBackgroundAggregation(aggregator, cpsId, loggerFactory.CreateLogger("ImportEndpoints"));

        return Results.Ok(new
        {
            Imported = importResults.Count(r => r.ContentId is not null),
            Total = requests.Length,
            Results = importResults,
        });
    }

    private static async Task<ImportResult> ImportSingle(
        IApplicationDbContext db,
        IEngagementAggregator aggregator,
        ImportSocialPostRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PlatformPostId))
            return new ImportResult(Error: "PlatformPostId is required.");

        var existing = await db.ContentPlatformStatuses
            .AnyAsync(cps => cps.Platform == request.Platform && cps.PlatformPostId == request.PlatformPostId, ct);

        if (existing)
            return new ImportResult(Error: $"Post {request.PlatformPostId} on {request.Platform} already imported.");

        var contentType = request.Platform switch
        {
            PlatformType.PersonalBlog or PlatformType.Substack => ContentType.BlogPost,
            PlatformType.YouTube => ContentType.VideoDescription,
            _ => ContentType.SocialPost,
        };

        var content = Content.Create(
            contentType,
            request.Body ?? string.Empty,
            request.Title,
            [request.Platform]);

        content.PublishedAt = request.PublishedAt ?? DateTimeOffset.UtcNow;
        content.TransitionTo(ContentStatus.Review);
        content.TransitionTo(ContentStatus.Approved);
        content.TransitionTo(ContentStatus.Scheduled);
        content.TransitionTo(ContentStatus.Publishing);
        content.TransitionTo(ContentStatus.Published);

        db.Contents.Add(content);

        var cps = new ContentPlatformStatus
        {
            ContentId = content.Id,
            Platform = request.Platform,
            Status = PlatformPublishStatus.Published,
            PlatformPostId = request.PlatformPostId,
            PostUrl = request.PostUrl,
            PublishedAt = content.PublishedAt,
        };

        db.ContentPlatformStatuses.Add(cps);

        return new ImportResult(ContentId: content.Id, CpsId: cps.Id);
    }

    private static void ScheduleBackgroundAggregation(
        IEngagementAggregator aggregator, Guid cpsId, ILogger logger)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await aggregator.FetchLatestAsync(cpsId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Background engagement aggregation failed for CPS {CpsId}", cpsId);
            }
        });
    }

    private record ImportResult(
        Guid? ContentId = null,
        Guid? CpsId = null,
        string? Error = null);

    private record ImportBulkEntry(Guid? ContentId, Guid? CpsId, string? Error);
}

public record ImportSocialPostRequest(
    PlatformType Platform,
    string PlatformPostId,
    string? PostUrl = null,
    string? Title = null,
    string? Body = null,
    DateTimeOffset? PublishedAt = null);
