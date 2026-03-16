Now I have all the context I need. Let me generate the section content.

# Section 09 — Content Analytics

## Overview

This section implements the content analytics subsystem: the `IEngagementAggregator` interface, `EngagementAggregator` service, `ContentPerformanceReport` and `TopPerformingContent` models, and the `EngagementSnapshot` domain entity with its EF Core configuration. The analytics system fetches engagement data from platform APIs via the existing `ISocialPlatform.GetEngagementAsync` method, persists point-in-time snapshots, and provides cross-platform performance reporting including cost-per-engagement calculations.

**Dependencies on other sections (do not implement these, assume they exist):**

- **Section 01 (Domain Entities):** Provides the `EngagementSnapshot` entity class and EF Core configuration. This section specifies the entity design but section 01 owns the actual domain file creation.
- **Section 10 (Background Processors):** The `EngagementAggregationProcessor` background service that calls this section's `IEngagementAggregator` on a schedule. This section provides the interface the processor uses; section 10 implements the processor itself.
- **Section 11 (API Endpoints):** The `AnalyticsEndpoints` Minimal API group that exposes `GET /api/analytics/content/{id}`, `GET /api/analytics/top`, and `POST /api/analytics/content/{id}/refresh`.

---

## Domain Entity: EngagementSnapshot

Defined in section 01 but documented here for context. The `EngagementSnapshot` entity captures a point-in-time engagement reading for a specific platform posting.

**File:** `src/PersonalBrandAssistant.Domain/Entities/EngagementSnapshot.cs`

```csharp
public class EngagementSnapshot : AuditableEntityBase
{
    public Guid ContentPlatformStatusId { get; set; }
    public int Likes { get; set; }
    public int Comments { get; set; }
    public int Shares { get; set; }
    public int? Impressions { get; set; }  // nullable — not all platforms provide this
    public int? Clicks { get; set; }  // nullable — not all platforms provide this
    public DateTimeOffset FetchedAt { get; set; }
}
```

Key design points:
- Links to `ContentPlatformStatus` (not directly to `Content`) because engagement is per-platform-posting.
- `Impressions` and `Clicks` are nullable because not all platforms provide these metrics (e.g., some free-tier APIs omit impression data).
- `FetchedAt` records when this snapshot was taken, distinct from `CreatedAt` (which is the DB insert time).

**EF Configuration:** `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/EngagementSnapshotConfiguration.cs`

Must include:
- Index on `(ContentPlatformStatusId, FetchedAt DESC)` for efficient latest-snapshot queries.
- Required relationship to `ContentPlatformStatus`.

**DbContext addition:** Add `DbSet<EngagementSnapshot> EngagementSnapshots` to `IApplicationDbContext` and `ApplicationDbContext`.

---

## Application Layer Models

### ContentPerformanceReport

**File:** `src/PersonalBrandAssistant.Application/Common/Models/ContentPerformanceReport.cs`

```csharp
/// <summary>
/// Cross-platform performance summary for a single content item.
/// LatestByPlatform contains the most recent EngagementSnapshot per platform.
/// TotalEngagement sums likes + comments + shares across all platforms.
/// CostPerEngagement divides total LLM cost by total engagement (null if no engagement or no cost data).
/// </summary>
public record ContentPerformanceReport(
    Guid ContentId,
    IReadOnlyDictionary<PlatformType, EngagementSnapshot> LatestByPlatform,
    int TotalEngagement,
    decimal? LlmCost,
    decimal? CostPerEngagement);
```

### TopPerformingContent

**File:** `src/PersonalBrandAssistant.Application/Common/Models/TopPerformingContent.cs`

```csharp
/// <summary>
/// Lightweight projection for top-content queries. Includes the content ID,
/// title, total engagement sum, and the date range the engagement covers.
/// </summary>
public record TopPerformingContent(
    Guid ContentId,
    string Title,
    int TotalEngagement,
    IReadOnlyDictionary<PlatformType, int> EngagementByPlatform);
```

---

## IEngagementAggregator Interface

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IEngagementAggregator.cs`

```csharp
public interface IEngagementAggregator
{
    /// <summary>
    /// Fetches fresh engagement data from the platform API for a specific ContentPlatformStatus
    /// and persists a new EngagementSnapshot. Returns the newly created snapshot.
    /// </summary>
    Task<Result<EngagementSnapshot>> FetchLatestAsync(Guid contentPlatformStatusId, CancellationToken ct);

    /// <summary>
    /// Builds a cross-platform performance report for a content item.
    /// Aggregates the latest snapshot per platform, sums total engagement,
    /// and calculates cost-per-engagement from AgentExecution cost data.
    /// </summary>
    Task<Result<ContentPerformanceReport>> GetPerformanceAsync(Guid contentId, CancellationToken ct);

    /// <summary>
    /// Returns the top-performing content items by total engagement within a date range.
    /// </summary>
    Task<Result<IReadOnlyList<TopPerformingContent>>> GetTopContentAsync(
        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);
}
```

---

## EngagementAggregator Implementation

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/EngagementAggregator.cs`

### Constructor Dependencies

- `IApplicationDbContext` -- for querying `ContentPlatformStatuses`, `EngagementSnapshots`, `AgentExecutions`, and `Contents`.
- `IEnumerable<ISocialPlatform>` -- the registered platform adapters (injected as a collection; select the correct one by `PlatformType`).
- `IRateLimiter` -- to check rate limits before calling platform APIs.
- `ILogger<EngagementAggregator>` -- structured logging.

### FetchLatestAsync Logic

1. Load `ContentPlatformStatus` by ID. Return `NotFound` if missing.
2. Validate that the status has a non-null `PlatformPostId` (can only fetch engagement for actually-published posts). Return validation error if null.
3. Find the `ISocialPlatform` adapter matching the `ContentPlatformStatus.Platform`. Return error if no adapter registered for that platform.
4. Check rate limits via `IRateLimiter.CanMakeRequestAsync`. If rate-limited, return a `RateLimited` error result (do not throw).
5. Call `ISocialPlatform.GetEngagementAsync(platformPostId, ct)`. The existing `EngagementStats` model has `Likes`, `Comments`, `Shares`, `Impressions`, `Clicks`.
6. Map the `EngagementStats` to a new `EngagementSnapshot`, setting `FetchedAt = DateTimeOffset.UtcNow`.
7. Add the snapshot to DbContext, save, and return it.
8. After a successful API call, record the request via `IRateLimiter.RecordRequestAsync`.

### GetPerformanceAsync Logic

1. Load all `ContentPlatformStatus` records for the given `contentId` where `Status == PlatformPublishStatus.Published`.
2. For each, query the latest `EngagementSnapshot` (ordered by `FetchedAt DESC`, take first).
3. Build a dictionary of `PlatformType -> EngagementSnapshot`.
4. Calculate `TotalEngagement` = sum of `(Likes + Comments + Shares)` across all platform snapshots.
5. Query `AgentExecutions` where `ContentId == contentId` and `Status == Completed`. Sum `Cost` to get `LlmCost`.
6. Calculate `CostPerEngagement`:
   - If `TotalEngagement == 0` or `LlmCost` is null/zero, set to `null`.
   - Otherwise: `LlmCost / TotalEngagement`.
7. Return the assembled `ContentPerformanceReport`.

### GetTopContentAsync Logic

1. Query `EngagementSnapshots` joined with `ContentPlatformStatuses` where the associated content was published within the `[from, to]` date range.
2. Group by `ContentId`.
3. For each group, sum `(Likes + Comments + Shares)` from the latest snapshot per platform (to avoid double-counting from multiple snapshots).
4. Order by total engagement descending, take `limit`.
5. Load content titles from the `Contents` table.
6. Return as `IReadOnlyList<TopPerformingContent>`.

### Snapshot Retention Policy

The `EngagementAggregationProcessor` (section 10) handles retention cleanup. The policy is:
- Keep hourly snapshots for 7 days.
- Keep daily snapshots (one per day, most recent) for 30 days.
- Delete everything older than 30 days.

The aggregator does not implement retention directly -- it only creates snapshots. The background processor calls a cleanup method. Consider adding a helper method:

```csharp
/// <summary>
/// Removes snapshots beyond the retention window. Called by EngagementAggregationProcessor.
/// Keeps hourly granularity for 7 days, daily for 30 days, deletes older.
/// </summary>
Task<Result<int>> CleanupSnapshotsAsync(CancellationToken ct);
```

This is optional on the interface (could be internal to the processor). If placed on `IEngagementAggregator`, the processor simply calls it after each aggregation cycle.

---

## Tests

All tests use xUnit + Moq + MockQueryable. Test class naming: `{Class}Tests`. Method naming: `{Method}_{Scenario}_{Expected}`. AAA pattern. Mock `DbSet<T>` via `BuildMockDbSet()`. Assert via `result.IsSuccess`, `result.ErrorCode`, `result.Value`.

**Test file:** `tests/PersonalBrandAssistant.Tests/Infrastructure/Services/ContentServices/EngagementAggregatorTests.cs`

### EngagementSnapshot Entity Tests

**Test file:** `tests/PersonalBrandAssistant.Tests/Domain/Entities/EngagementSnapshotTests.cs`

- `Impressions_IsNullable_DefaultsToNull` -- verify `Impressions` and `Clicks` are null by default on a new instance.

### EF Configuration Tests

**Test file:** `tests/PersonalBrandAssistant.Tests/Infrastructure/Data/Configurations/EngagementSnapshotConfigurationTests.cs`

- `Configuration_IncludesIndex_OnContentPlatformStatusIdAndFetchedAtDesc` -- verify the index exists on the model via integration test or configuration inspection.

### IEngagementAggregator / EngagementAggregator Tests

```
FetchLatestAsync_ValidContentPlatformStatus_CallsGetEngagementAndSavesSnapshot
```
- Arrange: Mock `ISocialPlatform` returning `EngagementStats(10, 5, 3, 100, 50, ...)`. Mock `IRateLimiter` returning allowed. Mock DbContext with a `ContentPlatformStatus` that has a `PlatformPostId`.
- Act: Call `FetchLatestAsync`.
- Assert: `result.IsSuccess`, snapshot has `Likes=10, Comments=5, Shares=3`, `SaveChangesAsync` called once.

```
FetchLatestAsync_NoPlatformPostId_ReturnsValidationError
```
- Arrange: `ContentPlatformStatus` with `PlatformPostId = null`.
- Assert: `result.IsSuccess == false`, appropriate error code.

```
FetchLatestAsync_RateLimited_ReturnsRateLimitedError
```
- Arrange: `IRateLimiter.CanMakeRequestAsync` returns denied.
- Assert: `result.IsSuccess == false`, no call to `ISocialPlatform`.

```
FetchLatestAsync_PlatformApiError_ReturnsErrorResult
```
- Arrange: `ISocialPlatform.GetEngagementAsync` returns failure result.
- Assert: `result.IsSuccess == false`, error propagated, no snapshot saved.

```
GetPerformanceAsync_MultiPlatformContent_AggregatesCorrectly
```
- Arrange: Content with 2 `ContentPlatformStatus` records (Twitter, LinkedIn). Each has snapshots. Mock `AgentExecutions` with costs.
- Assert: `TotalEngagement` sums across platforms. `LlmCost` is sum of execution costs. `CostPerEngagement` is `LlmCost / TotalEngagement`.

```
GetPerformanceAsync_ZeroEngagement_CostPerEngagementIsNull
```
- Arrange: All snapshots have 0 likes, 0 comments, 0 shares.
- Assert: `CostPerEngagement == null`.

```
GetPerformanceAsync_NoAgentExecutions_LlmCostIsNull
```
- Arrange: No `AgentExecution` records for the content.
- Assert: `LlmCost == null`, `CostPerEngagement == null`.

```
GetTopContentAsync_ReturnsOrderedByTotalEngagement
```
- Arrange: 3 content items with varying engagement totals. Query range covers all.
- Assert: Results ordered descending by `TotalEngagement`. Count respects `limit`.

```
GetTopContentAsync_UsesLatestSnapshotPerPlatform
```
- Arrange: Content with multiple snapshots per platform (different `FetchedAt` times).
- Assert: Only the most recent snapshot per platform is used in the calculation.

```
GetTopContentAsync_EmptyRange_ReturnsEmptyList
```
- Arrange: No published content in the query date range.
- Assert: `result.IsSuccess`, `result.Value` is empty list.

### EngagementAggregationProcessor Tests (documented here, implemented in section 10)

These tests belong to section 10 but are documented here for completeness:

- `Processor_QueriesPublishedContentWithinRetentionWindow` -- only content published within the configured retention period (default 30 days) is processed.
- `Processor_RespectsRateLimitsViaIRateLimiter` -- processor skips platform calls when rate-limited instead of failing.
- `Processor_HandlesPlatformApiErrorsGracefully` -- if one platform fails, other platforms still get processed (partial success).
- `Processor_RetentionCleanup_RemovesOldSnapshots` -- snapshots older than retention policy are deleted.

---

## Configuration

The `EngagementAggregator` uses settings from `ContentEngineOptions`:

**File:** `src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs` (shared with other content engine services)

Relevant properties for analytics:
- `EngagementRetentionDays` (int, default 30) -- how far back to query published content for aggregation and how long to keep daily snapshots.
- `EngagementAggregationIntervalHours` (int, default 4) -- used by the background processor, not the aggregator itself.

From `appsettings.json`:
```json
{
  "ContentEngine": {
    "EngagementRetentionDays": 30,
    "EngagementAggregationIntervalHours": 4
  }
}
```

---

## Key Existing Types Referenced

These types already exist in the codebase and are used by this section:

| Type | Location | Purpose |
|------|----------|---------|
| `ISocialPlatform` | `Application/Common/Interfaces/ISocialPlatform.cs` | Platform adapter interface with `GetEngagementAsync` |
| `EngagementStats` | `Application/Common/Models/EngagementStats.cs` | Return type from `GetEngagementAsync`: `(Likes, Comments, Shares, Impressions, Clicks, PlatformSpecific)` |
| `ContentPlatformStatus` | `Domain/Entities/ContentPlatformStatus.cs` | Links content to platform postings. Has `PlatformPostId`, `Platform`, `Status`, `ContentId` |
| `IRateLimiter` | `Application/Common/Interfaces/IRateLimiter.cs` | Rate limit checking per platform/endpoint |
| `AgentExecution` | `Domain/Entities/AgentExecution.cs` | Tracks LLM usage costs. Has `ContentId`, `Cost`, `Status` |
| `AuditableEntityBase` | `Domain/Common/EntityBase.cs` | Base with `Id`, `CreatedAt`, `UpdatedAt` |
| `Result<T>` | `Application/Common/Models/Result.cs` | Standard result pattern used throughout the app |
| `PlatformType` | `Domain/Enums/PlatformType.cs` | Enum for Twitter, LinkedIn, Instagram, YouTube |
| `PlatformPublishStatus` | `Domain/Enums/PlatformPublishStatus.cs` | Status enum including `Published` |
| `IApplicationDbContext` | `Application/Common/Interfaces/IApplicationDbContext.cs` | DbContext interface, needs `EngagementSnapshots` DbSet added |

---

## File Summary (Actual Implementation)

Files **created**:

| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IEngagementAggregator.cs` | Interface: FetchLatest, GetPerformance, GetTopContent, CleanupSnapshots |
| `src/PersonalBrandAssistant.Application/Common/Models/ContentPerformanceReport.cs` | Report record with cross-platform aggregation |
| `src/PersonalBrandAssistant.Application/Common/Models/TopPerformingContent.cs` | Top content record with per-platform breakdown |
| `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/EngagementAggregator.cs` | Full implementation with batch queries, input validation, ExecuteDeleteAsync cleanup |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/EngagementAggregatorTests.cs` | 15 unit tests covering all public methods |

Files **modified**:

| File | Change |
|------|--------|
| `src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs` | Added `EngagementRetentionDays` (30) and `EngagementAggregationIntervalHours` (4) |
| `src/PersonalBrandAssistant.Application/Common/Errors/ErrorCode.cs` | Added `RateLimited` enum value |

**Deviations from plan:**
- Tests placed in `Infrastructure.Tests` (not `PersonalBrandAssistant.Tests` which doesn't exist)
- `IApplicationDbContext` and `ApplicationDbContext` already had `EngagementSnapshots` from section 01 — no modification needed
- Added `ErrorCode.RateLimited` per code review (plan used `ErrorCode.Conflict`)
- `GetPerformanceAsync` uses batch query instead of N+1 per-platform queries (code review fix)
- `CleanupSnapshotsAsync` uses `ExecuteDeleteAsync` for expired rows (code review fix)
- Added input validation on `GetTopContentAsync` (limit, date range)
- Dictionaries wrapped with `.AsReadOnly()` for immutability

**Test count:** 15 passing