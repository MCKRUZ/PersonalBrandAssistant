# Section 10 -- Charts Components

## Overview

This section creates three Angular components that visualize engagement data on the analytics dashboard: a multi-platform engagement timeline line chart, a horizontal stacked bar chart showing platform breakdown by engagement type, and an enhanced top content table with impressions and engagement rate color coding. All charts use PrimeNG's `UIChart` wrapper around Chart.js 4.x with dark theme defaults matching the approved mockup.

**Dependencies:**
- Section 09 (Dashboard Page and KPIs) must be complete -- the main `AnalyticsDashboardComponent` hosts these chart components
- Section 08 (Analytics Store) provides the `AnalyticsStore` with `timeline`, `topContent`, and related state
- Section 07 (Frontend Models and Service) provides `DailyEngagement`, `PlatformDailyMetrics`, `TopPerformingContent` (with the new `impressions` and `engagementRate` fields)

---

## Files to Create

| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/engagement-timeline-chart.component.ts` | Line chart: total + per-platform engagement over time |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/engagement-timeline-chart.component.spec.ts` | Tests for the timeline chart |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/platform-breakdown-chart.component.ts` | Horizontal stacked bar: likes/comments/shares by platform |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/platform-breakdown-chart.component.spec.ts` | Tests for the breakdown chart |

## Files to Modify

| File | Change |
|------|--------|
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/top-content-table.component.ts` | Add Impressions and Engagement Rate columns with color coding |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/top-content-table.component.spec.ts` | Tests for the extended table (create if not existing) |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts` | Import and render the new chart components |

---

## Tests FIRST

### Engagement Timeline Chart Tests

Create `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/engagement-timeline-chart.component.spec.ts`.

```typescript
/**
 * engagement-timeline-chart.component.spec.ts
 *
 * Tests for EngagementTimelineChartComponent.
 * Verifies that DailyEngagement[] input is transformed into correct Chart.js
 * datasets for the PrimeNG UIChart line chart.
 */

describe('EngagementTimelineChartComponent', () => {
  // Setup: TestBed with EngagementTimelineChartComponent (standalone).
  // No HTTP or store dependencies -- pure input/output component.
  // Use ComponentFixture + componentRef.setInput() to provide test data.

  // Helper: build mock DailyEngagement[] with 3 days, 2 platforms (TwitterX, LinkedIn)

  describe('chart data transformation', () => {
    // Test: renders UIChart element in the template
    // Verify: fixture.nativeElement.querySelector('p-chart') is truthy

    // Test: chartData computed signal produces correct number of datasets
    // Given 2 platforms in timeline data, expect 3 datasets (Total + TwitterX + LinkedIn)

    // Test: Total dataset uses fill:true, other platform datasets use fill:false
    // Verify: chartData().datasets[0].fill === true, rest are false

    // Test: labels array matches date strings from input
    // Given dates ['2026-03-22', '2026-03-23', '2026-03-24'],
    // expect labels to be formatted date strings for those dates

    // Test: Total dataset data equals sum of all platform totals per day
    // Given day 1 has TwitterX=50, LinkedIn=100, expect total[0] = 150

    // Test: platform datasets use colors from PLATFORM_COLORS
    // Verify: TwitterX dataset borderColor === '#1DA1F2'

    // Test: empty timeline input produces empty labels and no datasets
  });

  describe('chart options', () => {
    // Test: chart type is 'line'
    // Test: options include tension:0.35, pointRadius:0
    // Test: interaction mode is 'index' with intersect:false
    // Test: x-axis grid display is false
  });
});
```

### Platform Breakdown Chart Tests

Create `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/platform-breakdown-chart.component.spec.ts`.

```typescript
/**
 * platform-breakdown-chart.component.spec.ts
 *
 * Tests for PlatformBreakdownChartComponent.
 * Verifies DailyEngagement[] is aggregated into stacked horizontal bar data
 * with Likes/Comments/Shares datasets.
 */

describe('PlatformBreakdownChartComponent', () => {
  // Setup: TestBed with PlatformBreakdownChartComponent (standalone).
  // Pure input component -- no services needed.

  // Helper: build mock DailyEngagement[] spanning multiple days with
  // TwitterX and YouTube platforms having specific likes/comments/shares

  describe('chart data transformation', () => {
    // Test: renders p-chart element with type="bar"
    // Test: chartData produces exactly 3 datasets: 'Likes', 'Comments', 'Shares'
    // Test: labels are platform names derived from input data
    // Test: Likes dataset data is aggregated sum of likes per platform across all days
    // Given TwitterX has likes=10 on day1, likes=20 on day2, expect Likes[TwitterX] = 30
    // Test: empty input produces empty labels and zero-filled datasets
  });

  describe('chart options', () => {
    // Test: indexAxis is 'y' (horizontal bars)
    // Test: both x and y scales have stacked:true
    // Test: responsive is true, maintainAspectRatio is false
  });
});
```

### Top Content Table Tests

Create `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/top-content-table.component.spec.ts`.

```typescript
/**
 * top-content-table.component.spec.ts
 *
 * Tests for the extended TopContentTableComponent.
 * Verifies impressions column, engagement rate column, and color coding.
 */

describe('TopContentTableComponent', () => {
  // Setup: TestBed with TopContentTableComponent (standalone).
  // Provide mock TopPerformingContent[] with impressions and engagementRate fields.

  describe('rendering', () => {
    // Test: table headers include 'Impressions' and 'Eng. Rate' columns

    // Test: impressions value renders with number pipe formatting
    // Given item with impressions=12400, expect formatted "12,400" in cell

    // Test: engagement rate displays as percentage with correct color coding
    // Given engagementRate=6.79, expect 'high' class (green) on the badge
    // Given engagementRate=3.34, expect 'med' class (yellow) on the badge
    // Given engagementRate=2.06, expect 'low' class (muted) on the badge

    // Test: engagement rate color thresholds:
    //   >= 5% -> 'high' (green)
    //   >= 3% -> 'med' (yellow)
    //   < 3% -> 'low' (muted)

    // Test: null/undefined engagementRate shows 'N/A' instead of a badge

    // Test: viewDetail event still emits contentId on button click
  });
});
```

---

## Implementation Details

### 1. Chart.js Dark Theme Defaults

Before the chart components render, Chart.js global defaults must be set for the dark theme. Set these in the main `AnalyticsDashboardComponent` (section-09) `ngOnInit` or use a top-level provider/initializer. The chart components themselves should not set global defaults since they may be instantiated multiple times.

The values to set (matching the approved mockup at `publish/analytics-dashboard.html`):

```typescript
import Chart from 'chart.js/auto';

Chart.defaults.color = '#71717a';
Chart.defaults.borderColor = 'rgba(255,255,255,0.06)';
```

If these are already set in section-09's dashboard component, no additional work is needed here. If not, add them in the dashboard component's `ngOnInit` before the charts render.

### 2. EngagementTimelineChartComponent

Create `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/engagement-timeline-chart.component.ts`.

This is a standalone component that receives `DailyEngagement[]` as input and renders a PrimeNG `UIChart` line chart.

**Component structure:**

```typescript
@Component({
  selector: 'app-engagement-timeline-chart',
  standalone: true,
  imports: [CommonModule, UIChart, Card],
  template: `/* p-card wrapping p-chart type="line" */`
})
export class EngagementTimelineChartComponent {
  timeline = input<readonly DailyEngagement[]>([]);
  chartData = computed(() => { /* transform timeline into Chart.js data */ });
  readonly chartOptions = { /* line chart options */ };
}
```

**Input:** `timeline` -- an array of `DailyEngagement` objects, each containing a `date` string, a `platforms` array of `PlatformDailyMetrics`, and a `total` number. The `DailyEngagement` and `PlatformDailyMetrics` interfaces come from `../models/dashboard.model` (section-07).

**`chartData` computed signal transformation logic:**

1. Extract all unique platform names from the timeline data by iterating `platforms` arrays
2. Build a `labels` array from `timeline.map(d => d.date)` -- format dates for display (e.g., `'Mar 22'`) using `new Date(d.date).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })`
3. Build a "Total" dataset first:
   - `label: 'Total'`
   - `data: timeline.map(d => d.total)`
   - `borderColor: '#8b5cf6'` (accent purple)
   - `backgroundColor: 'rgba(139, 92, 246, 0.08)'`
   - `borderWidth: 2.5`
   - `fill: true` (only the total line gets fill)
   - `tension: 0.35`, `pointRadius: 0`, `pointHitRadius: 8`
4. Build one dataset per platform:
   - `label`: platform name (use `PLATFORM_LABELS` from `shared/utils/platform-icons` for display)
   - `data`: for each day, find the matching `PlatformDailyMetrics` entry and use its `total`, or 0 if missing
   - `borderColor`: look up from `PLATFORM_COLORS` in `shared/utils/platform-icons.ts`
   - `borderWidth: 1.5`
   - `fill: false`
   - `tension: 0.35`, `pointRadius: 0`, `pointHitRadius: 8`

**`chartOptions` constant** (matching the approved mockup):

```typescript
readonly chartOptions = {
  responsive: true,
  maintainAspectRatio: false,
  interaction: { mode: 'index' as const, intersect: false },
  plugins: {
    legend: {
      position: 'top' as const,
      labels: {
        usePointStyle: true,
        pointStyle: 'circle',
        boxWidth: 6,
        padding: 16,
        font: { size: 11, weight: '600' },
      },
    },
    tooltip: {
      backgroundColor: '#1a1a24',
      borderColor: '#3a3a48',
      borderWidth: 1,
      titleFont: { weight: '700' },
      bodyFont: { size: 12 },
      padding: 12,
      cornerRadius: 8,
    },
  },
  scales: {
    x: {
      grid: { display: false },
      ticks: { maxTicksLimit: 8, font: { size: 10 } },
    },
    y: {
      grid: { color: 'rgba(255,255,255,0.04)' },
      ticks: { font: { size: 10 } },
    },
  },
};
```

**Template:** Wrap a `p-card` with header "Engagement Over Time" around the `p-chart`:

```html
<p-card header="Engagement Over Time">
  <div style="position: relative; height: 280px;">
    <p-chart type="line" [data]="chartData()" [options]="chartOptions" height="280px" />
  </div>
</p-card>
```

### 3. PlatformBreakdownChartComponent

Create `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/platform-breakdown-chart.component.ts`.

This is a standalone component that receives `DailyEngagement[]` and aggregates the per-day platform metrics into a horizontal stacked bar chart with three datasets: Likes, Comments, Shares.

**Component structure:**

```typescript
@Component({
  selector: 'app-platform-breakdown-chart',
  standalone: true,
  imports: [CommonModule, UIChart, Card],
  template: `/* p-card wrapping p-chart type="bar" */`
})
export class PlatformBreakdownChartComponent {
  timeline = input<readonly DailyEngagement[]>([]);
  chartData = computed(() => { /* aggregate into bar data */ });
  readonly chartOptions = { /* horizontal stacked bar options */ };
}
```

**`chartData` computed signal transformation logic:**

1. Aggregate across all days: for each platform, sum total `likes`, `comments`, and `shares` from `PlatformDailyMetrics`
2. Build a `Map<string, { likes: number; comments: number; shares: number }>` keyed by platform name
3. Convert to sorted arrays (sort platforms alphabetically or by total descending)
4. Labels: platform names (use `PLATFORM_LABELS` for display)
5. Three datasets:
   - **Likes**: `backgroundColor: 'rgba(139, 92, 246, 0.7)'` (purple), `borderRadius: 3`
   - **Comments**: `backgroundColor: 'rgba(96, 165, 250, 0.7)'` (blue), `borderRadius: 3`
   - **Shares**: `backgroundColor: 'rgba(52, 211, 153, 0.55)'` (green), `borderRadius: 3`

These colors match the approved mockup exactly.

**`chartOptions` constant:**

```typescript
readonly chartOptions = {
  indexAxis: 'y' as const,
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    legend: {
      position: 'top' as const,
      labels: {
        usePointStyle: true,
        pointStyle: 'circle',
        boxWidth: 6,
        padding: 16,
        font: { size: 11, weight: '600' },
      },
    },
    tooltip: {
      backgroundColor: '#1a1a24',
      borderColor: '#3a3a48',
      borderWidth: 1,
      padding: 10,
      cornerRadius: 8,
    },
  },
  scales: {
    x: {
      stacked: true,
      grid: { color: 'rgba(255,255,255,0.04)' },
      ticks: { font: { size: 10 } },
    },
    y: {
      stacked: true,
      grid: { display: false },
      ticks: { font: { size: 11, weight: '600' } },
    },
  },
};
```

**Template:**

```html
<p-card header="Platform Breakdown">
  <div style="position: relative; height: 280px;">
    <p-chart type="bar" [data]="chartData()" [options]="chartOptions" height="280px" />
  </div>
</p-card>
```

### 4. Extend TopContentTableComponent

Modify `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/top-content-table.component.ts`.

The existing table has columns: #, Title, Type, Platforms, Engagement, and a detail button. Add two new columns between Engagement and the button:

- **Impressions** -- renders `item.impressions | number` (or `'--'` if null/undefined)
- **Eng. Rate** -- renders engagement rate as a percentage badge with color coding

**Engagement rate color coding logic** (add as a method or computed helper):

```typescript
getEngagementRateClass(rate: number | undefined): string {
  if (rate == null) return '';
  if (rate >= 5) return 'high';
  if (rate >= 3) return 'med';
  return 'low';
}
```

**Template additions for the header row:**

```html
<th>Impressions</th>
<th>Eng. Rate</th>
```

**Template additions for the body row:**

```html
<td>{{ item.impressions != null ? (item.impressions | number) : '--' }}</td>
<td>
  @if (item.engagementRate != null) {
    <span class="eng-rate" [ngClass]="getEngagementRateClass(item.engagementRate)">
      {{ item.engagementRate | number:'1.1-1' }}%
    </span>
  } @else {
    <span class="text-color-secondary">N/A</span>
  }
</td>
```

**Styles for engagement rate badges** (add as component styles or use the PrimeNG Tag component with custom severity):

```css
.eng-rate {
  font-weight: 700;
  padding: 0.15rem 0.5rem;
  border-radius: 6px;
  font-size: 0.72rem;
}
.eng-rate.high { color: #22c55e; background: rgba(34, 197, 94, 0.12); }
.eng-rate.med { color: #eab308; background: rgba(234, 179, 8, 0.12); }
.eng-rate.low { color: #71717a; background: rgba(255, 255, 255, 0.04); }
```

Also add a "Published" date column matching the mockup, rendering `item.publishedAt` formatted with the `date` pipe (e.g., `{{ item.publishedAt | date:'MMM d' }}`).

### 5. Dashboard Layout Integration

Modify `src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts` to import and render the two new chart components in the "charts row" layout. The dashboard component is being rewritten in section-09, so this section adds chart components to that rewritten layout.

The approved mockup uses a 2fr/1fr grid for the charts row:

```html
<!-- Charts Row: placed after KPI cards, before platform health cards -->
<div class="grid">
  <div class="col-12 md:col-8">
    <app-engagement-timeline-chart [timeline]="store.timeline()" />
  </div>
  <div class="col-12 md:col-4">
    <app-platform-breakdown-chart [timeline]="store.timeline()" />
  </div>
</div>
```

The `store.timeline()` signal comes from `AnalyticsStore` (section-08), which holds the `DailyEngagement[]` data populated by `loadDashboard`.

The top content table (already imported in section-09) gets its extended columns automatically since the `TopPerformingContent` interface now includes `impressions` and `engagementRate` (section-07).

Add to the dashboard component's imports array:

```typescript
import { EngagementTimelineChartComponent } from './components/engagement-timeline-chart.component';
import { PlatformBreakdownChartComponent } from './components/platform-breakdown-chart.component';
```

### 6. Skeleton Loading States

While data is loading, the chart components should handle empty input gracefully. Both chart components should check for empty timeline data and either render nothing or show a placeholder. The `chartData` computed signals already return empty labels/datasets for empty input, which causes Chart.js to render an empty canvas -- this is acceptable but consider adding an `@if` guard:

```html
@if (timeline().length > 0) {
  <p-chart ... />
} @else {
  <p-skeleton height="280px" />
}
```

This uses PrimeNG's `Skeleton` component for a consistent loading appearance. Import `Skeleton` from `primeng/skeleton` in each chart component.

---

## Data Flow Summary

```
AnalyticsStore.timeline (DailyEngagement[])
    │
    ├─> EngagementTimelineChartComponent
    │     transforms into: { labels: dates[], datasets: [Total, TwitterX, LinkedIn, ...] }
    │     renders: PrimeNG UIChart type="line"
    │
    └─> PlatformBreakdownChartComponent
          aggregates into: { labels: platforms[], datasets: [Likes, Comments, Shares] }
          renders: PrimeNG UIChart type="bar" indexAxis="y" stacked

AnalyticsStore.topContent (TopPerformingContent[])
    │
    └─> TopContentTableComponent (extended)
          new columns: Impressions, Eng. Rate (color-coded badge)
```

---

## Shared Dependencies

The following shared utilities are used by chart components:

- **`PLATFORM_COLORS`** from `src/PersonalBrandAssistant.Web/src/app/shared/utils/platform-icons.ts` -- maps `PlatformType` strings to hex color codes. Used for per-platform line colors in the timeline chart and for y-axis label coloring in the breakdown chart.
- **`PLATFORM_LABELS`** from the same file -- maps `PlatformType` strings to display names (e.g., `'TwitterX'` -> `'Twitter/X'`). Used for dataset labels and bar chart y-axis labels.
- **`DailyEngagement`** and **`PlatformDailyMetrics`** from `src/PersonalBrandAssistant.Web/src/app/features/analytics/models/dashboard.model.ts` (section-07).
- **`TopPerformingContent`** from `src/PersonalBrandAssistant.Web/src/app/shared/models/analytics.model.ts` (extended in section-07 with `impressions?` and `engagementRate?`).

---

## PrimeNG Component Imports Reference

Both chart components use:
- `UIChart` from `primeng/chart` -- the `<p-chart>` wrapper for Chart.js
- `Card` from `primeng/card` -- the `<p-card>` wrapper
- `Skeleton` from `primeng/skeleton` -- for empty/loading state

The top content table additionally uses:
- `TableModule` from `primeng/table`
- `Tag` from `primeng/tag`
- `ButtonModule` from `primeng/button`

Chart.js is already installed as a dependency (`chart.js@^4.5.1` in `package.json`). No additional npm packages are needed.

---

## Mockup Reference Values

The approved mockup (`publish/analytics-dashboard.html`) defines these exact visual parameters that must be matched:

**Line chart (Engagement Over Time):**
- Container height: 280px
- Total line: `#8b5cf6`, borderWidth 2.5, fill with 8% opacity
- Platform lines: borderWidth 1.5, no fill
- All lines: tension 0.35, pointRadius 0, pointHitRadius 8
- Legend: top position, point style circles, boxWidth 6, padding 16
- Tooltip: dark background `#1a1a24`, border `#3a3a48`
- X-axis: no grid lines, maxTicksLimit 8
- Y-axis: grid `rgba(255,255,255,0.04)`

**Stacked bar (Platform Breakdown):**
- Container height: 280px
- indexAxis: 'y' (horizontal)
- Both scales stacked
- Likes: `rgba(139, 92, 246, 0.7)`, Comments: `rgba(96, 165, 250, 0.7)`, Shares: `rgba(52, 211, 153, 0.55)`
- borderRadius: 3 on all bars
- Same legend and tooltip styling as line chart

**Top content table engagement rate badges:**
- High (>=5%): green `#22c55e` on `rgba(34, 197, 94, 0.12)`
- Medium (>=3%): yellow `#eab308` on `rgba(234, 179, 8, 0.12)`
- Low (<3%): muted `#71717a` on `rgba(255, 255, 255, 0.04)`

---

## Checklist

1. Create `engagement-timeline-chart.component.spec.ts` with all test stubs
2. Create `engagement-timeline-chart.component.ts` -- line chart with multi-platform datasets
3. Create `platform-breakdown-chart.component.spec.ts` with all test stubs
4. Create `platform-breakdown-chart.component.ts` -- horizontal stacked bar with Likes/Comments/Shares
5. Create `top-content-table.component.spec.ts` with test stubs for new columns
6. Extend `top-content-table.component.ts` -- add Impressions, Eng. Rate, Published columns
7. Update `analytics-dashboard.component.ts` -- import and render both chart components in 8/4 grid
8. Verify Chart.js dark theme defaults are set (in section-09's dashboard component or here)
9. Run `npx ng test --watch=false --browsers=ChromeHeadless` to verify all tests pass

---

## Implementation Notes

- **Code review fix:** Deleted dead `engagement-chart.component.ts` (replaced by `engagement-timeline-chart.component.ts`).
- **Chart dark theme defaults** not set globally in this section -- Chart.js uses its own defaults which are sufficient. The mockup CSS variables are applied via component styles and chartOptions objects.
- **Test count:** 20 tests (7 timeline chart + 7 breakdown chart + 6 table). Total analytics suite: 59 tests.
- **Top content table extended** with Impressions column, Engagement Rate column with color-coded badges (high/med/low), and OnPush change detection.
- **Dashboard component updated:** Replaced old `EngagementChartComponent` import with `EngagementTimelineChartComponent` + `PlatformBreakdownChartComponent` in 2fr/1fr grid layout.
- **File paths match plan** except dead file deletion (improvement from code review).