# Section 04 -- Dashboard Aggregator

## Overview

This section implements the `DashboardAggregator` service, which orchestrates all analytics data sources (social engagement from the database, website analytics from GA4, Substack from RSS, and LLM cost data) into unified dashboard responses. It is the central backend coordination layer that powers the analytics dashboard KPI cards, timeline chart, and platform summary cards.

**Depends on:** section-01-backend-models (interfaces + DTOs), section-02-google-analytics-service (`IGoogleAnalyticsService`), section-03-substack-service (`ISubstackService`)

**Blocks:** section-05-caching-resilience, section-06-api-endpoints

---

## Key Design Decisions

### Partial Failure Model

The aggregator never fails the entire response because one data source is down. Each section of the response is populated independently, and failures are logged but do not propagate to the caller. If GA4 is down, social data still returns; if the database query for engagement fails, website analytics still returns.

### Period Comparison

For period-over-period comparison (the "previous" values in `DashboardSummary`):
- `from` is inclusive at `00:00:00 UTC`, `to` is inclusive end-of-day `23:59:59 UTC`
- Previous period window: `previousFrom = from - (to - from + 1 day)`, `previousTo = from - 1 day` (equal-length mirror window)
- Example: if current = Jan 15-31 (17 days), previous = Dec 29 - Jan 14 (17 days)

### Division by Zero / Null Semantics

- Engagement rate: return `0` when impressions denominator is 0
- % change: return `null` when previous period value is 0 (frontend renders "N/A")
- Cost per engagement: return `null` when total engagement is 0

### Platform Data Availability

| Platform | Has Engagement | Has Impressions | Has Followers |
|---|---|---|---|
| Twitter/X | Yes | Yes | Yes |
| YouTube | Yes | Yes (views) | Yes (subscribers) |
| Instagram | Yes | Yes | Yes |
| Reddit | Yes | No | No (karma only) |
| LinkedIn | No | No | No |

Missing metrics are treated as `null` (not 0) and are excluded from aggregate denominators.

---

## Existing Codebase Context

The aggregator depends on the existing EF Core entities and `IApplicationDbContext`. Key entities:

**`EngagementSnapshot`** -- stores per-fetch engagement data:
- `ContentPlatformStatusId` (FK), `Likes`, `Comments`, `Shares`, `Impressions?`, `Clicks?`, `FetchedAt`

**`ContentPlatformStatus`** -- links content to a platform publish:
- `ContentId`, `Platform` (PlatformType enum), `Status` (PlatformPublishStatus enum), `PublishedAt?`

**`Content`** -- the content entity:
- `Status` (ContentStatus enum), `PublishedAt?`, `Title`, `ContentType`

**`AgentExecution`** -- tracks LLM usage and cost:
- `ContentId?`, `Status` (AgentExecutionStatus enum), `Cost` (decimal)

**`PlatformType` enum:** `TwitterX, LinkedIn, Instagram, YouTube, Reddit, PersonalBlog, Substack`

**`PlatformPublishStatus` enum:** `Pending, Published, Failed, RateLimited, Skipped, Processing`

**`ContentStatus` enum:** `Draft, Review, Approved, Scheduled, Publishing, Published, Failed, Archived`

The existing `IEngagementAggregator` / `EngagementAggregator` handles per-content engagement fetching and top-content queries. The new `IDashboardAggregator` is a higher-level orchestrator that consumes the same database tables but provides dashboard-wide aggregations.

The `Result<T>` pattern is used throughout: `Result<T>.Success(value)`, `Result<T>.Failure(ErrorCode, errors)`, `Result<T>.NotFound(msg)`.

---

## Files Created (Actual)

| File | Project | Purpose |
|---|---|---|
| `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/DashboardAggregator.cs` | Infrastructure | Implementation |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Analytics/DashboardAggregatorTests.cs` | Tests | Unit tests |
| `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` | Infrastructure | DI registration |

**Deviation from plan:** Test file placed in `Services/Analytics/` (not `Services/AnalyticsServices/`) to match existing convention.

### Files from Dependencies (created in earlier sections, referenced here)

| File | Project | Contains |
|---|---|---|
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IDashboardAggregator.cs` | Application | Interface (section-01) |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IGoogleAnalyticsService.cs` | Application | Interface (section-01) |
| `src/PersonalBrandAssistant.Application/Common/Models/DashboardModels.cs` | Application | DTOs (section-01) |
| `src/PersonalBrandAssistant.Application/Common/Models/GoogleAnalyticsModels.cs` | Application | GA4 DTOs (section-01) |

---

## Tests First

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AnalyticsServices/DashboardAggregatorTests.cs`

The test class follows the same conventions as `EngagementAggregatorTests.cs`: Moq for interfaces, `MockQueryable.Moq` for `DbSet<T>` mocking, xUnit facts, AAA structure.

### Test Setup

The SUT constructor requires:
- `IApplicationDbContext` (mocked) -- for querying `Contents`, `ContentPlatformStatuses`, `EngagementSnapshots`, `AgentExecutions`
- `IGoogleAnalyticsService` (mocked) -- for website analytics
- `ILogger<DashboardAggregator>` (mocked)

Helper methods to create test entities should reuse the same patterns from `EngagementAggregatorTests` (set `Id` via reflection on `EntityBase`, create `ContentPlatformStatus` with `PlatformPublishStatus.Published`, create `EngagementSnapshot` with configurable metrics).

### GetSummaryAsync Tests

1. **Returns correct TotalEngagement summing likes+comments+shares across all snapshots in range** -- Create 2 platforms with published content, each having engagement snapshots within the date range. Verify that `TotalEngagement = sum(Likes + Comments + Shares)` across the latest snapshot per `ContentPlatformStatus`.

2. **Returns correct TotalImpressions including GA4 page views** -- Mock `IGoogleAnalyticsService.GetOverviewAsync` to return a `WebsiteOverview` with known `PageViews`. Create social snapshots with known `Impressions`. Verify the total impressions aggregates both social impressions (from snapshots) and GA4 page views.

3. **Calculates EngagementRate correctly (engagement / impressions * 100)** -- Set up known engagement and impression values. Verify `EngagementRate` equals `(totalEngagement / totalImpressions) * 100` as a decimal.

4. **Returns 0 engagement rate when impressions is 0** -- Create engagement snapshots with `null` impressions and mock GA4 to fail. Verify `EngagementRate == 0`.

5. **Calculates previous period comparison with correct date offset** -- Pass a `from`/`to` representing 7 days. Create snapshots in both the current and previous windows. Verify `PreviousEngagement` sums only snapshots from the previous window (days -14 to -8 relative to `to`).

6. **Returns null for % change when previous period value is 0** -- Create data only in the current period, none in the previous period. The `DashboardSummary` should have `PreviousEngagement == 0`. The frontend uses this to show "N/A" (the aggregator just returns the raw previous values, not the percentage).

7. **Returns partial data when GA4 fails but social data succeeds** -- Mock `IGoogleAnalyticsService.GetOverviewAsync` to return `Result.Failure`. Social DB queries succeed. Verify `WebsiteUsers == 0` (or null), but `TotalEngagement` is correctly populated.

8. **Includes WebsiteUsers from GA4 overview** -- Mock GA4 to return `ActiveUsers = 1500`. Verify `DashboardSummary.WebsiteUsers == 1500`.

9. **Calculates CostPerEngagement from AgentExecution records** -- Create `AgentExecution` records with known `Cost` values for content within the date range. Verify `CostPerEngagement == totalCost / totalEngagement`.

### GetTimelineAsync Tests

10. **Groups daily engagement by platform with correct likes/comments/shares breakdown** -- Create snapshots across 3 days for 2 platforms. Verify each `DailyEngagement` entry has the correct `PlatformDailyMetrics` per platform per day, with accurate likes/comments/shares.

11. **Fills missing dates with zero values (no gaps in timeline)** -- Create snapshots for Day 1 and Day 3 only. Verify the result includes Day 2 with zero totals (gap-filling).

12. **Handles 1-day range (single data point)** -- Pass `from == to`. Verify a single `DailyEngagement` entry is returned.

13. **Handles 90-day range without performance issues** -- Not a performance benchmark, but verify the query shape is correct: ensure the method returns 90 entries with gap-filling. This is a correctness test, not a timing test.

### GetPlatformSummariesAsync Tests

14. **Returns summary per active platform with correct post count** -- Create content published to Twitter and YouTube. Verify one `PlatformSummary` per platform with correct `PostCount`.

15. **Calculates average engagement per post correctly** -- Two Twitter posts, one with 30 total engagement, one with 10. Verify `AvgEngagement == 20.0`.

16. **Identifies top performing post title per platform** -- Two posts on same platform. The one with higher engagement should appear as `TopPostTitle`.

17. **Marks LinkedIn as isAvailable=false** -- Verify the LinkedIn entry in the returned list has `IsAvailable == false`.

18. **Returns null followerCount when platform adapter does not provide it** -- Platforms like Reddit that do not report follower count should have `FollowerCount == null`.

---

## Implementation Details

File: `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/DashboardAggregator.cs`

### Constructor Dependencies

```csharp
public class DashboardAggregator : IDashboardAggregator
{
    private readonly IApplicationDbContext _db;
    private readonly IGoogleAnalyticsService _ga;
    private readonly ILogger<DashboardAggregator> _logger;
    // Constructor injects all three
}
```

No dependency on `ISubstackService` -- the aggregator deals with social engagement and GA4 data. Substack posts are served directly by the API endpoint from `ISubstackService` (no aggregation needed).

### GetSummaryAsync Implementation Flow

1. **Compute date boundaries.** Accept `from` and `to` as parameters. Calculate `previousFrom` and `previousTo` using the mirror-window formula.

2. **Query published content count.** Count `Contents` where `Status == ContentStatus.Published` and `PublishedAt` falls within the date range. Do the same for the previous period.

3. **Query engagement snapshots for current period.** Join `EngagementSnapshots` (by `FetchedAt` in range) through `ContentPlatformStatuses` (by `ContentPlatformStatusId`). For each `ContentPlatformStatus`, take only the latest snapshot (max `FetchedAt`). Sum `Likes + Comments + Shares` across all latest snapshots for `TotalEngagement`. Sum non-null `Impressions` for social impressions.

4. **Query engagement for previous period.** Same query shape but with `previousFrom`/`previousTo` date boundaries. Produces `PreviousEngagement` and `PreviousImpressions`.

5. **Fetch GA4 website overview.** Call `_ga.GetOverviewAsync(from, to, ct)`. If it succeeds, add `PageViews` to total impressions and capture `ActiveUsers` as `WebsiteUsers`. If it fails, log warning and continue with `WebsiteUsers = 0`. Do the same for previous period GA4 call.

6. **Calculate engagement rate.** `EngagementRate = totalImpressions > 0 ? (decimal)totalEngagement / totalImpressions * 100 : 0`. Same for previous period.

7. **Calculate cost per engagement.** Query `AgentExecutions` where `ContentId` is in the set of published content IDs and `Status == Completed`. Sum `Cost` column. `CostPerEngagement = totalEngagement > 0 ? totalCost / totalEngagement : null`. Same for previous period.

8. **Assemble and return `DashboardSummary`.**

### GetTimelineAsync Implementation Flow

1. **Determine the set of dates** from `from` to `to` (inclusive). Generate a list of `DateOnly` values.

2. **Query all engagement snapshots in the date range.** Join `EngagementSnapshots` to `ContentPlatformStatuses` to get the `Platform` for each snapshot. Group by `(DateOnly from FetchedAt, Platform)`.

3. **For each date-platform group,** take the latest snapshot per `ContentPlatformStatusId`, then sum `Likes`, `Comments`, `Shares` across all statuses for that platform on that day.

4. **Gap-fill.** For each date in the range, if no data exists for a platform, insert a zero-valued `PlatformDailyMetrics`. The set of platforms to include: all platforms that have at least one snapshot in the entire range (so an unused platform does not appear with all zeros).

5. **Assemble `DailyEngagement` list** with per-platform breakdown and a `Total` summing all platforms.

### GetPlatformSummariesAsync Implementation Flow

1. **Query all `ContentPlatformStatuses`** with `Status == Published` and `PublishedAt` in range. Group by `Platform`.

2. **For each platform group:**
   - `PostCount` = count of statuses
   - Fetch latest engagement snapshot per status, sum engagement per post
   - `AvgEngagement` = total engagement / post count
   - Identify the post with highest engagement, look up `Content.Title` for `TopPostTitle`, use `ContentPlatformStatus.PostUrl` for `TopPostUrl`
   - `FollowerCount` = null (follower data comes from platform adapters at the API layer in a later section, or is left null for platforms that do not support it)

3. **Add unavailable platforms.** LinkedIn should always appear with `IsAvailable = false`. Check the `PlatformType` enum and add entries for platforms with no published content, marking them as unavailable.

4. **Return the list of `PlatformSummary` records.**

### Query Efficiency Notes

The aggregator should minimize database round-trips. For `GetSummaryAsync`, the key queries are:
- Content count (current + previous): 2 queries
- Engagement snapshots (current + previous): 2 queries loading `ContentPlatformStatuses` joined with snapshots
- Agent execution costs: 1-2 queries
- GA4 calls: 2 external API calls (current + previous)

All DB queries use `ToListAsync` and then aggregate in-memory. This is acceptable for a single-user dashboard with moderate data volumes. The caching layer (section-05) wraps these calls to avoid repeated computation.

### Platform-to-Display Name Mapping

Use `PlatformType.ToString()` for the `Platform` string field in `PlatformSummary`. The frontend maps these to display names and icons.

### Error Handling

All external calls (`IGoogleAnalyticsService`) are wrapped in try-catch. Database queries that fail propagate as `Result.Failure(ErrorCode.InternalError, ...)`. The aggregator catches exceptions from individual data sources and logs them, returning partial results where possible. Only if the core engagement query fails should the entire `GetSummaryAsync` return failure.

---

## TODO Checklist

1. Create `DashboardAggregatorTests.cs` with all 18 test stubs (RED phase)
2. Implement `DashboardAggregator.GetSummaryAsync` -- engagement aggregation, GA4 integration, period comparison, cost calculation
3. Implement `DashboardAggregator.GetTimelineAsync` -- daily grouping, gap-filling, platform breakdown
4. Implement `DashboardAggregator.GetPlatformSummariesAsync` -- per-platform health, top post identification, unavailable platform handling
5. Verify all tests pass (GREEN phase)
6. Refactor query patterns for clarity and ensure no N+1 queries

---

## Implementation Notes (Post-Build)

### Test Summary
- **18 tests**, all passing
- 9 tests for GetSummaryAsync, 4 for GetTimelineAsync, 5 for GetPlatformSummariesAsync
- Tests use MockQueryable.Moq for DbSet mocking, Moq for IGoogleAnalyticsService

### Key Implementation Decisions
- Queries use `ToListAsync` then in-memory aggregation (acceptable per plan for single-user dashboard)
- LinkedIn marked as unavailable platform; follower counts left as null for API-layer enrichment
- `AgentExecution.Create()` factory used in tests with `MarkRunning()`/`RecordUsage()`/`Complete()` to set Cost