# Section 09: Dashboard Page and KPI Cards

## Overview

This section rewrites the main `AnalyticsDashboardComponent` to serve as the new analytics dashboard page and introduces two new child components: `DashboardKpiCardsComponent` (6 KPI metric cards with trend indicators) and `DateRangeSelectorComponent` (preset period buttons plus a custom date picker). Together they form the top portion of the dashboard -- the page shell, period controls, KPI row, and staleness/refresh UX.

**Approved mockup reference:** `publish/analytics-dashboard.html` -- dark theme, purple/violet accent, KPI grid with up/down trends, period badge in header.

## Dependencies

- **section-08-analytics-store** -- The rewritten `AnalyticsStore` with `loadDashboard`, `refreshDashboard`, `setPeriod`, `summary()`, `loading()`, `lastRefreshedAt()`, `isStale()`, and `period()` signals must exist before this section works.
- **section-07-frontend-models-service** -- The `DashboardSummary`, `DashboardPeriod` types and extended `AnalyticsService` must exist in `features/analytics/models/dashboard.model.ts`.

## Files to Create or Modify

| File | Action |
|------|--------|
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts` | **Rewrite** |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/dashboard-kpi-cards.component.ts` | **Create** |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/date-range-selector.component.ts` | **Create** |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/dashboard-kpi-cards.component.spec.ts` | **Create** |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/date-range-selector.component.spec.ts` | **Create** |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.spec.ts` | **Create** |

## Tests FIRST

All tests use Jasmine/Karma following existing project conventions. Tests use Angular `TestBed` with standalone component imports.

### `dashboard-kpi-cards.component.spec.ts`

Test file location: `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/dashboard-kpi-cards.component.spec.ts`

Test cases:

1. **Renders all 6 KPI cards with correct values** -- Provide a `DashboardSummary` input with known values. Assert the DOM contains 6 `.kpi-card` elements. Verify each card's label text matches one of: "Total Engagement", "Total Impressions", "Engagement Rate", "Content Published", "Cost / Engagement", "Website Users". Verify each card's value element contains the formatted number.

2. **Shows up trend indicator when % change is positive** -- Provide summary where `totalEngagement > previousEngagement`. Assert the trend element for Total Engagement has the CSS class `up` and displays a positive percentage string like "+18.3%".

3. **Shows down trend indicator when % change is negative** -- Provide summary where `totalEngagement < previousEngagement`. Assert the trend element has CSS class `down` and displays a negative percentage.

4. **Shows "N/A" when change is null (previous period value was 0)** -- Provide summary where `previousEngagement === 0`. The component should detect the zero denominator and render "N/A" instead of a percentage. This aligns with the cross-cutting convention: `null` % change when previous period value is 0.

5. **Formats large numbers with abbreviations** -- Provide `totalImpressions: 284000`. Assert the displayed value is "284K" or "284,000" (whichever formatting approach is chosen -- the mockup uses "284K" for impressions).

6. **Formats engagement rate as percentage** -- Provide `engagementRate: 4.52`. Assert the displayed value is "4.52%".

Test structure (stub):

```typescript
describe('DashboardKpiCardsComponent', () => {
  /** Set up TestBed with DashboardKpiCardsComponent as standalone import.
   *  Create a host component or use ComponentFixture with setInput(). */

  it('should render all 6 KPI cards with correct values', () => { /* ... */ });
  it('should show up trend indicator for positive change', () => { /* ... */ });
  it('should show down trend indicator for negative change', () => { /* ... */ });
  it('should show N/A when previous period value is 0', () => { /* ... */ });
  it('should format large numbers with abbreviations', () => { /* ... */ });
  it('should format engagement rate as percentage', () => { /* ... */ });
});
```

### `date-range-selector.component.spec.ts`

Test file location: `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/date-range-selector.component.spec.ts`

Test cases:

1. **Emits `periodChanged` on preset button click** -- Click the "7D" button. Assert the `periodChanged` output emits `'7d'`. The default active preset should be `'30d'`.

2. **Highlights active preset with filled style** -- Set period to `'14d'`. Assert the 14D button has PrimeNG's filled/primary styling and all others do not.

3. **Emits custom date range from calendar** -- Simulate selecting a date range via the PrimeNG Calendar (DatePicker in range mode). Assert `periodChanged` emits an object `{ from: string, to: string }`.

4. **Defaults to 30D preset on initialization** -- On component init, the 30D button should be active.

Test structure (stub):

```typescript
describe('DateRangeSelectorComponent', () => {
  /** Set up TestBed with DateRangeSelectorComponent, FormsModule, PrimeNG imports. */

  it('should emit periodChanged on preset button click', () => { /* ... */ });
  it('should highlight active preset with filled style', () => { /* ... */ });
  it('should emit custom date range from calendar', () => { /* ... */ });
  it('should default to 30D preset on initialization', () => { /* ... */ });
});
```

### `analytics-dashboard.component.spec.ts`

Test file location: `src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.spec.ts`

Test cases:

1. **Calls `store.loadDashboard` on init** -- Provide a mock AnalyticsStore. Assert `loadDashboard` was called once during `ngOnInit`.

2. **Shows loading skeleton when `store.loading()` is true** -- Set store loading signal to true. Assert `p-skeleton` or loading spinner elements are present.

3. **Renders KPI cards section when summary data is available** -- Set store `summary()` to a valid `DashboardSummary`. Assert `app-dashboard-kpi-cards` is rendered.

4. **Shows staleness indicator when data is stale** -- Set `store.isStale()` to return true. Assert an "Updated X ago" or stale badge is visible.

5. **Triggers `store.refreshDashboard` on refresh button click** -- Click the refresh button. Assert `store.refreshDashboard` was called.

6. **Propagates period changes from DateRangeSelector to store** -- Simulate `periodChanged` event from `app-date-range-selector`. Assert `store.setPeriod` is called with the emitted value.

Test structure (stub):

```typescript
describe('AnalyticsDashboardComponent', () => {
  /** Set up TestBed with AnalyticsDashboardComponent. Provide a mock AnalyticsStore
   *  using overrideProvider or a signal-based mock. */

  it('should call store.loadDashboard on init', () => { /* ... */ });
  it('should show loading state when store.loading is true', () => { /* ... */ });
  it('should render KPI cards when summary is available', () => { /* ... */ });
  it('should show staleness indicator when data is stale', () => { /* ... */ });
  it('should trigger refreshDashboard on refresh button click', () => { /* ... */ });
  it('should propagate period changes to store', () => { /* ... */ });
});
```

## Implementation Details

### 1. `DateRangeSelectorComponent` (New)

**File:** `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/date-range-selector.component.ts`

A standalone component that provides period selection for the dashboard.

**Inputs and outputs:**
- `activePeriod` -- input signal of type `DashboardPeriod`, bound from the store's current period
- `periodChanged` -- output event emitting `DashboardPeriod`

**Template structure:**
- A `div` with `class="flex align-items-center gap-2"` containing:
  - 5 PrimeNG `p-button` elements, one for each preset: 1D, 7D, 14D, 30D, 90D
  - Each button uses `[outlined]="true"` when not active and `[outlined]="false"` (filled) when it matches the active period
  - A PrimeNG `p-datepicker` in `selectionMode="range"` for custom date selection
  - When a custom range is selected and confirmed, emit `{ from: isoString, to: isoString }` via `periodChanged`

**Behavior:**
- On preset click: emit the period string (e.g., `'7d'`) through `periodChanged`
- On custom calendar selection: emit `{ from, to }` object through `periodChanged`
- The `DashboardPeriod` type (from section-07) is `'1d' | '7d' | '14d' | '30d' | '90d' | { from: string; to: string }`
- Default active preset: `'30d'`

**PrimeNG imports:** `ButtonModule`, `DatePicker` (from `primeng/datepicker`), `FormsModule`

### 2. `DashboardKpiCardsComponent` (New)

**File:** `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/dashboard-kpi-cards.component.ts`

A standalone component that renders a row of 6 KPI cards based on the `DashboardSummary`.

**Input:**
- `summary` -- input signal of type `DashboardSummary | null`

**Template structure:**
- A `div.grid` container (or CSS grid matching the mockup's `kpi-grid` with `repeat(5, 1fr)` for first 5 + 1 for the 6th, or `repeat(6, 1fr)` if fitting 6 cards). The mockup shows 5 cards in a row; the plan adds a 6th "Website Users" card. Use a responsive grid: `grid-template-columns: repeat(auto-fit, minmax(180px, 1fr))`.
- 6 KPI card elements, each containing:
  - `.kpi-label` -- uppercase label text
  - `.kpi-value` -- large formatted number
  - `.kpi-trend` -- percentage change badge with up/down class

**The 6 KPIs (mapping from `DashboardSummary` fields):**

| # | Label | Value Field | Previous Field | Format |
|---|-------|-------------|----------------|--------|
| 1 | Total Engagement | `totalEngagement` | `previousEngagement` | Number with commas (e.g., "12,847") |
| 2 | Total Impressions | `totalImpressions` | `previousImpressions` | Abbreviated (e.g., "284K") |
| 3 | Engagement Rate | `engagementRate` | `previousEngagementRate` | Percentage (e.g., "4.52%") |
| 4 | Content Published | `contentPublished` | `previousContentPublished` | Plain number |
| 5 | Cost / Engagement | `costPerEngagement` | `previousCostPerEngagement` | Currency (e.g., "$0.03") |
| 6 | Website Users | `websiteUsers` | `previousWebsiteUsers` | Number with commas |

**Trend calculation logic (computed signal per card):**
- `percentChange = ((current - previous) / previous) * 100`
- If `previous === 0`, display "N/A" instead of a percentage
- If `percentChange > 0`, apply CSS class `up` (green background)
- If `percentChange < 0`, apply CSS class `down` (red background)
- If `percentChange === 0`, show "0%" with neutral styling

**Number formatting helper:**
- Create a private method or pipe: `formatKpiValue(value: number, format: 'number' | 'abbreviated' | 'percent' | 'currency'): string`
  - `number`: `value.toLocaleString()` -- "12,847"
  - `abbreviated`: For values >= 1000, divide by 1000 and append "K". For >= 1M, append "M". -- "284K"
  - `percent`: `value.toFixed(2) + '%'` -- "4.52%"
  - `currency`: `'$' + value.toFixed(2)` -- "$0.03"

**Styling:** Inline styles or component styles matching the mockup's `.kpi-card` pattern -- dark surface background, subtle border, hover lift effect. Use existing PrimeNG theme variables where possible (`--p-surface-800`, etc.) but the mockup's custom CSS variables can be applied as component styles.

**Component structure (stub):**

```typescript
@Component({
  selector: 'app-dashboard-kpi-cards',
  standalone: true,
  imports: [CommonModule],
  template: `...`,
  styles: `...`
})
export class DashboardKpiCardsComponent {
  summary = input<DashboardSummary | null>(null);

  /** Computed array of KPI card configs derived from summary. */
  kpiCards = computed(() => {
    // Return array of { label, value, formattedValue, percentChange, trend } objects
  });
}
```

The `kpiCards` computed signal should return an array of card descriptor objects so the template can iterate with `@for`.

### 3. `AnalyticsDashboardComponent` (Rewrite)

**File:** `src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.ts`

This is a full rewrite of the existing component. The current component only renders a date picker, engagement chart, and top content table. The rewrite turns it into the full dashboard page shell.

**Existing component to replace (current imports to preserve or update):**
- `PageHeaderComponent` -- keep, used for the page title
- `LoadingSpinnerComponent` -- replace with skeleton approach (PrimeNG `p-skeleton`)
- `EmptyStateComponent` -- keep for zero-data state
- `DateRangePickerComponent` -- **replace** with new `DateRangeSelectorComponent`
- `EngagementChartComponent` -- keep (rendered in this section for now, section-10 will enhance)
- `TopContentTableComponent` -- keep (section-10 will enhance)

**New imports to add:**
- `DashboardKpiCardsComponent`
- `DateRangeSelectorComponent`
- `Skeleton` from `primeng/skeleton`
- `ButtonModule` from `primeng/button`

**Template layout (matching mockup structure):**

```
Header row:
  [PageHeader "Brand Analytics"]  [DateRangeSelector]  [Refresh Button]  [Staleness indicator]

@if (store.loading()) {
  Skeleton placeholders for KPI row + chart area
} @else if (no data at all) {
  EmptyState
} @else {
  KPI Cards row (DashboardKpiCardsComponent with store.summary())
  
  Charts row (placeholder divs for section-10: EngagementTimelineChart + PlatformBreakdown)
  
  Platform Health row (placeholder for section-11)
  
  Bottom row: TopContentTable + Automation stats (placeholder for section-11)
}
```

For this section, the focus is on rendering the KPI cards and date range selector. Chart components and platform sections will be added in sections 10 and 11. Use placeholder `<div>` elements or `@if` blocks that render nothing for those areas so sections 10 and 11 can slot them in.

**Key behaviors:**

- `ngOnInit()` -- call `store.loadDashboard(store.period())` to fetch all dashboard data
- `onPeriodChanged(period: DashboardPeriod)` -- call `store.setPeriod(period)` which triggers a reload
- `onRefresh()` -- call `store.refreshDashboard()`
- **Staleness indicator:** Display a "Last updated X min ago" text derived from `store.lastRefreshedAt()`. If `store.isStale()` is true, add a visual indicator (yellow/orange color, "Data may be stale" text). Use `DatePipe` or manual relative-time formatting.

**Refresh button:** A PrimeNG `p-button` with `icon="pi pi-refresh"`, `[text]="true"`, `[loading]="store.loading()"`. Placed in the header actions area.

**Skeleton loading state:** Instead of a single spinner, show:
- 6 skeleton rectangles in the KPI grid area (matching KPI card dimensions)
- A larger skeleton rectangle where the chart would be
- This provides a better loading UX matching modern dashboard patterns

**Component structure (stub):**

```typescript
@Component({
  selector: 'app-analytics-dashboard',
  standalone: true,
  imports: [
    CommonModule, PageHeaderComponent, EmptyStateComponent,
    DashboardKpiCardsComponent, DateRangeSelectorComponent,
    ButtonModule, Skeleton
  ],
  template: `...`,
  styles: `...`
})
export class AnalyticsDashboardComponent implements OnInit {
  readonly store = inject(AnalyticsStore);

  ngOnInit(): void { /* load dashboard */ }
  onPeriodChanged(period: DashboardPeriod): void { /* setPeriod */ }
  onRefresh(): void { /* refreshDashboard */ }
}
```

**Page-level styles:** The component should define the overall grid layout for the dashboard sections using CSS grid or PrimeNG's flex grid utilities. Key layout rules from the mockup:
- KPI grid: responsive grid, min 180px per card
- Charts row: 2fr / 1fr split (will be populated by section-10)
- Platform grid: 5 equal columns (will be populated by section-11)
- Bottom row: 3fr / 1fr split (will be populated by section-11)
- Gap: 1rem between all sections
- Responsive breakpoints: collapse to fewer columns on smaller screens

### 4. Route Configuration

**File:** `src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.routes.ts`

No changes needed -- the existing route `{ path: '', component: AnalyticsDashboardComponent }` already points to the component being rewritten. The `:contentId` detail route also stays.

## Cross-Cutting Conventions to Respect

- **Null semantics:** When `previousEngagement` (or any previous period value) is 0, the % change is `null` and the KPI card shows "N/A". Do not compute Infinity or NaN.
- **Immutability:** All component inputs are `input()` signals. State is read from the store via signals. No mutations.
- **Standalone components:** All new components use `standalone: true` with explicit imports.
- **No console.log:** Use Angular patterns for error feedback (store error state, template conditionals).
- **File size:** Keep each component file under 200 lines. The KPI cards component may approach this with the template and styling -- if needed, extract styles to a separate `.scss` file.

## Implementation Checklist

1. Create `date-range-selector.component.spec.ts` with 4 test cases
2. Create `date-range-selector.component.ts` with preset buttons + calendar
3. Create `dashboard-kpi-cards.component.spec.ts` with 6 test cases
4. Create `dashboard-kpi-cards.component.ts` with 6 KPI cards and trend logic
5. Create `analytics-dashboard.component.spec.ts` with 6 test cases
6. Rewrite `analytics-dashboard.component.ts` as full dashboard page shell
7. Verify all tests pass with `npx ng test --watch=false --browsers=ChromeHeadless`
8. Verify the dashboard renders correctly in the browser with the existing store providing data

---

## Implementation Notes

- **Code review fixes:** Added Tooltip import (was missing despite pTooltip usage), added `invertTrend` flag for Cost/Engagement KPI (decrease is good = green), added `ChangeDetectionStrategy.OnPush` to all 3 components, added `ariaLabel` to refresh button.
- **Test count:** 16 tests (6 KPI cards + 4 date selector + 6 dashboard page). All passing.
- **KPI card design:** Uses computed signal returning card descriptor array iterated with `@for`. Formatting helpers handle number, abbreviated (K/M), percent, and currency formats.
- **Skeleton loading:** 6 skeleton rectangles for KPI row + 1 large skeleton for chart area, matching modern dashboard loading patterns.
- **File paths match plan.** Old `DateRangePickerComponent` remains but is no longer imported by the dashboard (replaced by `DateRangeSelectorComponent`).