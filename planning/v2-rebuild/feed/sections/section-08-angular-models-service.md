# Section 08: Angular Feed Models and Services

**Status:** COMPLETE

## Overview

Create TypeScript interfaces mirroring the backend Feed DTOs, an HTTP service (`FeedService`) wrapping all Feed API endpoints, and a standalone `FeedHubService` for real-time SignalR push notifications from `/hubs/feed`. These are the Angular data layer foundation -- every subsequent frontend section (store, components, pages) depends on them.

## Implementation Notes

- 3 model files: feed-item.model.ts (FeedItemType, FeedItemPriority, FeedItem, FeedActionResult, FeedListParams), feed-summary.model.ts, trending-topic.model.ts
- FeedService: 8 methods matching all Feed API endpoints exactly
- FeedHubService: standalone service reusing HUB_CONNECTION_FACTORY from content/signalr.service.ts
- Also restored 3 missing files from git history: error.interceptor.ts, api-key.interceptor.ts, environment.ts
- 16 tests pass: 10 FeedService HTTP tests + 6 FeedHubService SignalR tests
- All model properties verified against backend DTOs
- All API routes verified against FeedEndpoints.cs

## Dependencies

- **Section 05 (API Endpoints)** must be complete. The `FeedService` targets the exact HTTP routes defined there.

This section blocks **section-09 (Feed Store)** and **section-14 (New Items Banner + SignalR Wiring)**, which consume both services.

## What Gets Built

| Component | Path |
|-----------|------|
| `FeedItem` interface + enums | `src/PersonalBrandAssistant.Web/src/app/features/feed/models/feed-item.model.ts` |
| `FeedSummary` interface | `src/PersonalBrandAssistant.Web/src/app/features/feed/models/feed-summary.model.ts` |
| `TrendingTopic` interface | `src/PersonalBrandAssistant.Web/src/app/features/feed/models/trending-topic.model.ts` |
| `FeedService` | `src/PersonalBrandAssistant.Web/src/app/features/feed/services/feed.service.ts` |
| `FeedService` tests | `src/PersonalBrandAssistant.Web/src/app/features/feed/services/feed.service.spec.ts` |
| `FeedHubService` | `src/PersonalBrandAssistant.Web/src/app/features/feed/services/feed-hub.service.ts` |
| `FeedHubService` tests | `src/PersonalBrandAssistant.Web/src/app/features/feed/services/feed-hub.service.spec.ts` |

## Existing Patterns to Follow

**Content model pattern** (`content.model.ts`): Enums as TypeScript `enum` with string values matching backend. Interfaces with camelCase properties. Date fields as `string` (ISO 8601). IDs as `string` (GUIDs).

**Content service pattern** (`content.service.ts`): `@Injectable({ providedIn: 'root' })`, private `readonly baseUrl`, `HttpClient` injection, each method returns `Observable<T>`, query params built via `HttpParams` with conditional `.set()`.

**SignalR service pattern** (`signalr.service.ts`): Uses `HUB_CONNECTION_FACTORY` InjectionToken, internal `Subject` per event exposed as `Observable` via `.asObservable()`, `connect()`/`disconnect()` lifecycle methods.

## Tests (Write First)

### FeedService Tests

File: `src/PersonalBrandAssistant.Web/src/app/features/feed/services/feed.service.spec.ts`

```typescript
// beforeEach: configure TestBed with FeedService, provideHttpClient(), provideHttpClientTesting()
// afterEach: httpMock.verify()

// Test: list() sends GET /api/feed with correct query params
// Test: list() omits null/undefined filter params
// Test: getSummary() sends GET /api/feed/summary
// Test: getTrending() sends GET /api/feed/trending
// Test: markRead() sends PUT /api/feed/{id}/read
// Test: actOnItem() sends PUT /api/feed/{id}/act with action body
// Test: batchMarkRead() sends PUT /api/feed/batch/read with filter body
// Test: batchDismiss() sends PUT /api/feed/batch/dismiss with type body
// Test: batchAct() sends PUT /api/feed/batch/act with IDs and action
```

### FeedHubService Tests

File: `src/PersonalBrandAssistant.Web/src/app/features/feed/services/feed-hub.service.spec.ts`

```typescript
// Test: connect() establishes hub connection to /hubs/feed
// Test: connect() registers ReceiveFeedItem and FeedSummaryUpdated handlers
// Test: feedItemReceived$ emits when ReceiveFeedItem handler fires
// Test: summaryUpdated$ emits when FeedSummaryUpdated handler fires
// Test: disconnect() stops the connection
// Test: configures automatic reconnect
```

## Implementation Details

### Models

#### feed-item.model.ts

```typescript
// FeedItemType enum: AgentDraft, TrendAlert, AnalyticsHighlight, IdeaSuggestion, ApprovalRequest, SystemNotification
// FeedItemPriority enum: Low, Normal, High, Urgent
// FeedItem interface: id, type, title, summary, data, actionType, actionTargetId, priority, isRead, isActedOn, createdAt, expiresAt
// FeedActionResult interface: success, navigationTarget, targetId
```

#### feed-summary.model.ts

```typescript
// FeedSummary interface: unreadCount, pendingApprovals, trendingCount, engagementDelta
```

#### trending-topic.model.ts

```typescript
// TrendingTopic interface: topic, count, latestAt
```

### FeedService

`@Injectable({ providedIn: 'root' })`. Private `readonly baseUrl = '/api/feed'`. Constructor injects `HttpClient`.

Methods:
- `list(params: FeedListParams): Observable<PagedResult<FeedItem>>` -- Build HttpParams conditionally
- `getSummary(): Observable<FeedSummary>`
- `getTrending(): Observable<TrendingTopic[]>`
- `markRead(id: string): Observable<void>`
- `actOnItem(id: string, action: string): Observable<FeedActionResult>`
- `batchMarkRead(type?: FeedItemType, isRead?: boolean): Observable<{ count: number }>`
- `batchDismiss(type: FeedItemType): Observable<{ count: number }>`
- `batchAct(ids: string[], action: string): Observable<{ successCount: number; failures: string[] }>`

### FeedHubService

**Standalone service** -- separate from existing content `SignalRService`. `@Injectable({ providedIn: 'root' })`. Root-scoped so it stays alive across navigation.

Reuses `HUB_CONNECTION_FACTORY` InjectionToken from `signalr.service.ts` for testability.

Structure:
- Private `Subject<FeedItem>` and `Subject<FeedSummary>`
- Public `feedItemReceived$` and `summaryUpdated$` observables
- `connect()`: Creates connection to `/hubs/feed`, registers `ReceiveFeedItem` and `FeedSummaryUpdated` handlers
- `disconnect()`: Stops connection

## Verification Checklist

- [ ] All 6 `FeedItemType` members match backend exactly
- [ ] All 4 `FeedItemPriority` members match backend exactly
- [ ] `FeedService` has all 8 methods matching the API route table
- [ ] `FeedService.list()` builds `HttpParams` conditionally
- [ ] `FeedHubService` connects to `/hubs/feed` (not `/hubs/content`)
- [ ] `FeedHubService` exposes `feedItemReceived$` and `summaryUpdated$`
- [ ] All tests pass with `httpMock.verify()` in afterEach
- [ ] `ng build` succeeds
