# Opus Review

**Model:** claude-opus (architect subagent, opus)
**Generated:** 2026-06-02
**Verdict:** needs-rework (3 critical API-mismatch fixes before section split)

---

## Verdict
needs-rework — the design is sound but the central new primitive `ContentStore.setStatus` is
built on an API that doesn't exist (`UpdateContentRequest` has no `status`), and it's load-bearing
for the board (§3.3), drawer (§3.6), and optimistic-UX risk mitigation. Fix the status-transition
model + two phantom request fields before TDD section-splitting.

## Critical issues (must fix)
1. **`setStatus` can't call `update` — no `status` on `UpdateContentRequest`.** Status changes go
   only through dedicated state-machine endpoints (`approve`, `submitForReview`, `requestChanges`,
   `schedule`, `unschedule`, `publish`, `unpublish`, `restore`). Backend is a CONSTRAINED state
   machine (Draft→Review or Draft→Approved only; no Published→Draft, no Idea→Approved). A free-form
   "drag any card to any column" kanban does not map. Fix: redefine as a `transition(id, target)`
   dispatcher that (a) reads current status, (b) rejects illegal transitions, (c) calls the right
   endpoint, (d) for schedule needs a `scheduledAt` so that drop must open the schedule UI not fire
   blindly, (e) reload/patch on success. Board must forbid illegal drop targets via
   `cdkDropListEnterPredicate`, not present all 7 columns as universally droppable.
2. **Optimistic `updatedAt` patch breaks the concurrency token.** `autoSave` sends
   `lastUpdatedAt: content.updatedAt` as optimistic-concurrency token. Don't fabricate a client-side
   `updatedAt`; patch only `status` locally, take real `updatedAt` from server response (or reload).
   State that `ContentStore` and `ContentEditorStore` are independent and a board move invalidates an
   open editor's token (reload on focus, or document the limitation).
3. **`schedule()` has no `targetPlatforms`.** `ScheduleContentRequest = {scheduledAt}` only. §5.4
   passing `targetPlatforms` is a compile error. Existing editor calls it correctly. Drop the field;
   decide whether platform selection is meaningful in schedule mode (gray out if not).
4. **Voice-band boundary inconsistency.** Plan §2.1 says `≥80`; existing `voiceColor` uses `>80`
   (strict). Pick one (recommend `>=80` as the newer decision) and note the old is replaced.

## Should-fix
5. **CDK drop:** add `cdkDropListEnterPredicate` to reject illegal targets (else card snaps then
   rejects → jarring). `event.container.id = col.status` is fine (PascalCase = valid DOM id) — state it.
6. **TipTap contract underspecified:** (a) concrete guard — only `setContent` when incoming `value`
   differs from last serialized output AND editor not focused (prevents caret jump / update loop);
   (b) name the FALLBACK if round-trip test fails (constrained subset vs raw-markdown textarea);
   (c) drive `editable` from `canEdit()` explicitly.
7. **Two sources of truth** (`contents` paged vs `allContents`): commit to ONE model. Per the locked
   load-all decision: `loadAll()` is the only fetch, grid/table also render from `filtered()`, remove/
   quarantine the server-side `setFilter`/`setPage`/`loadContents` paged path. Decide in Section 2.
8. **`/content/new` already auto-creates a stub** and ignores query params. Either wire `ngOnInit`
   to read `topic`/`type`/`sourceIdeaId` into the `create` call, or drop the "pre-seeded" claim.
9. **`save-indicator` is inline `@if`, not a reusable component.** Rephrase §4.5 so implementer
   doesn't hunt for a component that isn't there.

## Nits
- Stage-tracker: specify exact dot index for all 7 enum values (it's testable; don't leave vague).
- §5.6 result view conflates content-level `Scheduled` status vs per-platform `PublishStatus`
  (which has no Scheduled). Spell out which drives the badge.
- `getPublishStatus` polling: define cadence + stop condition + cap (e.g. 2s until all platform
  statuses ∈ {Published,Failed}, cap ~30s).
- marked `lexer`/`walkTokens` exist since v1 — low risk, fine to verify.
- Bespoke modal a11y: budget real effort for focus-trap + add it to §5 tests (currently missing).

## Got right (do not touch)
Foundation-first sequencing; CDK pin ^19.2.0; TipTap over contenteditable; pure-logic test seams
(thread-splitter, markdown-blocks/plainText, nextStatus, deliveryBadge, voice band); deliveryBadge
from live `isConnected`; `voiceCheck(id)` exists; `markdown-editor` deletion + codemirror orphan
check; SignalR untouched.
