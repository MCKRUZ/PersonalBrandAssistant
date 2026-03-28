using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Api.McpTools;

[McpServerToolType]
public static class AnalyticsTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [McpServerTool]
    [Description("Returns current trending topics with relevance scores. Use when asked 'what's trending', 'any hot topics', or 'give me content ideas based on current topics'.")]
    public static async Task<string> pba_get_trends(
        IServiceProvider serviceProvider,
        [Description("Maximum number of trending topics to return. Defaults to 10.")] int limit = 10,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);

        using var scope = serviceProvider.CreateScope();
        var trendMonitor = scope.ServiceProvider.GetRequiredService<ITrendMonitor>();

        var result = await trendMonitor.GetSuggestionsAsync(limit, ct);
        if (!result.IsSuccess)
            return Error(string.Join("; ", result.Errors));

        var topics = result.Value!.Select(t => new
        {
            t.Topic,
            t.RelevanceScore,
            SuggestedContentType = t.SuggestedContentType.ToString(),
            SuggestedPlatforms = t.SuggestedPlatforms.Select(p => p.ToString()).ToArray(),
            t.Rationale
        }).ToList();

        return Success(new { topics, count = topics.Count });
    }

    [McpServerTool]
    [Description("Returns engagement metrics aggregated by platform and date range. Use when asked 'how's my engagement', 'give me this week's numbers', or 'LinkedIn performance'.")]
    public static async Task<string> pba_get_engagement_stats(
        IServiceProvider serviceProvider,
        [Description("Start date in ISO 8601 format, e.g. '2026-03-16T00:00:00Z'")] string startDate,
        [Description("End date in ISO 8601 format, e.g. '2026-03-23T23:59:59Z'")] string endDate,
        [Description("Optional platform filter: TwitterX, LinkedIn, Instagram, YouTube, Reddit")] string? platform = null,
        CancellationToken ct = default)
    {
        if (!DateTimeOffset.TryParse(startDate, out var from))
            return Error("Invalid startDate format. Use ISO 8601.");
        if (!DateTimeOffset.TryParse(endDate, out var to))
            return Error("Invalid endDate format. Use ISO 8601.");

        using var scope = serviceProvider.CreateScope();
        var aggregator = scope.ServiceProvider.GetRequiredService<IEngagementAggregator>();

        var result = await aggregator.GetTopContentAsync(from, to, 50, ct);
        if (!result.IsSuccess)
            return Error(string.Join("; ", result.Errors));

        var items = result.Value!.AsEnumerable();

        var platformBreakdown = new Dictionary<string, int>();
        var totalEngagement = 0;

        foreach (var item in items)
        {
            foreach (var (plat, eng) in item.EngagementByPlatform)
            {
                var platName = plat.ToString();
                if (platform is not null && !platName.Equals(platform, StringComparison.OrdinalIgnoreCase))
                    continue;
                platformBreakdown[platName] = platformBreakdown.GetValueOrDefault(platName) + eng;
                totalEngagement += eng;
            }
        }

        var topContent = result.Value!
            .OrderByDescending(i => i.TotalEngagement)
            .Take(10)
            .Select(i => new { i.ContentId, i.Title, i.TotalEngagement })
            .ToList();

        return Success(new { totalEngagement, platformBreakdown, topContent, dateRange = new { from, to } });
    }

    [McpServerTool]
    [Description("Returns detailed performance data for a specific published content item. Use when asked 'how did that post do' or 'performance of my latest article'.")]
    public static async Task<string> pba_get_content_performance(
        IServiceProvider serviceProvider,
        [Description("Content ID (GUID) to get performance data for")] string contentId,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(contentId, out var guid))
            return Error("Invalid contentId format. Must be a GUID.");

        using var scope = serviceProvider.CreateScope();
        var aggregator = scope.ServiceProvider.GetRequiredService<IEngagementAggregator>();

        var result = await aggregator.GetPerformanceAsync(guid, ct);
        if (!result.IsSuccess)
            return Error(string.Join("; ", result.Errors));

        var report = result.Value!;
        var platforms = report.LatestByPlatform.Select(kvp => new
        {
            Platform = kvp.Key.ToString(),
            kvp.Value.Likes,
            kvp.Value.Comments,
            kvp.Value.Shares,
            kvp.Value.Impressions,
            kvp.Value.Clicks,
            kvp.Value.FetchedAt
        }).ToList();

        return Success(new
        {
            report.ContentId,
            report.TotalEngagement,
            report.LlmCost,
            report.CostPerEngagement,
            platforms
        });
    }

    private static string Success(object data) =>
        JsonSerializer.Serialize(new { success = true, data }, JsonOptions);

    private static string Error(string message) =>
        JsonSerializer.Serialize(new { success = false, error = message }, JsonOptions);
}
