# Implementation Plan — Content Studio Redesign

## Reader orientation

This plan redesigns the **Content Studio** feature of an Angular web app
(`PersonalBrandAssistant.Web`). Content Studio is where a solo creator drafts, reviews, and
publishes posts (blog, LinkedIn, Twitter/X threads, Medium, Substack) with AI assistance.

The app today renders a generic GitHub-dark theme with a spreadsheet-style content list and a
split raw-markdown editor. A design handoff (`PBAv2/design_handoff_content_studio/`) specifies a
"terracotta-on-obsidian" writing studio: a kanban pipeline board, a clean prose editor with an AI
sidecar and a voice-quality meter, and a multi-format publish overlay with faithful per-platform
previews. This plan recreates that design in Angular without changing the backend.

**Tech baseline:** Angular 19 standalone components, `@ngrx/signals` (`signalStore`) for state,
PrimeNG 20 for some overlays, SCSS partials under `src/styles/`. State and HTTP go through four
existing singletons: `ContentStore`, `ContentEditorStore`, `ContentService`, `SignalRService`.

**Hard constraints (non-negotiable):**
- **No backend / API / database changes.** All needed endpoints already exist.
- **`SignalRService` transport must not be modified** (it streams AI tokens for the sidecar).
- **Markdown stays the source of truth** for post bodies (`ContentDetail.body: string`). The editor
  edits clean prose but reads/writes markdown. subtitle/byline shown in the design are **derived
  for display only — never persisted** (no new model fields).
- **One feature branch** `feature/content-studio-redesign` off `v2-rebuild`; foundation-first.

**Pixel/value fidelity:** the prototype's `<style>` block in
`PBAv2/design_handoff_content_studio/prototype/Content Studio.html` is the CSS source of truth.
Exact values are catalogued in `claude-research.md` §A — implementers should pull from there rather
than re-reading the prototype each time.

---

## Architecture overview

```
src/PersonalBrandAssistant.Web/src/
  styles/
    _tokens.scss            # NEW — :root CSS custom properties (the design system)
    _variables.scss         # MODIFY — add missing $vars, fix values, width 200->212
    _status-badges.scss     # MODIFY — status-idea, real archived, delivery badges
    _layout.scss            # MODIFY — app grid 212px 1fr
    (others unchanged)
  styles.scss               # MODIFY — @use partials, drop GitHub-dark body/scrollbar
  app/
    shell/sidebar/          # MODIFY — restyle to tokens + footer user block
    features/content/
      models/
        content.model.ts        # MODIFY — none required for body; see note
        platform-metadata.ts    # NEW — PLATFORM_META (delivery/charLimit/fmt/code/label)
      stores/
        content.store.ts        # MODIFY — viewMode union, loadAll, counts, activeStatus,
                                #          client search, transition() status dispatcher
        content-editor.store.ts # MODIFY (minimal) — body still the field; no new persisted fields
      services/
        content.service.ts      # UNCHANGED (already has every method needed)
        signalr.service.ts      # UNCHANGED — do not touch
      shared/                    # NEW — cross-flow presentational atoms + utils
        status-tag.component.ts
        voice-score-ring.component.ts
        platform-dot.component.ts
      content-list/
        content-list.component.ts        # MODIFY — orchestrates new views
        content-display.utils.ts         # MODIFY — status/type/voice/time maps, next-status
        pipeline-bar/                     # NEW
        content-board/                    # NEW — CDK kanban
        detail-drawer/                    # NEW — p-drawer
        filters-popover/                  # NEW — p-popover (relocated platform/type/date)
        studio-empty-state/               # NEW — inspire + filtered variants
        content-list-table/               # MODIFY — restyle, drop Actions, row->drawer
        content-card/                     # MODIFY — board card (ring, tags, dots, time)
        content-filter-sidebar/           # REMOVE (replaced by pipeline-bar + filters-popover)
        content-grid/, view-toggle/       # MODIFY (toggle: board/table)
      content-editor/
        content-editor.component.ts       # MODIFY — new layout; remove p-splitter
        editor-top-bar/                   # NEW — back, stage tracker, meta, saved, ring, toggle
        stage-tracker/                    # NEW
        manuscript-surface/               # NEW — TipTap prose surface
        prose-editor/                     # NEW — TipTap<->signals wrapper
        voice-meter/                      # NEW — side-panel meter
        sidecar-chat/                     # MODIFY — inline, restyle, 3-dot thinking
        platform-targets/                 # MODIFY — restyle (logic kept)
        markdown-editor/                  # DELETE (retired)
        publish-modal/
          publish-modal.component.ts      # MODIFY — full bespoke rewrite
          platform-targets... (see above)
          delivery-badge.component.ts     # NEW
          publish-result.component.ts     # NEW
          markdown-blocks.ts              # NEW — toBlocks + plainText (marked)
          thread-splitter.ts              # NEW — splitThread(text, 280)
          previews/                       # NEW — 5 renderers
            blog-preview.component.ts
            medium-preview.component.ts
            substack-preview.component.ts
            linkedin-preview.component.ts
            twitter-preview.component.ts
```

The work is sequenced foundation → shared atoms → list → editor → publish, because every
redesigned component depends on the token foundation (today `var(--…)` resolves to nothing).

---

## Section 1 — Styling foundation (blocks everything)

**Goal:** make the intended design system real and referenceable, and remove GitHub-dark.

**Why first:** `angular.json` builds `src/styles.scss`, which is GitHub-dark and imports none of
the `styles/` partials. No `--brand-*`/`--surface-*`/`--accent-soft` CSS custom properties exist
anywhere, so any component authored against `var(--surface-card)` renders unstyled. Nothing else
can be built faithfully until this exists.

**Work:**
1. **Add `@angular/cdk@^19.2.0`** to `package.json` (matches Angular 19 major). Run install.
2. **Add TipTap deps** (`@tiptap/core`, `@tiptap/pm`, `@tiptap/starter-kit`, a markdown
   serialization extension such as `tiptap-markdown`). Confirm they build under the app's ESM
   toolchain. (Used in Section 4; added here so the dependency change is a single foundation step.)
3. **Verify `marked` availability:** confirm the `marked` resolved via `ngx-markdown@21.3.0`
   exports `lexer` and `walkTokens`. If not, add an explicit compatible `marked` dep.
4. **`_tokens.scss` (new):** a `:root { … }` block defining the full custom-property set — brand
   (`--brand-primary #c87156`, `-hover #d4836a`, `-active #b5624a`), surfaces (`--surface-base
   #0e0e10`, `card #141418`, `elevated #1a1a20`, `hover #22222a`, `border #2c2c36`, `disabled
   #3a3a46`, `sidebar #0b0b0d`, `inset #0c0c0e`, `publish-canvas #08080a`), text (`primary #f0f0f5`,
   `secondary #8a8a96`, `muted #5a5a66`), `--accent-soft rgba(200,113,86,.13)`, status (`idea
   #8a7df0`, `draft #8a8a96`, `review #c87156`, `approved #4ade80`, `scheduled #60a5fa`, `published
   #34d399`, `archived #5a5a66`), voice bands (`--voice-high #4ade80`, `-mid #fbbf24`, `-low
   #f87171`), delivery (`--delivery-auto-bg #1f3a2b`/`-auto-fg #4ade80`, `-manual-bg #3a2f1c`/
   `-manual-fg #fbbf24`, `-warn-bg #3a2420`/`-warn-fg #f0935f`), radius (`--r 12px`, `--r-inner
   10px`, `--r-control 8px`, `--r-pill 99px`, `--r-modal 16px`), fonts (`--font-body`,
   `--font-display`, `--font-mono`). Source values from `claude-research.md` §A/§"SCSS $variables".
5. **`_variables.scss` (modify):** add the missing `$status-idea`, `$status-archived`, fix
   `$status-published #34d399`, change `$status-draft` to `#8a8a96` (display), add delivery + radius
   maps, set `$sidebar-width: 212px`. Keep `$vars` and `:root` consistent (ideally `:root` derives
   from `$vars` to avoid drift).
6. **`styles.scss` (modify):** `@use` the partials; replace `body { background:#0f1117; color:#e1e4e8 }`
   and the GitHub-dark scrollbar with token-driven values; add a global
   `@media (prefers-reduced-motion: reduce)` rule zeroing transition/animation durations.
7. **`_layout.scss` (modify):** app shell grid to `212px 1fr` full height; reconcile the three
   conflicting sidebar widths (200/212/220) to 212.
8. **`_status-badges.scss` (modify):** add `.status-idea`, a real `.status-archived`, and
   `.delivery-badge--auto|--manual|--warn` classes driven by the delivery tokens.
9. **Recolor the content feature + sidebar:** replace the ~90 hardcoded GitHub-dark hexes across the
   10 content files and the 11 in `sidebar.component.ts` with `var(--…)`. Add the sidebar footer
   user block (32px gradient avatar `135deg, var(--brand-primary) → #9c5440`, name 13px/600,
   "Solo studio" 11px `var(--text-muted)`, top border). Restyle brand to DM Serif Display 24px with
   the italic terracotta "v2". Active nav item → `var(--accent-soft)` bg + `var(--brand-primary)`
   icon.

**Acceptance:** `ng build` clean; a throwaway element styled `background: var(--surface-card)`
renders `#141418`; no GitHub-dark hexes (`#0d1117`/`#161b22`/`#30363d`/`#58a6ff`/`#1f6feb`) remain
in the content feature or sidebar; sidebar shows the footer user block.

---

## Section 2 — Shared atoms & store extensions

**Goal:** the presentational primitives and store state every flow reuses.

### 2.1 Display helpers — `content-list/content-display.utils.ts` (extend)
Add pure functions + maps (logic only; values from `claude-research.md`):

```ts
/** status -> { color: cssVar, label: string, order: number } for pills/tags/tracker. */
export const STATUS_META: Record<ContentStatus, { color: string; label: string; order: number }>;
/** ContentType -> single Unicode glyph for cards/rows. */
export const TYPE_GLYPH: Record<ContentType, string>;
/** numeric voice score -> band color css var. Boundary: >=80 high, >=60 mid, else low.
 *  NOTE: replaces the existing editor `voiceColor` which used strict `>80`; standardize on >=80. */
export function voiceBandColor(score: number | null): string;
/** legal forward/backward status transitions, derived from the real state-machine endpoints.
 *  Map (verified against content.service.ts): Idea->Draft, Draft->{Review,Approved},
 *  Review->{Approved,Draft}, Approved->{Scheduled,Published}, Scheduled->{Approved,Published},
 *  Published->Approved, Archived->(restore). Used by the board's drop predicate + drawer move. */
export const LEGAL_TRANSITIONS: Record<ContentStatus, ContentStatus[]>;
/** compact relative time, e.g. "2h", "in 3d" (scheduled future). */
export function relativeTime(iso: string): string;
/** next status in pipeline order; null if Published/Archived (no forward move). */
export function nextStatus(current: ContentStatus): ContentStatus | null;
```

### 2.2 Atom components — `features/content/shared/`
Standalone, presentational (inputs only, no store coupling):
- `status-tag.component.ts` — `@Input() status: ContentStatus` → dot + label in status color.
- `voice-score-ring.component.ts` — `@Input() score: number | null; @Input() size = 40` → SVG/conic
  ring + mono value, or dashed-empty ring when null.
- `platform-dot.component.ts` — `@Input() platform: Platform; @Input() variant: 'dot'|'tile'` →
  colored dot or 2-letter mono code tile.

### 2.3 `ContentStore` extensions — `stores/content.store.ts`
Change `viewMode`, switch to **load-all (single source of truth)**, and add a **status-transition
dispatcher** (NOT a raw `update`). New/changed surface:

```ts
// state changes
viewMode: 'board' | 'grid' | 'table';   // was 'list' | 'grid'
allContents: Content[];                  // THE source of truth — full set from loadAll()
activeStatus: ContentStatus | null;      // single-status pill filter
search: string;                          // client-side, title+tags
filters: Partial<ContentFilterState>;    // platform/type/date from the Filters popover (client-side)

// computed
counts: Signal<Record<ContentStatus, number>>;            // per-status, from allContents
filtered: Signal<Content[]>;                              // activeStatus + search + popover filters
byStatus: Signal<Record<ContentStatus, Content[]>>;       // board columns, from filtered

// methods
loadAll(): void;                          // the ONLY fetch (large pageSize / dedicated all fetch)
setActiveStatus(status: ContentStatus | null): void;     // toggle; re-click clears
setSearch(term: string): void;                            // debounced 300ms at the input layer
setView(mode: 'board' | 'grid' | 'table'): void;
/** Dispatch a status change via the legal state-machine endpoint. Reads the card's current status
 *  from allContents, validates target ∈ LEGAL_TRANSITIONS[current] (else no-op + notice), calls the
 *  matching ContentService transition (draft/approve/submitForReview/requestChanges/unschedule/
 *  publish/unpublish/restore). OPTIMISTIC: patch ONLY `status` on a NEW array immediately; on success
 *  reload the affected record (real updatedAt) — never fabricate updatedAt (it feeds the editor's
 *  lastUpdatedAt concurrency token); on error roll back + notify.
 *  Approved->Scheduled is special: schedule() needs a date, so the CALLER opens the schedule dialog
 *  and then calls ContentService.schedule(id,{scheduledAt}) — transition() does not fire schedule blindly. */
transition(id: string, target: ContentStatus): void;
```

**Single source of truth (resolves the prior two-source ambiguity):** `loadAll()` replaces the paged
`loadContents`/`setFilter`/`setPage` path — board, grid, AND table all render from `filtered()`. The
server-side paged machinery is removed (or quarantined) so client-side filters can't diverge from a
server query. Filtering (activeStatus + search + popover platform/type/date) is entirely in-memory
over `allContents`.

**Why not `update({status})`:** `UpdateContentRequest` has no `status` field; the backend is a
constrained state machine. Status changes MUST go through the dedicated endpoints. `transition()`
must build NEW arrays (never mutate in place) so signals/computeds react — this is also what makes
CDK drag-drop safe (Section 3).

**Concurrency note:** `ContentStore` and `ContentEditorStore` are independent. A board/drawer
transition invalidates an open editor's `lastUpdatedAt` token; the editor must reload on focus (or
the documented limitation applies — a concurrent edit + board move could be rejected by the server).

**Tests:** counts/filtered/byStatus computeds; `setActiveStatus` toggle; `transition` legal-vs-illegal
target (illegal → no-op + notice), correct endpoint dispatched per (current,target), optimistic
patch + rollback on error, no fabricated `updatedAt`; search matches title and tags.

---

## Section 3 — Content list flow

**Goal:** pipeline bar + kanban board + detail drawer + inspire empty state + refined table, with
the Filters popover for secondary filters.

### 3.1 `content-list.component.ts` (orchestrator, modify)
- Header: serif H1 "Content Studio"; subtitle bound to `store.allContents().length`
  ("{n} pieces moving through your pipeline"); "+ New Content" → `router /content/new`.
- Controls row: search input (`setSearch`, 300ms debounce via `debounceTime`/`takeUntilDestroyed`);
  segmented Board/Table toggle (`setView`); a "Filters" button opening `filters-popover`.
- Renders `pipeline-bar`, then `@switch (store.viewMode())` → `content-board` | `content-grid` |
  `content-list-table` (all render from `store.filtered()` — single source of truth). Hosts
  `detail-drawer` (opened by row/card click via a `selectedId` signal).
- Empty handling: `studio-empty-state` (inspire when `allContents().length === 0`; filtered variant
  when `filtered().length === 0` but content exists).
- **"+ New Content"** and empty-state idea cards route to `/content/new` with optional query params
  `topic` / `type` / `sourceIdeaId`. NOTE: `/content/new` today auto-creates a stub via
  `ContentService.create({title:'Untitled', contentType:BlogPost, primaryPlatform:Blog, tags:[]})`
  and ignores query params — so §4.1 must extend the editor's `ngOnInit` to read those params into the
  `create()` call (seed title/contentType/sourceIdeaId) for the pre-seed to actually work.

### 3.2 `pipeline-bar/` (new)
Horizontal wrap of pills. First "All {total}". Then one pill per `ContentStatus` (dot + label + mono
count from `store.counts()`). Click → `store.setActiveStatus(status)` (re-click clears). Selected
pill takes the status color; zero-count pills at .5 opacity. CSS per `claude-research.md` §A.

### 3.3 `content-board/` (new — CDK kanban, primary view)
- Template: `<div cdkDropListGroup>` containing one `<div cdkDropList [cdkDropListData]="col.cards"
  [id]="col.status" [cdkDropListEnterPredicate]="canDropInto(col.status)"
  (cdkDropListDropped)="onDrop($event)">` per status, each rendering `cdkDrag` cards. Columns come
  from `store.byStatus()` (a computed) mapped to `{status, cards}[]`. `[id]="col.status"` is a valid
  DOM id (PascalCase enum value).
- **Only legal transitions are droppable.** The board must NOT present all 7 columns as universally
  droppable (the backend is a constrained state machine). Use an enter-predicate:

```ts
/** Returns a CDK enter-predicate for a target column: allows the drag to enter only if the dragged
 *  card's current status -> targetStatus is in LEGAL_TRANSITIONS. Illegal targets reject entry, so
 *  the card never visually snaps into a column it can't move to. */
canDropInto(targetStatus: ContentStatus): (drag: CdkDrag<Content>, drop: CdkDropList) => boolean;

/** On a legal cross-column drop, dispatch the status transition. Same-column drops are no-ops
 *  (kanban conveys status, not manual order). MUST NOT mutate event arrays (they derive from a
 *  computed). For target === Scheduled, open the schedule dialog (date required) and call
 *  ContentService.schedule on confirm; otherwise call store.transition(card.id, targetStatus). */
onDrop(event: CdkDragDrop<Content[]>): void;
```

  Behavior: if `previousContainer === container` → ignore. Else `card = event.item.data`,
  `target = event.container.id as ContentStatus`. If `target === Scheduled` → open schedule dialog,
  on confirm `ContentService.schedule(id,{scheduledAt})` then reload; else `store.transition(card.id,
  target)`. The computed `byStatus` re-derives → board updates.
- Column: 286px, bg `--surface-inset`, header dot+name+count; empty column shows dashed "Drop here"
  with `min-height` so CDK accepts drops; `.col-over` highlight only when the predicate allows entry
  (via `(cdkDropListEntered)`/`Exited` gated by `canDropInto`). Card body via `content-card`
  (board variant).

### 3.4 `content-card/` (modify → board card)
Type glyph + uppercase type label + `voice-score-ring` (right); title 14.5px/600; mono tag chips;
footer `platform-dot`s + mono `relativeTime(updatedAt|scheduledAt)` (`◴ in 3d` when scheduled).
Drop the edit/delete/duplicate action emitters from the board variant (those move to the drawer).

### 3.5 `content-list-table/` (modify)
Card-wrapped; columns Status (`status-tag`) · Title (+ mono tag line) · Type (glyph+label) ·
Platforms (`platform-dot`s) · Voice (`voice-score-ring`) · Updated (mono, right). Remove the Actions
column. Row click → open `detail-drawer`.

### 3.6 `detail-drawer/` (new — PrimeNG `p-drawer`)
`<p-drawer [(visible)] position="right" [modal]="true">` styled to the 400px spec (border-left,
shadow, .26s slide). Content: `status-tag` + close; type label; serif title; meta list
(`voice-score-ring`, platform dots, Updated|Scheduled, tags); body preview (first N chars of
`body`). Footer: "Open in editor" (ghost → `/content/:id`) + context action — "Publish →" when
status ∈ {Approved, Scheduled} (opens publish modal) else "Move to {nextStatus(status)} →" calling
`store.transition(id, nextStatus(status))` (legal-transition dispatcher). If `nextStatus` is
`Scheduled`, the action opens the schedule dialog instead of firing blindly (schedule needs a date).

### 3.7 `filters-popover/` (new — PrimeNG `p-popover`)
Relocate the existing platform/type/date filter controls here (`p-select` ×2 + `p-datepicker`
from/to), wired to `store.setFilter`. Anchor to the "Filters" button via `op.toggle($event)`.

### 3.8 `studio-empty-state/` (new)
- Inspire variant: mark tile (64px, accent-soft bg, brand border, `✎`), serif H2 "Your studio is
  quiet.", paragraph, a 2-col grid of idea-suggestion cards (`@Input() suggestions`) each →
  `/content/new` with seeded query params (topic/type), then "or" divider + "+ Start from scratch".
- Filtered variant: `⌕` mark, "Nothing matches that filter", "Clear filters" ghost (resets
  activeStatus/search/popover filters).

### 3.9 Remove `content-filter-sidebar/`
Delete the component and its references once pipeline-bar + filters-popover cover its function.

**Tests:** pipeline counts/selection; board renders columns from `byStatus`; `onDrop` calls
`transition` with correct target, rejects illegal targets, ignores same-column; table row→drawer; drawer footer action per
status; empty-state variant selection.

---

## Section 4 — Editor flow

**Goal:** a clean TipTap prose studio replacing the split markdown editor, with stage tracker,
side-panel voice meter, and an inline restyled AI sidecar. Reuse the existing status action bar.

### 4.1 `content-editor.component.ts` (modify — relayout)
New layout column: `editor-top-bar` (58px) → body (manuscript scroll + optional 340px side panel)
→ action bar. **Remove `p-splitter` + `<app-markdown-editor>` + `<markdown>` preview and drop
`SplitterModule`.** Keep: `scheduleAutoSave` (3s debounce, Idea/Draft/Review only), `canEdit()`,
all status handlers, and the publish-modal open. A `panelOpen` signal toggles the side panel.
- **New-content seeding:** extend `ngOnInit` so the `/content/new` path reads `topic`/`type`/
  `sourceIdeaId` query params and passes them into the existing `ContentService.create(...)` stub call
  (seed title from `topic`, `contentType` from `type`, `sourceIdeaId` when present). Today
  `/content/new` auto-creates a fixed `Untitled` Blog stub and ignores query params — without this
  change the empty-state idea cards' pre-seed params are dead.

### 4.2 `prose-editor/` (new — TipTap ↔ signals wrapper)
The risky/core component. Wraps a TipTap `Editor` instance (headless, no toolbar; StarterKit +
markdown extension). Contract:

```ts
/** A markdown-backed rich prose surface. Loads markdown into a TipTap doc, emits markdown out.
 *  - value: markdown string (set once on load / on external change; NOT re-applied on every keystroke
 *    to avoid caret reset).
 *  - readOnly: disables editing (Approved+).
 *  - valueChange: debounced markdown serialization of the current doc. */
@Input() value: string;
@Input() readOnly: boolean;
@Output() valueChange: EventEmitter<string>;
```
Implementation notes (for the implementer, not code here): create the Editor in `ngOnInit`/effect,
`setContent(markdown)` once; on TipTap `update` serialize doc→markdown and emit (debounced). **Caret
guard (concrete mechanism):** only call `editor.commands.setContent(value)` when the incoming `value`
differs from the editor's last serialized output AND the editor is NOT focused — this prevents both
caret-jump and an update loop. `editable` is driven by `canEdit()` (Approved+ read-only). Sanitize
paste to a plain/marked-allowlist. Destroy the editor on `ngOnDestroy`. **Include a round-trip test:**
for the mark set we use (h1–h3, bold, italic, links, bullet/ordered lists, inline code), `markdown →
doc → markdown` must be stable. **Fallback if it isn't:** first constrain StarterKit to the marks that
do round-trip cleanly and document the dropped ones; if even the constrained subset drifts
unacceptably, fall back to a token-styled raw-markdown `<textarea>` (research option 2) — name this
decision in the section so a failing round-trip test mid-implementation doesn't strand the work.

### 4.3 `manuscript-surface/` (new)
Centered max-680px, padding `46px 32px 120px`. Renders: editable tag chips → editable title
(`DM Serif Display` 40px, contenteditable single-line or input, → `store.updateField('title')`) →
derived subtitle (19px, **display-only**, e.g. first non-heading line of body or a transient field
held in component state) → byline (avatar + "{author} · {status}") → `prose-editor` for the body.
Body change → `store.updateField('body', md)` + trigger `scheduleAutoSave`. **Idea state:** when
`status === Idea`, replace the prose area with a dashed "This is still just an idea." panel + "Start
draft" (calls existing `onStartDraft`).

### 4.4 `stage-tracker/` (new)
6 dots joined by 18px lines. `@Input() status`. Exact 7-enum → 6-dot mapping (current dot index,
0-based, dots = [Idea, Draft, Review, Approved, Scheduled, Published]):
- Idea→0, Draft→1, Review→2, Approved→3, **Scheduled→4**, Published→5.
- **Archived→** render all dots muted with a small "Archived" terminal label (no active dot), since
  Archived is off the linear path.
Dots before the current index are "completed" (filled `var(--text-muted)`); the current dot is
enlarged to 12px + its status color; dots after are empty (border only). Pure presentational; trivial
to unit-test by asserting the active index per enum value.

### 4.5 `editor-top-bar/` (new)
"← Studio" back (router to `/content`); `stage-tracker`; type·platform meta (12.5px); "Saved" mono
indicator — NOTE this is not a reusable component today, it's inline `@if` markup reading
`store.isSaving()`/`store.isDirty()` (Saving/Unsaved/Saved); reimplement the same `@if` in
`editor-top-bar`. `voice-score-ring` size 32; "✦ Assistant" toggle button shown when the side panel
is closed.

### 4.6 `voice-meter/` (new — side panel top)
Label + big mono value colored by `voiceBandColor` + track/fill bar + a one-line note keyed to band
(≥80 confident / ≥60 close / else flat), using `VoiceCheckResult.feedback` when available. Shows
`content.voiceScore` on load; a "re-check" affordance calls `ContentService.voiceCheck(id)` and
updates the meter. Fill bar transition 400ms.

### 4.7 `sidecar-chat/` (modify — inline + restyle)
Drop the `p-drawer` chrome; render as an inline 340px panel (bg `--surface-inset`) below the voice
meter. Restyle: assistant bubbles `--surface-elevated` + border, user bubbles brand bg + dark text;
swap the skeleton shimmer for a 3-dot "blink" thinking animation during `isStreaming`; keep
quick-action chips (Draft from idea/scratch when body empty; Refine/Shorten/Expand/Change tone
otherwise), input+send, and ✓ Apply to draft / ⧉ Copy. **SignalR wiring unchanged**
(`tokens$`/`generationComplete$`/`generationError$`, `sendChatMessage`).

### 4.8 Action bar + `platform-targets/` (modify)
Left: "Targets" label + `platform-dot`s (reuse `platform-targets` selection logic, restyle). Right:
existing `@switch(status)` buttons — keep every handler; restyle primary (`bg var(--brand-primary)`,
text `#1a0f0a`) and ghost (transparent + border). Approved/Scheduled "Publish" opens the publish
modal (Section 5).

### 4.9 Delete `markdown-editor/`
After §4.1 removes the only importer, delete the component + spec. Check whether
`@acrodata/code-editor` / `@codemirror/*` have other users in `src/`; if none, remove them from
`package.json`.

**Tests:** `prose-editor` markdown round-trip stability; manuscript idea-state vs draft-state render;
`stage-tracker` dot states per status; voice-meter band/color/note; sidecar streaming indicator +
apply/copy; action bar buttons per status; autosave still fires only for Idea/Draft/Review.

---

## Section 5 — Publish overlay flow

**Goal:** a bespoke 1080px publish modal with destinations, delivery badges, 5 faithful per-platform
previews, schedule, and a result view — driven by the same markdown body.

### 5.1 `models/platform-metadata.ts` (new)
Static design metadata, separate from runtime connection:

```ts
export type DeliveryMode = 'auto' | 'manual';
export interface PlatformMeta {
  code: string;          // 2-letter tile, e.g. "Bl", "Me", "Su", "Li", "Tw"
  label: string;
  delivery: DeliveryMode;
  charLimit: number | null;
  fmt: string;           // human note, e.g. "No publish API — paste into Medium"
}
/** Per PUBLISHABLE_PLATFORMS. Blog auto/null, Medium manual/null, Substack auto/null,
 *  LinkedIn auto/3000, Twitter auto/280. */
export const PLATFORM_META: Record<Platform, PlatformMeta>;
/** Badge label from static delivery + LIVE isConnected:
 *  auto&&connected->"⚡ Auto-publish"; auto&&!connected->"⚡ Connect to auto-publish"; manual->"✋ Manual". */
export function deliveryBadge(meta: PlatformMeta, isConnected: boolean): { text: string; variant: 'auto'|'warn'|'manual' };
```
Connection comes from `ContentService.getPlatforms()` → `PlatformConnectionStatus.isConnected`.
**Do not hardcode connection** (the prototype's `connected:false` for Twitter is sample data).

### 5.2 Markdown adapters — `publish-modal/markdown-blocks.ts` (new)
```ts
export type ProseBlock = { type: 'h1'|'h2'|'h3'|'h4'|'h5'|'h6'|'p'; text: string };
/** Parse markdown into ordered prose blocks via marked.lexer (headings by depth, paragraphs). */
export function toBlocks(markdown: string): ProseBlock[];
/** Strip markdown to plain text via marked.walkTokens, concatenating leaf text (exclude link
 *  hrefs and emphasis markers). Used for char/thread budgets — must reflect rendered length. */
export function plainText(markdown: string): string;
```

### 5.3 Thread splitter — `publish-modal/thread-splitter.ts` (new)
```ts
/** Greedy, sentence-aware split of plain text into tweets that, INCLUDING the "i/n" suffix,
 *  never exceed `limit` (default 280). Reserves suffix room: per-tweet budget = limit - len(" i/n").
 *  Returns the numbered tweet strings. */
export function splitThread(text: string, limit = 280): string[];
```
Boundary test: a long run with many tweets where `n` is 2+ digits still keeps each ≤280.

### 5.4 `publish-modal.component.ts` (modify — bespoke rewrite)
Bespoke overlay (scrim + 1080px card, .24s pop-in; reduced-motion honored). Header serif
"Publish"/"Schedule" + title. Body grid 340px/1fr:
- **Left (destinations):** one row per `PUBLISHABLE_PLATFORMS` — checkbox (primary platform
  checked+disabled), `platform-dot` tile, name + "Primary" badge, `meta.fmt` note, right stack of
  `delivery-badge` + char/thread usage (from `plainText(body).length` vs `meta.charLimit`, or thread
  count via `splitThread`). Selected rows border brand + bg accent-soft. "When" section: "Publish
  now" vs `datetime-local` (schedule mode, color-scheme dark).
- **Right (preview):** tabs per *selected* platform; sticky caption (auto deploys / you post this /
  connect to auto-deploy, from delivery + connection); rendered preview component on a white card.
- **State (local component signals):** `selected: Platform[]`, `activeTab: Platform`,
  `mode: 'now'|'schedule'`, `scheduledAt: string|null`, `result: ... | null`.
- **Schedule mode caveat:** `ScheduleContentRequest` is `{scheduledAt}` only — the backend schedule
  does NOT take platforms. So in schedule mode, show the destinations list **disabled** with a note
  ("Scheduling applies to the whole post; per-platform selection only affects immediate publish").
  Per-platform selection is meaningful only for "Publish now".
- **Footer:** "{n} destinations · {a} auto · {m} manual" + Cancel + "Publish {n} →" (disabled if
  none selected or schedule with no datetime). Confirm → for schedule mode call
  `ContentService.schedule(id, {scheduledAt})` (no platforms); for now call
  `ContentService.publish(id, {targetPlatforms:selected})`; then render the result view.

### 5.5 Preview renderers — `publish-modal/previews/` (new ×5)
Each `@Input() blocks: ProseBlock[]` (+ title/derived subtitle/byline as inputs); render on a white
card to the prototype spec:
- `blog-preview` — striped hero, kicker, serif H1, lede, byline, serif H2 sections.
- `medium-preview` — bold sans H1, gray subtitle, author row + Follow, clap/bookmark bar.
- `substack-preview` — masthead (publication + Subscribe), "to N subscribers" byline, unsubscribe
  footer.
- `linkedin-preview` — feed card; `plainText` truncated ~210 chars + "…more"; reaction/comment/repost
  bar; respect 3000 cap (warn if over).
- `twitter-preview` — `splitThread(plainText(body), 280)` rendered as numbered `1/n` tweets on a
  threaded connector rail.

### 5.6 `delivery-badge.component.ts` + `publish-result.component.ts` (new)
- `delivery-badge` — `@Input() meta; @Input() isConnected` → styled pill from `deliveryBadge(...)`.
- `publish-result` — per destination row: auto+connected → "Publishing…" (spinner) → "✓ Published —
  View ↗" (poll `getPublishStatus`, `retryPlatform` on failure); manual → "Ready to post" + ⧉ Copy
  (clipboard gets the platform-formatted body via the same transforms) + Open {platform} ↗;
  schedule mode → "◴ Scheduled for {datetime}". Footer note: manual destinations have no publish API.
  - **Two distinct "status" concepts — be explicit:** the per-platform spinner/✓/retry is driven by
    `PlatformPublish.publishStatus` (`Pending|Formatting|Published|Failed` — there is NO `Scheduled`
    value). The "◴ Scheduled" row is a **frontend-only** state derived from the modal's schedule mode
    + the content-level `Content.status === Scheduled` / `scheduledAt`, NOT from a `PublishStatus`.
  - **Polling cadence (define, don't leave to guess):** poll `getPublishStatus(id)` every 2s; stop
    when every `platformStatuses[].publishStatus ∈ {Published, Failed}`; hard cap ~30s then show a
    "still processing" state. Clean up the interval on modal close / `ngOnDestroy`.

**Tests:** `toBlocks`/`plainText` (headings, links, emphasis, code don't corrupt counts);
`splitThread` boundary (≤280 incl. suffix, multi-digit n); `deliveryBadge` matrix; each preview
renders from blocks; LinkedIn truncation + over-limit warn; modal footer summary + disabled logic;
confirm routes to publish vs schedule (schedule sends no platforms); schedule mode disables
destinations; result-view states (auto/manual/scheduled) + polling stop condition; **bespoke modal
a11y: focus-trap, Esc-to-close, scrim-dismiss, `aria-modal`.**

---

## Cross-cutting concerns

- **Routing:** unchanged (`''`, `'new'`, `':id'`). Empty-state ideas and "New Content" route to
  `/content/new` with optional seed query params.
- **Accessibility:** PrimeNG drawer/popover provide focus-trap/scrim/escape; the bespoke publish
  modal must replicate focus-trap, `Esc` to close, scrim click to dismiss, and `aria-modal`.
- **Reduced motion:** global media query (Section 1) covers bespoke animations; PrimeNG honored via
  the same CSS.
- **Performance:** load-all is fine at current volume; the board/counts read `allContents`. Revisit
  with a counts endpoint if content grows into the hundreds.
- **Optimistic UX:** `transition` (board drop, drawer move) updates status immediately and rolls back on
  service error with a user-visible notice.

## Risks & mitigations
1. **TipTap markdown round-trip fidelity** (community extension) → round-trip test gates the mark
   set; constrain StarterKit to the stable subset; fallback documented. *Highest risk.*
2. **CDK + signals mutation trap** → drop handler never mutates event arrays; dispatches via
   `store.transition`; columns re-derive from computed.
2b. **Constrained state machine** → status is NOT a free field; changes go through dedicated
   endpoints and only legal transitions exist. The board enforces this with
   `cdkDropListEnterPredicate` (illegal columns reject entry); `transition` validates against
   `LEGAL_TRANSITIONS` and never fabricates `updatedAt` (protects the editor's concurrency token).
   Approved→Scheduled requires a date → opens the schedule dialog, never fires `schedule` blindly.
3. **marked plain-text accuracy** → unit-tested stripper; char/thread budgets depend on it.
4. **`markdown-editor` removal** → verify no other importers + orphaned codemirror deps before
   deleting from `package.json`.
5. **Static vs live platform data** → delivery/charLimit/fmt static; isConnected from `getPlatforms`.
6. **Derived subtitle/byline** → never written to `UpdateContentRequest`; presentation-only.

## Out of scope
Backend/API/EF changes; `signalr.service.ts`; non-content features (Feed/Ideas/Discover/Calendar/
Analytics/Listening) beyond incidental token recoloring.
