# Section 07 — Code Review

Reviewer: deep-implement:code-reviewer. All 603 FE tests pass, `ng build` green.
No security/data-loss defects. Findings ranked by value:

## High-value

1. **Chart `[data]` built by method calls, not computed signals** — `channel-analytics.component.ts`
   `[data]="trendData(t, idx)"`, `[data]="deepSeriesData(s, idx)"`, and `overview.component.ts`
   `[data]="sparkData(c, $index)"` allocate a fresh datasets object on every change-detection cycle.
   With zone.js default CD, chart.js gets a new object reference on every tick → continuous
   re-init/redraw (perf thrash + flicker). The moved Website component correctly used `computed()`
   (`trafficData`). Fix: precompute chart-data arrays as `computed()` off data/period.

2. **`latestBreakdown()` samples last day instead of aggregating** — `channel-analytics.component.ts`.
   Collapses a labeled multi-day series to `points[last].value` for Traffic Sources / Demographics
   over a 7/30/90-day range, and assumes ascending sort. Should sum across the range.

3. **`geography` fetched + modeled but never rendered** — `YouTubeDeepAnalytics.geography` is
   deserialized and carried but the deep panel renders only daySeries/trafficSources/demographics.
   Render it (same breakdown pattern) or drop it.

## Test quality

4. **Lazy-load test bypasses the DOM wiring** — `analytics.component.spec.ts` calls
   `onTabChange('youtube')` directly rather than clicking the p-tab, so it never proves the
   `(valueChange)="onTabChange($event)"` binding is connected. Delete that binding and the test
   still passes. Fix: activate via the rendered tab element.

5. **Coverage gaps** — no test for `changePeriod()` re-fetch, delta up/down badge, engagement `rate`
   %, deep breakdown rendering, or `postColumns()` metric-union. Charts asserted only by count.

## Lower severity

6. **DRY: period selector + shared styles duplicated across overview + channel** (website excused as
   verbatim move). ~40 lines of period logic (`periodOptions`, `period`, `periodLabel`,
   `skeletonCells`, `changePeriod`, `absPct`) + `.kpi-grid/.kpi-card/.panel/.data-table/.sk` style
   blocks + palette/chart-builder copy-pasted. Past the "three lines" threshold; a shared
   `<app-period-selector>` + shared base styles warranted. → **interview**

7. **Channel load error indistinguishable from empty** — `getChannel` failure sets `data=null` and
   falls through to the generic "No analytics data available" empty state; no way to tell a fetch
   failure from a genuinely empty channel.

8. **`onTabChange` blind-casts `value as TabKey`** and stuffs it into `visited` unguarded — a stray
   p-tabs emission would pollute state.

## Correct (noted)
Connect/reconnect anchors use `[href]` with lowercase route token (correct full-page nav escaping
the SPA). Deep-panel `deepFailed` degradation wired + tested. Enum-string models match verified
backend serialization. Signals-only, standalone, lazy `/analytics` route + selector preserved.
Website regression specs correctly re-pointed at the moved component.
