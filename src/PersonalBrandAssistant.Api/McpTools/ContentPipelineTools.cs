using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.McpTools;

[McpServerToolType]
public static class ContentPipelineTools
{
    private static readonly ContentStatus[] TerminalStatuses =
        [ContentStatus.Published, ContentStatus.Archived];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool]
    [Description("Creates new content in the PBA pipeline from a topic. Use when asked to 'write a post', 'create content about X', or 'draft something for LinkedIn'. Returns the new content ID and initial status. If autonomy is set to manual, creates a draft for approval rather than auto-publishing.")]
    public static async Task<string> pba_create_content(
        IServiceProvider serviceProvider,
        [Description("The topic or subject to create content about")] string topic,
        [Description("Target platform: TwitterX, LinkedIn, Instagram, YouTube, Reddit, PersonalBlog, Substack")] string platform,
        [Description("Content type: BlogPost, SocialPost, Thread, VideoDescription")] string contentType,
        CancellationToken ct)
    {
        if (!Enum.TryParse<PlatformType>(platform, ignoreCase: true, out var platformEnum))
            return Error($"Invalid platform '{platform}'. Valid: {string.Join(", ", Enum.GetNames<PlatformType>())}");

        if (!Enum.TryParse<ContentType>(contentType, ignoreCase: true, out var contentTypeEnum))
            return Error($"Invalid contentType '{contentType}'. Valid: {string.Join(", ", Enum.GetNames<ContentType>())}");

        using var scope = serviceProvider.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<IContentPipeline>();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var autonomy = await dbContext.AutonomyConfigurations
            .FirstOrDefaultAsync(ct);
        var level = autonomy?.GlobalLevel ?? AutonomyLevel.SemiAuto;

        var request = new ContentCreationRequest(contentTypeEnum, topic, null, [platformEnum], null, null);
        var result = await pipeline.CreateFromTopicAsync(request, ct);

        if (!result.IsSuccess)
            return Error(string.Join("; ", result.Errors));

        var status = level is AutonomyLevel.Manual or AutonomyLevel.Assisted
            ? "queued-for-approval"
            : "created";

        return Success(new { contentId = result.Value, status });
    }

    [McpServerTool]
    [Description("Gets the current pipeline status. Without a contentId, returns all active items. With a contentId, returns that specific item's status. Use when asked 'what's in the pipeline', 'status of my post', or 'what stage is content X in'. Stages: Draft, Review, Approved, Scheduled, Publishing, Published.")]
    public static async Task<string> pba_get_pipeline_status(
        IServiceProvider serviceProvider,
        [Description("Optional content ID (GUID) for a specific item. Omit to list all active items.")] string? contentId = null,
        CancellationToken ct = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        if (contentId is not null)
        {
            if (!Guid.TryParse(contentId, out var guid))
                return Error("Invalid contentId format. Must be a GUID.");

            var content = await dbContext.Contents
                .AsNoTracking()
                .Where(c => c.Id == guid)
                .Select(c => new
                {
                    c.Id, c.Title, Stage = c.Status.ToString(),
                    Platform = c.TargetPlatforms.Length > 0 ? c.TargetPlatforms[0].ToString() : "Unknown",
                    ContentType = c.ContentType.ToString(), c.UpdatedAt
                })
                .FirstOrDefaultAsync(ct);

            return content is null
                ? Error($"Content {contentId} not found.")
                : Success(content);
        }

        var items = await dbContext.Contents
            .AsNoTracking()
            .Where(c => !TerminalStatuses.Contains(c.Status))
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new
            {
                c.Id, c.Title, Stage = c.Status.ToString(),
                Platform = c.TargetPlatforms.Length > 0 ? c.TargetPlatforms[0].ToString() : "Unknown",
                ContentType = c.ContentType.ToString(), c.UpdatedAt
            })
            .ToListAsync(ct);

        return Success(new { items, count = items.Count });
    }

    [McpServerTool]
    [Description("Publishes approved content to its target platform. Content must be in Approved state. Use when asked to 'publish my post', 'push content live', or 'send it out'. Returns the publishing status.")]
    public static async Task<string> pba_publish_content(
        IServiceProvider serviceProvider,
        [Description("The content ID (GUID) to publish")] string contentId,
        CancellationToken ct)
    {
        if (!Guid.TryParse(contentId, out var guid))
            return Error("Invalid contentId format. Must be a GUID.");

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var publishingPipeline = scope.ServiceProvider.GetRequiredService<IPublishingPipeline>();

        var content = await dbContext.Contents.FindAsync([guid], ct);
        if (content is null)
            return Error($"Content {contentId} not found.");

        if (content.Status != ContentStatus.Approved)
            return Error($"Content must be in Approved state to publish. Current state: {content.Status}.");

        var result = await publishingPipeline.PublishAsync(guid, ct);
        return result.IsSuccess
            ? Success(new { contentId = guid, status = "publishing" })
            : Error(string.Join("; ", result.Errors));
    }

    [McpServerTool]
    [Description("Lists content drafts with optional filtering. Use when asked 'show my drafts', 'what posts are pending', 'list LinkedIn content', or 'what's waiting for review'. Returns a list of content items with their current status and platform.")]
    public static async Task<string> pba_list_drafts(
        IServiceProvider serviceProvider,
        [Description("Optional status filter: Draft, Review, Approved, Scheduled. Omit for all non-published items.")] string? status = null,
        [Description("Optional platform filter: TwitterX, LinkedIn, Instagram, YouTube, Reddit, PersonalBlog, Substack")] string? platform = null,
        CancellationToken ct = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var query = dbContext.Contents.AsNoTracking().AsQueryable();

        if (status is not null)
        {
            if (!Enum.TryParse<ContentStatus>(status, ignoreCase: true, out var statusEnum))
                return Error($"Invalid status '{status}'. Valid: {string.Join(", ", Enum.GetNames<ContentStatus>())}");
            query = query.Where(c => c.Status == statusEnum);
        }
        else
        {
            query = query.Where(c => !TerminalStatuses.Contains(c.Status));
        }

        if (platform is not null)
        {
            if (!Enum.TryParse<PlatformType>(platform, ignoreCase: true, out var platformEnum))
                return Error($"Invalid platform '{platform}'. Valid: {string.Join(", ", Enum.GetNames<PlatformType>())}");
            query = query.Where(c => c.TargetPlatforms.Contains(platformEnum));
        }

        var items = await query
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new
            {
                c.Id, c.Title, ContentType = c.ContentType.ToString(),
                Status = c.Status.ToString(),
                Platforms = c.TargetPlatforms.Select(p => p.ToString()).ToArray(),
                c.UpdatedAt
            })
            .ToListAsync(ct);

        return Success(new { items, count = items.Count });
    }

    private static string Success(object data) =>
        JsonSerializer.Serialize(new { success = true, data }, JsonOptions);

    private static string Error(string message) =>
        JsonSerializer.Serialize(new { success = false, error = message }, JsonOptions);
}
