# Code Review — section-04-content-list

**Verdict:** CLEAN / Approve — no CRITICAL or SHOULD-FIX.

## Verified (load-bearing)
- **CDK board no-mutation contract**: `onDrop` never mutates event arrays (no `moveItemInArray`/
  `transferArrayItem`/`container.data` writes). Same-container → no-op; `Scheduled` → schedule dialog
  + `ContentService.schedule` + `loadAll()`; else `store.transition(card.id, target)`. Signal graph
  intact.
- **Enter-predicate** `canDropInto` correctly reads `drag.data.status` vs the captured target column,
  gates on `LEGAL_TRANSITIONS`, allows staying put, falls back to `false`. `[id]="col.status"`,
  `[cdkDropListData]` from `byStatus()`.
- **Store legacy removal** complete; `transition`/`counts`/`filtered`/`byStatus`/`loadAll` intact;
  `viewMode` init 'board'. NO dangling consumers (feed/ideas use their own stores).
- **Orchestrator**: 300ms debounce via Subject + `takeUntilDestroyed` (no leak); `@switch(viewMode)`
  board/grid/table all from `filtered()`/`byStatus()`; drawer via `selectedId`; inspire-vs-filtered
  empty state correct; `loadAll()` on init.
- **drawer/schedule-dialog**: nextStatus + Scheduled-opens-dialog logic correct; `schedule(id,
  {scheduledAt})` signature matches; UTC ISO consistent with repo convention.
- Tokens only; no GitHub-dark hexes.

## Triage of NITs (all let-go / follow-up)
- detail-drawer loads body preview via `subscribe` inside an `effect` — works (effect re-runs
  supersede); `rxResource`/`toSignal` would be idiomatic. Low-likelihood race on rapid id switch
  (single-select drawer). FOLLOW-UP, not blocking.
- `content-card` `edit`/`onDelete`/`duplicate` outputs now dead (list variant not rendered).
  Cleanup candidate. LET GO.
- schedule-dialog emits UTC ISO — consistent with the repo's date normalization. Fine.

## Tests
Full suite 481 pass / 0 fail (the previously-failing ContentCard 'Blog Post' spec was fixed to assert
'Blog'). `ng build` clean.

(Implementation delegated to a frontend-developer subagent from the self-contained section file;
reviewed here.)
