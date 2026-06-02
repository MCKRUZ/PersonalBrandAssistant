# Content Studio Redesign — Synthesized Specification

Synthesis of the initial spec (`spec.md`), research (`claude-research.md`), and interview
(`claude-interview.md`). This is the authoritative requirements document for `claude-plan.md`.

---

## 1. Objective
Recreate the Content Studio redesign from `PBAv2/design_handoff_content_studio/` inside
`src/PersonalBrandAssistant.Web` using the app's established patterns: Angular 19 standalone
components, `@ngrx/signals` signalStore state, PrimeNG 20, SCSS partials in `src/styles/`.
High fidelity to the handoff (colors/typography/spacing/radii/interactions are final). The
`prototype/*.jsx` files are React design references — **recreate in Angular, never copy React**.
Reuse existing `ContentStore`, `ContentEditorStore`, `ContentService`, `SignalRService`.

## 2. Settled decisions (do not revisit)
1. **Markdown is the source of truth.** `ContentDetail.body` stays a markdown string. The editor
   prose surface uses **TipTap (ProseMirror)** with markdown in/out — clean prose, no visible
   syntax, no free HTML. subtitle/byline are **derived (presentation-only), not persisted**.
   Publish pipeline untouched.
2. **List loads all content client-side.** Per-status counts + filtering + search + board
   grouping all computed in-memory. No backend change.
3. **Status pipeline pills are the primary filter.** platform/type/date demoted into a PrimeNG
   **`p-popover`** "Filters" menu (kept, not dropped).
4. **Overlays:** publish modal = **bespoke** token-styled (pixel-exact); detail drawer =
   **PrimeNG `p-drawer`** position=right; Filters = **PrimeNG `p-popover`**.
5. **Thread split limit = 280** (reserve room for the `1/n` suffix so numbered tweets stay ≤280).
6. **Delivery & sequencing:** one branch `feature/content-studio-redesign` off `v2-rebuild`;
   foundation-first; one PR (or stacked commits) at the end.
7. **No backend/API/EF changes. Do not touch `signalr.service.ts`.**

## 3. Versions / dependencies
- Angular `^19.2.0`, PrimeNG `^20.4.0`, `@ngrx/signals ^21.1.0`, `ngx-markdown ^21.3.0`,
  `@microsoft/signalr ^10.0.0`, `primeicons ^7.0.0`, `primeflex ^4.0.0`.
- **Add:** `@angular/cdk: ^19.2.0` (match Angular major — for board DragDrop).
- **Add:** TipTap — `@tiptap/core`, `@tiptap/starter-kit`, `@tiptap/pm`, and a markdown
  serialization extension (`tiptap-markdown` or equivalent). Verify versions support Angular's
  build (ESM) and round-trip the marks used (headings/bold/italic/lists/links/code).
- **marked:** use the copy pulled transitively by ngx-markdown (`import { marked } from 'marked'`);
  add explicit `marked` dep only if the resolved version lacks `lexer`/`walkTokens`.

## 4. Prerequisite: styling foundation (FIRST work, blocks everything)
Today `angular.json` builds `src/styles.scss`, which is GitHub-dark and imports none of the
`styles/` partials; **no `--brand-*`/`--surface-*`/`--accent-soft` CSS custom properties exist**,
so component `var(--…)` references resolve to nothing.
1. Author a `:root` CSS custom-property token block (new `src/styles/_tokens.scss` or `:root{}` in
   `_variables.scss`) exposing `--brand-primary/-hover/-active`, `--surface-base/card/elevated/
   hover/border/disabled`, `--surface-sidebar #0b0b0d`, `--surface-inset #0c0c0e`,
   `--surface-publish-canvas #08080a`, `--text-primary/secondary/muted`, `--accent-soft
   rgba(200,113,86,.13)`, status colors (incl. `--status-idea #8a7df0`, `--status-draft #8a8a96`,
   `--status-published #34d399`), voice bands (`#4ade80`/`#fbbf24`/`#f87171`), delivery badge
   pairs (auto `#1f3a2b`/`#4ade80`, manual `#3a2f1c`/`#fbbf24`, warn `#3a2420`/`#f0935f`), radius
   scale (`--r 12`, `--r-inner 10`, `--r-control 8`, `--r-pill 99`, `--r-modal 16`), and `--font-*`.
2. Wire the `styles/` partials into `src/styles.scss`; replace the GitHub-dark body/scrollbar with
   token-driven values. Optionally re-point PrimeNG `--p-*` overrides at the new tokens.
3. Add missing SCSS `$vars` to `_variables.scss`; `$sidebar-width` 200→212.
4. Recolor the content feature (~90 hardcoded GitHub-dark hexes across 10 files) + sidebar (11) to
   `var(--…)`. Add the sidebar footer user block. Reconcile the app grid to `212px 1fr`.

Acceptance: a redesigned component using `var(--surface-card)` etc. renders terracotta-on-obsidian;
no GitHub-dark hexes remain in the content feature or sidebar; `ng build` clean.

## 5. Shared atoms (used across flows)
New standalone presentational components + display helpers (extend `content-display.utils.ts`):
- `status-tag` (dot + label, status color), `voice-score-ring` (ring + mono value or dashed empty),
  `platform-dot` (colored 2-letter code tile / dot).
- Display maps: status→{color,label,order}, type→glyph, voice→band color, relative-time formatter,
  next-status(order) helper.
- `ContentStore` additions: `viewMode: 'board'|'grid'|'table'`; `loadAll()`; computed per-status
  `counts`; `activeStatus` signal + toggle; client-side `search` (title+tags, 300ms debounce);
  **`setStatus(id, status)`** optimistic mutation (copy array, patch status + updatedAt, call
  `ContentService.update(id, {status,…})`, roll back on error).

## 6. Flow 1 — Content list (`/content`)
- **Header:** serif H1 "Content Studio" + subtitle "{n} pieces moving through your pipeline" +
  primary "+ New Content" (routes `/content/new`). Search input (client title+tags, 300ms).
  Board/Table view toggle (segmented).
- **Pipeline bar:** All pill + per-status pills (dot + label + mono count chip from computed
  counts). Selected pill takes status color; toggles single-status filter (re-click clears).
  Replaces the checkbox sidebar as primary filter. Empty statuses at .5 opacity.
- **Filters popover (`p-popover`):** platform/type/date filters relocated here.
- **Board (CDK kanban, primary view):** columns per status (286px, bg #0c0c0e), header dot+name+
  count, dashed "Drop here" empty target (min-height so it accepts drops), cards draggable via
  `cdkDropListGroup`/`cdkDropList`/`cdkDrag`. Columns derive from a `computed()` grouping;
  `(cdkDropListDropped)` handler calls `ContentStore.setStatus` (do NOT mutate the computed array).
  Card: type glyph + uppercase type label + voice ring; title; mono tag chips; footer platform
  dots + mono updated/scheduled time (`◴ in 3d`).
- **Table (refined):** card-wrapped; columns Status·Title(+tag line)·Type·Platforms·Voice·Updated;
  drop the Actions column; status-tag pill (not dot), voice ring; row click → detail drawer.
- **Detail drawer (`p-drawer` right, 400px):** status tag + close; type label, serif title, meta
  list (Voice ring / Platforms / Updated|Scheduled / Tags), body preview; footer "Open in editor"
  (ghost → `/content/:id`) + context action ("Publish →" if Approved/Scheduled else
  "Move to {nextStatus} →" via `setStatus`).
- **Empty state (inspire):** mark tile + serif H2 "Your studio is quiet." + idea-suggestion cards
  (→ `/content/new` pre-seeded) + "+ Start from scratch". Filtered-empty variant: "Nothing matches
  that filter" + clear.

## 7. Flow 2 — Editor (`/content/:id`, `/content/new`)
- **Layout:** column — top bar (58px) → body (manuscript scroll + optional 340px side panel) →
  action bar. **Remove the `p-splitter` 50/50 markdown view; delete `markdown-editor` component**
  (only `content-editor.component.ts` imports it; verify `@acrodata/code-editor`/`@codemirror/*`
  have no other users before removing from package.json).
- **Top bar:** "← Studio" back; **stage tracker** (6 dots Idea→Published joined by lines; completed
  filled var(--text-3); current enlarged 12px + status color; map 7-value enum → 6 visible stages,
  handle Archived/Scheduled); type·platform meta; "Saved" mono indicator (reuse `save-indicator`);
  voice ring 32px; "✦ Assistant" toggle when panel closed.
- **Manuscript surface (TipTap):** centered max-680px, padding `46px 32px 120px`. Tag chips →
  editable title (DM Serif Display 40px → `updateField('title')`) → derived subtitle (19px,
  presentation-only) → byline → TipTap prose body (17.5px/1.75, #dcdce2, h2 serif 25px). On change:
  serialize doc→markdown, debounced autosave via existing component `scheduleAutoSave` (3s,
  Idea/Draft/Review only) → `updateField('body')` + `autoSave()`. Honor `canEdit()` (Approved+
  read-only). Idea state: dashed "This is still just an idea." panel + Start draft.
- **Side panel (340px, #0c0c0e):** **Voice meter** — label + big mono value colored by band
  (≥80 #4ade80 / ≥60 #fbbf24 / else #f87171) + track/fill bar + band note (from
  `VoiceCheckResult.feedback`); shows `voiceScore` on load, can call `voiceCheck(id)` on demand.
  **AI Assistant** = restyled `sidecar-chat` embedded inline (drop the `p-drawer` chrome): assistant
  bubbles var(--elev)+border, user bubbles brand bg; swap shimmer → 3-dot "blink" thinking;
  quick-action chips (Draft from idea/scratch when empty; Refine/Shorten/Expand/Change tone with
  body); input+send; replies expose ✓ Apply to draft / ⧉ Copy. **SignalR transport unchanged.**
- **Action bar:** left "Targets" + platform dots (reuse `platform-targets`); right = existing
  `@switch(status)` buttons — keep all handlers (`onStartDraft`/`onApprove`/`onSubmitForReview`/
  `onRequestChanges`/`onSchedule`/`onPublish`/`onUnschedule`/`onUnpublish`/`onRestore`), restyle
  only. Approved/Scheduled "Publish" opens the new publish modal.

## 8. Flow 3 — Publish overlay (bespoke modal)
- Bespoke 1080px modal (max-height 90vh, radius 16px, scrim rgba(0,0,0,.66), .24s pop-in;
  honor reduced-motion). Header serif "Publish"/"Schedule" + content title.
- **New static metadata** `models/platform-metadata.ts`: `PLATFORM_META` record per platform —
  `delivery:'auto'|'manual'`, `charLimit`, `fmt`, `code`, `label`. Values: Blog auto/—, Medium
  **manual**/—, Substack auto/—, LinkedIn auto/3000, Twitter auto/280. **Connection from live
  `getPlatforms()` `isConnected`, NOT hardcoded.** Badge: auto&connected→"⚡ Auto-publish";
  auto&!connected→"⚡ Connect to auto-publish"; manual→"✋ Manual".
- **Body grid 340px/1fr.** Left = destinations (checkbox; primary checked+disabled; code tile;
  name + Primary badge; fmt note; delivery badge + char/thread usage; selected row border brand +
  bg accent-soft) + "When" (Publish now vs `datetime-local`, color-scheme dark). Right = preview
  tabs per selected platform + sticky caption + **rendered preview on white card**.
- **Markdown adapters** (`markdown-blocks.ts`): `toBlocks(md)` via `marked.lexer` → `{type:'h1'..'h6'|'p',text}[]`;
  `plainText(md)` via `marked.walkTokens` concatenating leaf text (exclude link hrefs/markers) for
  char/thread budgets. `thread-splitter.ts`: `splitThread(plainText, limit=280)` reserving the
  `i/n` suffix length; sentence-based greedy packing.
- **5 preview renderers** (consume the same body): Blog (striped hero/kicker/serif H1/lede/byline/
  serif H2), Medium (bold sans H1/gray subtitle/author+Follow/clap-bookmark bar), Substack (masthead
  + Subscribe + "to N subscribers" + unsubscribe footer), LinkedIn (feed card, body truncated ~210
  chars "…more", reaction bar, 3000 cap), Twitter (numbered `1/n` thread ≤280, connector rail).
- **Footer:** "{n} destinations · {a} auto · {m} manual" + Cancel + "Publish {n} →" (disabled if
  none selected, or schedule with no datetime). Confirm calls existing `ContentService.publish` /
  `schedule`.
- **Result view:** per destination — auto+connected → "Publishing…" → "✓ Published — View ↗" (poll
  `getPublishStatus`/`retryPlatform`); manual → "Ready to post" + ⧉ Copy (platform-formatted body
  via same transforms) + Open {platform} ↗; schedule → "◴ Scheduled for {datetime}" (frontend-only
  state from mode+`scheduledAt`; backend `PublishStatus` has no Scheduled). Footer note explains
  manual destinations have no publish API.

## 9. Risks / watch-items
- **TipTap markdown round-trip fidelity** (community ext) — include a round-trip stability test for
  the marks used; document fallback if a mark drifts.
- **CDK + signals** — never mutate a `computed()` array; drop handler mutates the store via
  `setStatus`, grouping re-derives.
- **marked plain-text accuracy** drives char/thread budgets — unit-test the stripper (links,
  emphasis, code) so LinkedIn 3000 / Twitter 280 counts are correct.
- **Removing `markdown-editor`** — confirm no other importer + orphaned codemirror deps before
  deleting from package.json.
- **Live vs static platform data** — delivery/charLimit/fmt static; isConnected live.
- **Load-all** scales fine now; revisit if content volume grows large.

## 10. Testing (TDD)
Colocated `*.spec.ts` (Jasmine/Karma, `ng test`). Highest-value unit tests: `thread-splitter`,
`markdown-blocks` (toBlocks + plainText), next-status/status-order, delivery-badge logic, voice
band, `ContentStore.setStatus` optimistic+rollback, client search/counts computeds. Component
tests: render-by-state (board columns, empty/inspire/filtered, drawer actions, each of the 5
previews, editor idea vs draft state, action-bar per status). 80% coverage on new code.
