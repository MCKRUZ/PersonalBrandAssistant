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
public static class SocialEngagementTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool]
    [Description("Returns engagement opportunities ranked by relevance. Use when asked 'find posts to engage with', 'what should I comment on', 'show me opportunities', or 'any good Reddit threads'.")]
    public static async Task<string> pba_get_opportunities(
        IServiceProvider serviceProvider,
        [Description("Optional platform filter: Reddit, TwitterX, LinkedIn. Omit for all.")] string? platform = null,
        [Description("Maximum number of opportunities. Defaults to 10.")] int limit = 10,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 50);

        using var scope = serviceProvider.CreateScope();
        var engagementService = scope.ServiceProvider.GetRequiredService<ISocialEngagementService>();

        var discoverResult = await engagementService.DiscoverOpportunitiesAsync(ct);
        if (!discoverResult.IsSuccess)
            return Error(string.Join("; ", discoverResult.Errors));

        var items = discoverResult.Value!.AsEnumerable();

        if (platform is not null)
        {
            if (!Enum.TryParse<PlatformType>(platform, ignoreCase: true, out _))
                return Error($"Invalid platform '{platform}'. Valid: {string.Join(", ", Enum.GetNames<PlatformType>())}");
            items = items.Where(o => o.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase));
        }

        var result = items.Take(limit).Select(o => new
        {
            o.PostId,
            o.PostUrl,
            o.Title,
            o.ContentPreview,
            o.Community,
            o.Platform,
            o.DiscoveredAt,
            o.ImpactScore,
            o.Category
        }).ToList();

        return Success(new { opportunities = result, count = result.Count });
    }

    [McpServerTool]
    [Description("Drafts or sends a response to an engagement opportunity. Use when asked to 'reply to that post', 'engage with this thread', or 'comment on the Reddit question'. If no responseText given, AI generates a response. Autonomy dial controls send vs queue.")]
    public static async Task<string> pba_respond_to_opportunity(
        IServiceProvider serviceProvider,
        [Description("The opportunity post URL or post ID to respond to")] string opportunityId,
        [Description("Optional custom response text. If omitted, AI generates a contextual response.")] string? responseText = null,
        CancellationToken ct = default)
    {
        using var scope = serviceProvider.CreateScope();
        var engagementService = scope.ServiceProvider.GetRequiredService<ISocialEngagementService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var autonomy = await dbContext.AutonomyConfigurations.FirstOrDefaultAsync(ct);
        var level = autonomy?.GlobalLevel ?? AutonomyLevel.SemiAuto;

        if (level == AutonomyLevel.Manual)
        {
            return Success(new
            {
                status = "queued-for-approval",
                opportunityId,
                responseText,
                message = "Response queued for manual approval. Current autonomy level: Manual."
            });
        }

        var discoverResult2 = await engagementService.DiscoverOpportunitiesAsync(ct);
        if (!discoverResult2.IsSuccess)
            return Error(string.Join("; ", discoverResult2.Errors));

        var opportunity = discoverResult2.Value!.FirstOrDefault(o =>
            o.PostUrl == opportunityId || o.PostId == opportunityId);

        if (opportunity is null)
            return Error($"Opportunity '{opportunityId}' not found. Try passing the exact post URL from pba_get_opportunities.");

        var dto = new EngageSingleDto(
            opportunity.Platform,
            opportunity.PostId,
            opportunity.PostUrl,
            opportunity.Title,
            opportunity.ContentPreview,
            opportunity.Community);

        var result = await engagementService.EngageSingleAsync(dto, ct);

        return result.IsSuccess
            ? Success(new { status = "sent", opportunityId, platform = opportunity.Platform })
            : Error(string.Join("; ", result.Errors));
    }

    [McpServerTool]
    [Description("Returns social inbox items including mentions, DMs, comments, and replies. Use when asked 'check my inbox', 'any new mentions', 'show unread messages', or 'what DMs do I have'.")]
    public static async Task<string> pba_get_inbox(
        IServiceProvider serviceProvider,
        [Description("Optional platform filter: Reddit, TwitterX, LinkedIn. Omit for all.")] string? platform = null,
        [Description("If true, returns only unread items. Omit or false for all.")] bool unreadOnly = false,
        CancellationToken ct = default)
    {
        PlatformType? platformEnum = null;
        if (platform is not null)
        {
            if (!Enum.TryParse<PlatformType>(platform, ignoreCase: true, out var parsed))
                return Error($"Invalid platform '{platform}'. Valid: {string.Join(", ", Enum.GetNames<PlatformType>())}");
            platformEnum = parsed;
        }

        using var scope = serviceProvider.CreateScope();
        var inboxService = scope.ServiceProvider.GetRequiredService<ISocialInboxService>();

        var filter = new InboxFilterDto(platformEnum, unreadOnly ? false : null, 50);
        var inboxResult = await inboxService.GetItemsAsync(filter, ct);
        if (!inboxResult.IsSuccess)
            return Error(string.Join("; ", inboxResult.Errors));

        var result = inboxResult.Value!.Select(i => new
        {
            i.Id,
            Platform = i.Platform.ToString(),
            ItemType = i.ItemType.ToString(),
            i.AuthorName,
            i.Content,
            i.SourceUrl,
            i.IsRead,
            i.DraftReply,
            i.ReplySent,
            i.ReceivedAt
        }).ToList();

        return Success(new { items = result, count = result.Count });
    }

    private static string Success(object data) =>
        JsonSerializer.Serialize(new { success = true, data }, JsonOptions);

    private static string Error(string message) =>
        JsonSerializer.Serialize(new { success = false, error = message }, JsonOptions);
}
