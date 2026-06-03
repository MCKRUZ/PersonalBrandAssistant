# Section 04 — Content List Flow

## Goal

Build the list-view flow of Content Studio: a **pipeline bar** (status-filter pills), a **CDK kanban board** (primary view), a **detail drawer**, an **inspire/filtered empty state**, and a **refined table**, plus a **Filters popover** for secondary filters. Wire it all to the `ContentStore` so board, grid, and table render from a single `filtered()` source of truth. Remove the legacy `content-filter-sidebar`.

This is the **list flow** counterpart to the editor flow (section 05). Both run in parallel after section 03. No backend, API, or DB changes. `signalr.service.ts` is untouched.

## Tech baseline & paths

- Angular 19 standalone components, `@ngrx/signals` (`signalStore`), PrimeNG 20.
- Web app root: `src/PersonalBrandAssistant.Web/`.
- Tests colocated as `*.spec.ts`. Run command:
  `cd src/PersonalBrandAssistant.Web && npx ng test --watch=false --browsers=ChromeHeadless`
- Tests use Jasmine/Karma. Mock only HTTP (`HttpTestingController`) and SignalR; use **real** signalStores. 80% coverage minimum on new code.

Files for this section (under `src/app/features/content/content-list/`):

```
content-list.component.ts            # MODIFY — orchestrates new views
pipeline-bar/                        # NEW
content-board/                       # NEW — CDK kanban
content-card/                        # MODIFY — board card variant
content-list-table/                  # MODIFY — restyle, drop Actions, row->drawer
detail-drawer/                       # NEW — p-drawer
filters-popover/                     # NEW — p-popover
studio-empty-state/                  # NEW — inspire + filtered variants
content-grid/, view-toggle/          # MODIFY (toggle: board/table)
content-filter-sidebar/              # REMOVE
```

## Dependencies (provided by earlier sections — DO NOT re-implement)

This section **consumes** the following. They are guaranteed present once sections 01–03 are merged. Reference them; do not duplicate their logic.

**From section 01 (foundation):**
- `@angular/cdk@^19.2.0` is installed (provides `DragDropModule` / `CdkDropList` / `CdkDrag`).
- The `:root` CSS custom-property token set exists (`var(--surface-card)`, `var(--brand-primary)`, `var(--accent-soft)`, `var(--surface-inset)`, `var(--surface-border)`, `var(--text-secondary)`, `var(--text-muted)`, status/voice/delivery tokens, radius scale `var(--r)`=12px / `var(--r-inner)`=10px / `var(--r-pill)`=99px). These resolve to real colors. Without section 01 these resolve to nothing.

**From section 02 (shared atoms & store):**

Display helpers in `content-list/content-display.utils.ts`:
```ts
export const STATUS_META: Record<ContentStatus, { color: string; label: string; order: number }>;
export const TYPE_GLYPH: Record<ContentType, string>;
export function voiceBandColor(score: number | null): string;   // >=80 high, >=60 mid, else low
export const LEGAL_TRANSITIONS: Record<ContentStatus, ContentStatus[]>;
export function relativeTime(iso: string): string;              // "2h", "in 3d", "just now"
export function nextStatus(current: ContentStatus): ContentStatus | null;  // null for Published/Archived
```

Atom components in `features/content/shared/` (standalone, inputs-only, no store coupling):
- `status-tag.component.ts` — `@Input() status: ContentStatus` → colored dot + label.
- `voice-score-ring.component.ts` — `@Input() score: number | null; @Input() size = 40` → conic ring + mono value, or dashed-empty ring when null.
- `platform-dot.component.ts` — `@Input() platform: Platform; @Input() variant: 'dot' | 'tile'` → colored dot or 2-letter mono code tile.

`ContentStore` (`stores/content.store.ts`) surface used here:
```ts
// state
viewMode: 'board' | 'grid' | 'table';
allContents: Content[];                  // THE source of truth (from loadAll())
activeStatus: ContentStatus | null;
search: string;
filters: Partial<ContentFilterState>;

// computed
counts: Signal<Record<ContentStatus, number>>;
filtered: Signal<Content[]>;             // activeStatus + search + popover filters applied
byStatus: Signal<Record<ContentStatus, Content[]>>;   // board columns from filtered

// methods
loadAll(): void;                         // the ONLY fetch
setActiveStatus(status: ContentStatus | null): void;  // toggle; re-click clears
setSearch(term: string): void;
setView(mode: 'board' | 'grid' | 'table'): void;
setFilter(filters: Partial<ContentFilterState>): void;
/** Legal-transition dispatcher: validates target ∈ LEGAL_TRANSITIONS[current], calls the matching
 *  ContentService endpoint, optimistically patches ONLY status on a NEW array, rolls back on error.
 *  Does NOT fire schedule (Approved->Scheduled needs a date — caller-driven). */
transition(id: string, target: ContentStatus): void;
```

**From section 03 (publish overlay):**
- The bespoke **publish modal** (`publish-modal.component.ts`). The detail drawer's "Publish →" button opens it. Reference how the editor flow opens it (same component); pass the content id. Match its input/output contract (inputs `[visible]`/`[content]`/`[connectedPlatforms]`/`[mode]`, outputs `(confirm)`/`(cancel)`).

**From the existing codebase (unchanged):**
- `ContentService.schedule(id, { scheduledAt })` — the schedule endpoint; takes a date only, **no platforms**.
- `Content`, `ContentStatus`, `ContentType`, `Platform`, `ContentFilterState` models.
- Routing: `/content` (list), `/content/new`, `/content/:id`. `/content/new` accepts optional query params `topic` / `type` / `sourceIdeaId` (the editor flow, section 05 §4.1, reads them — here we just emit them in the link).

## Background: the constrained state machine

Status is **not** a free-text field. The backend is a state machine; status changes go through dedicated endpoints (draft / approve / submitForReview / requestChanges / unschedule / publish / unpublish / restore). Only **legal** transitions exist. `LEGAL_TRANSITIONS` (from section 02) encodes them:

```
Idea -> [Draft]
Draft -> [Review, Approved]
Review -> [Approved, Draft]
Approved -> [Scheduled, Published]
Scheduled -> [Approved, Published]
Published -> [Approved]
Archived -> (restore)
```

The board MUST enforce this with a CDK enter-predicate (illegal target columns reject entry). `Approved -> Scheduled` is special: scheduling needs a date, so the **caller** opens a schedule dialog and then calls `ContentService.schedule(...)` — `store.transition()` never fires schedule blindly.

`transition()` must build NEW arrays (never mutate in place) so signals/computeds react. This is also what makes CDK drag-drop safe: the drop handler must never mutate the event arrays (they derive from a computed).

---

## Tests first

Write the failing test, then the code. Component specs cover render-by-state plus the listed interactions. For CDK drop, **do not** simulate the native drag gesture — synthesize a `CdkDragDrop` object and call the handler directly.

### content-list.component.spec.ts (update existing)
```
# Test: subtitle shows allContents().length pieces.
# Test: viewMode switch renders board/grid/table; each reads from filtered().
# Test: "+ New Content" navigates to /content/new (with seed params when from an idea card).
# Test: shows inspire empty-state when allContents empty; filtered empty-state when filtered empty
#       but content exists.
# Test: search input debounces ~300ms before calling setSearch.
```

### pipeline-bar.spec.ts
```
# Test: renders an "All {total}" pill + one pill per ContentStatus with its count from counts().
# Test: clicking a pill calls setActiveStatus(status); clicking the active pill clears it.
# Test: zero-count pills render at reduced opacity; selected pill takes the status color.
```

### content-board.spec.ts (the tricky one)
```
# Test: renders one cdkDropList column per status from byStatus().
# Test: canDropInto(target) predicate returns true only when dragged card's status->target is legal.
# Test: onDrop with same container is a no-op (no transition call).
# Test: onDrop legal cross-column calls store.transition(cardId, targetStatus).
# Test: onDrop where target===Scheduled opens the schedule dialog (does NOT call transition/schedule
#       directly); on confirm calls ContentService.schedule(id,{scheduledAt}).
# Test: empty column renders the dashed "Drop here" target.
# (Drag simulation: dispatch CDK drop with a synthesized CdkDragDrop object; assert the handler,
#  not the native drag gesture.)
```

### content-card.spec.ts (update)
```
# Test: board variant shows type glyph + uppercase type label + voice ring + title + tag chips +
#       platform dots + relativeTime(updatedAt); scheduled shows "in {n}{unit}".
```

### content-list-table.spec.ts
```
# Test: renders Status/Title/Type/Platforms/Voice/Updated columns; NO Actions column.
# Test: row click emits/open the detail drawer with that content's id.
```

### detail-drawer.spec.ts
```
# Test: header shows status tag + serif title; meta list shows voice/platforms/updated|scheduled/tags.
# Test: footer "Open in editor" navigates to /content/:id.
# Test: footer shows "Publish ->" when status in {Approved,Scheduled}; else "Move to {nextStatus} ->".
# Test: "Move to {next}" calls store.transition(id, nextStatus); when next is Scheduled it opens the
#       schedule dialog instead.
```

### filters-popover.spec.ts
```
# Test: platform/type/date controls update store filters; clearing resets them.
```

### studio-empty-state.spec.ts
```
# Test: inspire variant renders idea-suggestion cards; clicking one navigates to /content/new with
#       seed query params (topic/type).
# Test: filtered variant renders "Nothing matches that filter" + "Clear filters" which resets
#       activeStatus/search/filters.
```

**Coverage:** 80% on all new files. Component specs cover render-by-state + the key interactions above.

---

## Implementation

### 3.1 `content-list.component.ts` (orchestrator — modify)

- **Header:** serif H1 "Content Studio"; subtitle bound to `store.allContents().length` ("{n} pieces moving through your pipeline"); "+ New Content" button → router `/content/new`.
- **Controls row:** search input wired to `store.setSearch` with a **300ms debounce** (`debounceTime(300)` + `takeUntilDestroyed()`); a segmented **Board/Table** toggle calling `store.setView(...)` (update `view-toggle/` to offer `board`/`table` instead of the old `list`/`grid`); a "Filters" button that opens `filters-popover` via `op.toggle($event)`.
- **Views:** render `pipeline-bar`, then `@switch (store.viewMode())` → `content-board` | `content-grid` | `content-list-table`. **All three read from `store.filtered()`** — single source of truth. Host `detail-drawer`, opened by row/card click via a `selectedId` signal.
- **Empty handling:** render `studio-empty-state`:
  - **inspire** variant when `store.allContents().length === 0`;
  - **filtered** variant when `store.filtered().length === 0` but content exists.
- **Loading:** call `store.loadAll()` on init (the only fetch).
- **Seeding routes:** "+ New Content" → `/content/new` (no params). Empty-state idea cards → `/content/new` with optional query params `topic` / `type` / `sourceIdeaId`. (The editor reads these in section 05 §4.1; here we only construct the link.)

### 3.2 `pipeline-bar/` (new)

Horizontal flex-wrap of pills. First pill is "All {total}". Then one pill per `ContentStatus` rendering dot + label + mono count from `store.counts()`. Click → `store.setActiveStatus(status)` (re-click the active pill clears to `null`). Selected pill takes its status color; zero-count pills render at `.5` opacity.

CSS (from `claude-research.md` §A):
- Container padding `18px 28px 14px`, gap 8px, flex-wrap.
- Pill: padding `7px 13px`, radius `var(--r-pill)` (99px), bg `var(--surface-card)`, border 1px `var(--surface-border)`, 13px/500 `var(--text-secondary)`, transition `.14s`. Hover bg hover + text-primary.
- Selected `.on`: bg elevated, color text-primary, border disabled. All-pill selected: border `var(--brand-primary)` (accent).
- Dot: 8×8px circle, `flex-shrink: 0`. Empty status: `opacity: .5`. Mono count chip.

### 3.3 `content-board/` (new — CDK kanban, primary view)

Template: `<div cdkDropListGroup>` containing one `<div cdkDropList>` per status. Columns come from `store.byStatus()` (a computed) mapped to `{ status, cards }[]`. `[id]="col.status"` is a valid DOM id (PascalCase enum value).

```html
<div cdkDropListGroup>
  @for (col of columns(); track col.status) {
    <div cdkDropList
         [cdkDropListData]="col.cards"
         [id]="col.status"
         [cdkDropListEnterPredicate]="canDropInto(col.status)"
         (cdkDropListDropped)="onDrop($event)">
      <!-- header: dot + name + count -->
      @for (card of col.cards; track card.id) {
        <app-content-card cdkDrag [cdkDragData]="card" [content]="card" variant="board" />
      }
      @if (col.cards.length === 0) {
        <!-- dashed "Drop here" target with min-height so CDK accepts drops -->
      }
    </div>
  }
</div>
```

Handlers:
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

`onDrop` behavior:
- If `event.previousContainer === event.container` → ignore (no-op).
- Else `card = event.item.data`, `target = event.container.id as ContentStatus`.
- If `target === 'Scheduled'` → open the schedule dialog; on confirm call `ContentService.schedule(card.id, { scheduledAt })` then reload the affected record. **Do not** call `store.transition` for this case.
- Else `store.transition(card.id, target)`. The `byStatus` computed re-derives → board updates.

Column CSS (research §A):
- Width / flex-basis **286px**, bg `var(--surface-inset)` (#0c0c0e), border 1px `var(--surface-border)`, radius `var(--r)` (12px), `max-height: 100%`, transition border-color/box-shadow `.14s`. Header padding `14px 14px 11px`; body padding `0 11px 13px`, gap 10px.
- Empty column: dashed "Drop here" placeholder with a `min-height` so CDK accepts drops.
- `.col-over` highlight (drop target): border `var(--brand-primary)` + bg `var(--accent-soft)`. Apply the highlight only when the predicate allows entry — gate `(cdkDropListEntered)` / `(cdkDropListExited)` via `canDropInto`.

### 3.4 `content-card/` (modify → board card)

Add a board variant: type glyph (`TYPE_GLYPH`) + uppercase type label, `voice-score-ring` on the right; title 14.5px/600, line-height 1.32; mono tag chips; footer = `platform-dot`s + mono `relativeTime(updatedAt | scheduledAt)` (show `◴ in 3d` when scheduled). **Drop** the edit/delete/duplicate action emitters from the board variant — those move to the detail drawer.

Card CSS (research §A): bg `var(--surface-card)`, border 1px `var(--surface-border)`, radius `var(--r-inner)` (10px), padding 13px. Hover: border disabled + shadow `0 6px 20px -10px rgba(0,0,0,.6)`.

### 3.5 `content-list-table/` (modify)

Card-wrapped table. Columns: **Status** (`status-tag`) · **Title** (+ mono tag line) · **Type** (glyph + label) · **Platforms** (`platform-dot`s) · **Voice** (`voice-score-ring`) · **Updated** (mono, right-aligned). **Remove the Actions column.** Row click → open `detail-drawer` with that content's id (emit to the orchestrator's `selectedId` signal).

### 3.6 `detail-drawer/` (new — PrimeNG `p-drawer`)

```html
<p-drawer [(visible)]="visible" position="right" [modal]="true"> ... </p-drawer>
```

Styled to the 400px spec (research §A): width **400px**, max-width 92vw, bg `var(--surface-card)`, border-left 1px `var(--surface-border)`, z-index 50, fixed full-height right. Slide-in `.26s cubic-bezier(.2,.8,.2,1)`; shadow `-20px 0 60px -20px rgba(0,0,0,.7)`. Header padding `18px 20px`; body `22px 20px`. Scrim `rgba(0,0,0,.55)`, z-index 40, fade `.2s`.

Content:
- Header: `status-tag` + close button (30×30px, radius 7px).
- Type label.
- Serif title (DM Serif Display 23px/400, line-height 1.18, margin `10px 0 20px`).
- Meta list (`.dm-row`: padding `12px 0`, gap 14px, border-bottom 1px border; label `.dm-k` width 96px, 12px uppercase, letter-spacing .5px): `voice-score-ring`, platform dots, **Updated** | **Scheduled** (whichever applies), tags.
- Body preview: first N chars of `content.body`.

Footer:
- "Open in editor" — ghost button → router `/content/:id`.
- Context action:
  - status ∈ {Approved, Scheduled} → **"Publish →"** opens the publish modal (section 03).
  - else → **"Move to {nextStatus(status)} →"** calling `store.transition(id, nextStatus(status))`.
  - **Special case:** if `nextStatus(status) === 'Scheduled'`, the action opens the **schedule dialog** instead of firing `transition` blindly (schedule needs a date), then calls `ContentService.schedule(id, { scheduledAt })`.

### 3.7 `filters-popover/` (new — PrimeNG `p-popover`)

Relocate the existing platform/type/date filter controls here: `p-select` ×2 (platform, type) + `p-datepicker` (from/to). Wire to `store.setFilter(...)` (these are client-side popover filters folded into `filtered()`). Anchor to the orchestrator's "Filters" button via `op.toggle($event)`. Provide a clear/reset that empties the filters.

### 3.8 `studio-empty-state/` (new)

Two variants selected by `@Input() variant: 'inspire' | 'filtered'`.

- **Inspire variant:** mark tile (64×64px, radius 18px, glyph `✎` 28px, color `var(--brand-primary)`, bg `var(--accent-soft)`, border 1px brand, margin-bottom 22px); serif H2 "Your studio is quiet." (DM Serif Display 34px/400); paragraph (`var(--text-secondary)` 15px, line-height 1.55, max-width 520px); a 2-col grid of idea-suggestion cards from `@Input() suggestions`, each → `/content/new` with seeded query params (`topic` / `type`); then an "or" divider + "+ Start from scratch" → `/content/new`.
- **Filtered variant:** `⌕` mark, "Nothing matches that filter", "Clear filters" ghost button that resets `activeStatus` + `search` + popover `filters` (`store.setActiveStatus(null)`, `store.setSearch('')`, `store.setFilter({})`).

### 3.9 Remove `content-filter-sidebar/`

Once `pipeline-bar` + `filters-popover` cover its function, delete `content-filter-sidebar.component.ts`, its `.spec.ts`, and all references/imports.

---

## Acceptance

- Board, grid, and table all render from `store.filtered()` — no divergent source.
- Pipeline pills reflect `store.counts()`; clicking toggles `activeStatus`.
- CDK board only accepts **legal** transitions (enter-predicate rejects illegal columns); legal cross-column drop dispatches `store.transition`; `Scheduled` target opens the schedule dialog; same-column drop is a no-op; no event-array mutation.
- Table has no Actions column; row click opens the drawer.
- Drawer footer shows "Publish →" for Approved/Scheduled, else "Move to {next} →"; the Scheduled next-status opens the schedule dialog.
- Empty state shows inspire vs filtered correctly; idea cards route with seed params.
- `content-filter-sidebar` is gone with no dangling imports.
- All listed specs pass; 80% coverage on new files; `ng build` clean.

## Notes / gotchas

- **No mutation in `onDrop`:** the event arrays derive from the `byStatus` computed. Mutating them (CDK's default `moveItemInArray`/`transferArrayItem`) corrupts the signal graph. Dispatch via `store.transition` and let the computed re-derive.
- **Optimistic UX:** `transition` patches status immediately and rolls back on service error with a user-visible notice (handled inside the store from section 02 — the list flow just calls it).
- **Concurrency:** a board/drawer transition can invalidate an open editor's `lastUpdatedAt` token. Out of scope to fully solve here; the documented limitation in section 02 applies.
- **Schedule dialog:** keep it simple — a date/time picker producing `scheduledAt`. The same dialog is reused by the board drop (`Scheduled` column) and the drawer "Move to Scheduled" action.

---

## IMPLEMENTATION NOTES (actual — as built)

Implemented by a subagent from this section file, then code-reviewed (CLEAN). Result: 481 tests pass,
0 failures; `ng build` clean; zero GitHub-dark hexes.

- **New components:** `pipeline-bar`, `content-board` (CDK kanban), `detail-drawer` (`p-drawer`),
  `filters-popover` (`p-popover`), `studio-empty-state` (inspire + filtered), and a shared
  `schedule-dialog` (datetime → `scheduledAt`) reused by the board's Scheduled-drop and the drawer's
  Move-to-Scheduled.
- **Store:** widened `viewMode` to `'board'|'grid'|'table'` (init `'board'`); REMOVED the legacy paged
  surface (`contents`/`page`/`pageSize`/`loadContents`/`setPage`/`toggleView`/`totalPages`/…);
  `deleteContent` now just `loadAll()`. `transition`/`counts`/`filtered`/`byStatus` intact.
- **Board:** `cdkDropListGroup` + per-status `cdkDropList`; `cdkDropListEnterPredicate` gates entry on
  `LEGAL_TRANSITIONS`; `onDrop` never mutates event arrays (dispatches `store.transition`, or opens the
  schedule dialog for the Scheduled column).
- **Orchestrator rewrite:** serif header + live subtitle, 300ms debounced search, Board/Table toggle,
  pipeline bar, Filters popover, `@switch(viewMode)` (all from `filtered()`/`byStatus()`), hosted
  drawer via `selectedId`, inspire/filtered empty states, `loadAll()` on init.
- **Restyled:** `content-card` (added `variant="board"`), `content-list-table` (card-wrapped, no
  Actions col, row→drawer), `content-grid` (board cards). **Fixed** the pre-existing ContentCard
  `'Blog Post'` spec → asserts `'Blog'` (`ContentType.BlogPost==='Blog'`).
- **Deleted:** `content-filter-sidebar/`, `view-toggle/` (toggle folded into the orchestrator).
- **Drawer "Publish →"** routes to `/content/:id` (editor owns the publish modal) rather than
  instantiating the modal from the list.
- **Follow-ups (non-blocking, from review):** drawer body-preview uses a `subscribe` in an `effect`
  (idiomatic `rxResource` would be cleaner; low-risk single-select race); `content-card`
  `edit`/`onDelete`/`duplicate` outputs are now dead — cleanup later.
- Review: `implementation/code_review/section-04-review.md`.

