# Section 11 Code Review: FeedFilterTabs + FeedBatchToolbar

## Verdict: Approve with Suggestions

No critical issues. Code follows established patterns, 87/87 tests pass.

## Findings

### [IMPORTANT] batchMarkRead() marks ALL items, not selected items
The batch toolbar's "Mark Read" button calls `store.batchMarkRead()` with no args, which marks all items read — not just the selected ones. Approve/Dismiss correctly use `store.selectedIds()`. Behavioral mismatch with user expectations in selection context.

### [SUGGESTION] getBadge returns 0 vs null semantics
`getBadge()` returns `0` for tabs without badges, relying on JS falsiness in `@if`. Returning `null` would make intent explicit.

### [SUGGESTION] SystemNotification not validated against tab values
URL param `?type=SystemNotification` would set filter but no tab would highlight. Minor UX gap.

### [SUGGESTION] Hardcoded color values across components
GitHub dark palette repeated inline in filter-tabs, batch-toolbar, stats-bar. Systemic concern, not this PR.

### [NITPICK] Test description casing
Button tests start with capitals ("Approve button calls..."). Consistent within their group, acceptable.
