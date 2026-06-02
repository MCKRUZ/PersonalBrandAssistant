# Content Studio Redesign — Spec

## Goal
Recreate the **Content Studio** redesign from the design handoff at
`PBAv2/design_handoff_content_studio/` inside the existing Angular app
`src/PersonalBrandAssistant.Web` (Angular standalone components, `signal()`/`computed()`
state, PrimeNG, SCSS partials in `src/styles/`). The handoff `README.md` is the
authoritative visual/behavior spec. The files in `prototype/*.jsx` are
**HTML/React design references only — do NOT copy them or introduce React.** Map each
prototype component to its Angular equivalent and build it with the app's established
patterns and existing stores/services (`ContentStore`, `ContentEditorStore`,
`ContentService`, `SignalRService`).

**Fidelity:** high. Colors, typography, spacing, radii, and interactions in the handoff
are final. Where the prototype hardcodes a hex, prefer the matching SCSS token / CSS
custom property.

## Scope: three flows
1. **Content list (`/content`)** — pipeline status bar + draggable kanban board (Angular
   CDK DragDrop) + detail slide-over drawer + "inspire" empty state + refined table.
2. **Editor (`/content/:id`, `/content/new`)** — a single clean prose writing surface that
   **replaces the 50/50 raw-markdown `p-splitter`** and **retires the `markdown-editor`
   component**; stage tracker; side-panel voice meter; restyled inline AI sidecar; reuse
   the existing `@switch(status)` action-bar logic.
3. **Publish overlay** — 1080px modal: destinations panel with delivery badges, 5
   per-platform rendered previews (Blog/Medium/Substack/LinkedIn/Twitter), schedule, and a
   post-publish result view. Auto-publish vs manual distinction.

## LOCKED DECISIONS (frontend-only — confirmed with user 2026-06-02)
These are settled. Do not re-litigate them in the interview.

1. **Markdown stays the source of truth.** `ContentDetail.body` remains a single markdown
   string. The editor is a **markdown-aware prose surface** (NOT free-form HTML). Title is
   the existing `title` field; **subtitle and byline are DERIVED (presentation-only), not
   persisted** — no new model fields, no backend change. Previews parse markdown to blocks
   (`marked.lexer`) for prose rendering and **strip markdown to plain text** for char/thread
   budgets (LinkedIn 3000, Twitter 280). The **publish pipeline is untouched** — it keeps
   consuming markdown `body`.
2. **List loads all content client-side.** Replace server-paginated 20/page with a load-all
   fetch; compute per-status counts (for pipeline bar + board columns) and run all
   filtering/search/board grouping in-memory via `computed()`. No backend change.
3. **Secondary filters kept behind a "Filters" popover.** Status pipeline pills become the
   primary filter. Platform/type/date filters are demoted into a secondary "Filters"
   affordance (popover), not dropped.

## PREREQUISITES (must land before component work)
1. **Token system is currently dead.** `src/styles/_variables.scss` has the correct
   terracotta-on-obsidian tokens but **is never imported** — `angular.json` builds
   `src/styles.scss`, which is still GitHub-dark (`body { background:#0f1117 }`) and
   `@use`s none of the `styles/` partials. There are **no `--brand-*`/`--surface-*`/
   `--accent-soft` CSS custom properties anywhere** (only SCSS `$vars`), so any redesigned
   component written against `var(--surface-hover)` resolves to nothing today.
   **Action:** wire the partials into `src/styles.scss` and author a `:root` CSS
   custom-property token block exposing `--brand-*`, `--surface-*`, `--text-*`,
   `--accent-soft`, `--radius-*`, `--font-*`. Add missing tokens: `--accent-soft
   rgba(200,113,86,0.13)`; sidebar `#0b0b0d` / inset `#0c0c0e` / publish-canvas `#08080a`;
   status `Idea #8a7df0`, `Draft #8a8a96` (contrast), fix `Published #34d399`; delivery
   badge color pairs; radius scale (cards 12 / inner 10 / controls 8 / pills 99 / modal 16);
   `$sidebar-width` 200→212.
2. **Add `@angular/cdk` as an explicit dependency** (currently only transitive via PrimeNG)
   for board DragDrop.
3. **Add a store status-mutation method** (e.g. `ContentStore.setStatus(id, status)`):
   optimistic patch of `contents()` (new array, set `status` + bump `updatedAt`), call
   `ContentService.update(id, { status })`, roll back on error. Needed for board drag-drop
   and the drawer "Move to {nextStatus}" action.
4. **Replace hardcoded GitHub-dark hexes with tokens** across the content feature
   (~90 occurrences in 10 files) + sidebar (11) once `:root` tokens exist. (Repo-wide there
   are ~247 across 34 files; only content + sidebar are in scope for this redesign.)

## GAP ANALYSIS (completed 2026-06-02 — current state vs spec)

### Already in good shape (reuse, mostly restyle)
- Component folder structure matches the spec's component map nearly 1:1.
- Editor `@switch(status)` action-bar logic + all handlers exist and are reusable:
  `onStartDraft`, `onApprove`, `onSubmitForReview`, `onRequestChanges`, `onSchedule`,
  `onPublish`, `onUnschedule`, `onUnpublish`, `onRestore`, `doStatusAction()`. Restyle only.
- Sidecar chat fully functional: SignalR streaming (`tokens$`/`generationComplete$`/
  `generationError$` on `signalr.service.ts`, hub `/hubs/content`), quick-action chips,
  Apply/Copy. Restyle only — swap shimmer→3-dot thinking, move from `p-drawer` to inline
  340px panel. **Do not touch `signalr.service.ts`.**
- Services exist: `publish(id, {targetPlatforms?})`, `schedule(id, {scheduledAt})`,
  `getPlatforms()` → `PlatformConnectionStatus[]` (drives live `isConnected`),
  `getPublishStatus(id)`, `retryPlatform`. Backend models per-platform `publishStatus` +
  `publishedUrl` — use these to drive a real result view, not a fake timer.
- All 3 Google Fonts already loaded in `src/index.html`. Nav items already match spec list.

### Content list — gaps
- `viewMode` is `'list' | 'grid'` today; extend to `'board' | 'grid' | 'table'`.
- **Pipeline bar:** MISSING. New component — All pill + per-status pills (dot + label + mono
  count chip), selected pill takes status color, toggles single-status filter.
- **Board + DnD:** MISSING. New `content-board` — flex columns per status (286px, bg
  `#0c0c0e`), header dot+name+count, dashed "Drop here" empty target, cards draggable via
  **Angular CDK** (`cdkDropList`/`cdkDrag` + `transferArrayItem`). Prototype uses native
  HTML5 DnD — do NOT transliterate; use CDK. On drop → `setStatus` (optimistic).
- **Detail drawer:** MISSING. New right slide-over (400px) + scrim: status tag, serif title,
  meta list (Voice ring / Platforms / Updated|Scheduled / Tags), body preview; footer
  "Open in editor" (ghost) + conditional "Publish →" (Approved/Scheduled) else
  "Move to {nextStatus} →".
- **Inspire empty state:** MISSING (only a plain "No content found" stub). Centered mark tile
  + serif H2 "Your studio is quiet." + clickable idea-suggestion cards (open editor
  pre-seeded) + "+ Start from scratch". Plus filtered-empty variant ("Nothing matches that
  filter" + clear).
- **Table:** PARTIAL. Hand-rolled div table exists; restyle to card-wrapped, drop the Actions
  column, use status-tag pill (not dot), add tag chips under title, voice **ring** (not dot),
  row-click → drawer.
- **Header:** PARTIAL. Has H1 + New Content + search; add subtitle "{n} pieces moving through
  your pipeline"; replace icon-only grid/list toggle with a labeled Board/Table toggle.
- **Store:** no `activeStatus` signal (status lives in `filters.status`), no per-status counts,
  no `setStatus`. Search currently server-side via `setFilter('search')`; move to client-side
  title+tags match (debounce 300ms) given load-all decision.
- Suggested shared atoms (from prototype `studio-parts.jsx`): `status-tag`,
  `voice-score-ring`, `platform-dot` components; shared display maps in
  `content-display.utils.ts` (status color/label, type glyph, voice ring color, relative time).

### Editor — gaps
- **Current surface:** `p-splitter` 50/50 — left `MarkdownEditorComponent` (CodeMirror via
  `@acrodata/code-editor` + `@codemirror/lang-markdown`), right `ngx-markdown` preview. No
  contenteditable/prose surface exists. `MarkdownEditorComponent` is imported ONLY by
  `content-editor.component.ts` — safe to delete after replacement (also orphans
  `@acrodata/code-editor` + `@codemirror/*` — verify before removing from package.json).
- **Manuscript surface:** MISSING (biggest gap). Centered max-680px: tag chips → editable
  title (`DM Serif Display` 40px) → derived subtitle (19px) → byline → body prose
  (17.5px/1.75, h2 serif 25px). Markdown-aware editing; persist title/body via
  `ContentEditorStore.updateField` + existing autosave (3s debounce, lives in the component's
  `scheduleAutoSave`, fires for Idea/Draft/Review only). Idea state → dashed
  "This is still just an idea." panel + Start draft.
- **Stage tracker:** MISSING. 6 dots Idea→Published joined by lines; completed filled
  `--text-muted`, current enlarged + status color. Enum has 7 values (incl. Archived) → map
  to 6 visible stages (handle Archived/Scheduled).
- **Top bar (58px):** "← Studio" back (MISSING), tracker, type·platform meta, "Saved" mono
  indicator (exists as `save-indicator` span — restyle), voice ring 32px (exists as `p-knob`
  size 48 with band colors — resize/move), "✦ Assistant" toggle when panel closed.
- **Side panel (340px, bg `#0c0c0e`):** voice meter MISSING (only top-bar knob today) — label
  + big mono value colored by band (≥80 `#4ade80` / ≥60 `#fbbf24` / else `#f87171`) +
  track/fill bar + note keyed to band; wire to `Content.voiceScore` (from API on load) and
  `VoiceCheckResult.feedback` (type exists but unused — note: no live re-check mechanism
  exists, meter reflects last loaded score). AI Assistant = restyled `sidecar-chat` embedded
  inline (assistant bubbles `--surface-elevated`, user bubbles brand bg, 3-dot thinking,
  quick-action chips, input+send, Apply/Copy).
- **Action bar:** PARTIAL. `@switch(status)` footer exists — keep logic, restyle buttons;
  left side = "Targets" label + platform dots (reuse `platform-targets` strip).
- **Store API (verified, `content-editor.store.ts`):** `updateField<K>(field, value)`,
  `autoSave()` (PUTs `contentService.update(id, {title,body,tags,contentType,primaryPlatform,
  targetPlatforms,lastUpdatedAt})`), `applyToEditor(text)` (writes `body`),
  `addChatMessage`/`appendToken`/`completeGeneration`, `isDirty`/`isSaving`/`isStreaming`/
  `currentTokens`/`chatMessages`. `canEdit()` gates editing to Idea/Draft/Review — new
  surface must honor it (Approved+ read-only).

### Publish — gaps
- **Current modal:** 480px checkbox list only; shows single `body.length/limit` number; does
  NOT call publish/schedule itself — emits `confirm.emit({platforms, scheduledAt?})` to the
  parent. No preview, no delivery, no result view.
- **Model (`content.model.ts`):** `PUBLISHABLE_PLATFORMS` = [Blog, Medium, Substack, LinkedIn,
  Twitter] ✓. `PLATFORM_CHAR_LIMITS` = `{Twitter:280, LinkedIn:3000}` (partial).
  `PlatformCapabilities` + `PlatformConnectionStatus` (runtime, from `/api/platforms`).
  **No `delivery` (auto/manual) concept, no code tile, no fmt note.** Add a static
  `PLATFORM_META` map: `delivery: 'auto'|'manual'`, `charLimit`, `fmt`, `code`, `label`.
  Delivery values: Blog auto, Medium **manual** (no API — paste), Substack auto, LinkedIn auto
  (3000), Twitter auto (280). **Drive connection from live `PlatformConnectionStatus.isConnected`,
  NOT a hardcoded flag** (prototype's `connected:false` for Twitter is just sample state).
  Badge logic: `auto && connected → "⚡ Auto-publish"`; `auto && !connected → "⚡ Connect to
  auto-publish"`; `manual → "✋ Manual"`.
- **Previews:** MISSING — 5 renderers (Blog hero/kicker/serif H1/lede/byline/H2; Medium;
  Substack masthead; LinkedIn truncate ~210 "…more" + 3000 cap; Twitter numbered thread).
  Need a markdown→blocks adapter + plain-text stripper (the body is a markdown string, NOT
  structured blocks; raw `##`/`**` would corrupt char/thread budgets). Port `splitThread`
  logic from prototype `data.jsx` (sentence-based greedy packer; reconcile limit — spec says
  ≤270, real Twitter 280, prototype default 270 — pick one and test the boundary).
- **Result view:** MISSING — per destination: auto+connected → Publishing… → ✓ Published
  View↗ (poll `getPublishStatus` / SignalR); manual → "Ready to post" + ⧉ Copy text + Open
  {platform}↗ (clipboard gets platform-formatted body via the same preview transforms);
  schedule → ◴ Scheduled for {datetime} (frontend-only state from `mode==='schedule'` +
  `Content.scheduledAt`; backend `PublishStatus` has no Scheduled value).

## Out of scope
- Backend / API / EF changes (all decisions chosen to avoid them).
- `signalr.service.ts` transport (must remain unchanged).
- Feed/Ideas/Discover/Calendar/Analytics/Listening features (only their shared
  token-recoloring is incidental; not a deliverable here).

## Reference material
- Handoff README (authoritative spec): `PBAv2/design_handoff_content_studio/README.md`
- Prototype (design reference, do NOT copy): `PBAv2/design_handoff_content_studio/prototype/`
  - `Content Studio.html` (`<style>` block = CSS source of truth), `data.jsx`,
    `studio-parts.jsx`, `studio-views.jsx`, `editor.jsx`, `publish.jsx`
- Memory: `project_content_studio_redesign.md`
- App base: `src/PersonalBrandAssistant.Web/src/app/features/content` +
  `src/app/shell/sidebar` + `src/styles/`
