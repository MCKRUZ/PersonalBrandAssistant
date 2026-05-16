# Section 10: Feed Page and Stats Bar

## Overview

Build the FeedPage layout component (the application's home page) and the FeedStatsBar component. FeedPage is a CSS Grid two-column shell that composes all feed sub-components. FeedStatsBar renders four clickable KPI cards driven by `FeedStore.summary()`. This section replaces the existing placeholder `FeedComponent`.

## Dependencies

- **section-09-feed-store** must be complete.
- Components from sections 11-14 do not need to exist yet -- use placeholder divs initially.

## What Gets Built

| Component | Path |
|-----------|------|
| `FeedPageComponent` | `src/PersonalBrandAssistant.Web/src/app/features/feed/feed-page/feed-page.component.ts` |
| `FeedStatsBarComponent` | `src/PersonalBrandAssistant.Web/src/app/features/feed/feed-stats-bar/feed-stats-bar.component.ts` |
| Route update | `src/PersonalBrandAssistant.Web/src/app/app.routes.ts` |

## Route Update

Change `app.routes.ts` from loading `FeedComponent` to `FeedPageComponent`. Delete old placeholder `feed.component.ts`.

## Tests (Write First)

### FeedPage Tests

```typescript
// Test: renders FeedStatsBar component
// Test: renders FeedFilterTabs component
// Test: renders FeedCardList component
// Test: renders FeedSidebar component
// Test: renders FeedBatchToolbar when items are selected
// Test: hides FeedBatchToolbar when no items selected
// Test: renders FeedNewItemsBanner when newItemCount > 0
// Test: renders paginator when totalCount > pageSize
// Test: two-column CSS Grid layout
```

### FeedStatsBar Tests

```typescript
// Test: renders 4 stat cards
// Test: displays pending approvals count from summary
// Test: displays trending count from summary
// Test: displays engagement delta with trend arrow
// Test: displays unread count from summary
// Test: click on stat card triggers setFilter on store
// Test: shows skeleton state when summary is null/loading
```

## Implementation Details

### FeedPageComponent

CSS Grid layout: full-width header (stats bar), then two columns (main content + 320px sidebar). Main column stacks: filter tabs, batch toolbar (conditional), new items banner (conditional), card list, paginator (conditional).

### FeedStatsBarComponent

Four KPI cards in a grid row. Each card shows: large number, label text, click handler calling `store.setFilter()`. Skeleton loading state when summary is null. Engagement delta shows percentage with up/down arrow and positive/negative color.

**Stat card click mapping:**
- Pending Approvals -> `setFilter('ApprovalRequest')`
- Trending -> `setFilter('TrendAlert')`
- Engagement -> `setFilter('AnalyticsHighlight')`
- Unread -> `setFilter(null)` (all items)

**Dark theme:** `#161b22` card background, `#f0f6fc` values, `#8b949e` labels, `#58a6ff` hover border, `#3fb950` positive delta, `#f85149` negative delta.

### Deviations from Plan

- **`Math` replaced with computed signal** -- `totalPages` is a `computed()` signal instead of exposing `Math` global on the component (code review fix).
- **Responsive breakpoints added** -- `@media (max-width: 768px)` stacks grid to single column and stats to 2x2; `@media (max-width: 480px)` stacks stats to single column (code review fix).
- **Old FeedComponent deleted** -- `feed.component.ts` and `feed.component.html` (untracked placeholders) removed. Route updated to lazy-load `FeedPageComponent`.
- **15 tests total** -- 11 FeedPage + 15 FeedStatsBar (11 main + 2 negative delta + 1 skeleton + 1 engagement click) = 40 component tests across both specs.
