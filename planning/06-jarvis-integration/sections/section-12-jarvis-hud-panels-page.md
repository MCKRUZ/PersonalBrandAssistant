# Section 12: Jarvis HUD -- Panels and Content Page

## Overview

Adds PBA integration to the Jarvis HUD's existing dashboard panels and creates a dedicated `/content` page with four data-rich panels. This section covers three areas:

1. **Existing Panel Integration**: Extends MetricCards, BriefingPanel, and AlertFeed on the main dashboard page to include PBA-derived data.
2. **Dedicated `/content` Page**: A new route with Pipeline (Kanban), Calendar, Engagement, and Trends panels.
3. **Navigation Update**: Adds a "Content" entry to the sidebar.

All components are client components that consume data from the Zustand `pba-store` (section 11) and the BFF proxy routes (section 11). The SSE connection for real-time pipeline updates is managed by the `usePbaSse` hook from section 11.

## Dependencies

- **section-10-jarvis-monitor-pba-monitors**: PBA alerts must flow through jarvis-monitor into the standard alert system for the AlertFeed integration.
- **section-11-jarvis-hud-bff-store**: BFF proxy routes and Zustand store must be implemented. All components in this section read from `usePbaStore` and fetch from `/api/pba/*` endpoints.

## Tests (Write First)

### MetricCards Tests

Test file: `app/(dashboard)/__tests__/pba-metric-cards.test.tsx`

Use Vitest + React Testing Library.

```typescript
// Test: renders Content Queue card with queueDepth from briefingSummary
//   Mock usePbaStore with briefingSummary.queueDepth = 7
//   Render the MetricCards section
//   Assert a card with title "Content Queue" displays "7"

// Test: renders Scheduled Today card with count from briefingSummary
//   Mock usePbaStore with briefingSummary.scheduledToday having 3 items
//   Assert a card with title "Scheduled Today" displays "3"

// Test: renders Engagement card with highlights from briefingSummary
//   Mock usePbaStore with briefingSummary.engagementHighlights having engagement data
//   Assert a card with title "Engagement" displays the 7-day engagement number

// Test: renders degraded state when briefingSummary is null
//   Mock usePbaStore with briefingSummary = null
//   Assert PBA-related cards show a skeleton/loading state or "unavailable" message
```

### BriefingPanel Tests

Test file: `app/(dashboard)/__tests__/pba-briefing-panel.test.tsx`

```typescript
// Test: renders PBA section with scheduled posts count
//   Mock usePbaStore with briefingSummary.scheduledToday having 2 items
//   Assert briefing includes text like "Content: 2 posts scheduled today"

// Test: renders engagement highlights
//   Mock with engagementHighlights data
//   Assert briefing includes engagement summary text

// Test: renders trending topic count
//   Mock with trendingTopics having 4 items
//   Assert briefing includes "4 new trending topics matching your brand"

// Test: renders queue depth
//   Mock with queueDepth = 5
//   Assert briefing includes "5 items in pipeline"

// Test: omits PBA section when data unavailable
//   Mock usePbaStore with briefingSummary = null
//   Assert PBA section is not rendered in the briefing
```

### AlertFeed Tests

Test file: `app/(dashboard)/__tests__/pba-alert-feed.test.tsx`

```typescript
// Test: renders PBA alerts with source tag
//   Provide alert data with source: "pba"
//   Assert the alert row displays a "PBA" source badge/tag

// Test: PBA alerts sorted by severity alongside infra alerts
//   Provide a mix of PBA and infra alerts with different severities
//   Assert alerts are ordered by severity (critical > high > medium > info)
//   Assert PBA and infra alerts are interleaved by severity, not grouped by source
```

### Pipeline Panel Tests

Test file: `app/(dashboard)/content/__tests__/pipeline-panel.test.tsx`

```typescript
// Test: renders kanban board with items from pba-store
//   Mock usePbaStore with 5 pipeline items in various stages
//   Render PipelinePanel
//   Assert each stage column contains the correct items

// Test: updates in real-time when SSE events arrive
//   Start with 3 items
//   Simulate an SSE pipeline:stage-change event via store update
//   Assert the kanban board reflects the stage change

// Test: shows stage name, platform icon, time in stage per card
//   Mock with a pipeline item: stage "Draft", platform "LinkedIn", updatedAt 2 hours ago
//   Assert the card shows "Draft" stage label, LinkedIn icon, and "2h" time indicator
```

### Calendar Panel Tests

Test file: `app/(dashboard)/content/__tests__/calendar-panel.test.tsx`

```typescript
// Test: renders weekly view with scheduled items
//   Mock calendar fetch to return 5 items across the week
//   Assert each item appears in the correct day column/cell

// Test: platform icons display correctly
//   Mock items with different platforms (Twitter, LinkedIn, Reddit)
//   Assert each item shows the correct platform icon

// Test: date range selector updates fetched data
//   Render CalendarPanel
//   Change the date range via the selector control
//   Assert a new fetch was made with the updated date range params
```

### Engagement Panel Tests

Test file: `app/(dashboard)/content/__tests__/engagement-panel.test.tsx`

```typescript
// Test: renders platform-by-platform metric cards
//   Mock usePbaStore with engagement metrics for 3 platforms
//   Assert 3 metric cards are rendered with platform names and values

// Test: renders top-performing content list
//   Mock with engagement data including top content items
//   Assert a list of top-performing content is rendered with titles and engagement numbers

// Test: renders engagement trend chart
//   Mock with engagement trend data
//   Assert a chart element (Recharts line chart) is rendered
```

### Trends Panel Tests

Test file: `app/(dashboard)/content/__tests__/trends-panel.test.tsx`

```typescript
// Test: renders trending topic cards with relevance bars
//   Mock usePbaStore with 5 trending topics with relevance scores
//   Assert 5 topic cards are rendered, each with a relevance score bar

// Test: renders source attribution per topic
//   Mock with topics having different sources (Reddit, HackerNews, RSS)
//   Assert each card shows the source label
```

### Navigation Tests

Test file: `app/(dashboard)/__tests__/navigation.test.tsx`

```typescript
// Test: sidebar includes "Content" nav item
//   Render the sidebar navigation
//   Assert an item with text "Content" is present

// Test: clicking Content navigates to /content route
//   Render the sidebar
//   Click the "Content" nav item
//   Assert navigation occurred to "/content"

// Test: Content nav item shows active state when on /content
//   Render sidebar with current path = "/content"
//   Assert the Content nav item has the active CSS class/style
```

## File Paths

### New Files

- `app/(dashboard)/content/page.tsx` -- The dedicated `/content` page.
- `app/(dashboard)/content/components/pipeline-panel.tsx` -- Kanban pipeline panel.
- `app/(dashboard)/content/components/calendar-panel.tsx` -- Calendar view panel.
- `app/(dashboard)/content/components/engagement-panel.tsx` -- Engagement metrics panel.
- `app/(dashboard)/content/components/trends-panel.tsx` -- Trending topics panel.
- `app/(dashboard)/content/__tests__/pipeline-panel.test.tsx` -- Tests.
- `app/(dashboard)/content/__tests__/calendar-panel.test.tsx` -- Tests.
- `app/(dashboard)/content/__tests__/engagement-panel.test.tsx` -- Tests.
- `app/(dashboard)/content/__tests__/trends-panel.test.tsx` -- Tests.
- `app/(dashboard)/__tests__/pba-metric-cards.test.tsx` -- Tests.
- `app/(dashboard)/__tests__/pba-briefing-panel.test.tsx` -- Tests.
- `app/(dashboard)/__tests__/pba-alert-feed.test.tsx` -- Tests.
- `app/(dashboard)/__tests__/navigation.test.tsx` -- Tests.

### Modified Files

- `app/(dashboard)/page.tsx` -- Add PBA MetricCards and BriefingPanel PBA section.
- `app/(dashboard)/components/briefing-panel.tsx` -- Extend to include PBA data.
- `app/(dashboard)/components/alert-feed.tsx` -- Add source tag rendering for PBA alerts.
- `app/(dashboard)/components/sidebar.tsx` (or equivalent nav component) -- Add "Content" nav item.

## Existing Panel Integration

### MetricCards (Main Dashboard)

Add three PBA-derived metric cards to the existing dashboard `page.tsx`. These use the same `MetricCard` component pattern already in use for infrastructure metrics.

New cards:
- **"Content Queue"**: Shows `briefingSummary.queueDepth`. Trend indicator compares current depth to the previous reading (store the last value for delta comparison).
- **"Scheduled Today"**: Shows `briefingSummary.scheduledToday.length`. No trend -- just the count for today.
- **"Engagement"**: Shows `briefingSummary.engagementHighlights` summarized as total recent engagement. The 7-day rolling engagement number from the summary serves as the display value.

Data source: `usePbaStore().briefingSummary`, populated by fetching `/api/pba/status` on page load. When `briefingSummary` is null (PBA unavailable or not yet loaded), render the cards in a skeleton/degraded state with a muted "PBA unavailable" label.

### BriefingPanel

Extend the existing briefing template to include a PBA section. The PBA briefing section renders when `briefingSummary` is non-null and contains:

- "Content: N posts scheduled today, M drafts pending review"
- "Engagement: [top engagement highlights, e.g., 'LinkedIn post got 3x average engagement']"
- "Trends: N new trending topics matching your brand"
- "Queue: N items in pipeline, next publish at HH:MM"

The text is assembled from `briefingSummary` fields. When `briefingSummary` is null, the PBA section is omitted entirely from the briefing (not an error -- just silent absence).

### AlertFeed

PBA alerts from jarvis-monitor flow through the standard alert system and appear in the AlertFeed component. Add a `source` field to the alert display:

- PBA alerts get a "PBA" badge/tag (e.g., a small pill label).
- Infrastructure alerts get an "Infra" badge/tag.
- Sorting remains by severity, with PBA and infra alerts interleaved. No separate sections or tabs -- alerts are unified and sorted by importance.

This allows users to visually distinguish PBA alerts from infrastructure alerts at a glance, and optionally filter by source in the future.

## Dedicated /content Page

### Page Layout

The `/content` page at `app/(dashboard)/content/page.tsx` is a client component that:
1. Mounts the `usePbaSse` hook for real-time pipeline updates.
2. Fetches initial data from `/api/pba/calendar`, `/api/pba/engagement`, and `/api/pba/trends` on mount.
3. Renders four panels in a responsive grid layout:
   - Pipeline Panel (top, full width)
   - Calendar Panel (left half, bottom)
   - Engagement Panel (right half, bottom)
   - Trends Panel (below or sidebar, depending on viewport)

```typescript
// app/(dashboard)/content/page.tsx
"use client";

// 1. Mount usePbaSse() for real-time pipeline data
// 2. Fetch calendar, engagement, trends on mount
// 3. Read pipelineItems from usePbaStore
// 4. Render four panels in grid layout
```

### Pipeline Panel (Kanban)

A client component showing content flowing through pipeline stages as a Kanban board.

Stage columns (left to right): Ideation, Outline, Draft, VoiceCheck, Approval, Scheduled, Publishing.

Each card displays:
- **Title**: The content item's title (truncated to ~60 chars)
- **Platform icon**: Small icon for Twitter/LinkedIn/Reddit/Blog
- **Content type badge**: Post, Article, Thread, Comment
- **Time in stage**: Relative time since the item entered the current stage (e.g., "2h", "1d")

Data source: `usePbaStore().pipelineItems`. Updated in real-time via SSE events through the `usePbaSse` hook.

The initial load comes from the `pipeline:snapshot` SSE event sent on connection (see section 02). After the snapshot, subsequent `pipeline:stage-change`, `pipeline:created`, `pipeline:published`, and `pipeline:failed` events update the board in real-time.

### Calendar Panel

A client component showing scheduled content in a weekly or monthly grid view.

Features:
- Default view: Current week, with option to switch to monthly.
- Date range selector for navigating forward/backward.
- Each cell shows the scheduled content items for that day/slot.
- Each item displays a platform icon, content type, and scheduled time.
- Color coding by platform for quick visual scanning.

Data source: Fetched from `/api/pba/calendar` with the selected date range. Stored in `usePbaStore().calendarItems`. Refetches when the date range changes.

### Engagement Panel

A client component showing engagement metrics with charts and top-performing content.

Layout:
- **Top row**: Platform-by-platform metric cards showing total engagement per platform. Each card shows the platform name, engagement number, and a small sparkline or percentage change.
- **Middle**: A Recharts line chart showing engagement trend over the last 7-30 days. Line per platform, with a total engagement line overlay.
- **Bottom**: A sorted list of top-performing content items with title, platform, engagement count, and published date.

Data source: Fetched from `/api/pba/engagement`. Stored in `usePbaStore().engagementMetrics`.

Chart library: Recharts (already a common choice in Next.js dashboards; add as a dependency if not already present).

### Trends Panel

A client component showing trending topics with relevance scores and source attribution.

Each topic card displays:
- **Topic name**: The trending topic text.
- **Relevance score bar**: A horizontal bar (0-100% of the max score) showing relative relevance.
- **Source attribution**: Where the trend was detected (Reddit, HackerNews, RSS, etc.), shown as small labels/badges.
- **"Create content" button**: An action button that either links to PBA's Angular dashboard for content creation or (stretch goal) triggers the `pba_create_content` MCP tool via a future API endpoint.

Data source: Fetched from `/api/pba/trends`. Stored in `usePbaStore().trendingTopics`.

## Navigation Update

Add a "Content" entry to the sidebar navigation. The sidebar currently contains items like Command, Alerts, Projects, Infra, Briefings.

New entry:
- **Label**: "Content"
- **Route**: `/content`
- **Icon**: `FileText` from Lucide React (or `PenSquare` for a writing-focused icon)
- **Position**: After "Briefings" or in a logical grouping (PBA-related items could be grouped)

The nav item uses the same `NavItem` component pattern as existing entries. It shows an active state (highlighted background, bold text, or accent border) when the current path is `/content`.

## Responsive Design

- **Desktop (>= 1280px)**: Pipeline panel full width, Calendar and Engagement side by side below, Trends panel below or in a sidebar column.
- **Tablet (768-1279px)**: All panels stack vertically, full width.
- **Mobile (< 768px)**: Single column stack. Pipeline panel switches from Kanban columns to a list view. Calendar shows day view instead of week.

Use Tailwind CSS responsive classes (consistent with existing HUD patterns).

## Degraded States

When PBA is unavailable or data hasn't loaded:

- **Pipeline Panel**: Shows an empty Kanban board with stage columns but no cards, and a centered "Connecting to PBA..." or "PBA unavailable" message.
- **Calendar Panel**: Shows an empty calendar grid with a subtle "No data" indicator.
- **Engagement Panel**: Shows skeleton cards and a "Loading..." chart placeholder.
- **Trends Panel**: Shows a "No trending topics available" message.
- **MetricCards**: Show "---" or a skeleton animation instead of numbers, with a muted "PBA unavailable" sublabel.
- **BriefingPanel**: Omits the PBA section entirely when data is null.

## Implementation Notes

- All panels are client components (`"use client"` directive) because they depend on Zustand store state and the SSE hook.
- The Pipeline Panel is the most complex component. Consider breaking it into `KanbanBoard`, `KanbanColumn`, and `KanbanCard` subcomponents for maintainability.
- The "time in stage" calculation on Kanban cards uses `updatedAt` from the pipeline item and the current time. Use a `useInterval` or similar hook to update the relative time display periodically (every 30 seconds is sufficient).
- The "Create content" button on Trends Panel cards is a stretch goal. For v1, it can simply link to PBA's Angular dashboard URL with the topic pre-filled as a query parameter.
- Data fetching on the `/content` page should use `useEffect` on mount. Consider extracting a `usePbaData` hook that orchestrates the initial fetch for calendar, engagement, and trends data, then populates the store.
- The Recharts engagement trend chart needs time-series data. If the `/api/pba/engagement` endpoint only returns aggregates, the chart may need a date-bucketed breakdown. This can be added as an optional `includeTimeSeries=true` query parameter on the BFF route.
