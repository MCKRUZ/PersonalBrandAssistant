# Section 09 Code Review: Dashboard Page and KPI Cards

**Reviewer:** Claude Code (code-reviewer agent)
**Date:** 2026-03-25
**Verdict:** WARNING -- No critical issues. Two HIGH issues and several MEDIUM suggestions. Merge after fixing the HIGH items.

---

## HIGH Issues (should fix before merge)

### H1. Missing Tooltip import -- pTooltip directive will silently fail

**File:** analytics-dashboard.component.ts:37

The template uses pTooltip on the refresh button, but Tooltip from primeng/tooltip is not in the component imports array. The other component in the project that uses pTooltip (automation-dashboard.component.ts) correctly imports Tooltip. Without the import, the directive is ignored and the tooltip never appears.

**Fix:** Add Tooltip import from primeng/tooltip to the imports array, matching the pattern used in automation-dashboard.component.ts.

### H2. Cost / Engagement trend direction is semantically inverted

**File:** dashboard-kpi-cards.component.ts (computeChange + KPI_DEFS)

The computeChange function treats all positive changes as up (green) and all negative changes as down (red). But for Cost / Engagement, a decrease is good (you are paying less per engagement) and an increase is bad. Showing a cost increase in green misleads the user.

The KPI_DEFS array should support an optional invertTrend flag, and the mapping logic should flip the trend color when set. The test suite should also add a case verifying inverted trend behavior.

---

## MEDIUM Issues (should fix)

### M1. getRelativeTime is impure and called from template -- triggers on every CD cycle

**File:** analytics-dashboard.component.ts:126-133

getRelativeTime() calls Date.now() on every invocation, producing a new value each time Angular runs change detection. Because the result is bound in the template, Angular cannot optimize it away.

**Fix options (pick one):**
1. Convert to a computed signal that recalculates on a timer interval (e.g., every 30 seconds via setInterval updating a tick signal).
2. Use Angular DatePipe or compute once in a signal derived from store.lastRefreshedAt().

This is medium because the computation is trivial, but it sets a pattern others will copy.

### M2. No ChangeDetectionStrategy.OnPush on any of the three components

**Files:** All three new/rewritten component files.

The KPI cards and date range selector are pure presentation components driven entirely by signal inputs. They are ideal candidates for OnPush. The dashboard component reads only from signals too. Other feature components in the project (e.g., sidecar-chat-panel.component.ts) already use OnPush.

### M3. selectPreset accepts string instead of the narrower union type

**File:** date-range-selector.component.ts

The selectPreset and isActive methods accept string instead of typeof PRESETS[number]. The "as DashboardPeriod" cast inside selectPreset papers over a type mismatch. Type the parameter as the literal union to get compile-time safety.

### M4. Accessibility -- KPI cards lack ARIA semantics

**File:** dashboard-kpi-cards.component.ts template

The KPI cards are informational widgets but have no ARIA roles or labels. Screen readers see them as generic div elements with scattered text nodes.

Recommended additions:
- Add role="group" and aria-label="Key performance indicators" to the .kpi-grid container.
- Add role="status" to each .kpi-card div so screen readers announce them.
- The trend arrows use HTML entities (up/down triangles) which screen readers may announce as "black up-pointing triangle." Add aria-hidden="true" on the arrow character and use aria-label on the .kpi-trend span.

### M5. Accessibility -- Refresh button has no accessible label

**File:** analytics-dashboard.component.ts:32-38

The refresh button is icon-only (icon="pi pi-refresh") with no label or aria-label. The pTooltip is visual only (and broken per H1). Screen readers will announce it as an unlabeled button. Add ariaLabel="Refresh dashboard data" to the p-button.

---

## LOW Issues (consider improving)

### L1. getRelativeTime only handles minutes and hours -- no day-level output

**File:** analytics-dashboard.component.ts:126-133

If the user leaves the dashboard open overnight or returns the next day, getRelativeTime will show "24h ago" instead of "1d ago" or "yesterday." Consider adding a day-level branch for hours >= 24.

### L2. Hardcoded color values in CSS

**Files:** dashboard-kpi-cards.component.ts styles, analytics-dashboard.component.ts styles

Several colors are hardcoded rather than using PrimeNG CSS variables:
- .kpi-trend.up uses #22c55e -- consider var(--p-green-400)
- .kpi-trend.down uses #ef4444 -- consider var(--p-red-400)
- .staleness-text.stale uses #f59e0b -- consider var(--p-yellow-400)

This makes future theme changes harder. Not blocking since the mockup uses specific hex values, but worth noting for consistency.

### L3. customRange is a mutable array

**File:** date-range-selector.component.ts

Per project coding style rules, prefer immutable patterns. This is bound to PrimeNG ngModel which mutates the array, so full immutability is impractical here. Consider at minimum documenting why mutation is required (PrimeNG binding).

### L4. Empty second chart placeholder renders as invisible collapsed div

**File:** analytics-dashboard.component.ts:63

The .charts-row uses grid-template-columns: 2fr 1fr, so the empty chart-placeholder div takes up 1/3 of the row as invisible whitespace. Until section-10 populates it, consider either hiding the entire charts row or adding a skeleton/placeholder text so users do not see an asymmetric layout.

### L5. Test for custom date range uses weak assertion

**File:** date-range-selector.component.spec.ts

The ISO string assertions in the custom date range test are inside a runtime if guard. If the shape is wrong, the test passes silently because the if block is skipped. Refactor to cast directly and assert unconditionally.

---

## Test Coverage Assessment

| Component | Tests | Coverage Notes |
|-----------|-------|----------------|
| DashboardKpiCardsComponent | 6 | Good -- covers all 6 cards, up/down/neutral trends, formatting. Missing: null summary input, Cost/Engagement inverted trend. |
| DateRangeSelectorComponent | 4 | Adequate -- covers preset click, active highlight, custom range, default. Missing: incomplete custom range (only start date selected). |
| AnalyticsDashboardComponent | 6 | Good -- covers init, loading, KPI rendering, staleness, refresh, period propagation. Missing: empty-state rendering test, error state. |
| **Total** | **16** | Meets the 80% threshold for new code. |

---

## Architecture Assessment

**Signal usage:** Correct and idiomatic. The input() / computed() / output() pattern in Angular 19 is used properly. The KPI cards component is a clean pure function of its input -- no side effects, fully derived state via computed.

**Component decomposition:** Good separation. The KPI cards and date selector are reusable presentation components. The dashboard page acts as a smart component orchestrating store interaction.

**Template correctness:** Angular control flow (@if, @for, @else) is used correctly. The @for uses track expressions. The @if (store.lastRefreshedAt(); as ts) pattern correctly aliases the signal result.

**Design system alignment:** The CSS follows the mockup dark-surface card pattern with PrimeNG variable fallbacks. Grid layout matches the spec responsive approach.

---

## Summary

| Priority | Count | Items |
|----------|-------|-------|
| HIGH | 2 | H1 (missing Tooltip import), H2 (inverted cost trend) |
| MEDIUM | 5 | M1-M5 |
| LOW | 5 | L1-L5 |

Fix H1 and H2 before merge. The remaining items are improvements that can be addressed in this PR or tracked for follow-up.
