# Section 03: Feed Queries

## Overview

This section implements three MediatR query handlers for the Feed module: `ListFeedItems`, `GetFeedSummary`, and `GetTrendingTopics`. These are read-only operations that power the feed page's main card list, the stats bar KPIs, and the trending topics sidebar widget respectively.

## Dependencies

- **Section 01 (Prerequisites):** `IAppDbContext` must expose `DbSet<FeedItem> FeedItems`. The EF indexes on `(Type, IsActedOn)` and `(Type, CreatedAt)` must exist.
- **Section 02 (DTOs, Validators, Mappings):** All response DTOs (`FeedItemDto`, `FeedSummaryDto`, `TrendingTopicDto`) and the `FeedMappings.ToDto()` extension method must exist.

## Files to Create

| File | Purpose |
|------|---------|
| `src/PBA.Application/Features/Feed/Queries/ListFeedItems.cs` | Paginated, filtered feed item listing |
| `src/PBA.Application/Features/Feed/Queries/GetFeedSummary.cs` | Aggregated stats for KPI cards |
| `src/PBA.Application/Features/Feed/Queries/GetTrendingTopics.cs` | Grouped trending topics from TrendAlert items |
| `tests/PBA.Application.Tests/Features/Feed/Queries/ListFeedItemsHandlerTests.cs` | Unit tests for ListFeedItems |
| `tests/PBA.Application.Tests/Features/Feed/Queries/GetFeedSummaryHandlerTests.cs` | Unit tests for GetFeedSummary |
| `tests/PBA.Application.Tests/Features/Feed/Queries/GetTrendingTopicsHandlerTests.cs` | Unit tests for GetTrendingTopics |

## Existing Patterns to Follow

All query handlers follow the pattern established in the Idea Bank and Content Studio. Each is a static class wrapping a `record Query` (implementing `IRequest<Result<T>>`) and a sealed `Handler` class with primary constructor DI. Tests use `ApplicationDbContext` with EF Core InMemoryDatabase, direct handler instantiation, and xUnit `[Fact]` methods.

Key references:
- `src/PBA.Application/Features/Ideas/Queries/ListIdeas.cs` -- paginated list query with filter chain, sort expression switch, `AsNoTracking()`, count + skip/take pattern
- `src/PBA.Application/Features/Content/Queries/ListContent.cs` -- same pattern with different filter predicates
- `src/PBA.Application/Common/Models/PagedResult.cs` -- generic paged result record with `TotalPages` computed property

## Domain Context

The `FeedItem` entity already exists in `src/PBA.Domain/Entities/FeedItem.cs`:

```csharp
public class FeedItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public FeedItemType Type { get; set; }
    public required string Title { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? Data { get; set; }           // JSON blob, schema varies by Type
    public string? ActionType { get; set; }
    public Guid? ActionTargetId { get; set; }
    public FeedItemPriority Priority { get; set; } = FeedItemPriority.Normal;
    public bool IsRead { get; set; }
    public bool IsActedOn { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
}
```

Enums: `FeedItemType { AgentDraft, TrendAlert, AnalyticsHighlight, IdeaSuggestion, ApprovalRequest, SystemNotification }` and `FeedItemPriority { Low, Normal, High, Urgent }`.

The `Data` column is a JSON blob. Its schema varies by `FeedItemType`. For these queries, two schemas matter:

- **TrendAlert Data:** `{ "topic": "Claude Code", "source": "Twitter", "mentionCount": 45, "sentiment": "positive" }` -- `GetTrendingTopics` extracts the `topic` field.
- **AnalyticsHighlight Data:** `{ "metric": "impressions", "currentValue": 500, "previousValue": 400, "delta": 25.0 }` -- `GetFeedSummary` extracts the `delta` field.

These JSON fields are parsed in-memory (load entities first, then deserialize), not in EF Core LINQ queries. At 50-200 items/day scale, in-memory processing is trivial.

## Tests First

### ListFeedItemsHandlerTests

File: `tests/PBA.Application.Tests/Features/Feed/Queries/ListFeedItemsHandlerTests.cs`

1. **`Handle_DefaultQuery_ReturnsPaginatedResultsSortedByCreatedAtDesc`** -- Seed 25 feed items with varying `CreatedAt`. Default query (page 1, pageSize 20). Assert 20 items returned, totalCount 25, first item has most recent `CreatedAt`.

2. **`Handle_TypeFilter_ReturnsMatchingOnly`** -- Seed feed items across multiple types. Filter by `FeedItemType.TrendAlert`. Assert only TrendAlert items returned.

3. **`Handle_PriorityFilter_ReturnsMatchingOnly`** -- Seed items with different priorities. Filter by `FeedItemPriority.High`. Assert only High priority items returned.

4. **`Handle_IsReadFilter_ReturnsMatchingOnly`** -- Seed mix of read and unread items. Filter by `IsRead = false`. Assert only unread items returned.

5. **`Handle_ExcludesExpiredItemsByDefault`** -- Seed items including some with `ExpiresAt` in the past. Default query (IncludeExpired = false). Assert expired items are excluded.

6. **`Handle_IncludesExpiredItemsWhenRequested`** -- Same seed. Set `IncludeExpired = true`. Assert expired items are included.

7. **`Handle_NoMatchingItems_ReturnsEmptyPage`** -- Seed items of one type. Filter by a different type. Assert empty items list with totalCount 0.

8. **`Handle_RespectsPageSizeLimit`** -- Seed 10 items. Query with pageSize 5. Assert 5 items returned, totalCount 10, totalPages 2.

9. **`Handle_SortDirectionAscending_ReturnsOldestFirst`** -- Seed items with varying `CreatedAt`. Set `SortDirection = "asc"`. Assert first item has oldest `CreatedAt`.

10. **`Handle_CombinedFilters_AppliesAll`** -- Seed a diverse mix. Apply Type + Priority + IsRead filters simultaneously. Assert only items matching all three filters returned.

### GetFeedSummaryHandlerTests

File: `tests/PBA.Application.Tests/Features/Feed/Queries/GetFeedSummaryHandlerTests.cs`

1. **`Handle_ReturnsCorrectUnreadCount`** -- Seed 5 unread + 3 read items (none expired). Assert `UnreadCount == 5`.

2. **`Handle_ReturnsCorrectPendingApprovals`** -- Seed 2 AgentDraft (IsActedOn = false) + 1 ApprovalRequest (IsActedOn = false) + 1 AgentDraft (IsActedOn = true). Assert `PendingApprovals == 3` (both types, only not-acted-on).

3. **`Handle_ReturnsCorrectTrendingCount`** -- Seed 4 TrendAlert items (IsRead = false) + 2 TrendAlert (IsRead = true). Assert `TrendingCount == 4`.

4. **`Handle_CalculatesEngagementDeltaFromLast24Hours`** -- Seed 3 AnalyticsHighlight items within last 24h with Data JSON containing `delta` values of 10.0, 20.0, 30.0. Seed 1 AnalyticsHighlight from 48h ago. Assert `EngagementDelta == 20.0` (average of the three recent ones).

5. **`Handle_ReturnsZeroEngagementDeltaWhenNoAnalyticsItems`** -- Seed only non-AnalyticsHighlight items. Assert `EngagementDelta == 0`.

6. **`Handle_EmptyFeed_ReturnsAllZeros`** -- No items in DB. Assert all fields are 0.

7. **`Handle_ExcludesExpiredItemsFromAllCounts`** -- Seed items with `ExpiresAt` in the past. Assert they are not counted in any stat.

### GetTrendingTopicsHandlerTests

File: `tests/PBA.Application.Tests/Features/Feed/Queries/GetTrendingTopicsHandlerTests.cs`

1. **`Handle_ReturnsTopicsGroupedAndCounted`** -- Seed 3 TrendAlert items with `topic: "AI"` and 2 with `topic: "Rust"` in their Data JSON. Assert returns two topics: AI (count 3), Rust (count 2).

2. **`Handle_OrdersByCountDescending`** -- Seed topics with varying counts. Assert first result has the highest count.

3. **`Handle_LimitsToTop10`** -- Seed TrendAlert items with 12 distinct topics. Assert only 10 returned.

4. **`Handle_OnlyConsidersLast7Days`** -- Seed TrendAlert items: some within 7 days, some from 10 days ago. Assert only recent items contribute to topics.

5. **`Handle_ReturnsEmptyListWhenNoTrendAlertItems`** -- Seed only non-TrendAlert items. Assert empty list.

6. **`Handle_SetsLatestAtToMostRecentCreatedAtPerTopic`** -- Seed multiple items for one topic at different times. Assert `LatestAt` equals the most recent `CreatedAt` among them.

---

## Implementation Details

### ListFeedItems

File: `src/PBA.Application/Features/Feed/Queries/ListFeedItems.cs`

**Query record:**
```csharp
public record Query : IRequest<Result<PagedResult<FeedItemDto>>>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public FeedItemType? Type { get; init; }
    public FeedItemPriority? Priority { get; init; }
    public bool? IsRead { get; init; }
    public bool IncludeExpired { get; init; } = false;
    public string SortBy { get; init; } = "CreatedAt";
    public string SortDirection { get; init; } = "desc";
}
```

**Handler logic:**
- Primary constructor injection: `IAppDbContext db`
- Start with `db.FeedItems.AsNoTracking().AsQueryable()`
- Filter chain (each applied only when value is non-null/specified):
  - `Type`: `Where(f => f.Type == request.Type.Value)`
  - `Priority`: `Where(f => f.Priority == request.Priority.Value)`
  - `IsRead`: `Where(f => f.IsRead == request.IsRead.Value)`
- Expiry filter: if `IncludeExpired` is false, exclude items where `ExpiresAt != null && ExpiresAt < DateTimeOffset.UtcNow`
- Count total matching items
- Apply sort via expression switch (same pattern as `ListIdeas.ApplySort`): `SortBy` matches "CreatedAt" (default), "Title", "Priority", "Type"
- Apply pagination: `Skip((Page - 1) * PageSize).Take(PageSize)`
- Project to `FeedItemDto` via the `ToDto()` mapping extension from Section 02
- Return wrapped in `PagedResult<FeedItemDto>`

### GetFeedSummary

File: `src/PBA.Application/Features/Feed/Queries/GetFeedSummary.cs`

**Query record:**
```csharp
public record Query : IRequest<Result<FeedSummaryDto>>;
```

**Handler logic:**
- Primary constructor injection: `IAppDbContext db`
- Build a base query that excludes expired items: `db.FeedItems.AsNoTracking().Where(f => f.ExpiresAt == null || f.ExpiresAt >= DateTimeOffset.UtcNow)`
- Run four count queries sequentially (DbContext is not thread-safe — Task.WhenAll was changed to sequential awaits during code review):
  - `UnreadCount`: `await base.CountAsync(f => !f.IsRead)`
  - `PendingApprovals`: `await base.CountAsync(f => (f.Type == FeedItemType.AgentDraft || f.Type == FeedItemType.ApprovalRequest) && !f.IsActedOn)`
  - `TrendingCount`: `await base.CountAsync(f => f.Type == FeedItemType.TrendAlert && !f.IsRead)`
  - AnalyticsHighlight items from last 24h: `await base.Where(f => f.Type == FeedItemType.AnalyticsHighlight && f.CreatedAt >= cutoff).ToListAsync()`
- Compute `EngagementDelta` in-memory:
  - For each loaded AnalyticsHighlight item, deserialize `Data` JSON using `System.Text.Json.JsonDocument`
  - Extract the `delta` property as a double
  - Average all delta values. If no items, return 0.
  - Wrap in try/catch for malformed JSON -- skip items with unparseable Data
- Return `FeedSummaryDto` with all four values

### GetTrendingTopics

File: `src/PBA.Application/Features/Feed/Queries/GetTrendingTopics.cs`

**Query record:**
```csharp
public record Query : IRequest<Result<IReadOnlyList<TrendingTopicDto>>>;
```

**Handler logic:**
- Primary constructor injection: `IAppDbContext db`
- Load TrendAlert items from last 7 days to memory (with `.Take(1000)` safety cap added during code review): `db.FeedItems.AsNoTracking().Where(...).Take(1000).ToListAsync()`
- For each item, deserialize `Data` JSON and extract `topic` string field. Skip items with null/empty Data or missing topic field.
- Group by topic (case-insensitive)
- For each group: count occurrences, find the most recent `CreatedAt`
- Order by count descending, then by `LatestAt` descending
- Take top 10
- Map to `IReadOnlyList<TrendingTopicDto>`

---

## DTO Reference

These DTOs are defined in Section 02:

**FeedItemDto**: `Id` (Guid), `Type` (FeedItemType), `Title` (string), `Summary` (string), `Data` (string?), `ActionType` (string?), `ActionTargetId` (Guid?), `Priority` (FeedItemPriority), `IsRead` (bool), `IsActedOn` (bool), `CreatedAt` (DateTimeOffset), `ExpiresAt` (DateTimeOffset?)

**FeedSummaryDto**: `UnreadCount` (int), `PendingApprovals` (int), `TrendingCount` (int), `EngagementDelta` (double)

**TrendingTopicDto**: `Topic` (string), `Count` (int), `LatestAt` (DateTimeOffset)

---

## Testing Infrastructure

All tests use the same pattern as the existing Idea Bank and Content Studio tests:

```csharp
private static ApplicationDbContext CreateContext()
{
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options;
    return new ApplicationDbContext(options);
}
```

No mocks needed for these queries -- all three are pure database reads. Seed `FeedItem` entities directly via `context.FeedItems.AddRange(...)` and `SaveChangesAsync()`.

For tests that exercise JSON deserialization (GetFeedSummary EngagementDelta, GetTrendingTopics topic extraction), seed items with valid Data JSON strings matching the canonical schemas:
- TrendAlert: `"""{"topic": "AI", "source": "Twitter", "mentionCount": 10, "sentiment": "positive"}"""`
- AnalyticsHighlight: `"""{"metric": "impressions", "currentValue": 500, "previousValue": 400, "delta": 25.0}"""`

## Blockers

- Section 01 must be complete (`DbSet<FeedItem> FeedItems` on `IAppDbContext`, EF indexes)
- Section 02 must be complete (all DTO definitions, `FeedMappings.ToDto()` extension)
- No dependency on Sections 04-16
- **Parallelizable with Section 04 (Feed Commands)** -- both depend on Sections 01+02 but not each other
