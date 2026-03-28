using Microsoft.EntityFrameworkCore;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.Endpoints;

public static class BlogPublishEndpoints
{
    public static void MapBlogPublishEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/content/{contentId:guid}").WithTags("BlogPublish");
        group.MapGet("/blog-prep", GetBlogPrep);
        group.MapPost("/blog-publish", PublishToBlog);
        group.MapGet("/blog-status", GetBlogStatus);
    }

    private static async Task<IResult> GetBlogPrep(
        Guid contentId, IBlogHtmlGenerator generator, CancellationToken ct)
    {
        var result = await generator.GenerateAsync(contentId, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.ErrorCode == ErrorCode.NotFound
                ? Results.NotFound(new { error = result.Errors.FirstOrDefault() })
                : Results.BadRequest(new { error = result.Errors.FirstOrDefault() });
    }

    private static async Task<IResult> PublishToBlog(
        Guid contentId,
        IBlogHtmlGenerator generator,
        IGitHubPublishService publisher,
        IApplicationDbContext db,
        CancellationToken ct)
    {
        var content = await db.Contents.FirstOrDefaultAsync(c => c.Id == contentId, ct);
        if (content is null)
            return Results.NotFound(new { error = "Content not found" });

        if (string.IsNullOrWhiteSpace(content.SubstackPostUrl))
            return Results.BadRequest(new { error = "SubstackPostUrl must be set before publishing to blog. Publish to Substack first." });

        // Regenerate HTML with real canonical URL
        var htmlResult = await generator.GenerateAsync(contentId, ct);
        if (!htmlResult.IsSuccess)
            return Results.BadRequest(new { error = htmlResult.Errors.FirstOrDefault() });

        // Create or update BlogPublishRequest
        var publishRequest = await db.BlogPublishRequests
            .FirstOrDefaultAsync(r => r.ContentId == contentId, ct);

        if (publishRequest is null)
        {
            publishRequest = new Domain.Entities.BlogPublishRequest
            {
                ContentId = contentId,
                Html = htmlResult.Value!.Html,
                TargetPath = htmlResult.Value.FilePath,
                Status = BlogPublishStatus.Publishing
            };
            db.BlogPublishRequests.Add(publishRequest);
        }
        else
        {
            publishRequest.Html = htmlResult.Value!.Html;
            publishRequest.TargetPath = htmlResult.Value.FilePath;
            publishRequest.Status = BlogPublishStatus.Publishing;
            publishRequest.ErrorMessage = null;
        }

        await db.SaveChangesAsync(ct);

        // Commit to GitHub
        var commitResult = await publisher.CommitBlogPostAsync(publishRequest, ct);
        if (!commitResult.IsSuccess)
        {
            publishRequest.Status = BlogPublishStatus.Failed;
            publishRequest.ErrorMessage = commitResult.Errors.FirstOrDefault();
            await db.SaveChangesAsync(ct);

            return Results.UnprocessableEntity(new { error = commitResult.Errors.FirstOrDefault() });
        }

        publishRequest.CommitSha = commitResult.Value!.CommitSha;
        publishRequest.CommitUrl = commitResult.Value.CommitUrl;

        // Build blog URL from deploy verification pattern
        var blogUrl = content.BlogPostUrl
            ?? htmlResult.Value.FilePath.Replace("content/blog/", "https://matthewkruczek.ai/blog/")
                .Replace(".html", "");

        // Verify deployment
        var deployed = await publisher.VerifyDeploymentAsync(blogUrl, ct);

        if (deployed)
        {
            publishRequest.Status = BlogPublishStatus.Published;
            publishRequest.BlogUrl = blogUrl;
            content.BlogPostUrl = blogUrl;
            content.BlogDeployCommitSha = commitResult.Value.CommitSha;

            // Update platform status for PersonalBlog
            var platformStatus = await db.ContentPlatformStatuses
                .FirstOrDefaultAsync(s => s.ContentId == contentId && s.Platform == PlatformType.PersonalBlog, ct);
            if (platformStatus is not null)
            {
                platformStatus.Status = PlatformPublishStatus.Published;
                platformStatus.PostUrl = blogUrl;
                platformStatus.PublishedAt = DateTimeOffset.UtcNow;
            }
        }
        else
        {
            publishRequest.Status = BlogPublishStatus.Failed;
            publishRequest.ErrorMessage = "Deploy verification timed out";

            var platformStatus = await db.ContentPlatformStatuses
                .FirstOrDefaultAsync(s => s.ContentId == contentId && s.Platform == PlatformType.PersonalBlog, ct);
            if (platformStatus is not null)
            {
                platformStatus.Status = PlatformPublishStatus.Failed;
                platformStatus.ErrorMessage = "Deploy verification timed out";
            }
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            commitSha = commitResult.Value.CommitSha,
            commitUrl = commitResult.Value.CommitUrl,
            blogUrl,
            status = publishRequest.Status.ToString(),
            deployed
        });
    }

    private static async Task<IResult> GetBlogStatus(
        Guid contentId, IApplicationDbContext db, CancellationToken ct)
    {
        var publishRequest = await db.BlogPublishRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ContentId == contentId, ct);

        if (publishRequest is null)
            return Results.NotFound(new { error = "No blog publish request found for this content" });

        return Results.Ok(new
        {
            commitSha = publishRequest.CommitSha,
            blogUrl = publishRequest.BlogUrl,
            status = publishRequest.Status.ToString(),
            publishedAt = publishRequest.Status == BlogPublishStatus.Published
                ? publishRequest.UpdatedAt
                : (DateTimeOffset?)null,
            errorMessage = publishRequest.ErrorMessage
        });
    }
}
