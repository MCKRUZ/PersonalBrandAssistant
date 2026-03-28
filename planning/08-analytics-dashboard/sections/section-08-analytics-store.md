# Section 08 -- Analytics Store (Rewrite)

## Overview

This section rewrites the existing `AnalyticsStore` at `features/analytics/store/analytics.store.ts` to support the full analytics dashboard. The current store only tracks `topContent`, `selectedReport`, `dateRange`, and `loading`. The rewrite adds state slices for dashboard summary, engagement timeline, platform summaries, website analytics, Substack posts, and top content -- all loaded in parallel via `forkJoin`. It also adds period management, cache-bypass refresh, per-section error tracking, and computed signals for derived values like percentage changes and staleness detection.

**Dependencies:** Section 07 (Frontend Models and Service) must be complete. The store consumes the new `AnalyticsService` methods (`getDashboardSummary`, `getEngagementTimeline`, `getPlatformSummaries`, `getWebsiteAnalytics`, `getSubstackPosts`) and the `DashboardPeriod` type + model interfaces from `features/analytics/models/dashboard.model.ts`.

---

## Files to Create

| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/store/analytics.store.spec.ts` | Unit tests for the rewritten store |

## Files to Modify

| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/store/analytics.store.ts` | Complete rewrite with full dashboard state |

---

## Tests FIRST

Create `src/PersonalBrandAssistant.Web/src/app/features/analytics/store/analytics.store.spec.ts`.

The tests use Angular `TestBed` to inject the signal store and a mock `AnalyticsService`. Because NgRx signal stores are `providedIn: 'root'`, the test creates a fresh `TestBed` for each test and injects the store instance. The `AnalyticsService` is replaced with a Jasmine spy object that returns controllable Observables.

### Test Stubs

```typescript
/**
 * analytics.store.spec.ts
 *
 * Tests for the rewritten AnalyticsStore with full dashboard state.
 * Mocks AnalyticsService using jasmine.createSpyObj.
 * Uses fakeAsync/tick for async rxMethod testing.
 */

describe('AnalyticsStore', () => {
  // Setup: TestBed with AnalyticsStore, provide mock AnalyticsService via jasmine.createSpyObj
  // with spies for: getDashboardSummary, getEngagementTimeline, getPlatformSummaries,
  //                 getWebsiteAnalytics, getSubstackPosts, getTopContent, getContentReport

  describe('loadDashboard', () => {
    // Test: dispatches parallel requests for all 6 data sources via forkJoin
    //   - Verify all 6 service methods called exactly once
    //   - Verify all state slices populated after emission

    // Test: sets loading=true during fetch, false after all complete
    //   - Call loadDashboard, verify loading() === true immediately
    //   - Flush observables, verify loading() === false

    // Test: passes current period to all service methods
    //   - Set period to '7d', call loadDashboard
    //   - Verify each spy was called with '7d' as period argument

    // Test: partial API failure still populates available sections
    //   - Make getDashboardSummary return throwError, others return data
    //   - Verify summary is null, but timeline/platforms/etc. are populated

    // Test: lastRefreshedAt is updated with ISO string after successful load
  });

  describe('refreshDashboard', () => {
    // Test: passes refresh=true to all service methods
    //   - Call refreshDashboard()
    //   - Verify each spy received refresh=true argument
  });

  describe('setPeriod', () => {
    // Test: updates period state and triggers reload
    //   - Call setPeriod('14d')
    //   - Verify store.period() === '14d'
    //   - Verify loadDashboard was triggered (service methods called)

    // Test: accepts custom date range object
    //   - Call setPeriod({ from: '2026-01-01', to: '2026-01-31' })
    //   - Verify service methods receive the custom range
  });

  describe('computed signals', () => {
    // Test: engagementChange computes correct % from summary
    //   - Set summary with totalEngagement=150, previousEngagement=100
    //   - Verify engagementChange() === 50

    // Test: engagementChange returns null when previousEngagement is 0

    // Test: impressionsChange computes correct % from summary

    // Test: isStale returns true when lastRefreshedAt is older than 30 minutes

    // Test: isStale returns false when lastRefreshedAt is recent
  });
});
```

### Key Testing Patterns

The project uses NgRx signal store (`@ngrx/signals`) with `signalStore`, `withState`, `withMethods`, `withComputed`, and `rxMethod`. For testing `rxMethod` calls:

1. Inject the store from `TestBed`
2. Call the store method (e.g., `store.loadDashboard()`)
3. Use `fakeAsync` + `tick()` to flush the inner Observable pipeline
4. Assert state via signal reads: `store.loading()`, `store.summary()`, etc.

The `AnalyticsService` mock should be created as:

```typescript
const mockAnalyticsService = jasmine.createSpyObj('AnalyticsService', [
  'getDashboardSummary',
  'getEngagementTimeline',
  'getPlatformSummaries',
  'getWebsiteAnalytics',
  'getSubstackPosts',
  'getTopContent',
  'getContentReport',
  'refreshAnalytics',
]);
```

Provide it in TestBed via `{ provide: AnalyticsService, useValue: mockAnalyticsService }`.

---

## Implementation Details

### 1. New State Shape

Replace the existing `AnalyticsState` interface entirely. The new state tracks all dashboard data slices, per-section error strings, the active period, loading flag, and a refresh timestamp.

```typescript
interface AnalyticsDashboardState {
  readonly summary: DashboardSummary | null;
  readonly timeline: readonly DailyEngagement[];
  readonly platformSummaries: readonly PlatformSummary[];
  readonly websiteData: WebsiteAnalyticsResponse | null;
  readonly substackPosts: readonly SubstackPost[];
  readonly topContent: readonly TopPerformingContent[];
  readonly period: DashboardPeriod;
  readonly loading: boolean;
  readonly lastRefreshedAt: string | null;
  readonly errors: DashboardErrors;
}

interface DashboardErrors {
  readonly summary: string | null;
  readonly timeline: string | null;
  readonly platforms: string | null;
  readonly website: string | null;
  readonly substack: string | null;
  readonly topContent: string | null;
}
```

Default `period` is `'30d'`. Default `errors` has all fields set to `null`. All data slices default to `null` or empty arrays.

### 2. Store Definition

The store uses:
- `signalStore({ providedIn: 'root' })` -- singleton, same as existing
- `withState(initialState)` -- new state shape
- `withComputed(...)` -- derived signals for percentage changes and staleness
- `withMethods(...)` -- `loadDashboard`, `refreshDashboard`, `setPeriod`, plus preserved existing methods

### 3. Computed Signals

Define inside `withComputed((store) => { ... })`:

**`engagementChange`** -- Percentage change between current and previous total engagement:
- If `summary` is null or `previousEngagement` is 0, return `null`
- Otherwise: `((totalEngagement - previousEngagement) / previousEngagement) * 100`

**`impressionsChange`** -- Same pattern for impressions.

**`engagementRateChange`** -- Absolute difference: `engagementRate - previousEngagementRate`.

**`contentPublishedChange`** -- Same % pattern for content count.

**`websiteUsersChange`** -- Same % pattern for website users.

**`costPerEngagementChange`** -- Same % pattern for cost per engagement.

**`isStale`** -- Compares `lastRefreshedAt` to current time. Returns `true` if null or older than 30 minutes. Implementation: `!lastRefreshedAt || (Date.now() - new Date(lastRefreshedAt).getTime()) > 30 * 60 * 1000`.

### 4. loadDashboard Method

Use `rxMethod<void>` that reads the current `store.period()` and calls all data-fetching endpoints in parallel via RxJS `forkJoin`. Each Observable in the `forkJoin` is individually wrapped with `catchError` so that a single source failure does not cancel the entire batch.

Pseudocode flow:

```
rxMethod<void>(
  pipe(
    tap(() => patchState(store, { loading: true, errors: initialErrors })),
    switchMap(() => {
      const period = store.period();
      return forkJoin({
        summary: analyticsService.getDashboardSummary(period).pipe(catchError(...)),
        timeline: analyticsService.getEngagementTimeline(period).pipe(catchError(...)),
        platforms: analyticsService.getPlatformSummaries(period).pipe(catchError(...)),
        website: analyticsService.getWebsiteAnalytics(period).pipe(catchError(...)),
        substack: analyticsService.getSubstackPosts().pipe(catchError(...)),
        topContent: analyticsService.getTopContent(...).pipe(catchError(...)),
      });
    }),
    tap(results => {
      patchState(store, {
        summary: results.summary.data,
        timeline: results.timeline.data ?? [],
        platformSummaries: results.platforms.data ?? [],
        websiteData: results.website.data,
        substackPosts: results.substack.data ?? [],
        topContent: results.topContent.data ?? [],
        loading: false,
        lastRefreshedAt: new Date().toISOString(),
        errors: { /* populate from results.*.error */ },
      });
    })
  )
)
```

For the `catchError` wrapper, define a small helper type and function:

```typescript
interface DataOrError<T> {
  readonly data: T | null;
  readonly error: string | null;
}

function wrapCatchError<T>(obs: Observable<T>): Observable<DataOrError<T>> {
  return obs.pipe(
    map(data => ({ data, error: null })),
    catchError(err => of({ data: null, error: err?.message ?? 'Unknown error' }))
  );
}
```

This ensures each `forkJoin` slot always emits a value (never errors), so partial failures propagate as `null` data + error string rather than canceling the entire load.

For `getTopContent`, convert the current period into `from`/`to` date strings. If `period` is a preset string like `'30d'`, calculate `from = now - N days` and `to = now`. If it is a custom `{ from, to }` object, use those dates directly.

### 5. refreshDashboard Method

Identical to `loadDashboard` but passes `refresh = true` to each service call so the backend bypasses its `HybridCache`. Implementation: a simple method that calls the same parallel fetch logic but with the refresh flag set.

```typescript
refreshDashboard: rxMethod<void>(
  // Same pipeline as loadDashboard, but passes refresh=true to each service method
)
```

To avoid code duplication, extract the parallel fetch logic into a private helper function that accepts a `refresh` boolean, then have both `loadDashboard` and `refreshDashboard` call it.

### 6. setPeriod Method

A synchronous method that updates the `period` state and then triggers a reload:

```typescript
setPeriod(period: DashboardPeriod): void {
  patchState(store, { period });
  this.loadDashboard();  // triggers the rxMethod
}
```

Note: because `loadDashboard` is an `rxMethod<void>`, calling it without an argument triggers the pipeline.

### 7. Preserving Existing Methods

The current store has `loadTopContent` and `loadContentReport` methods used by the existing performance detail view. These are **not removed** -- the store retains them for backward compatibility. The `selectedReport` and `dateRange` state fields from the old store can be dropped since the new `period` replaces `dateRange` and `selectedReport` can remain for the detail view if still used.

Check whether `analytics-dashboard.component.ts` or `performance-detail.component.ts` still reference `loadContentReport` or `selectedReport`. If they do, keep those state fields and methods. If not, remove them.

For the rewrite, the safest approach: keep `selectedReport` in state and `loadContentReport` as a method. Drop the old `loadTopContent` rxMethod since `loadDashboard` now fetches top content as part of the parallel batch. Drop the old `setDateRange` since `setPeriod` replaces it.

### 8. Imports

The rewritten store file imports from:

```typescript
import { computed, inject } from '@angular/core';
import { signalStore, withState, withMethods, withComputed, patchState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { pipe, switchMap, tap, forkJoin, of, catchError, map, Observable } from 'rxjs';
import { tapResponse } from '@ngrx/operators';
import { AnalyticsService } from '../services/analytics.service';
import { ContentPerformanceReport, TopPerformingContent } from '../../../shared/models';
import {
  DashboardSummary,
  DailyEngagement,
  PlatformSummary,
  WebsiteAnalyticsResponse,
  SubstackPost,
  DashboardPeriod,
} from '../models/dashboard.model';
```

---

## Existing Code Context

### Current Analytics Store (being replaced)

Located at `src/PersonalBrandAssistant.Web/src/app/features/analytics/store/analytics.store.ts`. Currently 70 lines. Uses `signalStore` with `withState` and `withMethods`. State shape:

- `topContent: readonly TopPerformingContent[]`
- `selectedReport: ContentPerformanceReport | undefined`
- `dateRange: DateRange` (local interface with `from`/`to` strings)
- `loading: boolean`

Methods: `loadTopContent` (rxMethod accepting DateRange), `loadContentReport` (rxMethod accepting content ID string), `setDateRange` (synchronous).

### NgRx Signal Store Patterns in This Project

Other stores in the codebase (`SidecarStore`, `NewsStore`) follow these patterns:
- `{ providedIn: 'root' }` for singleton injection
- `withComputed` for derived signals using `computed()`
- `rxMethod<T>(pipe(...))` for async operations
- `tapResponse({ next, error })` from `@ngrx/operators` for error handling in rxMethod pipes
- `patchState(store, { ... })` for immutable state updates
- Spread operators for immutable array/object updates

### AnalyticsService Methods Available (from section 07)

The extended service provides these methods that the store will consume:

| Method | Returns |
|--------|---------|
| `getDashboardSummary(period, refresh?)` | `Observable<DashboardSummary>` |
| `getEngagementTimeline(period, refresh?)` | `Observable<DailyEngagement[]>` |
| `getPlatformSummaries(period, refresh?)` | `Observable<PlatformSummary[]>` |
| `getWebsiteAnalytics(period, refresh?)` | `Observable<WebsiteAnalyticsResponse>` |
| `getSubstackPosts()` | `Observable<SubstackPost[]>` |
| `getTopContent(from, to, limit?)` | `Observable<TopPerformingContent[]>` (existing) |
| `getContentReport(contentId)` | `Observable<ContentPerformanceReport>` (existing) |

### Division by Zero / Null Semantics

Backend returns `null` for percentage change when the previous period value is 0. The computed signals in the store should mirror this: if `previousEngagement` is `0`, `engagementChange` returns `null` (not `Infinity` or `NaN`). The frontend components (section 09) will render `null` as "N/A".

---

## Checklist

1. Create `features/analytics/store/analytics.store.spec.ts` with all test stubs listed above
2. Rewrite `features/analytics/store/analytics.store.ts` with the new `AnalyticsDashboardState` shape
3. Implement `withComputed` block with `engagementChange`, `impressionsChange`, `engagementRateChange`, `contentPublishedChange`, `websiteUsersChange`, `costPerEngagementChange`, and `isStale` computed signals
4. Implement `loadDashboard` rxMethod using `forkJoin` with per-source `catchError` wrapping
5. Implement `refreshDashboard` rxMethod (same as load but with `refresh=true`)
6. Implement `setPeriod` method that patches period and triggers reload
7. Preserve `loadContentReport` method for the performance detail view
8. Remove old `loadTopContent` and `setDateRange` methods (replaced by new methods)
9. Run `npx ng test --watch=false --browsers=ChromeHeadless` to verify all tests pass

---

## Implementation Notes

- **Code review fix:** `percentChange` rounded to 2 decimal places via `Math.round(x * 10000) / 100` to prevent floating-point display artifacts.
- **Dashboard component updated:** `analytics-dashboard.component.ts` updated to call `loadDashboard()` and `setPeriod(range)` instead of removed `loadTopContent`/`dateRange`.
- **Partial failure pattern:** `wrapCatchError<T>` + `DataOrError<T>` wraps each forkJoin slot so individual API failures populate `errors` state without canceling other requests.
- **Preserved:** `loadContentReport` and `selectedReport` for `performance-detail.component.ts`.
- **Removed:** `loadTopContent`, `setDateRange`, `DateRange` interface (replaced by `DashboardPeriod`).
- **Test count:** 13 tests covering loadDashboard, refreshDashboard, setPeriod, computed signals, partial failure, and staleness.
- **File paths match plan.** No deviations from planned architecture.