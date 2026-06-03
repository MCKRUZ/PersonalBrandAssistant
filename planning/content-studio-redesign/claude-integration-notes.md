# Integration Notes — Opus Review (iteration 1)

Verdict was **needs-rework** on 3 critical API mismatches. All findings verified against the real
code and **integrated** (none rejected — the review was accurate throughout). Verification:
`UpdateContentRequest` (content.model.ts:117-125) has no `status`, all fields optional + a
`lastUpdatedAt` token; `ScheduleContentRequest` (133-135) = `{scheduledAt}` only; status changes go
through dedicated endpoints `draft/approve/submitForReview/requestChanges/schedule/unschedule/
publish/unpublish/restore` (content.service.ts:64-100); `onStartDraft` calls
`draft(id,{action:'draft'})` (content-editor.component.ts:433).

## Integrated (with the change made to claude-plan.md)

1. **CRITICAL — status mutation redesigned (§2.3, §3.3, §3.6).** Replaced the fictional
   `setStatus`→`update({status})` with a **`transition(id, target)` dispatcher** over the real
   state-machine endpoints. Added the LEGAL transition map:
   - Idea→Draft `draft(id,{action:'draft'})`; Draft→Review `submitForReview`; Draft→Approved
     `approve`; Review→Approved `approve`; Review→Draft `requestChanges`; Approved→Scheduled
     `schedule(needs date)`; Approved→Published `publish`; Scheduled→Approved `unschedule`;
     Scheduled→Published `publish`; Published→Approved `unpublish`; Archived→restore.
   Board now uses `cdkDropListEnterPredicate` to permit ONLY legal target columns; illegal drops are
   rejected (card won't enter). Approved→Scheduled drop **opens the schedule dialog** (date required)
   rather than firing blindly. Drawer "Move to {nextStatus}" routes through the same `transition`.

2. **CRITICAL — concurrency token (§2.3).** Optimistic update patches ONLY `status` locally; never
   fabricates `updatedAt`. On endpoint success, reload the affected record (or read server
   `updatedAt`) before it can feed a later `update`. Documented that `ContentStore` and
   `ContentEditorStore` are independent and a board move invalidates an open editor's token (editor
   reloads on focus).

3. **CRITICAL — schedule() field (§5.4).** Dropped `targetPlatforms` from the schedule call
   (`schedule(id,{scheduledAt})`). In schedule mode the destinations selection is shown disabled with
   a note ("scheduling applies to the content; per-platform selection only affects immediate
   publish") since the backend schedule takes no platforms.

4. **CRITICAL — voice band (§2.1).** `voiceBandColor` boundary set explicitly to `>=80` high /
   `>=60` mid / else low, replacing the existing `>80`. Noted the old strict boundary is superseded.

5. **CDK enter predicate (§3.3).** Added (see #1). Noted `[id]="col.status"` is a valid DOM id
   (PascalCase enum values).

6. **TipTap contract (§4.2).** Added concrete guard: only call `setContent` when incoming `value`
   differs from last serialized output AND editor is not focused. Named the FALLBACK if the
   round-trip test fails: ship the stable StarterKit subset; if even that drifts, fall back to a
   token-styled raw-markdown textarea (research option 2). `editable` driven by `canEdit()`.

7. **Two sources of truth (§2.3, §3.1, §3.5).** Committed to load-all only: `loadAll()` is the sole
   fetch; board, grid, AND table all render from `filtered()`; the server-side
   `setFilter`/`setPage`/`loadContents` paged path is removed/quarantined. Popover filters apply
   client-side over `allContents`.

8. **/content/new seeding (§3.1, §3.8, §4.1).** `/content/new` already auto-creates a stub and
   ignores query params. Wired `ngOnInit` to read `topic`/`type`/`sourceIdeaId` query params into the
   `create()` call so empty-state idea cards genuinely pre-seed; otherwise the params were dead.

9. **save-indicator (§4.5).** Rephrased: it's inline `@if` markup (not a component); `editor-top-bar`
   reimplements the same `isSaving()/isDirty()` `@if`, no component to import.

10. **Nits integrated:** stage-tracker exact 7→6 dot mapping table (§4.4); result-view clarifies
    content-level `Scheduled` status vs per-platform `PublishStatus` (§5.6); `getPublishStatus`
    polling cadence defined (2s until all platform statuses ∈ {Published,Failed}, cap ~30s) (§5.6);
    bespoke-modal focus-trap added to §5 test list + cross-cutting a11y.

## Rejected / not integrated
None. Every finding was correct and actionable.
