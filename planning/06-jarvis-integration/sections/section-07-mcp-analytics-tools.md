# Section 07: MCP Analytics Tools

## Overview

An MCP tool class `AnalyticsTools` exposing three tools that wrap the existing analytics, engagement aggregation, and trend monitoring services. These tools allow Jarvis to query trending topics, engagement statistics, and per-content performance data through voice commands or chat.

All three tools are read-only -- they do not modify any data and do not require autonomy checks.

## Dependencies

- **section-04-mcp-server-infrastructure**: The MCP server infrastructure must be in place. Tool classes are discovered via `[McpServerToolType]` assembly scanning.

## Existing Services

The tools wrap these existing application-layer interfaces:

- `ITrendMonitor` -- `GetSuggestionsAsync(limit, ct)` returns `IReadOnlyList<TrendSuggestion>` sorted by relevance.
- `IEngagementAggregator` -- `GetPerformanceAsync(contentId, ct)` returns `ContentPerformanceReport`, `GetTopContentAsync(from, to, limit, ct)` returns `IReadOnlyList<TopPerformingContent>`.
- `IApplicationDbContext` -- Direct queries on `EngagementSnapshots` and `ContentPlatformStatuses` for aggregated engagement stats.

Key domain models:

```csharp
// TrendSuggestion entity has:
//   Guid Id, string Topic, double RelevanceScore, TrendSuggestionStatus Status,
//   ICollection<TrendSuggestionItem> Items (individual trend data points)

// ContentPerformanceReport record:
//   Guid ContentId,
//   IReadOnlyDictionary<PlatformType, EngagementSnapshot> LatestByPlatform,
//   int TotalEngagement, decimal? LlmCost, decimal? CostPerEngagement

// EngagementSnapshot entity has:
//   Guid Id, Guid ContentPlatformStatusId, int Likes, int Comments,
//   int Shares, int Impressions, DateTimeOffset CapturedAt
```

## Tests (Write First)

Test file: `tests/PersonalBrandAssistant.Application.Tests/McpServer/AnalyticsToolsTests.cs`

Use xUnit + Moq. Mock `ITrendMonitor`, `IEngagementAggregator`, and `IApplicationDbContext`.

```csharp
// --- pba_get_trends ---

// Test: returns topics sorted by relevance
//   Mock ITrendMonitor.GetSuggestionsAsync to return 5 suggestions
//   Call pba_get_trends
//   Assert result JSON contains 5 items ordered by relevanceScore descending

// Test: respects limit parameter
//   Mock to return 10 suggestions
//   Call pba_get_trends(limit: 3)
//   Assert ITrendMonitor was called with limit 3
//   Assert result JSON contains 3 items

// Test: returns default limit when not specified
//   Call pba_get_trends without limit
//   Assert ITrendMonitor was called with a sensible default (e.g., 10)


// --- pba_get_engagement_stats ---

// Test: aggregates by platform
//   Seed EngagementSnapshots for Twitter and LinkedIn
//   Call pba_get_engagement_stats for the seeded date range
//   Assert result JSON contains per-platform breakdown with correct totals

// Test: filters by date range
//   Seed snapshots across 30 days
//   Call with a 7-day range
//   Assert only data within the range is included in totals

// Test: filters by platform
//   Seed snapshots for Twitter, LinkedIn, Reddit
//   Call with platform: "LinkedIn"
//   Assert result contains only LinkedIn metrics

// Test: returns zero metrics for empty date range
//   Call with a range that has no data
//   Assert result JSON shows zero totals and empty breakdown

// Test: includes total engagement across all platforms
//   Seed multi-platform data
//   Assert result has a totalEngagement field summing all platforms


// --- pba_get_content_performance ---

// Test: returns metrics for published content
//   Mock IEngagementAggregator.GetPerformanceAsync to return a report
//   Call pba_get_content_performance with a valid contentId
//   Assert result JSON contains per-platform metrics, total engagement, cost data

// Test: returns error for unpublished content
//   Mock GetPerformanceAsync to return a not-found result
//   Call with an ID for unpublished content
//   Assert result JSON contains error message

// Test: returns error for non-existent content
//   Call with a GUID that does not exist
//   Assert result JSON contains not-found error

// Test: includes platform-specific metric breakdown
//   Mock report with metrics for 2 platforms
//   Assert result has per-platform entries with likes, comments, shares, impressions
```

## File Paths

### New Files

- `src/PersonalBrandAssistant.Api/McpTools/AnalyticsTools.cs` -- The tool class with 3 MCP tools.
- `tests/PersonalBrandAssistant.Application.Tests/McpServer/AnalyticsToolsTests.cs` -- Tests.

## Tool Definitions

### pba_get_trends

```csharp
[McpServerTool]
[Description("Returns current trending topics with relevance scores and suggested content angles. Use when asked 'what's trending', 'any hot topics', 'what should I write about', or 'show me content ideas'. Returns topics sorted by relevance with source attribution.")]
public static async Task<string> pba_get_trends(
    IServiceProvider serviceProvider,
    [Description("Maximum number of trends to return (default: 10, max: 25)")] int? limit,
    CancellationToken ct)
```

Implementation logic:
1. Resolve `ITrendMonitor` from a new DI scope.
2. Clamp the limit: `Math.Clamp(limit ?? 10, 1, 25)`.
3. Call `GetSuggestionsAsync(clampedLimit, ct)`.
4. If the result is a failure, return error JSON with the failure message.
5. Project each `TrendSuggestion` to a response shape: topic, relevanceScore, source (derived from the suggestion's `Items` collection -- aggregate the sources), status, suggestedAngles (if available from the suggestion data), createdAt.
6. Serialize and return.

Response shape:

```json
{
  "trends": [
    {
      "topic": "AI Agents in Enterprise",
      "relevanceScore": 0.92,
      "sources": ["Reddit", "HackerNews"],
      "status": "New",
      "createdAt": "2026-03-23T08:00:00Z"
    }
  ],
  "count": 5,
  "asOf": "2026-03-23T14:00:00Z"
}
```

### pba_get_engagement_stats

```csharp
[McpServerTool]
[Description("Returns engagement metrics (likes, shares, comments, impressions) aggregated by platform and date. Use when asked 'how are my posts doing', 'show engagement stats', 'LinkedIn performance this week', or 'social media numbers'. Returns total engagement, per-platform breakdown, and daily averages.")]
public static async Task<string> pba_get_engagement_stats(
    IServiceProvider serviceProvider,
    [Description("Start date in ISO 8601 format (e.g., '2026-03-16')")] string startDate,
    [Description("End date in ISO 8601 format (e.g., '2026-03-23')")] string endDate,
    [Description("Optional platform filter: Twitter, LinkedIn, Reddit, Blog. Omit for all platforms.")] string? platform,
    CancellationToken ct)
```

Implementation logic:
1. Parse `startDate` and `endDate` to `DateTimeOffset`. Return validation error if parsing fails.
2. If `platform` is provided, parse to `PlatformType`. Return validation error if invalid.
3. Resolve `IApplicationDbContext` from a new DI scope.
4. Query `EngagementSnapshots` joined with `ContentPlatformStatuses` to get platform information:
   - Filter by `CapturedAt` within the date range.
   - If platform is specified, filter to that platform.
   - Group by platform.
   - For each platform, sum likes, comments, shares, impressions.
5. Calculate `totalEngagement` as sum across all platforms.
6. Calculate `dailyAverage` as totalEngagement divided by the number of days in the range.
7. Assemble the per-platform breakdown with individual metric totals.
8. Serialize and return.

Response shape:

```json
{
  "totalEngagement": 4250,
  "dailyAverage": 607,
  "dateRange": { "start": "2026-03-16", "end": "2026-03-23" },
  "platformBreakdown": {
    "Twitter": {
      "likes": 820,
      "comments": 145,
      "shares": 310,
      "impressions": 12500,
      "totalEngagement": 1275
    },
    "LinkedIn": {
      "likes": 650,
      "comments": 230,
      "shares": 180,
      "impressions": 8900,
      "totalEngagement": 1060
    }
  }
}
```

### pba_get_content_performance

```csharp
[McpServerTool]
[Description("Returns detailed performance data for a specific published content item. Use when asked 'how did that post do', 'performance of my LinkedIn article', or 'engagement on content X'. Returns platform-specific metrics including likes, comments, shares, impressions, total engagement, and cost data. Content must be published to have performance data.")]
public static async Task<string> pba_get_content_performance(
    IServiceProvider serviceProvider,
    [Description("The content ID to get performance for (GUID format)")] string contentId,
    CancellationToken ct)
```

Implementation logic:
1. Parse `contentId` to GUID. Return validation error if invalid.
2. Resolve `IEngagementAggregator` from a new DI scope.
3. Call `GetPerformanceAsync(parsedGuid, ct)`.
4. If the result is a failure (not found or not published), return error JSON.
5. Map the `ContentPerformanceReport` to a response shape:
   - Per-platform metrics from `LatestByPlatform` dictionary: likes, comments, shares, impressions, capturedAt.
   - `totalEngagement` from the report.
   - `llmCost` and `costPerEngagement` if available.
6. Serialize and return.

Response shape:

```json
{
  "contentId": "guid",
  "totalEngagement": 1450,
  "llmCost": 0.0042,
  "costPerEngagement": 0.0000029,
  "platforms": {
    "Twitter": {
      "likes": 420,
      "comments": 85,
      "shares": 210,
      "impressions": 15200,
      "capturedAt": "2026-03-23T12:00:00Z"
    },
    "LinkedIn": {
      "likes": 380,
      "comments": 145,
      "shares": 120,
      "impressions": 9800,
      "capturedAt": "2026-03-23T12:00:00Z"
    }
  }
}
```

## Response Serialization

Same pattern as sections 05 and 06 -- all tools return JSON strings using `System.Text.Json.JsonSerializer` with camelCase naming policy.

```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false
};
```

Error responses follow the consistent shape:

```json
{
  "error": true,
  "message": "Description of what went wrong"
}
```

## Engagement Data Query Pattern

The `pba_get_engagement_stats` tool requires joining `EngagementSnapshots` with `ContentPlatformStatuses` to get the platform dimension. The query pattern:

```csharp
var query = dbContext.EngagementSnapshots
    .AsNoTracking()
    .Include(s => s.ContentPlatformStatus)
    .Where(s => s.CapturedAt >= from && s.CapturedAt <= to);

if (targetPlatform is not null)
{
    query = query.Where(s => s.ContentPlatformStatus.Platform == targetPlatform.Value);
}

var grouped = await query
    .GroupBy(s => s.ContentPlatformStatus.Platform)
    .Select(g => new
    {
        Platform = g.Key,
        Likes = g.Sum(s => s.Likes),
        Comments = g.Sum(s => s.Comments),
        Shares = g.Sum(s => s.Shares),
        Impressions = g.Sum(s => s.Impressions)
    })
    .ToListAsync(ct);
```

This query runs entirely in the database, avoiding loading all snapshots into memory.

## Implementation Notes

- All three tools are read-only. No autonomy checks needed. No idempotency handling needed.
- Each tool method creates its own DI scope via `serviceProvider.CreateScope()`.
- Date parsing should use `DateTimeOffset.TryParse` with `CultureInfo.InvariantCulture` for reliable ISO 8601 parsing.
- The `pba_get_trends` tool maps `TrendSuggestion` entities. The `Items` collection on each suggestion contains the individual trend data points with source information. Aggregate the distinct `TrendSourceType` values from the items to build the "sources" list in the response.
- The `pba_get_content_performance` tool delegates entirely to the existing `IEngagementAggregator.GetPerformanceAsync`. The `ContentPerformanceReport` already has the right shape -- the tool just reformats it for LLM consumption.
- Engagement totals (likes + comments + shares) are the standard "engagement" metric. Impressions are tracked separately since they represent reach, not interaction.
- The `EngagementSnapshot` entity's `ContentPlatformStatus` navigation property must be included for the platform grouping query. Ensure the relationship is loaded via `Include` or a projected query.
