# Section 11: Filter Tabs and Batch Toolbar

## Overview

Build two components: `FeedFilterTabs` (6 horizontal tabs with count badges, URL query param sync) and `FeedBatchToolbar` (selection-dependent bulk action bar). Both inject `FeedStore`.

## Dependencies

- **section-09-feed-store** must be complete.

## What Was Built

| Component / File | Path |
|-----------|------|
| `FeedFilterTabsComponent` | `src/PersonalBrandAssistant.Web/src/app/features/feed/feed-filter-tabs/feed-filter-tabs.component.ts` |
| `FeedFilterTabsComponent spec` | `src/PersonalBrandAssistant.Web/src/app/features/feed/feed-filter-tabs/feed-filter-tabs.component.spec.ts` |
| `FeedBatchToolbarComponent` | `src/PersonalBrandAssistant.Web/src/app/features/feed/feed-batch-toolbar/feed-batch-toolbar.component.ts` |
| `FeedBatchToolbarComponent spec` | `src/PersonalBrandAssistant.Web/src/app/features/feed/feed-batch-toolbar/feed-batch-toolbar.component.spec.ts` |
| `FeedPageComponent` (updated) | `src/PersonalBrandAssistant.Web/src/app/features/feed/feed-page/feed-page.component.ts` |
| `FeedPageComponent spec` (updated) | `src/PersonalBrandAssistant.Web/src/app/features/feed/feed-page/feed-page.component.spec.ts` |
| `FeedStore` (updated) | `src/PersonalBrandAssistant.Web/src/app/features/feed/store/feed.store.ts` |
| `FeedService` (updated) | `src/PersonalBrandAssistant.Web/src/app/features/feed/services/feed.service.ts` |
| `BatchMarkRead` (updated) | `src/PBA.Application/Features/Feed/Commands/BatchMarkRead.cs` |
| `BatchReadRequest` (updated) | `src/PBA.Application/Features/Feed/Dtos/BatchReadRequest.cs` |
| `FeedEndpoints` (updated) | `src/PBA.Api/Endpoints/FeedEndpoints.cs` |

## Tests (Write First)

### FeedFilterTabs Tests

```typescript
// Test: renders All, Drafts, Trends, Ideas, Analytics, Approvals tabs
// Test: highlights active tab based on store.activeFilter
// Test: click on tab calls store.setFilter with correct type
// Test: "All" tab passes null to setFilter
// Test: reads initial filter from URL query params
```

### FeedBatchToolbar Tests

```typescript
// Test: shows toolbar when store.hasSelection is true
// Test: hides toolbar when store.hasSelection is false
// Test: displays selected count
// Test: Approve button calls store.batchAct('approve')
// Test: Dismiss button calls store.batchAct('dismiss')
// Test: Mark Read button calls store.batchMarkRead()
// Test: Clear button calls store.clearSelection()
```

## Implementation Details

### FeedFilterTabsComponent

**Tab configuration:**
```typescript
readonly tabs = [
  { label: 'All', value: null },
  { label: 'Drafts', value: 'AgentDraft' },
  { label: 'Trends', value: 'TrendAlert' },
  { label: 'Ideas', value: 'IdeaSuggestion' },
  { label: 'Analytics', value: 'AnalyticsHighlight' },
  { label: 'Approvals', value: 'ApprovalRequest' },
];
```

SystemNotification has no tab -- only appears under "All".

**URL sync:** Read `ActivatedRoute.queryParams` on init. On tab change, use `router.navigate([], { queryParams: { type }, queryParamsHandling: 'merge' })`.

**Count badges:** All tab shows `summary.unreadCount`, Trends shows `summary.trendingCount`, Approvals shows `summary.pendingApprovals`. Others have no badge.

**Tabs:** Custom tab buttons (not PrimeNG TabsModule) — matches existing codebase patterns and provides simpler URL sync + badge integration.

### FeedBatchToolbarComponent

Horizontal bar with: selection count text, Approve button (success), Mark Read button (info), Dismiss button (secondary), Clear button (text). All delegate to `FeedStore` methods. Component controls its own visibility via `@if (store.hasSelection())`.

**Dark theme:** `#161b22` background, `#30363d` border, `8px` radius.

## Deviations from Plan

1. **Custom tabs instead of PrimeNG TabsModule** — Simpler integration with URL sync, badges, and consistent with FeedStatsBar's custom approach.
2. **Added `batchMarkReadByIds` full-stack** — Code review identified that `batchMarkRead()` marks ALL items, not selected ones. Added IDs support through backend command, DTO, endpoint, service, store, and toolbar.
3. **`getBadge` returns `number | null`** — Explicit null semantics instead of relying on JS falsiness of `0`.
4. **Batch toolbar self-contained visibility** — Component handles its own `@if (store.hasSelection())` internally rather than relying solely on parent.
