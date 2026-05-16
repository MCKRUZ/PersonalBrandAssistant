# Section 13: Sidebar Widgets -- FeedSidebar, QuickComposeWidget, TrendingTopicsWidget

**Status:** COMPLETE

## Overview

Build the right-column sidebar: a layout wrapper (`FeedSidebar`) stacking two widgets -- `QuickComposeWidget` (dual-mode idea capture + content creation) and `TrendingTopicsWidget` (ranked topics from store).

## Implementation Notes

- 3 components created: FeedSidebarComponent (layout wrapper), QuickComposeWidgetComponent (dual-mode form), TrendingTopicsWidgetComponent (ranked topics)
- FeedPageComponent updated to replace sidebar placeholder with FeedSidebarComponent
- Uses FormsModule/ngModel for the simple 2-3 field form (spec-driven choice over ReactiveFormsModule)
- Error feedback added: errorMessage signal displays user-visible errors on submission failure
- Topic click filters by TrendAlert type (store API doesn't support topic-level filtering)
- Auto-maps contentType to platform via static PLATFORM_MAP
- Feed-page tests fixed: stale placeholder tests updated to check for actual components
- 26 tests pass (14 new + 12 existing feed-page tests)
- Test files: feed-sidebar.component.spec.ts (2 tests), quick-compose-widget.component.spec.ts (7 tests), trending-topics-widget.component.spec.ts (5 tests)

## Dependencies

- **section-09-feed-store** (provides `trendingTopics()`, `setFilter()`)
- **section-08-angular-models-service** (provides `TrendingTopic` model, `FeedItemType`)
- Existing services: `IdeaService`, `ContentService`

## What Gets Built

| Component | Path |
|-----------|------|
| `FeedSidebarComponent` | `src/PersonalBrandAssistant.Web/src/app/features/feed/feed-sidebar/` |
| `QuickComposeWidgetComponent` | `src/PersonalBrandAssistant.Web/src/app/features/feed/quick-compose-widget/` |
| `TrendingTopicsWidgetComponent` | `src/PersonalBrandAssistant.Web/src/app/features/feed/trending-topics-widget/` |

## Tests (Write First)

### FeedSidebar Tests

```typescript
// Test: renders QuickComposeWidget
// Test: renders TrendingTopicsWidget
```

### QuickComposeWidget Tests

```typescript
// Test: defaults to Quick Idea mode
// Test: toggles to New Content mode
// Test: Quick Idea mode shows title + note fields
// Test: New Content mode shows title + content type dropdown
// Test: Quick Idea submit calls IdeaService.createIdea
// Test: New Content submit calls ContentService.createContent and navigates
// Test: clears form after successful submission
```

### TrendingTopicsWidget Tests

```typescript
// Test: renders topic list from store.trendingTopics
// Test: shows rank number, topic name, and count badge
// Test: click on topic triggers store.setFilter for TrendAlert
// Test: shows "No trends yet" when list is empty
// Test: shows skeleton loader when loading
```

## Implementation Details

### FeedSidebar

Thin layout wrapper. Vertical flex column, `gap: 16px`. Imports and renders both child widgets.

### QuickComposeWidget

Dual-mode form with signal-based state: `mode` ('idea' | 'content'), `title`, `note`, `contentType`, `submitting`.

**Idea mode:** title input + note textarea + Submit. Calls `IdeaService.create()`. Clears form on success.

**Content mode:** title input + content type dropdown + Submit. Calls `ContentService.create()`. Navigates to `/content/{id}` on success.

Uses `FormsModule` with `[(ngModel)]` (appropriate for 2-3 simple fields). PrimeNG `ButtonModule`, `SelectModule`, `InputTextModule`, `TextareaModule`.

### TrendingTopicsWidget

Reads `store.trendingTopics()`. Shows ranked list with: rank number, topic name, count badge. Clicking a topic calls `store.setFilter('TrendAlert')`. Three states: loading (skeleton rows), empty ("No trends yet"), data (topic list).

**Dark theme:** `#161b22` background, `#30363d` border, `8px` radius, `16px` padding. Topic rows with `#f0f6fc` text, count badges with `#30363d` background.
