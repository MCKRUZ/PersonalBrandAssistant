# Section 10 — Charts Components: Code Review

**Verdict: APPROVE with minor findings**

No CRITICAL or HIGH issues. Three MEDIUM items and a few low-priority suggestions.

---

## Warnings (MEDIUM — should fix)

### 1. Dead file: `engagement-chart.component.ts` not removed
File: `src/.../analytics/components/engagement-chart.component.ts`

The diff removes the import from the dashboard but leaves the 67-line file on disk. No other file imports it. Delete it to avoid confusion.

### 2. `as Record<string, string>` type casts bypass type safety
Files: `engagement-timeline-chart.component.ts:50,53` and `platform-breakdown-chart.component.ts:23`

Both components cast the strongly-typed `PLATFORM_COLORS` and `PLATFORM_LABELS` maps with `as Record<string, string>` to allow arbitrary string keys from the API data. This suppresses the compiler warning that `p.platform` (a raw `string` from the API model `PlatformDailyMetrics.platform`) might not be a valid `PlatformType`.

The underlying issue is that `PlatformDailyMetrics.platform` is typed as `string` in `dashboard.model.ts:22`, not `PlatformType`. If the backend sends a platform name the frontend doesn't know about, the cast silently falls through to the `?? '#999'` / `?? name` defaults — which is acceptable behavior but hides the type mismatch.

**Fix:** Type `PlatformDailyMetrics.platform` as `PlatformType` if the backend contract guarantees it, or create a typed lookup helper that returns the fallback explicitly:

```typescript
// shared/utils/platform-icons.ts
export function platformColor(key: string): string {
  return (PLATFORM_COLORS as Record<string, string>)[key] ?? '#999';
}
export function platformLabel(key: string): string {
  return (PLATFORM_LABELS as Record<string, string>)[key] ?? key;
}
```

This centralizes the cast and fallback in one place instead of repeating it across components.

### 3. `mutableItems` copy on every signal evaluation
File: `top-content-table.component.ts:line ~40`

`mutableItems = computed(() => [...this.items()])` creates a shallow copy on every read to satisfy PrimeNG Table's mutability requirement. This is correct, but it re-allocates on every change detection that reads it. Since the input is `readonly`, this only fires when the parent provides new data, so it's fine in practice — just noting the pattern for awareness. No action needed unless the table grows very large.

---

## Suggestions (LOW — consider improving)

### 4. Date parsing: `new Date(d.date)` is locale-sensitive
File: `engagement-timeline-chart.component.ts:27`

`new Date('2026-03-22')` is parsed as UTC midnight but `toLocaleDateString` renders in local time. For date-only strings this shifts the displayed day for users in negative-UTC timezones. Consider appending `T00:00:00` or using a date-fns/luxon helper for deterministic formatting:

```typescript
// Safe approach — parse as local time
new Date(d.date + 'T00:00:00').toLocaleDateString(...)
```

### 5. Test: `viewDetail` emit test bypasses the actual DOM event flow
File: `top-content-table.component.spec.ts:57-67`

The test dispatches a DOM event on the PrimeNG button then immediately calls `component.viewDetail.emit('1')` directly, making the DOM dispatch irrelevant. This tests the output emitter works (trivially true) but not that the template wiring is correct. Consider using `fixture.debugElement.query(By.css('p-button')).triggerEventHandler('onClick', {})` or testing via the component method that the template calls.

### 6. Hardcoded color values in breakdown chart
File: `platform-breakdown-chart.component.ts:28-30`

The Likes/Comments/Shares dataset colors (`rgba(139, 92, 246, 0.7)`, etc.) are hardcoded. These happen to match the project's purple/blue/green palette, but consider extracting them to a shared constant (e.g., `METRIC_COLORS`) alongside `PLATFORM_COLORS` for consistency.

### 7. `chartOptions` objects could be `Object.freeze()`-d
Files: both chart components

The `chartOptions` objects are `readonly` class fields but their nested properties are mutable. Chart.js sometimes mutates options internally. Freezing prevents accidental mutation:

```typescript
readonly chartOptions = Object.freeze({ ... }) as const;
```

This is defensive — Chart.js 4.x typically clones options, so low priority.

---

## What looks good

- **OnPush + signal inputs + computed signals** — correct reactive pattern, no unnecessary subscriptions.
- **Empty-state handling** — both charts show a skeleton when the timeline is empty, matching the dashboard's loading UX.
- **Aggregation logic** — the platform breakdown correctly accumulates across days and sorts by total engagement descending.
- **Test coverage** — 20 new tests covering data transformation, edge cases (empty input), chart config assertions, and DOM rendering. Adequate for the component complexity.
- **Stacked bar config** — `indexAxis: 'y'` + `stacked: true` on both axes is correct for horizontal stacked bars.
- **Grid layout** — `2fr 1fr` split gives the timeline chart more space, appropriate for a time-series vs. a summary breakdown.
- **Immutability** — all inputs use `readonly` arrays, computed signals produce new objects. No mutation patterns.
- **File sizes** — all components under 100 lines. Clean separation of concerns.

---

## Summary

| Priority | Count | Action |
|----------|-------|--------|
| CRITICAL | 0 | — |
| HIGH | 0 | — |
| MEDIUM | 3 | Fix #1 (dead file), consider #2-3 |
| LOW | 4 | At discretion |

**Approved to merge.** Recommend deleting the dead `engagement-chart.component.ts` file and centralizing the platform lookup casts before or alongside the merge.
