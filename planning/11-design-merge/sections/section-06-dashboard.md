# Section 06: Dashboard -- KPI Strip, Today's Schedule, Recent Items, AI Suggestions

## Overview

The Dashboard is the default landing page at `/dashboard`. It provides an at-a-glance overview of the PBA state: 4 KPI cards (pending review, published this week, total reach, AI cost), today's publishing schedule as a vertical timeline, a list of recent content items with status badges, and AI content suggestions from the daily briefing. This is a full replacement of the existing `DashboardComponent`.

**Depends on**: Section 01 (Backend Extensions -- analytics/content/calendar endpoints), Section 04 (App Shell -- route configuration, core models, layout)
**Blocks**: Nothing (leaf section)

## Files to Create

| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Web/src/app/pages/dashboard/dashboard.component.ts` | Main dashboard page component |
| `src/PersonalBrandAssistant.Web/src/app/pages/dashboard/dashboard.component.scss` | Dashboard styles |
| `src/PersonalBrandAssistant.Web/src/app/pages/dashboard/dashboard.component.spec.ts` | Component tests |
| `src/PersonalBrandAssistant.Web/src/app/pages/dashboard/dashboard.store.ts` | NgRx SignalStore (feature-scoped) |
| `src/PersonalBrandAssistant.Web/src/app/pages/dashboard/dashboard.store.spec.ts` | Store tests |
| `src/PersonalBrandAssistant.Web/src/app/pages/dashboard/dashboard-api.service.ts` | API service wrapping dashboard data calls |

## Files to Modify

| File | Change |
|------|--------|
| `src/PersonalBrandAssistant.Web/src/app/app.routes.ts` | Update dashboard route to point to new component (if not already done by Section 04) |

## Files to Delete (after implementation is verified)

| File | Reason |
|------|--------|
| `src/PersonalBrandAssistant.Web/src/app/features/dashboard/` | Replaced by new `pages/dashboard/` implementation |

## Tests (Write First)

### DashboardStore Tests

File: `src/PersonalBrandAssistant.Web/src/app/pages/dashboard/dashboard.store.spec.ts`

```typescript
// Test: DashboardStore loads KPIs from GET /api/analytics/dashboard
//   - Mock HttpClient to return { pendingCount: 5, publishedCount: 12, reach: 45200, aiCost: 3.47 }
//   - Inject DashboardStore, trigger load
//   - Assert store.kpis() matches the mock response

// Test: DashboardStore loads today's schedule from GET /api/calendar
//   - Mock HttpClient to return array of CalendarSlot objects for today
//   - Trigger load
//   - Assert store.schedule() contains the correct slots
//   - Assert the API was called with from={today}&to={today} query params

// Test: DashboardStore loads recent items from GET /api/content (sorted by created desc, limit 10)
//   - Mock HttpClient to return 10 ContentItem objects sorted by createdAt desc
//   - Trigger load
//   - Assert store.recentItems() has length 10
//   - Assert items are in descending created order
//   - Assert the API was called with sort=created&order=desc&take=10

// Test: DashboardStore sets loading state during API calls
//   - Trigger load
//   - Assert store.isLoading() is true before response
//   - Flush mock response
//   - Assert store.isLoading() is false after response

// Test: DashboardStore handles API errors gracefully
//   - Mock HttpClient to return 500 error
//   - Trigger load
//   - Assert store.error() contains error message
//   - Assert store.isLoading() is false
//   - Assert store.kpis() remains undefined or empty (not stale data from a different call)
```

### DashboardComponent Tests

File: `src/PersonalBrandAssistant.Web/src/app/pages/dashboard/dashboard.component.spec.ts`

```typescript
// Test: DashboardComponent renders 4 KpiCard components with correct data
//   - Provide DashboardStore with mock KPI data:
//     { pendingCount: 3, publishedCount: 8, reach: 15000, aiCost: 2.15 }
//   - Assert 4 KpiCard components are rendered
//   - Assert first card shows value "3" with label "pending review"
//   - Assert second card shows value "8" with label "published this week"
//   - Assert third card shows value "15K" with label "reach" (formatted)
//   - Assert fourth card shows value "$2.15" with label containing "ai" and "cost"

// Test: DashboardComponent renders today's schedule as vertical timeline
//   - Provide store with 3 CalendarSlot objects:
//     [{ time: '09:00', contentTitle: 'AI Trends Post', platform: 'LinkedIn', status: 'Scheduled' },
//      { time: '12:00', contentTitle: null, platform: null, status: null },
//      { time: '15:00', contentTitle: 'MCP Retro', platform: 'TwitterX', status: 'Draft' }]
//   - Assert 3 timeline entries are rendered
//   - Assert first entry shows time "09:00", title "AI Trends Post", LinkedIn icon, status badge
//   - Assert second entry shows time "12:00" and "Empty" or empty slot indicator
//   - Assert third entry shows time "15:00", title "MCP Retro", Twitter icon

// Test: DashboardComponent renders recent items as list with status badges
//   - Provide store with 5 ContentItem objects with varying statuses
//   - Assert 5 list items are rendered
//   - Assert each item shows title, platform icon, StatusBadge component, and date

// Test: Clicking a schedule slot navigates to content editor
//   - Provide store with a CalendarSlot that has contentId = 'abc-123'
//   - Click the schedule slot element
//   - Assert Router.navigate was called with ['/content', 'abc-123', 'edit']

// Test: Clicking a recent item navigates to content editor
//   - Provide store with a ContentItem with id = 'def-456'
//   - Click the recent item row
//   - Assert Router.navigate was called with ['/content', 'def-456', 'edit']

// Test: DashboardComponent shows loading skeleton while data loads
//   - Set store.isLoading() to true
//   - Assert KPI cards show skeleton/placeholder content
//   - Assert schedule shows loading indicator
//   - Assert recent items show loading indicator

// Test: DashboardComponent shows empty state when no schedule items exist
//   - Provide store with empty schedule array
//   - Assert a "No content scheduled today" message or empty state is rendered

// Test: DashboardComponent shows error state with retry button on API failure
//   - Set store.error() to 'Failed to load dashboard data'
//   - Assert error message is displayed
//   - Assert retry button is present
//   - Click retry button
//   - Assert store load method was called again
```

## Implementation Details

### 1. DashboardApiService

**File: `src/PersonalBrandAssistant.Web/src/app/pages/dashboard/dashboard-api.service.ts`**

A lightweight service wrapping the 3-4 API calls the dashboard needs. Inject `HttpClient` directly (or extend the existing `ApiService` base class if one exists).

Methods:
- `getKpis(): Observable<DashboardKpis>` -- calls `GET /api/analytics/dashboard`
- `getTodaySchedule(): Observable<CalendarSlot[]>` -- calls `GET /api/calendar?from={today}&to={today}` where `{today}` is the current date in ISO format
- `getRecentItems(): Observable<ContentItem[]>` -- calls `GET /api/content?sort=created&order=desc&take=10`
- `getBriefingSummary(): Observable<AiSuggestion[]>` -- calls `GET /api/integration/briefing/summary` (daily AI content suggestions)

Define the `DashboardKpis` interface locally in this file or in a dashboard models file:

```typescript
export interface DashboardKpis {
  pendingCount: number;
  publishedCount: number;
  reach: number;
  aiCost: number;
}

export interface AiSuggestion {
  topic: string;
  platform: PlatformType;
  source: string;  // e.g., "from feed: Gemini 3.0 release", "trend: MCP +89%"
}
```

Import `CalendarSlot` and `ContentItem` from core models (defined in Section 04).

### 2. DashboardStore

**File: `src/PersonalBrandAssistant.Web/src/app/pages/dashboard/dashboard.store.ts`**

NgRx SignalStore, feature-scoped (provided at the component level, not root). State is released on navigation away from the dashboard.

State shape:

```typescript
interface DashboardState {
  kpis: DashboardKpis | undefined;
  schedule: CalendarSlot[];
  recentItems: ContentItem[];
  suggestions: AiSuggestion[];
  isLoading: boolean;
  error: string | undefined;
}
```

Use `withState()` for the initial state, `withMethods()` for the load action.

The load method should use `resource()` or `rxMethod` to fetch all data sources in parallel (`forkJoin` of the 3-4 API calls). Set `isLoading` to true before the calls, false after all complete. On error, set the `error` signal and reset `isLoading`.

Refresh strategy: Load on component init. Optionally refresh on route activation (if the user navigates away and back). No polling -- the dashboard shows a snapshot that refreshes on visit.

### 3. DashboardComponent

**File: `src/PersonalBrandAssistant.Web/src/app/pages/dashboard/dashboard.component.ts`**

Angular standalone component. Injects `DashboardStore` (provided locally via `providers: [DashboardStore]`) and `Router`.

Template layout (top to bottom):

1. **Page header** with breadcrumb "analyze / dashboard", title "Dashboard" in display font, and action buttons: "Today's brief" (secondary) and "New content" (primary, navigates to `/content/new`).

2. **KPI strip** -- a 4-column CSS grid containing 4 `KpiCardComponent` instances:
   - Pending Review: `value=store.kpis().pendingCount`, `label='pending review'`, `flagged=count > 0`, `sub=count > 0 ? 'needs your eyes' : 'all clear'`
   - Published This Week: `value=store.kpis().publishedCount`, `label='published this week'`, `trend` computed from comparison to last week (if available)
   - Reach: `value=store.kpis().reach`, `label='reach'`, `sub='last 14 days'` (formatted with K/M suffixes by KpiCard)
   - AI Cost: `value='$' + store.kpis().aiCost.toFixed(2)`, `label='ai cost'`, `sub='/ $X budget'` (if budget info available)

3. **Two-column layout** below the KPI strip:
   - Left column (wider, ~60%): Today's Schedule + AI Suggestions
   - Right column (~40%): Recent Items

4. **Today's Schedule** -- a vertical timeline showing time slots from the calendar API. Each slot renders:
   - Time label (e.g., "09:00")
   - Content title (or "Empty" with a muted "+" icon for empty slots)
   - Platform icon (small icon representing the target platform)
   - `StatusBadgeComponent` showing the content's current status
   - Click handler: navigate to `/content/{contentId}/edit` for filled slots

5. **AI Suggestions** (optional, from briefing API) -- a list of suggested content topics:
   - Topic text
   - Target platform chip
   - Source label (e.g., "from feed: Gemini 3.0 release")
   - "Use as inspiration" action that navigates to `/content/new` with query params

6. **Recent Items** -- a compact list of the 10 most recent content items:
   - Content title
   - Platform icon
   - `StatusBadgeComponent`
   - Created date (relative format: "2h ago", "yesterday")
   - Click handler: navigate to `/content/{contentId}/edit`

### 4. Component Styling

**File: `src/PersonalBrandAssistant.Web/src/app/pages/dashboard/dashboard.component.scss`**

Key layout styles:

```scss
:host {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow-y: auto;
}

.dashboard-content {
  padding: 24px 32px 60px;
}

.kpi-strip {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 14px;
  margin-bottom: 28px;
}

.dashboard-body {
  display: grid;
  grid-template-columns: 1.5fr 1fr;
  gap: 24px;
}

.schedule-timeline {
  display: flex;
  flex-direction: column;
  gap: 0;

  .slot {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 12px 16px;
    border-left: 2px solid var(--p-surface-300);
    cursor: pointer;
    transition: background 150ms;

    &:hover {
      background: var(--p-surface-50);
    }

    &.empty {
      opacity: 0.5;
    }
  }

  .slot-time {
    font-family: 'JetBrains Mono', monospace;
    font-size: 12px;
    color: var(--p-surface-500);
    min-width: 48px;
  }
}

.recent-list {
  .recent-item {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 10px 12px;
    border-radius: 6px;
    cursor: pointer;
    transition: background 150ms;

    &:hover {
      background: var(--p-surface-50);
    }
  }

  .item-title {
    flex: 1;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .item-date {
    font-size: 12px;
    color: var(--p-surface-500);
  }
}
```

### 5. Loading and Empty States

**Loading state**: While `store.isLoading()` is true, render:
- KPI strip: 4 skeleton cards (PrimeNG `p-skeleton` with card dimensions)
- Schedule: 3-5 skeleton rows
- Recent items: 5 skeleton rows

**Empty states**:
- No scheduled items: "No content scheduled today" with a subtle "Plan your day" link to `/calendar`
- No recent items: "No content created yet" with a "Create your first post" button linking to `/content/new`
- AI suggestions unavailable: Hide the suggestions section entirely (don't show an error for an optional feature)

**Error state**: If `store.error()` is set, show a PrimeNG toast with error severity and a retry action. The main content area shows a centered error message with a "Retry" button that calls `store.load()`.

### 6. Routing

The dashboard route should already be configured by Section 04 in `app.routes.ts`. If updating, ensure:

```typescript
{
  path: 'dashboard',
  loadComponent: () => import('./pages/dashboard/dashboard.component').then(m => m.DashboardComponent),
  data: { title: 'Dashboard', sidecarContext: 'dashboard' }
}
```

The `sidecarContext: 'dashboard'` enables dashboard-specific quick prompts in the sidecar panel (e.g., "What should I post today?", "Summarize this week's performance").

## Error Handling

| Scenario | Behavior |
|----------|----------|
| Any API call fails | PrimeNG toast with error severity, store sets `error` signal, retry button shown |
| Partial API failure (one of 3-4 calls fails) | Show available data, toast for failed section, retry loads all |
| Briefing API unavailable | Hide suggestions section silently (optional feature) |
| Calendar returns empty | Show "No content scheduled today" empty state |
| Navigation to editor for deleted content | Editor handles 404, not dashboard's responsibility |

## Implementation Order

1. Create `DashboardApiService` with all 4 API methods
2. Create `DashboardStore` with state, load method, loading/error handling
3. Write `DashboardStore` tests
4. Create `DashboardComponent` template and styles
5. Wire component to store, implement KPI strip with `KpiCardComponent`
6. Implement schedule timeline with `StatusBadgeComponent`
7. Implement recent items list with navigation
8. Implement AI suggestions section
9. Add loading skeletons and empty states
10. Write `DashboardComponent` tests
11. Delete old `features/dashboard/` directory after verification
12. Run tests: `cd src/PersonalBrandAssistant.Web && npx ng test --watch=false --browsers=ChromeHeadless`

## Actual Implementation Notes

### Deviations from Plan

1. **Old `features/dashboard/` NOT deleted**: Kept to avoid breaking potential references from other components. Will clean up in a dedicated pass after all sections are migrated.

2. **API method naming**: `getBriefingSummary()` was implemented as `getSuggestions()` for clarity.

3. **SCSS uses design tokens**: All hardcoded px values replaced with `$space-N` tokens from `_variables.scss`. The plan's `gap: 14px` was corrected to `$space-4` (16px) per code review.

4. **Template `as` alias limitation**: Angular 19 disallows `as` on `@else if` blocks. KPI section uses `store.kpis()!.field` pattern inside an `@else if (store.kpis())` guard instead.

5. **`formatTime()` added**: Converts `CalendarSlot.scheduledAt` ISO strings to 12-hour AM/PM format. The plan assumed a `time` field on CalendarSlot, but the model uses `scheduledAt`.

6. **`trackSuggestion` uses composite key**: `${topic}::${platform}` to avoid collisions across platforms (per code review fix).

7. **DatePipe removed**: Custom `formatTime()` and `relativeDate()` methods replaced all date formatting needs.

### Test Coverage

- **Store tests (4)**: Initial state, loading state, successful load with all endpoints, graceful individual endpoint failure
- **Component tests (12)**: Creation, store.load on init, navigation (content edit, new, new with suggestion, calendar), formatCost, formatTime, relativeDate, track functions (slots, items, suggestions)
- **Total**: 16 new tests, all passing. 6 pre-existing failures unchanged.
