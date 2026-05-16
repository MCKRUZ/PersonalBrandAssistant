# Section 09: Feed Signal Store

## Overview

Create the `FeedStore` -- a root-scoped NgRx signal store that manages all feed state: paginated items, summary statistics, trending topics, selection, filtering, and real-time updates from SignalR. This is the single source of truth for the entire Feed UI.

## Dependencies

- **section-08-angular-models-service** must be complete (`FeedService`, `FeedHubService`, all TypeScript interfaces).

Blocks **section-10** through **section-14** (all feed UI components).

## What Gets Built

| Component | Path |
|-----------|------|
| `FeedStore` | `src/PersonalBrandAssistant.Web/src/app/features/feed/store/feed.store.ts` |
| `FeedStore` spec | `src/PersonalBrandAssistant.Web/src/app/features/feed/store/feed.store.spec.ts` |

## State Shape

```typescript
type FeedState = {
  items: FeedItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  activeFilter: FeedItemType | null;
  loading: boolean;
  error: string | null;
  summary: FeedSummary | null;
  summaryLoading: boolean;
  trendingTopics: TrendingTopic[];
  selectedIds: string[];
  newItemCount: number;
  lastBatchFailures: { id: string; reason: string }[];
};
```

Initial: empty arrays, null filter, page 1, pageSize 20, loading false, lastBatchFailures empty.

## Tests (Write First)

```typescript
// --- Initial State ---
// Test: initial state has empty items, page 1, null filter, not loading

// --- Data Loading ---
// Test: loadItems patches items and totalCount from service response
// Test: loadItems sets loading true then false
// Test: loadSummary patches summary from service response
// Test: loadTrending patches trendingTopics from service response

// --- Filtering/Pagination ---
// Test: setFilter updates activeFilter, resets page to 1, triggers reload
// Test: setPage updates page, triggers reload

// --- Selection ---
// Test: toggleSelect adds id when not present, removes when present
// Test: selectAll sets selectedIds to all item IDs
// Test: clearSelection sets selectedIds to empty array

// --- Computed ---
// Test: hasSelection returns true when selectedIds non-empty
// Test: selectedCount returns correct count

// --- Actions ---
// Test: markRead calls service.markRead, updates local item state
// Test: actOnItem calls service.actOnItem, handles navigation result
// Test: batchMarkRead calls service, reloads items and summary
// Test: batchAct calls service, reloads, clears selection

// --- SignalR ---
// Test: incrementNewItemCount increases counter by 1
// Test: loadNewItems resets page, reloads, resets newItemCount to 0
// Test: updateSummary patches summary directly

// --- onInit ---
// Test: onInit calls loadItems, loadSummary, loadTrending
// Test: onInit subscribes to feedHubService observables
```

## Implementation Details

### Store Definition

```typescript
export const FeedStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed(({ selectedIds, items }) => ({
    hasSelection: computed(() => selectedIds().length > 0),
    selectedCount: computed(() => selectedIds().length),
    isAllSelected: computed(() => selectedIds().length === items().length && items().length > 0),
  })),
  withMethods(...),
  withHooks(...)
);
```

### Methods

**Data loading (rxMethod<void>):** `loadItems`, `loadSummary`, `loadTrending` -- follow established pipe/switchMap/tapResponse pattern from content.store.ts.

**Synchronous mutations:** `setFilter`, `setPage`, `toggleSelect`, `selectAll`, `clearSelection` -- all use `patchState` with spread operators for immutability.

**API actions:** `markRead`, `actOnItem`, `batchMarkRead`, `batchDismiss`, `batchAct` -- call service methods, update local state or reload.

**SignalR:** `incrementNewItemCount`, `loadNewItems`, `updateSummary`.

### withHooks (onInit)

1. Call `loadItems()`, `loadSummary()`, `loadTrending()`
2. Subscribe to `feedHubService.feedItemReceived$` -> `incrementNewItemCount()`
3. Subscribe to `feedHubService.summaryUpdated$` -> `updateSummary(summary)`

Both store and service are root-scoped. SignalR subscriptions use `takeUntilDestroyed(DestroyRef)` for safe cleanup. The store does NOT call `feedHubService.connect()` -- the feed page component owns the SignalR connection lifecycle.

### Deviations from Plan

- **Added `lastBatchFailures`** to state -- captures partial failures from `batchAct` for UI display (code review decision).
- **DestroyRef cleanup** -- SignalR subscriptions use `takeUntilDestroyed` instead of raw `.subscribe()` (code review fix).
- **Error handling** -- `loadSummary` and `loadTrending` error handlers now set `error` state consistently (code review fix).
- **28 tests** total (25 planned + 3 added during review: markRead error, batchMarkRead isRead=false, batchAct partial failures).
