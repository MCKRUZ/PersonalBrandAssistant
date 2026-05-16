# Section 16: Frontend Tests (Jasmine/Karma)

## Overview

This section covers all Angular Jasmine/Karma test files for the Feed module: services, signal store, and all components. Every spec file is colocated with its source file as `*.spec.ts` or `*.component.spec.ts`. A shared test utility file provides mock factories and store mocking.

## Dependencies

- **Section 08** (Angular Models & Service): `FeedItem`, `FeedSummary`, `TrendingTopic`, `FeedActionResult` interfaces, `FeedItemType`/`FeedItemPriority` enums, `FeedService`, `FeedHubService`
- **Section 09** (Feed Store): `FeedStore` signal store
- **Section 10** (Feed Page & Stats Bar): `FeedPageComponent`, `FeedStatsBarComponent`
- **Section 11** (Filter Tabs & Batch Toolbar): `FeedFilterTabsComponent`, `FeedBatchToolbarComponent`
- **Section 12** (Card List & Card): `FeedCardListComponent`, `FeedCardComponent`
- **Section 13** (Sidebar Widgets): `FeedSidebarComponent`, `QuickComposeWidgetComponent`, `TrendingTopicsWidgetComponent`
- **Section 14** (New Items Banner): `FeedNewItemsBannerComponent`

## Test Conventions

- `TestBed.configureTestingModule` for all test suites
- `jasmine.createSpyObj` for service mocks
- `NO_ERRORS_SCHEMA` for components that import child components
- `overrideComponent()` to strip real child imports from standalone components (prevents transitive dependency injection errors like ActivatedRoute)
- `provideHttpClient()` + `provideHttpClientTesting()` for HTTP service tests
- `afterEach(() => httpMock.verify())` for HTTP service tests
- `fixture.componentRef.setInput('name', value)` for signal inputs
- `signal()` for mocking store computed signals in `createMockFeedStore()`
- `data-testid` selectors for DOM queries

## Actual File Paths

All files under `src/PersonalBrandAssistant.Web/src/app/features/feed/`:

```
testing/
  feed-test-utils.ts              (NEW — shared mock factories)
services/
  feed.service.spec.ts            (NEW — 11 tests)
  feed-hub.service.spec.ts        (NEW — 4 tests)
store/
  feed.store.spec.ts              (NEW — 29 tests)
feed-page/
  feed-page.component.spec.ts     (NEW — 7 tests)
feed-stats-bar/
  feed-stats-bar.component.spec.ts    (NEW — 7 tests)
feed-filter-tabs/
  feed-filter-tabs.component.spec.ts  (NEW — 5 tests)
feed-batch-toolbar/
  feed-batch-toolbar.component.spec.ts (NEW — 7 tests)
feed-card-list/
  feed-card-list.component.spec.ts    (NEW — 5 tests)
feed-card/
  feed-card.component.spec.ts         (NEW — 14 tests)
feed-sidebar/
  feed-sidebar.component.spec.ts      (NEW — 2 tests)
quick-compose-widget/
  quick-compose-widget.component.spec.ts  (NEW — 7 tests)
trending-topics-widget/
  trending-topics-widget.component.spec.ts (NEW — 5 tests)
feed-new-items-banner/
  feed-new-items-banner.component.spec.ts  (NEW — 5 tests)
```

**Total: 14 files, 108 tests (337 Angular tests total, 0 failures)**

---

## Shared Test Utility: feed-test-utils.ts

**File:** `testing/feed-test-utils.ts`

Exports 4 functions:

1. **`mockFeedItem(overrides?)`** — returns a valid `FeedItem` with sensible defaults, accepts partial overrides. Uses `crypto.randomUUID()` for unique IDs.
2. **`mockFeedSummary(overrides?)`** — returns a valid `FeedSummary` with defaults.
3. **`mockTrendingTopic(overrides?)`** — returns a valid `TrendingTopic` with defaults.
4. **`createMockFeedStore()`** — returns an object with `WritableSignal` properties for all state/computed (items, loading, summary, trendingTopics, selectedIds, hasSelection, selectedCount, isAllSelected, newItemCount, page, totalCount, pageSize, activeFilter, error, lastBatchFailures, summaryLoading) and `jasmine.createSpy()` for all store methods.

---

## Test Results by File

| File | Tests | Notes |
|------|-------|-------|
| feed.service.spec.ts | 11 | All HTTP methods including batchMarkReadByIds |
| feed-hub.service.spec.ts | 4 | SignalR connection, feedItemReceived$, summaryUpdated$, disconnect |
| feed.store.spec.ts | 29 | onInit, filters, selection, markRead, actOnItem, batch ops, error paths, isAllSelected, SignalR |
| feed-page.component.spec.ts | 7 | Child element rendering, page title/subtitle |
| feed-stats-bar.component.spec.ts | 7 | Stat cards, summary values, click-to-filter, skeleton |
| feed-filter-tabs.component.spec.ts | 5 | Tab rendering, active tab, click, All passes null |
| feed-batch-toolbar.component.spec.ts | 7 | Show/hide, count, approve, dismiss, clear, mark read |
| feed-card-list.component.spec.ts | 5 | Card rendering, skeleton, empty state, selectedIds |
| feed-card.component.spec.ts | 14 | Borders (4 types), icons, priority badges, primary actions (3 types), dismiss, is-read, action emit |
| feed-sidebar.component.spec.ts | 2 | QuickCompose + TrendingTopics child rendering |
| quick-compose-widget.component.spec.ts | 7 | Mode toggle, form fields, submit, navigation, clear |
| trending-topics-widget.component.spec.ts | 5 | Topic list, rank/name/count, click filter, loading, empty state |
| feed-new-items-banner.component.spec.ts | 5 | Show/hide, count, click load, animation class |

---

## Deviations from Plan

1. **`overrideComponent()` needed for standalone components**: The plan specified `NO_ERRORS_SCHEMA` alone for parent components, but standalone components that import real children need `overrideComponent(Component, { set: { imports: [], schemas: [NO_ERRORS_SCHEMA] } })` to prevent Angular from instantiating child components and their transitive dependencies (e.g., ActivatedRoute).

2. **No `fakeAsync`/`tick` used**: The store tests rely on synchronous `of()` returns from mocked services, so timing-sensitive test patterns weren't necessary.

3. **Code review auto-fixes added 11 tests**: The initial implementation had ~97 tests. Code review identified missing error paths, untested batch methods, isAllSelected computed, fragile ng-reflect assertion, missing Mark Read button test, and missing batchMarkReadByIds service test. All auto-fixed, bringing total to 108 feed tests (337 Angular total).

4. **`ComponentRef` import**: Must be imported from `@angular/core`, not `@angular/core/testing`. The testing module declares it locally but doesn't export it.

5. **Store test count**: Plan outlined ~18 tests. Final count is 29, including error path coverage (3 tests), batch method coverage (batchDismiss, batchMarkReadByIds, batchAct — 3 tests), isAllSelected (2 tests), and summaryUpdated$ SignalR (1 test) added during code review.

---

## Implementation Notes

1. **Shared mock factory** eliminates ~150 lines of duplicated mock creation code across 13 spec files.

2. **`createMockFeedStore()` pattern**: Uses `WritableSignal` (not readonly computed) for all store properties, allowing tests to set values directly with `.set()`. This is a pragmatic trade-off — it doesn't enforce read-only semantics but enables clean test setup.

3. **Run command**: `cd src/PersonalBrandAssistant.Web && npx ng test --watch=false --browsers=ChromeHeadless`

4. **PrimeNG components**: `NO_ERRORS_SCHEMA` avoids importing PrimeNG modules. Tests verify behavior and DOM state, not PrimeNG rendering.

5. **SignalR in store tests**: `FeedHubService` mocked with `Subject` observables for `feedItemReceived$` and `summaryUpdated$`, enabling controlled emission in tests.
