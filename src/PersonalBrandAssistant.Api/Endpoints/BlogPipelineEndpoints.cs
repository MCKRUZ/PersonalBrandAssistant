using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class BlogPipelineEndpoints
{
    public static void MapBlogPipelineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/blog-pipeline").WithTags("BlogPipeline");
        group.MapGet("/", GetBlogPipeline);
        group.MapPut("/{contentId:guid}/advance", AdvanceStage);
        group.MapPut("/{contentId:guid}/stage", SetStage);
        group.MapPost("/{contentId:guid}/schedule", ConfirmSchedule);
        group.MapPut("/{contentId:guid}/delay", UpdateDelay);
        group.MapPost("/{contentId:guid}/skip-blog", SkipBlog);
    }

    private static async Task<IResult> GetBlogPipeline(
        IApplicationDbContext db,
        string? status,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct)
    {
        var query = db.Contents.AsNoTracking()
            .Where(c => c.ContentType == ContentType.BlogPost);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ContentStatus>(status, true, out var parsed))
            query = query.Where(c => c.Status == parsed);

        if (from.HasValue)
            query = query.Where(c => c.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(c => c.CreatedAt <= to.Value);

        var contents = await query
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id,
                c.Title,
                c.Status,
                c.CreatedAt,
                c.SubstackPostUrl,
                c.BlogPostUrl,
                c.BlogDeployCommitSha,
                c.BlogSkipped,
                c.BlogDelayOverride,
                c.CurrentBlogStage,
                c.BlogStageHistory
            })
            .ToListAsync(ct);

        var contentIds = contents.Select(c => c.Id).ToList();

        var platformStatuses = await db.ContentPlatformStatuses.AsNoTracking()
            .Where(s => contentIds.Contains(s.ContentId)
                && (s.Platform == PlatformType.Substack || s.Platform == PlatformType.PersonalBlog))
            .ToListAsync(ct);

        var result = contents.Select(c => new
        {
            c.Id,
            c.Title,
            c.Status,
            c.CreatedAt,
            c.SubstackPostUrl,
            c.BlogPostUrl,
            c.BlogDeployCommitSha,
            c.BlogSkipped,
            BlogDelayDays = c.BlogDelayOverride?.TotalDays,
            c.CurrentBlogStage,
            c.BlogStageHistory,
            Substack = platformStatuses
                .Where(s => s.ContentId == c.Id && s.Platform == PlatformType.Substack)
                .Select(s => new { s.Status, s.PublishedAt, s.PostUrl })
                .FirstOrDefault(),
            PersonalBlog = platformStatuses
                .Where(s => s.ContentId == c.Id && s.Platform == PlatformType.PersonalBlog)
                .Select(s => new { s.Status, s.ScheduledAt, s.PublishedAt, s.PostUrl })
                .FirstOrDefault()
        });

        return Results.Ok(result);
    }

    private static async Task<IResult> AdvanceStage(
        Guid contentId, IApplicationDbContext db, AdvanceStageRequest? request, CancellationToken ct)
    {
        var content = await db.Contents.FirstOrDefaultAsync(c => c.Id == contentId, ct);
        if (content is null)
            return Results.NotFound(new { error = "Content not found" });

        try
        {
            content.AdvanceBlogStage(request?.Note);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { content.CurrentBlogStage, content.BlogStageHistory });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> SetStage(
        Guid contentId, SetStageRequest request, IApplicationDbContext db, CancellationToken ct)
    {
        var content = await db.Contents.FirstOrDefaultAsync(c => c.Id == contentId, ct);
        if (content is null)
            return Results.NotFound(new { error = "Content not found" });

        if (!Enum.IsDefined(request.Stage))
            return Results.BadRequest(new { error = "Invalid pipeline stage" });

        content.SetBlogStage(request.Stage, request.Note);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { content.CurrentBlogStage, content.BlogStageHistory });
    }

    private static async Task<IResult> ConfirmSchedule(
        Guid contentId, IBlogSchedulingService scheduling, CancellationToken ct)
    {
        var result = await scheduling.ConfirmBlogScheduleAsync(contentId, ct);
        return result.IsSuccess
            ? Results.Ok(new { scheduledAt = result.Value })
            : Results.BadRequest(new { error = result.Errors.FirstOrDefault() });
    }

    private static async Task<IResult> UpdateDelay(
        Guid contentId, DelayUpdateRequest request, IApplicationDbContext db, CancellationToken ct)
    {
        var content = await db.Contents.FirstOrDefaultAsync(c => c.Id == contentId, ct);
        if (content is null)
            return Results.NotFound(new { error = "Content not found" });

        try
        {
            var delay = request.DelayDays.HasValue
                ? TimeSpan.FromDays(request.DelayDays.Value)
                : (TimeSpan?)null;

            content.SetBlogDelay(delay);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { blogDelayDays = content.BlogDelayOverride?.TotalDays });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> SkipBlog(
        Guid contentId, IApplicationDbContext db, CancellationToken ct)
    {
        var content = await db.Contents.FirstOrDefaultAsync(c => c.Id == contentId, ct);
        if (content is null)
            return Results.NotFound(new { error = "Content not found" });

        content.SkipBlog();
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { blogSkipped = true });
    }
}

public record DelayUpdateRequest(double? DelayDays);
public record AdvanceStageRequest(string? Note = null);
public record SetStageRequest(BlogPipelineStage Stage, string? Note = null);
