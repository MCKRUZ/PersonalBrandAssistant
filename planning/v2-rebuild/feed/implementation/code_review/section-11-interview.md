# Section 11 Code Review Interview

## Interview Decision: batchMarkRead behavioral mismatch
**Severity:** Important
**Finding:** Batch toolbar's "Mark Read" called `store.batchMarkRead()` which marks ALL items read, not selected ones. Inconsistent with Approve/Dismiss that use selectedIds.
**User Decision:** Add `batchMarkReadByIds` store method
**Action:** Added full-stack IDs support:
- Backend: `BatchMarkRead.Command` now accepts optional `IReadOnlyList<Guid>? Ids`
- DTO: `BatchReadRequest` gains `Ids` property
- Endpoint: passes `body.Ids` to command
- Frontend Service: new `batchMarkReadByIds(ids, isRead)` method
- Store: new `batchMarkReadByIds(ids, isRead)` with optimistic item update + clear selection
- Toolbar: `markRead()` now calls `batchMarkReadByIds(selectedIds())`
- Test updated to verify selectedIds are passed

## Auto-fix: getBadge return type
**Severity:** Suggestion
**Finding:** `getBadge` returned `0` for no-badge tabs, relying on JS falsiness in `@if`. Unclear intent.
**Action:** Changed return type to `number | null`, returns `null` explicitly when no badge should show.

## Let Go
- SystemNotification URL param gap (acceptable edge case)
- Hardcoded color values (systemic, not this PR)
- Test description casing (nitpick, consistent within group)
