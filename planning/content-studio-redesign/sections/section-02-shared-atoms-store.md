# Section 02 — Shared Atoms & Store Extensions

## Purpose

This section builds the cross-flow presentational primitives and the central `ContentStore` state that every downstream flow (publish overlay §03, content list §04, editor §05) reuses. The work splits into three parts, ordered for highest test coverage first:

1. **Pure display helpers** — new functions/maps in `content-display.utils.ts` (status metadata, type glyphs, voice band colors, relative time, legal transitions, next-status). Fully testable, no Angular.
2. **Atom components** — three standalone presentational components (`status-tag`, `voice-score-ring`, `platform-dot`). Inputs only, no store coupling.
3. **`ContentStore` extensions** — a `viewMode` widening, a load-all single-source-of-truth rewrite, `counts`/`filtered`/`byStatus` computeds, `activeStatus`/`search`/`filters` state, and the `transition(id, target)` state-machine dispatcher.

**Work branch:** `feature/content-studio-redesign` (off `v2-rebuild`). Web app at `src/PersonalBrandAssistant.Web/`. Tests colocated as `*.spec.ts`.

**Test command:**
```
cd src/PersonalBrandAssistant.Web && npx ng test --watch=false --browsers=ChromeHeadless
```

## Dependencies

- **Requires §01 (foundation):** §01 authored the `:root` CSS custom-property token block. This section's display helpers return CSS variable references like `var(--status-idea)`, `var(--voice-high)` — those tokens MUST already exist. The relevant tokens and their literal values (defined in §01's `_tokens.scss`) are:
  - Status: `--status-idea #8a7df0`, `--status-draft #8a8a96`, `--status-review #c87156`, `--status-approved #4ade80`, `--status-scheduled #60a5fa`, `--status-published #34d399`, `--status-archived #5a5a66`
  - Voice bands: `--voice-high #4ade80`, `--voice-mid #fbbf24`, `--voice-low #f87171`
  - Surfaces/text: `--surface-card`, `--surface-border`, `--text-secondary`, `--text-muted`, fonts `--font-mono`
- **Blocks §03, §04, §05.** Build this fully before they start.

## Background — existing types (do not redefine)

These already exist in `src/PersonalBrandAssistant.Web/src/app/features/content/models/content.model.ts`. Import them; do not re-declare.

```ts
export enum ContentStatus {
  Idea = 'Idea', Draft = 'Draft', Review = 'Review', Approved = 'Approved',
  Scheduled = 'Scheduled', Published = 'Published', Archived = 'Archived',
}
export enum ContentType {
  BlogPost = 'Blog', LinkedInPost = 'LinkedInPost', Tweet = 'Tweet',
  ThreadedTweet = 'ThreadedTweet', SubstackNewsletter = 'SubstackNewsletter',
  RedditPost = 'RedditPost', YouTubeVideo = 'YouTubeVideo', YouTubeShort = 'YouTubeShort',
}
export enum Platform {
  Blog = 'Blog', Medium = 'Medium', Substack = 'Substack', LinkedIn = 'LinkedIn',
  Twitter = 'Twitter', Reddit = 'Reddit', YouTube = 'YouTube',
}
export interface Content {
  id: string; title: string; contentType: ContentType; status: ContentStatus;
  primaryPlatform: Platform; targetPlatforms: Platform[]; voiceScore: number | null;
  tags: string[]; createdAt: string; updatedAt: string; scheduledAt: string | null;
  publishedAt: string | null; platformPublishes: PlatformPublishSummary[];
}
export interface ContentFilterState {
  status?: ContentStatus; platform?: Platform; contentType?: ContentType;
  dateFrom?: string; dateTo?: string; search?: string;
}
```

Existing helpers already in `content-list/content-display.utils.ts` (keep them; you are ADDING to this file): `formatContentType`, `voiceScoreClass`, `platformIconClass`, `truncateText`, `publishStatusSeverity`.

> **NOTE — `voiceScoreClass` vs new `voiceBandColor`:** the existing `voiceScoreClass` uses strict `> 80` for the high band. The new `voiceBandColor` standardizes on `>= 80`. They coexist for now (different return shapes: CSS class string vs CSS var). The editor's old `voiceColor` (strict `>80`) is replaced by `voiceBandColor` downstream in §05 — not your concern here beyond exporting the new function.

The `ContentService` (`services/content.service.ts`) already exposes every state-machine endpoint the dispatcher needs: `draft`, `approve`, `submitForReview`, `requestChanges`, `schedule`, `unschedule`, `publish`, `unpublish`, `restore`, plus `list`, `get`, `delete`.

---

## Tests FIRST

Write these before implementation. Tests are described as behavior; do not over-specify literal assertions beyond what is stated.

### 2.1 — `content-list/content-display.utils.spec.ts` (pure, write first)

```
# Test: STATUS_META has an entry for EVERY ContentStatus value.
#       Each entry's color is a css var ref (string starting with "var(--").
#       order values are 0..n, unique, and increasing along the pipeline
#       (Idea < Draft < Review < Approved < Scheduled < Published; Archived placed last).
# Test: TYPE_GLYPH has an entry for EVERY ContentType value (single-char/Unicode glyph string).
# Test: voiceBandColor(85) -> high band var; voiceBandColor(80) -> high band var
#       (>=80 boundary, NOT >80); voiceBandColor(60) -> mid; voiceBandColor(59) -> low;
#       voiceBandColor(null) -> neutral/empty var.
# Test: relativeTime — a past iso (~2h ago) -> "2h"; a past iso (~3d ago) -> "3d";
#       a future iso (~3d ahead, e.g. a scheduledAt) -> "in 3d"; now -> "just now" (or "0m").
#       (Use a fixed clock — inject/stub Date.now via jasmine.clock() so the test is deterministic.)
# Test: nextStatus(Idea) -> Draft; nextStatus(Draft) -> first legal forward (Review);
#       nextStatus(Approved) -> Scheduled; nextStatus(Published) -> null; nextStatus(Archived) -> null.
# Test: LEGAL_TRANSITIONS — Idea:[Draft]; Draft includes Review AND Approved;
#       Review includes Approved AND Draft; Approved includes Scheduled AND Published;
#       Scheduled includes Approved AND Published; Published includes Approved;
#       Archived supports restore. Every listed target is reachable by a real ContentService endpoint.
```

### 2.2 — atom component specs

`features/content/shared/status-tag.component.spec.ts`:
```
# status-tag.spec: for each ContentStatus input, renders a dot + the status label,
#                  styled with that status's color (STATUS_META color var present in the DOM/style).
```
`features/content/shared/voice-score-ring.component.spec.ts`:
```
# voice-score-ring.spec: score=85 renders "85" text + uses the high band color;
#                        score=null renders a dashed-empty ring (no number);
#                        the size input is respected (svg/element width reflects size).
```
`features/content/shared/platform-dot.component.spec.ts`:
```
# platform-dot.spec: variant='tile' renders a 2-letter mono code for the platform;
#                    variant='dot' renders a colored dot;
#                    color differs correctly per Platform.
```

### 2.3 — `stores/content.store.spec.ts` (extend existing — high value)

Use a spy/stub `ContentService` (provide a fake via TestBed). The store is `{ providedIn: 'root' }`.

```
# Test: loadAll() populates allContents from the (mocked) service; loading toggles true->false.
# Test: counts() returns correct per-status tallies computed from allContents.
# Test: filtered() applies activeStatus + search (matches title AND tags) + popover filters together
#       (intersection semantics — a record must satisfy all active filters).
# Test: byStatus() groups filtered() into columns keyed by ContentStatus.
# Test: setActiveStatus toggles — setting a status filters to it; re-setting the SAME status clears to null.
# Test: setSearch updates search; filtered() reflects it.
# Test: transition(id, legalTarget) calls the CORRECT ContentService endpoint for the (current,target)
#       pair — assert via spy, one transition per case:
#         (Idea->Draft)=draft, (Draft->Review)=submitForReview, (Draft->Approved)=approve,
#         (Review->Approved)=approve, (Review->Draft)=requestChanges,
#         (Approved->Published)=publish, (Scheduled->Approved)=unschedule,
#         (Scheduled->Published)=publish, (Published->Approved)=unpublish,
#         (Archived->restore target)=restore.
# Test: transition(id, illegalTarget) is a no-op (NO service call) + surfaces a user notice.
# Test: transition optimistically patches ONLY status on a NEW array
#       (allContents() reference changes; the patched record's updatedAt is UNCHANGED client-side);
#       on service success the affected record is reloaded (real updatedAt arrives).
# Test: transition rolls back the status patch to the original when the service errors.
# Test: transition does NOT call schedule() for Approved->Scheduled
#       (that path is caller-driven and needs a date — see dispatcher note below).
```

---

## Implementation

### 2.1 Display helpers — `content-list/content-display.utils.ts` (EXTEND)

Add these pure exports to the existing file (keep all existing functions). Values are CSS variable references that resolve against §01's `:root` tokens.

```ts
import { ContentStatus, ContentType } from '../models/content.model';

/** status -> { color: cssVar, label, order } for pills/tags/tracker. order = pipeline rank. */
export const STATUS_META: Record<ContentStatus, { color: string; label: string; order: number }>;
// Idea:      var(--status-idea)      "Idea"       0
// Draft:     var(--status-draft)     "Draft"      1
// Review:    var(--status-review)    "Review"     2
// Approved:  var(--status-approved)  "Approved"   3
// Scheduled: var(--status-scheduled) "Scheduled"  4
// Published: var(--status-published) "Published"  5
// Archived:  var(--status-archived)  "Archived"   6

/** ContentType -> single Unicode glyph for cards/rows. Pick legible glyphs per type. */
export const TYPE_GLYPH: Record<ContentType, string>;

/** numeric voice score -> band color css var. Boundary: >=80 high, >=60 mid, else low; null -> neutral. */
export function voiceBandColor(score: number | null): string;
// score >= 80  -> 'var(--voice-high)'
// score >= 60  -> 'var(--voice-mid)'
// score != null-> 'var(--voice-low)'
// null         -> 'var(--text-muted)'  (neutral/empty)

/** legal forward/backward transitions, derived from the real state-machine endpoints. */
export const LEGAL_TRANSITIONS: Record<ContentStatus, ContentStatus[]>;
// Idea:      [Draft]
// Draft:     [Review, Approved]
// Review:    [Approved, Draft]
// Approved:  [Scheduled, Published]
// Scheduled: [Approved, Published]
// Published: [Approved]
// Archived:  []   (restore is handled specially by transition() — see dispatcher)

/** compact relative time: past -> "2h"/"3d"; future -> "in 3d"; ~now -> "just now". */
export function relativeTime(iso: string): string;

/** next status in pipeline order; null if Published or Archived (no forward move). */
export function nextStatus(current: ContentStatus): ContentStatus | null;
// Idea->Draft, Draft->Review, Review->Approved, Approved->Scheduled,
// Scheduled->Published, Published->null, Archived->null
```

Notes:
- `relativeTime` must read `Date.now()` (so tests can stub via `jasmine.clock()`). Use compact units (m/h/d). "in Xd" only for future timestamps.
- `nextStatus` returns the first/primary legal forward target — derive it from `LEGAL_TRANSITIONS` where sensible (forward = higher `STATUS_META.order`), but the explicit mapping above is the contract.

### 2.2 Atom components — `features/content/shared/`

Create the `shared/` directory. All three are standalone, presentational, inputs-only. No store, no service injection. Use `ChangeDetectionStrategy.OnPush`. Pull colors/labels from the §2.1 helpers.

**`status-tag.component.ts`**
```ts
@Component({ selector: 'app-status-tag', standalone: true, /* OnPush */ })
export class StatusTagComponent {
  @Input({ required: true }) status!: ContentStatus;
  // renders: a colored dot (STATUS_META[status].color) + STATUS_META[status].label
}
```

**`voice-score-ring.component.ts`**
```ts
@Component({ selector: 'app-voice-score-ring', standalone: true, /* OnPush */ })
export class VoiceScoreRingComponent {
  @Input({ required: true }) score!: number | null;
  @Input() size = 40;
  // score != null: SVG/conic ring stroked in voiceBandColor(score) + mono numeric value centered.
  // score == null: dashed-empty ring, no number. Ring diameter = size px.
}
```

**`platform-dot.component.ts`**
```ts
@Component({ selector: 'app-platform-dot', standalone: true, /* OnPush */ })
export class PlatformDotComponent {
  @Input({ required: true }) platform!: Platform;
  @Input() variant: 'dot' | 'tile' = 'dot';
  // 'dot': colored dot per platform.
  // 'tile': 2-letter uppercase mono code (Bl, Me, Su, Li, Tw, Re, Yo / or chosen pair) in a tile.
}
```

Per-platform colors and 2-letter codes are local to this component (a small `Record<Platform, {color; code}>`). Choose brand-recognizable colors; the codes must be unambiguous 2-letter abbreviations per `Platform`.

### 2.3 `ContentStore` extensions — `stores/content.store.ts` (REWRITE state model)

The store today is paged (`contents`/`totalCount`/`page`/`pageSize`/`filters`/`viewMode: 'list'|'grid'`, methods `loadContents`/`setFilter`/`setPage`/`deleteContent`/`toggleView`). Replace the paged machinery with **load-all single source of truth**. Board, grid, AND table all render from `filtered()` — there is no second server-query source that could diverge.

New/changed state:
```ts
type ContentStoreState = {
  allContents: Content[];                  // THE source of truth — full set from loadAll()
  viewMode: 'board' | 'grid' | 'table';    // was 'list' | 'grid'
  activeStatus: ContentStatus | null;      // single-status pill filter
  search: string;                          // client-side, matches title + tags
  filters: Partial<ContentFilterState>;    // platform/type/date from the Filters popover (client-side)
  loading: boolean;
  error: string | null;
};
```

Computeds:
```ts
counts:   Signal<Record<ContentStatus, number>>;      // per-status tallies from allContents
filtered: Signal<Content[]>;                          // allContents ∩ activeStatus ∩ search ∩ popover filters
byStatus: Signal<Record<ContentStatus, Content[]>>;   // groups filtered() by status (board columns)
```

Methods:
```ts
loadAll(): void;            // the ONLY fetch — pull the full set (large pageSize, or a dedicated all-fetch
                           //   via contentService.list({}, 1, BIG)). Toggle loading; map result.items -> allContents.
setActiveStatus(status: ContentStatus | null): void;   // toggle: same status re-clicked -> null
setSearch(term: string): void;                          // store term; input layer debounces 300ms (not here)
setView(mode: 'board' | 'grid' | 'table'): void;
setFilter(filters: Partial<ContentFilterState>): void;  // popover platform/type/date
deleteContent(id: string): void;                        // keep; on success re-run loadAll()
transition(id: string, target: ContentStatus): void;    // state-machine dispatcher — see below
```

**`filtered()` semantics (intersection):** a record is included only if it satisfies ALL active filters:
- `activeStatus` null → no status filter; else `c.status === activeStatus`.
- `search` empty → no search filter; else case-insensitive substring match against `c.title` OR any tag in `c.tags`.
- popover `filters`: if `filters.platform` set → `c.primaryPlatform === filters.platform` (or in `targetPlatforms`); if `filters.contentType` set → match; if `filters.dateFrom`/`dateTo` set → `c.createdAt` within range.

**`transition(id, target)` dispatcher — the critical piece:**

Do NOT use a raw `update({status})`. `UpdateContentRequest` has no `status` field; the backend is a constrained state machine. Status changes MUST go through dedicated endpoints.

Algorithm:
1. Read the card's current status from `allContents` (`const current = record.status`). If no record, no-op.
2. **Special-case `Approved -> Scheduled`:** do NOT fire. `schedule()` needs a date — the CALLER opens the schedule dialog and then calls `ContentService.schedule(id, { scheduledAt })` itself. `transition()` returns a no-op for this pair (no error notice — it's a legal move handled elsewhere).
3. **Special-case restore:** if `current === Archived`, the legal action is `restore()` (target is the restored status). Map appropriately.
4. Validate `target ∈ LEGAL_TRANSITIONS[current]` (plus the restore case). If illegal → no service call, surface a user notice (set `error` and/or notify), return.
5. Map `(current, target)` → the correct `ContentService` method:

   | current → target | endpoint |
   |---|---|
   | Idea → Draft | `draft(id, …)` |
   | Draft → Review | `submitForReview(id)` |
   | Draft → Approved | `approve(id)` |
   | Review → Approved | `approve(id)` |
   | Review → Draft | `requestChanges(id)` |
   | Approved → Published | `publish(id)` |
   | Scheduled → Approved | `unschedule(id)` |
   | Scheduled → Published | `publish(id)` |
   | Published → Approved | `unpublish(id)` |
   | Archived → (restore) | `restore(id)` |

   > `Idea -> Draft` uses `draft(id, request)` which takes a `DraftContentRequest`. Pass the minimal valid request the endpoint requires (verify the request shape in `content.model.ts`). If draft-from-board needs no extra payload, send the minimal object.

6. **Optimistic update:** immediately `patchState` a NEW `allContents` array where the target record has `status: target` and **everything else unchanged**. Critically: do NOT fabricate `updatedAt` — leave it exactly as-is. (`updatedAt` feeds the editor's `lastUpdatedAt` concurrency token; a fabricated value corrupts it.) Building a new array (never mutating in place) is what makes the signals/computeds react and what makes CDK drag-drop safe in §04.
7. On service **success:** reload the affected record (`contentService.get(id)` → real `updatedAt`) and patch that single record into a new `allContents` array. (Reloading the one record is cheaper than a full `loadAll()` and gives the authoritative `updatedAt`.)
8. On service **error:** roll back — patch `allContents` back to the original status for that record (new array) and surface the error notice.

**Concurrency note (documented limitation, not a fix required here):** `ContentStore` and `ContentEditorStore` are independent. A board/drawer transition invalidates an open editor's `lastUpdatedAt` token. The editor reloads on focus (§05); otherwise a concurrent edit + board move may be rejected by the server. No action in this section beyond not fabricating `updatedAt`.

**Removal of paged path:** delete (or quarantine) `loadContents`/`setFilter` (old single-key form)/`setPage`/`totalPages`/`hasNextPage`/`hasPreviousPage`/`page`/`pageSize`/`totalCount`. If any current consumer references them, they will be replaced in §04 — note breakages but the new surface is the contract. `toggleView` is replaced by `setView`. (NOTE: the new `setFilter(filters)` takes a partial object for the popover; if you prefer to keep the old per-key `setFilter<K>(key, value)` signature, expose both clearly — §04 calls `setFilter` with popover values.)

## Files

Create:
- `src/PersonalBrandAssistant.Web/src/app/features/content/shared/status-tag.component.ts` (+ `.spec.ts`)
- `src/PersonalBrandAssistant.Web/src/app/features/content/shared/voice-score-ring.component.ts` (+ `.spec.ts`)
- `src/PersonalBrandAssistant.Web/src/app/features/content/shared/platform-dot.component.ts` (+ `.spec.ts`)

Modify:
- `src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-display.utils.ts` (+ `content-display.utils.spec.ts` — create if absent)
- `src/PersonalBrandAssistant.Web/src/app/features/content/stores/content.store.ts` (+ extend `content.store.spec.ts`)

## Definition of Done

- All §2.1 / §2.2 / §2.3 tests pass via the project test command.
- `voiceBandColor` honors the `>=80` boundary (not `>80`).
- `transition()` dispatches the correct endpoint per `(current, target)`, no-ops illegal targets with a notice, optimistically patches status on a NEW array without fabricating `updatedAt`, reloads the record on success, rolls back on error, and never calls `schedule()`.
- No GitHub-dark hex colors introduced; all colors are `var(--…)` token references.
- Build green: `cd src/PersonalBrandAssistant.Web && npx ng build`.
