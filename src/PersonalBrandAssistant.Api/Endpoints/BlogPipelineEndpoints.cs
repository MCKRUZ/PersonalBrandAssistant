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
                c.BlogDelayOverride
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
        if (request.DelayDays is < 0 or > 365)
            return Results.BadRequest(new { error = "DelayDays must be between 0 and 365" });

        var content = await db.Contents.FirstOrDefaultAsync(c => c.Id == contentId, ct);
        if (content is null)
            return Results.NotFound(new { error = "Content not found" });

        content.BlogDelayOverride = request.DelayDays.HasValue
            ? TimeSpan.FromDays(request.DelayDays.Value)
            : null;

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { blogDelayDays = content.BlogDelayOverride?.TotalDays });
    }

    private static async Task<IResult> SkipBlog(
        Guid contentId, IApplicationDbContext db, CancellationToken ct)
    {
        var content = await db.Contents.FirstOrDefaultAsync(c => c.Id == contentId, ct);
        if (content is null)
            return Results.NotFound(new { error = "Content not found" });

        content.BlogSkipped = true;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { blogSkipped = true });
    }
}

public record DelayUpdateRequest(double? DelayDays);
