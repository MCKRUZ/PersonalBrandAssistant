# section-03-publish-overlay

The bespoke 1080px publish modal plus the markdown adapters that drive it. This is the publish flow shared by both the content list (detail-drawer "Publish →") and the editor action bar. Build it now, before sections 04 and 05, so neither has a forward reference.

## Prerequisites (from earlier sections — do not re-implement)

- **Section 01** added `@angular/cdk@^19.2.0` and verified `marked` (resolved via `ngx-markdown@21.3.0`) exposes `lexer` and `walkTokens`. Authored the `:root` CSS token block (`_tokens.scss`) — use `var(--…)` tokens, never raw GitHub-dark hexes. If `marked` does not expose `lexer`/`walkTokens`, section 01 added an explicit compatible `marked` dep; import `marked` directly (`import { marked } from 'marked'`).
- **Section 02** added the `platform-dot` atom component, plus `content-display.utils.ts` helpers. The destinations rows use `platform-dot` for the platform tile. The store's `transition` dispatcher is not used here — this section calls `ContentService` publish/schedule directly.

All work is on branch `feature/content-studio-redesign` off `v2-rebuild`. Web app: `src/PersonalBrandAssistant.Web/`. Tests colocated `*.spec.ts`. Run with:

```
cd src/PersonalBrandAssistant.Web && npx ng test --watch=false --browsers=ChromeHeadless
```

No backend changes. `signalr.service.ts` untouched.

## Existing types & service methods you depend on

From `src/app/features/content/models/content.model.ts` (already present — do not modify):

```ts
export enum Platform { Blog='Blog', Medium='Medium', Substack='Substack',
  LinkedIn='LinkedIn', Twitter='Twitter', Reddit='Reddit', YouTube='YouTube' }

export enum PublishStatus { Pending='Pending', Formatting='Formatting',
  Published='Published', Failed='Failed' }   // NOTE: no `Scheduled` value

export const PUBLISHABLE_PLATFORMS: readonly Platform[] =
  [Platform.Blog, Platform.Medium, Platform.Substack, Platform.LinkedIn, Platform.Twitter];

export interface ContentDetail extends Content {
  body: string; primaryPlatform: Platform; targetPlatforms: Platform[];
  status: ContentStatus; scheduledAt: string | null; /* … */
}

export interface PlatformPublish {
  platform: Platform; publishStatus: PublishStatus;
  publishedUrl: string | null; publishedAt: string | null;
  retryCount: number; nextRetryAt: string | null;
}

export interface ScheduleContentRequest { scheduledAt: string; }   // NO platforms
export interface PublishRequest { targetPlatforms?: Platform[]; }
export interface PublishStatusResponse {
  contentId: string; primaryPlatform: Platform; platformStatuses: PlatformPublish[];
}
export interface PlatformConnectionStatus {
  platform: Platform; isConnected: boolean; isExpiring: boolean;
  expiresAt: string | null; capabilities: PlatformCapabilities;
}
```

From `src/app/features/content/services/content.service.ts` (already present):

```ts
schedule(id: string, request: ScheduleContentRequest): Observable<void>   // PUT  /api/content/{id}/schedule
publish(id: string, request?: PublishRequest): Observable<void>           // POST /api/content/{id}/publish
getPublishStatus(id: string): Observable<PublishStatusResponse>           // GET  /api/content/{id}/publish-status
retryPlatform(id: string, platform: Platform): Observable<void>           // POST /api/content/{id}/retry/{platform}
getPlatforms(): Observable<PlatformConnectionStatus[]>                     // GET  /api/platforms
```

`PLATFORM_CHAR_LIMITS` exists in the model but is incomplete (`Twitter:280, LinkedIn:3000`). This section replaces its use with `PLATFORM_META.charLimit` (below). Do not delete `PLATFORM_CHAR_LIMITS` (other files may import it) — just don't use it here.

The existing `publish-modal.component.ts` is a 480px GitHub-dark modal that emits `confirm`/`cancel` outputs. It is rewritten in 3.4. The existing `publish-modal.component.spec.ts` is replaced.

> **API-contract caution (cross-section):** the editor (section 05) currently opens `<app-publish-modal>` with `[visible]` / `[content]` / `[connectedPlatforms]` / `[mode]` and `(confirm)`/`(cancel)`. If you change the modal's public inputs/outputs (e.g. having the modal call `ContentService` itself instead of emitting `confirm`), you MUST keep the editor wiring working — coordinate the exact contract with section 05. The load-bearing behavior either way: opens with a `ContentDetail`, drives publish/schedule, renders the result view in-place, closes. Recommended: keep the existing `(confirm)`/`(cancel)` output contract and let the PARENT call publish/schedule, OR move the calls inside and emit a `(closed)` — pick one and make both sections agree.

---

## Tests first

Write the pure-logic specs (3.1–3.3) and watch them fail before any implementation. They carry the business rules and should approach 100% coverage. Then the component specs (3.4–3.6) cover render-by-state and key interactions. 80% minimum on every new file.

### 3.1 `models/platform-metadata.spec.ts` (pure — write first)

```
# Test: PLATFORM_META has an entry per PUBLISHABLE_PLATFORMS with delivery/charLimit/fmt/code/label;
#       Blog auto/null, Medium manual/null, Substack auto/null, LinkedIn auto/3000, Twitter auto/280.
# Test: deliveryBadge(meta, connected) -> "⚡ Auto-publish" variant 'auto';
#       deliveryBadge(autoMeta, !connected) -> "⚡ Connect to auto-publish" variant 'warn';
#       deliveryBadge(manualMeta, *) -> "✋ Manual" variant 'manual'.
```

### 3.2 `content-editor/publish-modal/markdown-blocks.spec.ts` (pure — write first)

```
# Test: toBlocks maps "# H"->h1, "## H"->h2, "### H"->h3, paragraphs->p, in document order.
# Test: plainText strips markdown — "**bold**"->"bold", "[label](url)"->"label" (href excluded),
#       "`code`"->"code"; headings/lists contribute their text only.
# Test: plainText length is the RENDERED length (a 290-char paragraph with **markers** still
#       reports the visible char count, so char/thread budgets are correct).
```

### 3.3 `content-editor/publish-modal/thread-splitter.spec.ts` (pure — write first; boundary-critical)

```
# Test: short text -> single tweet (apply the chosen numbering rule consistently: "1/1" or unnumbered).
# Test: long text -> multiple tweets, each INCLUDING the "i/n" suffix <= 280.
# Test: boundary — a length that would be 280 WITHOUT the suffix splits so the numbered tweet stays
#       <= 280 (suffix budget reserved).
# Test: multi-digit n (e.g. 12 tweets) still keeps every numbered tweet <= 280.
# Test: splits on sentence boundaries when possible (greedy packing).
```

### 3.4 `content-editor/publish-modal/publish-modal.component.spec.ts` (replace existing)

```
# Test: destinations list one row per PUBLISHABLE_PLATFORMS; primary checked+disabled.
# Test: delivery badge + char/thread usage per row driven by plainText(body) and PLATFORM_META.
# Test: selecting destinations updates preview tabs + footer summary "{n} dest · {a} auto · {m} manual".
# Test: "Publish {n}" disabled when none selected, or schedule mode with no datetime.
# Test: schedule mode disables the destinations list (shows the note) — selection ignored.
# Test: confirm in now-mode calls publish(id,{targetPlatforms:selected}); schedule-mode calls
#       schedule(id,{scheduledAt}) with NO platforms.
# Test: a11y — focus trapped within modal; Esc closes; scrim click closes; aria-modal set.
```

### 3.5 `content-editor/publish-modal/previews/*.spec.ts` (×5)

```
# blog-preview:     renders kicker / serif H1 / lede / byline / serif H2 sections from blocks.
# medium-preview:   bold sans H1 + gray subtitle + author row/Follow + clap-bookmark bar.
# substack-preview: masthead (publication + Subscribe) + "to N subscribers" byline + unsubscribe footer.
# linkedin-preview: plainText truncated ~210 chars + "…more"; over-3000 shows a warning.
# twitter-preview:  renders splitThread(plainText,280) as numbered 1/n tweets on the connector rail.
```

### 3.6 `delivery-badge.spec.ts` + `publish-result.spec.ts`

```
# delivery-badge:  pill text/variant matches deliveryBadge() for the auto/connected/manual matrix.
# publish-result:  auto+connected row -> "Publishing…" -> "✓ Published — View ↗" as getPublishStatus resolves;
#                  manual row -> "Ready to post" + ⧉ Copy (clipboard gets platform-formatted body) + Open ↗.
# publish-result:  schedule mode row -> "◴ Scheduled for {datetime}" (frontend-only, NOT a PublishStatus).
# publish-result:  polling stops when all platformStatuses ∈ {Published,Failed}; interval cleared on
#                  destroy; hard cap ~30s -> "still processing" state.
```

---

## Implementation

### 3.1 `models/platform-metadata.ts` (new)

Static design metadata, separate from runtime connection state.

```ts
export type DeliveryMode = 'auto' | 'manual';

export interface PlatformMeta {
  code: string;          // 2-letter tile: "Bl", "Me", "Su", "Li", "Tw"
  label: string;
  delivery: DeliveryMode;
  charLimit: number | null;
  fmt: string;           // human note, e.g. "No publish API — paste into Medium"
}

/** One entry per PUBLISHABLE_PLATFORMS.
 *  Blog: auto / null. Medium: manual / null. Substack: auto / null.
 *  LinkedIn: auto / 3000. Twitter: auto / 280. */
export const PLATFORM_META: Record<Platform, PlatformMeta>;

/** Badge from STATIC delivery + LIVE isConnected:
 *  auto && connected  -> { text:'⚡ Auto-publish',           variant:'auto'  }
 *  auto && !connected -> { text:'⚡ Connect to auto-publish', variant:'warn'  }
 *  manual             -> { text:'✋ Manual',                  variant:'manual'} */
export function deliveryBadge(meta: PlatformMeta, isConnected: boolean):
  { text: string; variant: 'auto' | 'warn' | 'manual' };
```

`PLATFORM_META` only needs entries for the 5 `PUBLISHABLE_PLATFORMS`. Typing it `Record<Platform, PlatformMeta>` would require Reddit/YouTube entries — prefer `Record<(typeof PUBLISHABLE_PLATFORMS)[number], PlatformMeta>` or a `Partial<Record<Platform, …>>` with a non-null assertion at lookup, whichever keeps the lookup clean. Pick one and be consistent.

Connection state comes from `ContentService.getPlatforms()` → `PlatformConnectionStatus.isConnected`. **Never hardcode connection** — the prototype's `connected:false` for Twitter was sample data.

### 3.2 `content-editor/publish-modal/markdown-blocks.ts` (new)

```ts
export type ProseBlock = { type: 'h1'|'h2'|'h3'|'h4'|'h5'|'h6'|'p'; text: string };

/** Parse markdown into ordered prose blocks via marked.lexer.
 *  Heading tokens -> h{depth}; paragraph tokens -> p. Preserve document order. */
export function toBlocks(markdown: string): ProseBlock[];

/** Strip markdown to plain text via marked.walkTokens, concatenating leaf text.
 *  Exclude link hrefs (keep link label) and emphasis/code markers.
 *  Used for char/thread budgets — MUST reflect rendered (visible) length. */
export function plainText(markdown: string): string;
```

Import `marked` as section 01 verified (`import { marked } from 'marked'` or via the ngx-markdown re-export). `marked.lexer(md)` returns the token list for `toBlocks`. For `plainText`, run `marked.lexer` then `marked.walkTokens`, accumulating `token.text` from leaf tokens and skipping `link.href`. The accuracy of this stripper gates every char/thread budget — test it hard (3.2).

### 3.3 `content-editor/publish-modal/thread-splitter.ts` (new)

```ts
/** Greedy, sentence-aware split of plain text into tweets that, INCLUDING the "i/n" suffix,
 *  never exceed `limit` (default 280). Reserve suffix room: per-tweet budget = limit - len(" i/n").
 *  The suffix width depends on n, so compute n, then re-pack with the correct reserved width
 *  (multi-digit n must still keep every numbered tweet <= 280). Returns numbered tweet strings. */
export function splitThread(text: string, limit = 280): string[];
```

The trap: `n` is not known until you've split, but the per-tweet budget depends on `len(" i/n")` which depends on `n`. Compute a first pass, then if the suffix width grew (e.g. 9→10 tweets pushes n from 1 to 2 digits), re-pack with the wider reserved budget until stable. Boundary test (3.3): a long run with 2+-digit `n` keeps every numbered tweet ≤ limit.

### 3.4 `content-editor/publish-modal/publish-modal.component.ts` (rewrite — bespoke)

Replace the existing 480px modal with the bespoke overlay. Standalone component.

**Inputs / outputs.** Keep accepting `content: ContentDetail`, `visible: boolean`, the connection list, and `mode` (the existing modal uses `'publish' | 'schedule'` — keep that literal so section-05 wiring is unchanged unless you coordinate a rename). Keep `(confirm)` / `(cancel)` OR move the publish/schedule calls inside and emit `(closed)` — but make section 05 agree (see the API-contract caution above). The result view renders in-place after confirm.

**Shell.** Scrim (`position:fixed; inset:0`) + centered 1080px card, `.24s` pop-in animation (honor the global reduced-motion media query from section 01 — no transform when reduced). All colors via `var(--…)` tokens.

**Header.** Serif title "Publish" (now mode) / "Schedule" (schedule mode) + the content title.

**Body grid `340px / 1fr`:**

- **Left — destinations.** One row per `PUBLISHABLE_PLATFORMS`:
  - Checkbox. Primary platform (`content().primaryPlatform`) is checked **and disabled**.
  - `platform-dot` tile (section 02 atom) for the platform.
  - Name + "Primary" badge on the primary row.
  - `meta.fmt` note.
  - Right stack: `delivery-badge` (3.6) + usage. Usage = `plainText(body).length` vs `meta.charLimit` for char-capped platforms; for Twitter, the thread count via `splitThread(plainText(body), 280).length`.
  - Selected rows: border `var(--brand-primary)`, background `var(--accent-soft)`.
- **"When" section (left, below destinations):** "Publish now" vs a `datetime-local` input (schedule mode; set `color-scheme: dark` so the native picker matches).
- **Right — preview.** Tabs, one per *selected* platform. Sticky caption above the preview (derived from delivery + connection: "auto deploys" / "you post this" / "connect to auto-deploy"). The active preview component renders on a white card (3.5).

**Local component signals:**

```ts
selected = signal<Platform[]>([]);      // secondary picks; primary always included
activeTab = signal<Platform>(...);      // currently previewed platform
mode  = input<'publish'|'schedule'>('publish');
scheduledAt = signal<string|null>(null);
result = signal<...|null>(null);        // set after confirm -> swaps body to publish-result
```

**Schedule-mode caveat (critical).** `ScheduleContentRequest` is `{ scheduledAt }` only — the backend schedule takes **no platforms**. So in schedule mode:
- Render the destinations list **disabled** with a note: "Scheduling applies to the whole post; per-platform selection only affects immediate publish."
- Per-platform selection is meaningful only for "Publish now".

**Footer.** Summary "{n} destinations · {a} auto · {m} manual" (counts from `selected` + `PLATFORM_META.delivery`). Cancel + "Publish {n} →". Disable the confirm button when no destination is selected, or in schedule mode with no datetime.

**Confirm routing:**
- now mode → `contentService.publish(id, { targetPlatforms: selected })` (or emit the existing `confirm` payload), then render the publish-result view.
- schedule mode → `contentService.schedule(id, { scheduledAt })` (**no platforms**), then render the scheduled result row.

**Modal a11y (must replicate — bespoke modal has no PrimeNG focus management):**
- Focus-trap inside the modal (CDK `cdkTrapFocus` from `@angular/cdk/a11y`, added in section 01, is the clean path).
- `Esc` closes.
- Scrim click dismisses (stop propagation on the card so inner clicks don't close).
- `role="dialog"` + `aria-modal="true"` on the card.

### 3.5 Preview renderers — `content-editor/publish-modal/previews/` (new ×5)

Each takes `@Input() blocks: ProseBlock[]` plus title / derived subtitle / byline as inputs, and renders on a white card to the prototype spec. Subtitle/byline are **presentation-only** — never written back to `UpdateContentRequest`.

- `blog-preview.component.ts` — striped hero, kicker, serif H1, lede, byline, serif H2 sections.
- `medium-preview.component.ts` — bold sans H1, gray subtitle, author row + Follow, clap/bookmark bar.
- `substack-preview.component.ts` — masthead (publication + Subscribe), "to N subscribers" byline, unsubscribe footer.
- `linkedin-preview.component.ts` — feed card; `plainText` truncated ~210 chars + "…more"; reaction/comment/repost bar; respect the 3000 cap (warn if over).
- `twitter-preview.component.ts` — `splitThread(plainText(body), 280)` rendered as numbered `1/n` tweets on a threaded connector rail.

Exact CSS values: `claude-research.md §A` is the design source of truth. Use `var(--…)` tokens for the chrome; the preview *cards themselves* are deliberately white (they mimic the target platforms).

### 3.6 `delivery-badge.component.ts` + `publish-result.component.ts` (new)

**`delivery-badge.component.ts`** — `@Input() meta: PlatformMeta; @Input() isConnected: boolean`. Renders a styled pill from `deliveryBadge(meta, isConnected)`; class keyed off the returned `variant` (`auto`/`warn`/`manual`) → the `.delivery-badge--*` classes from section 01.

**`publish-result.component.ts`** — per-destination rows after confirm:
- auto + connected → "Publishing…" (spinner) → "✓ Published — View ↗" (link = `publishedUrl`). On `Failed`, show retry → `contentService.retryPlatform(id, platform)`.
- manual → "Ready to post" + "⧉ Copy" (clipboard gets the platform-formatted body via the same `plainText`/`splitThread` transforms) + "Open {platform} ↗".
- schedule mode → "◴ Scheduled for {datetime}".
- Footer note: manual destinations have no publish API.

**Two distinct "status" concepts — keep them separate:**
- The per-platform spinner / ✓ / retry is driven by `PlatformPublish.publishStatus` (`Pending | Formatting | Published | Failed` — there is **no** `Scheduled` value in that enum).
- The "◴ Scheduled" row is a **frontend-only** state derived from the modal's schedule mode + the content-level `Content.status === Scheduled` / `scheduledAt`. Never read it from a `PublishStatus`.

**Polling cadence (define exactly — don't leave to guess):**
- Poll `contentService.getPublishStatus(id)` every **2s**.
- Stop when every `platformStatuses[].publishStatus ∈ { Published, Failed }`.
- Hard cap ~**30s**, then show a "still processing" state.
- Clean up the interval on modal close / `ngOnDestroy` (use `takeUntilDestroyed` or clear the timer explicitly).

---

## Files

New:
- `src/app/features/content/models/platform-metadata.ts` (+ `.spec.ts`)
- `src/app/features/content/content-editor/publish-modal/markdown-blocks.ts` (+ `.spec.ts`)
- `src/app/features/content/content-editor/publish-modal/thread-splitter.ts` (+ `.spec.ts`)
- `src/app/features/content/content-editor/publish-modal/delivery-badge.component.ts` (+ `.spec.ts`)
- `src/app/features/content/content-editor/publish-modal/publish-result.component.ts` (+ `.spec.ts`)
- `src/app/features/content/content-editor/publish-modal/previews/blog-preview.component.ts` (+ `.spec.ts`)
- `src/app/features/content/content-editor/publish-modal/previews/medium-preview.component.ts` (+ `.spec.ts`)
- `src/app/features/content/content-editor/publish-modal/previews/substack-preview.component.ts` (+ `.spec.ts`)
- `src/app/features/content/content-editor/publish-modal/previews/linkedin-preview.component.ts` (+ `.spec.ts`)
- `src/app/features/content/content-editor/publish-modal/previews/twitter-preview.component.ts` (+ `.spec.ts`)

Modified:
- `src/app/features/content/content-editor/publish-modal/publish-modal.component.ts` (bespoke rewrite)
- `src/app/features/content/content-editor/publish-modal/publish-modal.component.spec.ts` (replace)

## Risks

1. **`marked` plain-text accuracy** — every char/thread budget depends on `plainText`. Unit-test it against bold/link/code/heading inputs (3.2) before trusting the budgets.
2. **`splitThread` suffix-width feedback loop** — n's digit count changes the per-tweet budget. Re-pack until stable; the multi-digit boundary test (3.3) is the gate.
3. **Static vs live platform data** — delivery / charLimit / fmt are static (`PLATFORM_META`); `isConnected` is live (`getPlatforms`). Never conflate.
4. **Bespoke modal a11y** — no PrimeNG to lean on. Focus-trap, Esc, scrim-dismiss, `aria-modal` must all be hand-wired and tested (3.4).
5. **Schedule sends no platforms** — easy to accidentally forward `selected` into the schedule call. The 3.4 test asserts schedule-mode confirm sends `{scheduledAt}` only.
6. **Modal API contract** — section 05 opens this modal; if you change its inputs/outputs, keep the editor wiring working (coordinate; default = preserve `(confirm)`/`(cancel)`).

---

## IMPLEMENTATION NOTES (actual — as built)

- **Pure logic** (`platform-metadata.ts`, `markdown-blocks.ts` toBlocks/plainText via marked@17
  `lexer`, `thread-splitter.ts` with the ` i/n` suffix re-pack loop) — built + unit-tested first;
  14 pure-logic specs green. Reviewer verified the splitter terminates and never exceeds the limit,
  and the markdown stripper doesn't double-count.
- **5 preview components** (blog/medium/substack/linkedin/twitter) built by a subagent; cards use
  intentional light/white platform-mock colors (NOT a token violation). 10 specs green.
- **`delivery-badge`** + **`publish-result`** (presentational result rows: auto Publishing→Published
  + Retry, manual Copy/Open, scheduled) built + tested.
- **`publish-modal` rewrite**: bespoke 1080px modal (HTML+SCSS templates), 340px/1fr grid,
  destinations (primary checked+disabled, platform-dot tile, delivery badge, char/thread usage),
  when-section (schedule disables destinations), preview tabs → 5 previews, footer summary, result
  view, `cdkTrapFocus`/Esc/scrim. Injects `ContentService` for `getPublishStatus` polling (2s, 30s
  cap) + `retryPlatform`. **Contract PRESERVED** (`visible`/`content`/`connectedPlatforms`/`mode` in;
  `confirm({platforms,scheduledAt?})`/`cancel` out) so the editor keeps compiling.
- **CRITICAL fix during review**: the editor closed the modal on `confirm`, making the result view +
  polling dead code. Removed the immediate `publishModalVisible.set(false)` in
  `content-editor.component.ts:onPublishConfirm` (the ONE small editor change made in this section) so
  the modal stays open to show the result view; it closes via the result view's "Done" → `cancel`.
  Also: stop polling when hidden (effect else-branch); a11y `aria-labelledby` + datetime label.
- **Tests:** 38 section-03 specs; full suite 452 pass / 1 pre-existing unrelated fail (ContentCard,
  section 04). `ng build` clean. Review: see `implementation/code_review/section-03-review.md`.
- **Section 05 note:** the modal already owns the result view + polling. When section 05 rewrites the
  editor, keep the modal mounted after `confirm` (do not auto-close) and close on `cancel`/Done.

