# Section 01: PBA Monitor Endpoints

## Overview

Four new lightweight, read-only REST endpoints in the PBA API designed for jarvis-monitor to poll at intervals. Each returns pre-aggregated data to minimize database load. These endpoints provide the external observability surface that Jarvis uses to monitor PBA health, content queue status, engagement anomalies, and morning briefing data.

The PBA API is a .NET 10 Minimal API project (`PersonalBrandAssistant.Api`). Existing endpoints follow a pattern of static mapper classes in `src/PersonalBrandAssistant.Api/Endpoints/` with extension methods on `IEndpointRouteBuilder`. Services are injected via DI and return `Result<T>` from the application layer.

## Dependencies

- **section-03-scoped-api-keys**: The new endpoints use API key authentication (existing `ApiKeyMiddleware`). The readonly scope is sufficient for all four endpoints. Until scoped keys are implemented, the existing single API key middleware provides authentication.

## Endpoints

### `GET /api/content/queue-status`

Returns the current content pipeline queue depth, next scheduled post, recent publishing activity, and stage breakdown.

Response shape:

```json
{
  "queueDepth": 12,
  "nextScheduledPost": {
    "contentId": "guid",
    "platform": "LinkedIn",
    "scheduledAt": "2026-03-24T09:00:00Z"
  },
  "postsLast24h": 3,
  "itemsByStage": {
    "draft": 4,
    "review": 2,
    "approved": 3,
    "scheduled": 3
  }
}
```

`nextScheduledPost` is null when nothing is scheduled. `queueDepth` is the total count of all non-terminal content items (statuses: Draft, Review, Approved, Scheduled). `postsLast24h` counts items where `PublishedAt` falls within the last 24 hours. `itemsByStage` groups active content by `ContentStatus`.

Service logic: Query the `Contents` DbSet, filter to non-terminal statuses (Draft, Review, Approved, Scheduled), group by `Status` for `itemsByStage`. Count items with `PublishedAt` in the last 24 hours for `postsLast24h`. Find the nearest future `ScheduledAt` for `nextScheduledPost`.

### `GET /api/content/pipeline-health`

Returns stuck pipeline items, failure rate, and active item count. Used by jarvis-monitor's `PbaPipelineMonitor` to detect content that's stalled or error-prone.

Response shape:

```json
{
  "stuckItems": [
    {
      "contentId": "guid",
      "stage": "Draft",
      "stuckSince": "2026-03-23T06:00:00Z",
      "hoursStuck": 14.5
    }
  ],
  "failedGenerations24h": 2,
  "errorRate": 0.08,
  "activeCount": 10
}
```

Service logic: "Stuck" means a content item whose `UpdatedAt` is older than 2 hours AND whose `Status` is not terminal (not Published, Archived, or Failed). `failedGenerations24h` counts items that transitioned to `Failed` status in the last 24 hours (query `WorkflowTransitionLogs` where `NewStatus == Failed` and `TransitionedAt` within 24h). `errorRate` is `failedGenerations24h / (failedGenerations24h + successfulCompletions24h)` where successful completions are transitions to Published in the same window. `activeCount` is total non-terminal items.

### `GET /api/analytics/engagement-summary`

Returns rolling engagement metrics, anomaly detection, and platform breakdown. Used by jarvis-monitor's `PbaEngagementMonitor` and also feeds into the briefing summary.

Response shape:

```json
{
  "rolling7DayEngagement": 1842,
  "averageEngagement": 263,
  "anomalies": [
    {
      "contentId": "guid",
      "platform": "Twitter",
      "metric": "likes",
      "value": 580,
      "average": 45,
      "multiplier": 12.9,
      "direction": "positive",
      "confidence": 0.95
    }
  ],
  "platformBreakdown": {
    "Twitter": 820,
    "LinkedIn": 650,
    "Reddit": 372
  }
}
```

Service logic: Aggregate from `EngagementSnapshots` for the last 7 days. `rolling7DayEngagement` sums total engagement across all platforms. `averageEngagement` divides by 7 (daily average). For anomaly detection, compare each content item's engagement against the rolling average for its platform. Flag as anomaly when engagement is greater than 2x the average (positive, direction: "positive") or less than 0.5x the average (negative, direction: "negative"). Confidence is calculated as `min(1.0, |multiplier - 1.0| / 3.0)` -- higher deviation means higher confidence. `platformBreakdown` sums engagement per platform over the 7-day window.

### `GET /api/briefing/summary`

Aggregates data from content calendar, engagement analytics, trend monitoring, and pipeline status into a single briefing-ready payload. Used by jarvis-monitor's briefing cycle and the Jarvis HUD's BriefingPanel and MetricCards.

Response shape:

```json
{
  "scheduledToday": [
    {
      "contentId": "guid",
      "platform": "LinkedIn",
      "time": "09:00",
      "title": "AI Trends in 2026"
    }
  ],
  "engagementHighlights": [
    {
      "contentId": "guid",
      "platform": "Twitter",
      "metric": "likes",
      "value": 580
    }
  ],
  "trendingTopics": [
    {
      "topic": "AI Agents",
      "relevanceScore": 0.92,
      "source": "Reddit"
    }
  ],
  "queueDepth": 12,
  "nextPublish": {
    "contentId": "guid",
    "platform": "LinkedIn",
    "time": "09:00"
  },
  "pendingApprovals": 2
}
```

Service logic: This endpoint composes data from multiple service queries in a single request. `scheduledToday` queries `CalendarSlots` joined with `Contents` where `ScheduledAt` falls on the current date. `engagementHighlights` selects the top 3 engagement metrics from the last 24 hours (highest absolute values). `trendingTopics` comes from the trend service, limited to the top 5 by relevance score. `queueDepth` and `nextPublish` reuse the same logic as `/api/content/queue-status`. `pendingApprovals` counts content items in `Review` or `Approved` status that require manual action.

## Tests (Write First)

Test file: `tests/PersonalBrandAssistant.Application.Tests/Services/IntegrationMonitorServiceTests.cs`

Use xUnit + Moq. The service under test receives `IApplicationDbContext` (mocked with in-memory EF Core DbSets) and `IDateTimeProvider` (mocked for deterministic time).

```csharp
// --- GET /api/content/queue-status ---

// Test: returns correct queueDepth from active content items
//   Seed 5 Draft, 3 Review, 2 Approved, 2 Scheduled, 1 Published, 1 Archived
//   Assert queueDepth == 12

// Test: returns nearest nextScheduledPost
//   Seed two scheduled items (one at +2h, one at +6h)
//   Assert nextScheduledPost.scheduledAt matches the +2h item

// Test: returns null nextScheduledPost when nothing scheduled
//   Seed only Draft items
//   Assert nextScheduledPost is null

// Test: returns correct postsLast24h count
//   Seed 3 items with PublishedAt in last 24h, 2 with PublishedAt 48h ago
//   Assert postsLast24h == 3

// Test: returns correct itemsByStage breakdown
//   Seed known counts per status
//   Assert each stage count matches expected

// Test: requires API key authentication
//   (Integration test via WebApplicationFactory)
//   Call without X-Api-Key header, assert 401

// Test: readonly key scope is sufficient
//   (Integration test - depends on section-03)
//   Call with readonly key, assert 200


// --- GET /api/content/pipeline-health ---

// Test: returns stuck items (UpdatedAt > 2h in non-terminal state)
//   Seed item with UpdatedAt = now - 3h, Status = Draft
//   Assert stuckItems contains the item with hoursStuck ~= 3

// Test: returns zero stuck items when all active items are recent
//   Seed items with UpdatedAt = now - 30min, non-terminal status
//   Assert stuckItems is empty

// Test: returns correct failedGenerations24h count
//   Seed WorkflowTransitionLogs with 2 Failed transitions in last 24h, 1 older
//   Assert failedGenerations24h == 2

// Test: calculates error rate correctly
//   Seed 2 Failed + 8 Published transitions in 24h
//   Assert errorRate == 0.2

// Test: requires API key authentication
//   Call without header, assert 401


// --- GET /api/analytics/engagement-summary ---

// Test: calculates rolling 7-day engagement correctly
//   Seed EngagementSnapshots across 7 days
//   Assert rolling7DayEngagement matches sum

// Test: detects positive anomaly (engagement > 2x average)
//   Seed one item with 3x the average engagement
//   Assert anomalies contains item with direction == "positive"

// Test: detects negative anomaly (engagement < 0.5x average)
//   Seed one item with 0.3x the average engagement
//   Assert anomalies contains item with direction == "negative"

// Test: includes direction and confidence in anomaly objects
//   Assert anomaly has direction and confidence fields

// Test: returns platform breakdown
//   Seed engagement across 3 platforms
//   Assert platformBreakdown has 3 keys with correct sums

// Test: requires API key authentication
//   Call without header, assert 401


// --- GET /api/briefing/summary ---

// Test: returns scheduledToday items for current date
//   Mock IDateTimeProvider to return fixed date
//   Seed calendar slots for that date
//   Assert scheduledToday matches seeded items

// Test: returns engagement highlights
//   Seed engagement data, assert top items appear

// Test: returns trending topics
//   Seed trend suggestions, assert top 5 by relevance appear

// Test: returns correct queueDepth
//   Same logic as queue-status test

// Test: returns pendingApprovals count
//   Seed 2 Review + 1 Approved items
//   Assert pendingApprovals == 3

// Test: requires API key authentication
//   Call without header, assert 401
```

## File Paths

### New Files

- `src/PersonalBrandAssistant.Application/Common/Interfaces/IIntegrationMonitorService.cs` -- Service interface with four methods, one per endpoint.
- `src/PersonalBrandAssistant.Application/Common/Models/MonitorDtos.cs` -- Response DTOs: `QueueStatusResponse`, `PipelineHealthResponse`, `EngagementSummaryResponse`, `BriefingSummaryResponse`, and their nested records.
- `src/PersonalBrandAssistant.Infrastructure/Services/IntegrationServices/IntegrationMonitorService.cs` -- Service implementation querying the database.
- `src/PersonalBrandAssistant.Api/Endpoints/IntegrationEndpoints.cs` -- Endpoint mapper registering all four routes.
- `tests/PersonalBrandAssistant.Application.Tests/Services/IntegrationMonitorServiceTests.cs` -- Unit tests.

### Modified Files

- `src/PersonalBrandAssistant.Api/Program.cs` -- Add `app.MapIntegrationEndpoints();` call.
- `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` -- Register `IIntegrationMonitorService` as scoped.

## Service Interface

```csharp
public interface IIntegrationMonitorService
{
    Task<Result<QueueStatusResponse>> GetQueueStatusAsync(CancellationToken ct);
    Task<Result<PipelineHealthResponse>> GetPipelineHealthAsync(CancellationToken ct);
    Task<Result<EngagementSummaryResponse>> GetEngagementSummaryAsync(CancellationToken ct);
    Task<Result<BriefingSummaryResponse>> GetBriefingSummaryAsync(CancellationToken ct);
}
```

## Endpoint Registration

The `IntegrationEndpoints` mapper class follows the same static extension method pattern as existing endpoints. It registers routes under their existing API prefixes (not a new `/api/integration/` prefix) so they sit alongside related domain endpoints:

- `/api/content/queue-status` -- under the content group
- `/api/content/pipeline-health` -- under the content group
- `/api/analytics/engagement-summary` -- under the analytics group
- `/api/briefing/summary` -- new briefing group

Each handler injects `IIntegrationMonitorService`, calls the corresponding method, and maps the `Result<T>` to an HTTP result using the existing `ToHttpResult()` extension.

## Implementation Notes

- The service receives `IApplicationDbContext` and `IDateTimeProvider` via constructor injection. All time comparisons use `IDateTimeProvider.UtcNow` for testability.
- Queries should use `AsNoTracking()` since these are read-only endpoints.
- The `BriefingSummaryResponse` composes data from multiple queries in a single service call. It does not call the other three service methods internally -- it runs its own optimized queries to avoid redundant database hits.
- All DTOs are immutable records.
- Error handling: if the database is unreachable, the `Result<T>` pattern propagates the failure. The endpoint returns a 500 via the global exception handler. Jarvis-monitor treats timeouts and errors as "PBA unreachable" and routes a critical alert.
