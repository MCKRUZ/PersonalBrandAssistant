# Section 07 — Review Triage & Fixes

Verified after all fixes: **605 FE tests green** (was 603; +2 new), **`ng build` clean**.

## Interviewed (real tradeoff)

### Finding 6 — DRY: duplicated period logic + styles across overview/channel
**User decision: "Extract shared period-selector + styles."**
Applied:
- `shared/period-selector.component.ts` — `<app-period-selector [period] (periodChange)>` wraps the
  p-selectButton; overview + channel consume it.
- `shared/analytics-cards.styles.ts` — `ANALYTICS_CARD_STYLES` (kpi/panel/data-table/skeleton CSS),
  included in overview + channel `styles` arrays (single edit point).
- `shared/analytics-shared.ts` — `PERIOD_OPTIONS`, `periodDisplayLabel()`, `CHART_PALETTE`.
- website-analytics kept fully verbatim (excused move) — untouched.

## Auto-fixed (clear wins, low risk)

- **F1 — chart `[data]` reallocated every CD cycle.** Replaced method-call bindings with `computed()`
  chart-data arrays: overview `channelViews()`, channel `trendCharts()` / `deepDayCharts()`. Stable
  object refs → no chart.js redraw thrash.
- **F2 — `latestBreakdown()` sampled last day.** Replaced with `sumBreakdown()` — sums each labeled
  series across the whole range; sort-order independent.
- **F3 — `geography` fetched but never rendered.** Deep panel now renders all three breakdowns
  (Traffic Sources, Geography, Demographics) via a `deepBreakdowns()` computed; empty ones hide.
- **F4 — lazy-load test bypassed the DOM.** Shell spec now finds the `[role="tab"]` YouTube element
  and `.click()`s it, exercising the real `(valueChange)` wiring (confirmed: PrimeNG renders role=tab).
- **F5 — coverage gaps.** Added: channel period-change re-fetch; delta-badge (▲) + engagement-rate
  assertions; deep breakdown rendering (Traffic Sources/SUGGESTED, Demographics/age25-34);
  `postColumns()` header-union assertion; channel error-state + retry test.
- **F7 — load error indistinguishable from empty.** Added `loadError` signal + a distinct error
  state ("Couldn't load … analytics") with a Retry button (re-issues the fetch).
- **F8 — `onTabChange` blind cast.** Guarded against unknown values via a `TAB_KEYS` allowlist before
  mutating `activeTab`/`visited`.

## Let go
Nothing outstanding — all findings actioned.

## Build note (pre-existing, not a section-07 defect)
`website-analytics.component` trips the 4 kB per-component style budget (+1.6 kB) — it's the verbatim
CSS moved out of the old AnalyticsComponent, warning only, build succeeds.
