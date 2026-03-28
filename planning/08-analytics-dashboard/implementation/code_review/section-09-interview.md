# Section 09 Code Review Interview

**Date:** 2026-03-25
**Verdict:** APPROVE after fixes (2 HIGH + 2 MEDIUM)

## Triage

### Auto-fix (4 items)
1. **H1: Missing Tooltip import** - Add `Tooltip` from `primeng/tooltip` to dashboard component imports
2. **H2: Cost/Engagement inverted trend** - Add `invertTrend` flag to KPI definition so cost decrease shows as green
3. **M2: Add OnPush** - All 3 components are signal-driven, should use `ChangeDetectionStrategy.OnPush`
4. **M5: Refresh button ariaLabel** - Add `ariaLabel="Refresh dashboard data"` to the refresh button

### Let go
- M1: getRelativeTime impure - acceptable overhead for a timestamp display
- M3: string type for selectPreset - runtime constrained by preset array
- M4: ARIA for KPI cards - improvement for later accessibility pass
- All LOW items
