# Section 14: New Items Banner and SignalR Wiring

**Status:** COMPLETE

## Overview

Create the `FeedNewItemsBanner` component and wire the real-time SignalR connection between `FeedHubService` and `FeedStore`. The banner appears when new feed items arrive via SignalR, offering a "Show" button to refresh the list.

## Implementation Notes

- FeedNewItemsBannerComponent created with slide-down animation, singular/plural count, "Show" button
- SignalR wiring was already completed in section-09 (FeedStore onInit) with takeUntilDestroyed
- FeedPageComponent updated: replaced inline placeholder with banner component, removed unused .placeholder CSS
- Feed-page tests updated: data-testid changed from "new-items-banner-slot" to "new-items-banner"
- 6 new tests pass (visible/hidden, singular/plural, click handler, animation class)
- Test file: feed-new-items-banner.component.spec.ts

## Dependencies

- **section-08-angular-models-service** (provides `FeedHubService`)
- **section-09-feed-store** (provides `FeedStore` with `newItemCount`, `incrementNewItemCount()`, `loadNewItems()`)

Parallelizable with sections 10-13. Blocks **section-16-frontend-tests**.

## What Gets Built

| Component | Path |
|-----------|------|
| `FeedNewItemsBannerComponent` | `src/PersonalBrandAssistant.Web/src/app/features/feed/feed-new-items-banner/` |
| SignalR wiring in FeedStore | Modification to `feed.store.ts` `withHooks.onInit` |

## Real-Time Update Flow

1. **Backend:** `CreateFeedItem.Handler` saves item, pushes via `IHubContext<FeedHub>`
2. **FeedHubService:** Receives `ReceiveFeedItem` event, pushes through `feedItemReceived$` Subject
3. **FeedStore:** Subscribes in `onInit`, increments `newItemCount` (does NOT auto-insert items)
4. **FeedNewItemsBanner:** Shows count, "Show" button calls `store.loadNewItems()` (resets page, reloads from API, zeros counter)

## Tests (Write First)

### FeedNewItemsBanner Tests

```typescript
// Test: visible when store.newItemCount > 0
// Test: hidden when store.newItemCount is 0
// Test: displays correct count in message (singular/plural)
// Test: click Show button calls store.loadNewItems
// Test: has slide-down animation class
```

### SignalR Wiring Tests (in FeedStore spec)

```typescript
// Test: subscribes to feedHubService.feedItemReceived$ and calls incrementNewItemCount
// Test: subscribes to feedHubService.summaryUpdated$ and calls updateSummary
// Test: multiple feedItemReceived$ emissions accumulate count
```

## Implementation Details

### FeedNewItemsBanner Component

Standalone component. Injects `FeedStore`. Conditionally rendered by parent `FeedPage` with `@if (store.newItemCount() > 0)`.

**Template:** Blue accent banner (`#1f6feb` background) with count message (singular/plural) and "Show" button. Slide-down CSS animation on entrance.

**Component:** Exposes `count` computed signal from `store.newItemCount()` and `onShow()` method delegating to `store.loadNewItems()`.

### SignalR Wiring in FeedStore

In `withHooks.onInit`:

```typescript
const feedHubService = inject(FeedHubService);
feedHubService.feedItemReceived$.subscribe(() => store.incrementNewItemCount());
feedHubService.summaryUpdated$.subscribe((summary) => store.updateSummary(summary));
```

Both store and service are root-scoped -- subscriptions live for app lifetime. Feed updates accumulate even when user is on other pages.
