# Section 07 -- Frontend Models and Service

## Overview

This section adds TypeScript interfaces for all new analytics dashboard response types and extends the existing `AnalyticsService` with methods for every new backend endpoint (introduced in section-06). It also introduces a `DashboardPeriod` type and date range utility functions used throughout the frontend dashboard.

**Dependencies:** Section 06 (API Endpoints) must be complete so the backend routes exist. The existing `ApiService` (`core/services/api.service.ts`) and `analytics.model.ts` in `shared/models/` are assumed unchanged.

---

## Files to Create

| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/models/dashboard.model.ts` | All new TypeScript interfaces for dashboard responses |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.spec.ts` | Tests for the extended service methods |

## Files to Modify

| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.ts` | Add 5 new endpoint methods + period/date-range helpers |
| `src/PersonalBrandAssistant.Web/src/app/shared/models/analytics.model.ts` | Add `impressions` and `engagementRate` to `TopPerformingContent` |
| `src/PersonalBrandAssistant.Web/src/app/shared/models/index.ts` | No change needed (already re-exports analytics.model) |

---

## Tests FIRST

Create `src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.spec.ts`.

The test file uses Angular's `HttpTestingController` to verify each new service method calls the correct URL with the correct query parameters. The existing `ApiService` prepends `environment.apiUrl` (currently `http://localhost:5000/api`) to all paths.

### Test Stubs

```typescript
/**
 * analytics.service.spec.ts
 *
 * Tests for the extended AnalyticsService dashboard methods.
 * Uses HttpClientTestingModule + HttpTestingController.
 * afterEach verifies no outstanding HTTP requests.
 */

describe('AnalyticsService', () => {
  // Setup: TestBed with provideHttpClientTesting, inject AnalyticsService + HttpTestingController

  afterEach(() => {
    // httpMock.verify() -- ensures no unmatched requests
  });

  describe('getDashboardSummary', () => {
    // Test: calls GET analytics/dashboard?period=7d when period string provided
    // Test: calls GET analytics/dashboard?from=...&to=... when custom date range provided
    // Test: appends refresh=true when refresh flag is set
  });

  describe('getEngagementTimeline', () => {
    // Test: calls GET analytics/engagement-timeline?period=30d
    // Test: passes custom from/to when DashboardPeriod is a DateRange object
  });

  describe('getPlatformSummaries', () => {
    // Test: calls GET analytics/platform-summary?period=30d
  });

  describe('getWebsiteAnalytics', () => {
    // Test: calls GET analytics/website?period=30d
  });

  describe('getSubstackPosts', () => {
    // Test: calls GET analytics/substack (no period param needed)
  });
});
```

Each test subscribes to the Observable, flushes the `HttpTestingController` expectation with mock data, and verifies the returned shape. The `afterEach` block must call `httpMock.verify()` as per project conventions.

---

## Implementation Details

### 1. Dashboard Model Interfaces

Create `src/PersonalBrandAssistant.Web/src/app/features/analytics/models/dashboard.model.ts` with the following interfaces. All properties use `readonly` per project immutability conventions.

**DashboardPeriod type** -- represents either a preset string or a custom date range:

```typescript
export type DashboardPeriod = '1d' | '7d' | '14d' | '30d' | '90d' | { readonly from: string; readonly to: string };
```

**DashboardSummary** -- mirrors the backend `DashboardSummary` record from section-01:

- `totalEngagement`, `previousEngagement` (number)
- `totalImpressions`, `previousImpressions` (number)
- `engagementRate`, `previousEngagementRate` (number)
- `contentPublished`, `previousContentPublished` (number)
- `costPerEngagement`, `previousCostPerEngagement` (number)
- `websiteUsers`, `previousWebsiteUsers` (number)
- `generatedAt` (string -- ISO 8601 from backend `DateTimeOffset`)

**DailyEngagement** -- timeline data point:

- `date` (string -- `"YYYY-MM-DD"` format)
- `platforms` (readonly array of `PlatformDailyMetrics`)
- `total` (number)

**PlatformDailyMetrics** -- per-platform breakdown within a day:

- `platform` (string)
- `likes`, `comments`, `shares`, `total` (all number)

**PlatformSummary** -- per-platform health data:

- `platform` (string)
- `followerCount` (number | null)
- `postCount` (number)
- `avgEngagement` (number)
- `topPostTitle` (string | null)
- `topPostUrl` (string | null)
- `isAvailable` (boolean)

**WebsiteAnalyticsResponse** -- composite GA4 + GSC response:

- `overview` of type `WebsiteOverview`
- `topPages` (readonly array of `PageViewEntry`)
- `trafficSources` (readonly array of `TrafficSourceEntry`)
- `searchQueries` (readonly array of `SearchQueryEntry`)

**WebsiteOverview:**

- `activeUsers`, `sessions`, `pageViews` (number)
- `avgSessionDuration` (number -- seconds)
- `bounceRate` (number -- 0-100)
- `newUsers` (number)

**PageViewEntry:** `pagePath` (string), `views` (number), `users` (number)

**TrafficSourceEntry:** `channel` (string), `sessions` (number), `users` (number)

**SearchQueryEntry:** `query` (string), `clicks` (number), `impressions` (number), `ctr` (number), `position` (number)

**SubstackPost:**

- `title` (string)
- `url` (string)
- `publishedAt` (string -- ISO 8601)
- `summary` (string | null)

### 2. Date Range Utility Function

Add a helper function to `dashboard.model.ts` (or a separate utility file if preferred) that converts a `DashboardPeriod` into `HttpParams`:

```typescript
/**
 * Converts a DashboardPeriod into HttpParams for API calls.
 * Preset strings become ?period=Xd, custom ranges become ?from=...&to=...
 * Optionally appends refresh=true.
 */
export function periodToParams(period: DashboardPeriod, refresh = false): HttpParams;
```

Logic:
- If `period` is a string (`'7d'`, etc.), set `params.set('period', period)`
- If `period` is an object with `from`/`to`, set `params.set('from', period.from).set('to', period.to)`
- If `refresh` is `true`, append `.set('refresh', 'true')`

### 3. Extend AnalyticsService

Modify `src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.ts` to add five new methods. The service already injects `ApiService` and follows the pattern of returning `Observable<T>`.

New methods to add:

```typescript
getDashboardSummary(period: DashboardPeriod, refresh = false): Observable<DashboardSummary>
```
Calls `GET analytics/dashboard` with params from `periodToParams(period, refresh)`.

```typescript
getEngagementTimeline(period: DashboardPeriod, refresh = false): Observable<DailyEngagement[]>
```
Calls `GET analytics/engagement-timeline` with params from `periodToParams(period, refresh)`.

```typescript
getPlatformSummaries(period: DashboardPeriod, refresh = false): Observable<PlatformSummary[]>
```
Calls `GET analytics/platform-summary` with params from `periodToParams(period, refresh)`.

```typescript
getWebsiteAnalytics(period: DashboardPeriod, refresh = false): Observable<WebsiteAnalyticsResponse>
```
Calls `GET analytics/website` with params from `periodToParams(period, refresh)`.

```typescript
getSubstackPosts(): Observable<SubstackPost[]>
```
Calls `GET analytics/substack` with no params (Substack RSS does not use date ranges).

The existing three methods (`getContentReport`, `getTopContent`, `refreshAnalytics`) remain unchanged.

Import the new types from `../models/dashboard.model` and import `periodToParams` from the same file.

### 4. Extend TopPerformingContent Interface

Modify `src/PersonalBrandAssistant.Web/src/app/shared/models/analytics.model.ts` to add two optional fields to `TopPerformingContent`:

```typescript
export interface TopPerformingContent {
  // ... existing fields remain unchanged ...
  readonly impressions?: number;
  readonly engagementRate?: number;
}
```

These are optional (`?`) for backward compatibility -- the existing `/api/analytics/top` endpoint will be extended in section-06 to include them, but old responses without them should not break.

### 5. No Changes to Shared Index

The `shared/models/index.ts` barrel already re-exports `analytics.model.ts`. The new `dashboard.model.ts` lives inside `features/analytics/models/` (feature-scoped), not in shared models, so no barrel update is needed.

---

## Backend API Routes Reference

For implementer awareness, the five new backend routes (created in section-06) that this service targets are:

| Endpoint | Method Mapping |
|----------|---------------|
| `GET /api/analytics/dashboard?period=Xd` | `getDashboardSummary()` |
| `GET /api/analytics/engagement-timeline?period=Xd` | `getEngagementTimeline()` |
| `GET /api/analytics/platform-summary?period=Xd` | `getPlatformSummaries()` |
| `GET /api/analytics/website?period=Xd` | `getWebsiteAnalytics()` |
| `GET /api/analytics/substack` | `getSubstackPosts()` |

All endpoints accept `period` (string preset) **or** `from`/`to` (ISO date strings), plus optional `refresh=true`. When `period` and `from`/`to` are both provided, the backend gives `period` precedence.

---

## Existing Code Context

The existing `AnalyticsService` is at `src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.ts`. It is a `providedIn: 'root'` injectable that delegates HTTP calls to the `ApiService` wrapper (`core/services/api.service.ts`). The `ApiService.get<T>(path, params)` method prepends the configured `environment.apiUrl` (`http://localhost:5000/api`) to the path.

The existing `AnalyticsStore` at `features/analytics/store/analytics.store.ts` uses NgRx signal store with `signalStore`, `withState`, `withMethods`, and `rxMethod`. It currently tracks `topContent`, `selectedReport`, `dateRange`, and `loading`. This store will be rewritten in section-08 to consume the new service methods.

Platform types and colors are defined in `shared/models/enums.ts` (`PlatformType`) and `shared/utils/platform-icons.ts` (`PLATFORM_COLORS`). The `PlatformDailyMetrics.platform` field uses string values matching these platform type strings for chart coloring in later sections.

---

## Checklist

1. Create `features/analytics/models/dashboard.model.ts` with all interfaces, the `DashboardPeriod` type, and the `periodToParams` utility function
2. Create `features/analytics/services/analytics.service.spec.ts` with tests for all 5 new methods
3. Extend `features/analytics/services/analytics.service.ts` with 5 new endpoint methods
4. Add `impressions?` and `engagementRate?` to `TopPerformingContent` in `shared/models/analytics.model.ts`
5. Run `npx ng test --watch=false --browsers=ChromeHeadless` to verify all tests pass

---

## Implementation Notes

- **Code review fixes:** Added error handling test (HTTP 500 propagation) and refresh flag test for `getEngagementTimeline` per code review warnings. Total: 10 tests (8 original + 2 review fixes).
- **All interfaces verified** against C# backend records from section-01. `PlatformSummary.platform` correctly uses `string` (not enum) since `JsonStringEnumConverter` serializes enums as strings.
- **Test approach:** Uses `provideHttpClient()` + `provideHttpClientTesting()` (Angular 19 standalone pattern, not deprecated `HttpClientTestingModule`).
- **File paths match plan** exactly. No deviations from planned architecture.