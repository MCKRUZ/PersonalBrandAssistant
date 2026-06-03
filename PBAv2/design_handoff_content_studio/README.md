# Handoff: Content Studio Redesign (PBAv2)

## Overview
This package redesigns the **Content Studio** feature of `PersonalBrandAssistant.Web` (Angular standalone + PrimeNG + signals/stores). It covers three flows:

1. **Content list** (`/content`) — replaces the empty spreadsheet with a **pipeline overview bar + draggable kanban board**, a refined table view, and an **inspiring empty state**.
2. **Content editor** (`/content/:id`, `/content/new`) — a focused **writing studio** with a live voice meter and AI assistant sidecar, replacing the 50/50 raw-markdown split. **The side-by-side markdown view is removed** (confirmed) — authors write on a single clean prose surface.
3. **Multi-format publishing** — a **publish overlay** with per-platform rendered previews, an explicit **auto-publish vs manual** distinction, and a post-publish result screen.

The single most important change is visual identity (see **Design Tokens → The #1 fix**): the app currently renders generic GitHub-dark navy/teal, but `src/styles/_variables.scss` already defines the intended **terracotta-on-obsidian** system. The redesign simply *uses* those tokens.

---

## About the Design Files
The files in `prototype/` are **design references built in HTML/React (Babel-in-browser)**. They are **not production code to copy**. They demonstrate intended look, layout, and behavior.

**Your task:** recreate these designs inside the existing Angular app, using its established patterns — standalone components, `signal()`/`computed()` state, PrimeNG where it fits, SCSS partials in `src/styles/`, and the existing stores (`ContentStore`, `ContentEditorStore`) and services (`ContentService`, `SignalRService`). Do **not** introduce React. Map each prototype component to its Angular equivalent (table below).

## Fidelity
**High-fidelity.** Colors, typography, spacing, radii, and interactions are final. Recreate pixel-faithfully using the existing SCSS token system. Where the prototype hardcodes a hex, prefer the matching SCSS variable.

---

## Prototype → Angular component map

| Prototype (in `prototype/`) | Recreate in (existing file) | Notes |
|---|---|---|
| `studio-app.jsx` (App shell, routing state) | `content-list.component.ts` + `shell/sidebar` | List ↔ editor is real Angular routing, not local state |
| `studio-parts.jsx` `AppSidebar` | `shell/sidebar/sidebar.component.*` | Restyle to tokens; add user footer block |
| `studio-parts.jsx` `StudioHeader` | `content-list.component.ts` header | Serif H1, subtitle with live count |
| `studio-parts.jsx` `PipelineBar` | **NEW** `content-list/pipeline-bar/` | Replaces `content-filter-sidebar` as primary filter |
| `studio-views.jsx` `Board`, `ContentCard` | **NEW** `content-list/content-board/` | Sits beside existing `content-grid` / `content-list-table` |
| `studio-views.jsx` `Table` | `content-list/content-list-table.component` | Restyle existing table to tokens |
| `studio-views.jsx` `EmptyState` | `shared/components/empty-state` (extend) | Add "inspire" variant w/ idea suggestions |
| `studio-views.jsx` `DetailDrawer` | **NEW** `content-list/content-detail-drawer/` | Quick-look + advance/publish without full editor |
| `editor.jsx` `Editor`, `VoiceMeter` | `content-editor/content-editor.component.ts` | Manuscript surface **replaces** the `p-splitter` markdown view; `markdown-editor` component retired |
| `editor.jsx` `AISidecar` | `content-editor/sidecar-chat.component.ts` | Same `SignalRService` streaming; restyle to tokens |
| `editor.jsx` `stageActions()` | existing editor `@switch (status)` action bar | Logic already exists — keep it, restyle buttons |
| `publish.jsx` `PublishOverlay` + previews | `content-editor/publish-modal.component.ts` | Add tabbed **previews** + auto/manual + result view |
| `publish.jsx` `DeliveryBadge`, `DestRow` | `content-editor/platform-targets.component.ts` | Drive badges from `PlatformConnectionStatus` + new `delivery` field |
| `data.jsx` | `features/content/models/content.model.ts` | Add `delivery` to platform metadata (see below) |

---

## Screens / Views

### 1. App shell (sidebar)
- **Layout:** CSS grid `212px 1fr`, full height. Sidebar is `#0b0b0d`, right border `1px solid var(--surface-border)`.
- **Brand:** "PBA" + "v2" — `DM Serif Display`, 24px; "v2" is `italic`, colored `var(--brand-primary)`.
- **Nav items:** existing list (Feed, Discover, Ideas, **Create**, Calendar, Analytics, Listening, Settings). Item: 14px/500, color `--text-secondary`, gap 13px, padding `9px 12px`, radius 8px. Hover → bg `--surface-hover`, color `--text-primary`. **Active** → bg `--accent-soft` (= brand at ~13% alpha), label `--text-primary`, icon `--brand-primary`.
- **Footer (new):** avatar (32px circle, gradient `135deg, brand → #9c5440`, white 12px/700 initials) + name (13px/600) + sub ("Solo studio", 11px `--text-muted`). Top border `1px solid --surface-border`.

### 2. Content list — header + pipeline bar
- **Header:** padding `22px 28px 0`. H1 "Content Studio" `DM Serif Display` 30px/400. Subtitle 13.5px `--text-secondary`: "{n} pieces moving through your pipeline". Right: primary button "**+ New Content**".
- **Controls row (margin-top 18px):** search input (max 420px, `⌕` icon left, bg `--surface-card`, border `--surface-border`, radius 8px, padding `10px 12px 10px 36px`; focus border `--brand-primary`) + a Board/Table **view toggle** (segmented, bg `--surface-card`, active segment bg `--surface-elevated`).
- **Pipeline bar:** horizontal wrap of rounded **status pills**, padding `18px 28px 14px`. First pill "All {total}". Then one per status with a colored dot + label + mono count chip. Pill: bg `--surface-card`, border `--surface-border`, 13px/500, radius 99px. **Selected** → bg `--surface-elevated`, border + text take that status color. Empty statuses at 0.5 opacity. Clicking toggles a single-status filter (clicking the active one clears it). This **replaces** the left checkbox filter sidebar as the primary filter; keep platform/type/date filters behind a secondary "Filters" affordance if still needed.

### 3. Content list — Board (kanban) — NEW primary view
- **Layout:** horizontal flex of columns, `gap 14px`, scrolls horizontally, fills height.
- **Column:** `flex: 0 0 286px`, bg `#0c0c0e`, border `--surface-border`, radius 12px, column flex with its own scrolling body. Header: status dot + name (13px/600) + mono count chip. Body: vertical stack, `gap 10px`, padding `0 11px 13px`.
- **Card:** bg `--surface-card`, border `--surface-border`, radius 10px, padding 13px. Hover → border `--surface-disabled` + shadow `0 6px 20px -10px rgba(0,0,0,.6)`.
  - Top row: type glyph (`--brand-primary`) + uppercase type label (11px/600, `--text-muted`, letter-spacing .6px) + **voice score ring** (right).
  - Title: 14.5px/600, line-height 1.32, `text-wrap: pretty`.
  - Tag row: mono chips (10.5px, bg `--surface-base`, border, radius 99px).
  - Footer: platform dots (left) + mono updated/scheduled time (right). Scheduled shows `◴ in 3d`.
- **Drag & drop:** cards are draggable between columns; on drop, set `content.status` to the target column and bump `updatedAt`. In Angular use Angular CDK `DragDrop` (`cdkDropList` per column, `cdkDrag` per card) wired to `ContentStore`/`ContentService` status mutation. Empty columns show a dashed "Drop here" target; the hovered column highlights border + bg `--accent-soft`.

### 4. Content list — Table view (refined)
- Card-wrapped table, bg `--surface-card`, radius 12px. Header row bg `#0c0c0e`, 10.5px uppercase `--text-muted` headers. Columns: Status (tag w/ dot) · Title (+ mono tag line) · Type (glyph + label) · Platforms (dots) · Voice (ring) · Updated (mono, right-aligned). Rows: 13.5px, hover bg `--surface-hover`, click → open detail/editor.

### 5. Content list — Detail drawer (NEW)
- Right slide-over, 400px, bg `--surface-card`, left border, shadow `-20px 0 60px -20px rgba(0,0,0,.7)`, slide-in 260ms `cubic-bezier(.2,.8,.2,1)`; scrim `rgba(0,0,0,.55)`.
- Header: status tag + close. Body: type label, serif title (23px), a meta list (Voice ring, Platforms, Updated/Scheduled, Tags), and a faux body-preview block. Footer: "Open in editor" (ghost) + context action — **Publish →** when status is Approved/Scheduled, else **Move to {nextStatus} →**.

### 6. Empty state (inspire variant)
- Centered column, max 720px. Rounded mark tile (64px, bg `--accent-soft`, border brand, glyph `✎`). Serif H2 "Your studio is quiet." (34px). Paragraph (15px `--text-secondary`). **Idea suggestions**: 2-col grid of clickable cards (topic kicker in brand color + hook line + "Start draft" CTA) → each opens the editor pre-seeded. Then an "or" divider and a large "+ Start from scratch" primary button.
- **Filtered-empty** variant: `⌕` mark, "Nothing matches that filter", "Clear filters" ghost button.

### 7. Editor — writing studio
- **Layout:** column. Top bar (58px) → body (manuscript scroll + optional 340px side panel) → action bar.
- **Top bar:** "← Studio" back; a **stage tracker** = 6 dots (Idea→Published) joined by lines, completed dots filled `--text-muted`, current dot enlarged + status color; status label in status color. Right: type·platform meta (12.5px), "Saved" (mono, `--text-muted`), voice score ring (32px), and an "✦ Assistant" toggle when the panel is closed.
- **Manuscript:** centered, max 680px, padding `46px 32px 120px`. Tag chips → editable **title** (`DM Serif Display` 40px/400) → editable **subtitle** (19px `--text-secondary`) → byline (avatar + "Jordan Lee · draft", bottom border). Body prose: 17.5px/1.75, color `#dcdce2`; `h2` = serif 25px. **No markdown** (confirmed): this is a single rich/plain prose surface — retire the `markdown-editor` + preview `p-splitter`. In Angular, make title/subtitle/body editable (contenteditable or a lightweight rich-text surface) and persist via `ContentEditorStore.updateField` + autosave, exactly as today.
  - **Idea state:** instead of prose, a dashed panel: "This is still just an idea." + prompt to hit **Start draft** or open the assistant.
- **Side panel (340px, bg `#0c0c0e`):**
  - **Voice meter** (top, bordered): label + big mono value (color by score band) + track/fill bar + a one-line note keyed to the band (≥80 confident / ≥60 close / else flat). Wire to `voiceScore` / `VoiceCheckResult`.
  - **AI Assistant** = `sidecar-chat` restyled: message list (assistant bubbles `--surface-elevated` + border; user bubbles brand bg, dark text), a "thinking" 3-dot animation during streaming, **quick-action chips** (Draft from idea / from scratch when empty; Refine / Shorten / Expand / Change tone when there's a body — same as today's `quickActionChips()`), and an input with send button. Assistant replies expose **✓ Apply to draft** / **⧉ Copy** (existing `applyToEditor` / clipboard).
- **Action bar (bottom):** left = "Targets" label + platform dots; right = status-aware buttons (existing `@switch (status)` logic — keep it):
  - Idea → **Start draft** (primary)
  - Draft → Save draft · Submit for review · **Approve**
  - Review → Request changes · **Approve**
  - Approved → Schedule · **Publish →**
  - Scheduled → Unschedule · **Publish now**
  - Published → Open published · Create variant
- Primary buttons: bg `--brand-primary`, text `#1a0f0a`, 14px/600, radius 8px. Ghost: transparent, border `--surface-border`, hover bg `--surface-hover`.

### 8. Publish overlay — multi-format preview (CORE NEW WORK)
Replaces the checkbox-only `publish-modal`. Centered modal, 1080px, max-height 90vh, bg `--surface-card`, radius 16px, scrim `rgba(0,0,0,.66)`, pop-in 240ms.
- **Header:** serif "Publish" / "Schedule" + content title.
- **Body grid `340px 1fr`:**
  - **Left — Destinations:** one row per publishable platform (Blog, Medium, Substack, LinkedIn, Twitter). Each row: checkbox (primary platform checked + disabled), platform code tile (30px, colored border/text), name (+ "Primary" badge), a **format note**, and a right-aligned stack of a **delivery badge** + char/thread usage. Selected rows: border brand + bg `--accent-soft`. Below: a "When" section — "Publish now" vs a `datetime-local` (schedule mode, `color-scheme: dark`).
  - **Right — Preview:** tabs for each *selected* platform; a sticky caption ("How it appears on **X** · deploys automatically / you post this one / connect to auto-deploy"); then the **rendered preview** on a white card:
    - **Blog:** striped hero, kicker, serif H1, lede, byline, serif H2 sections.
    - **Medium:** bold sans H1, gray subtitle, author row + Follow, clap/bookmark bar.
    - **Substack:** masthead (publication + Subscribe) + "to N subscribers" byline + unsubscribe footer.
    - **LinkedIn:** feed card; body **truncated at ~210 chars** with "…more"; reaction/comment/repost bar. Respect 3000-char limit.
    - **Twitter/X:** body **auto-split into ≤270-char tweets**, numbered `1/n`, threaded with the connector rail.
  - The preview renderers should consume the **same body content** the editor produced (structured rich text → platform-specific rendering). This is the heart of the feature: one source, N faithful previews.
- **Footer:** summary ("{n} destinations · {a} auto · {m} manual") + Cancel + **Publish {n} →** (disabled if none selected, or schedule with no datetime).
- **Result view (after confirm):** one row per destination:
  - **auto + connected** → "Publishing…" (spinner) → "✓ Published — View ↗"
  - **manual** → "Ready to post" + **⧉ Copy text** + **Open {platform} ↗**
  - **schedule mode** → "◴ Scheduled for {datetime}"
  - Footer note explains manual destinations have no publish API. This directly models *"some deploy automatically, some won't."*

---

## Interactions & Behavior
- **List → editor:** clicking a card opens the detail drawer; "Open in editor" routes to `/content/:id`. "New Content" and empty-state ideas route to `/content/new` (seed title/type where applicable).
- **Board DnD:** drag card to a column → status update (optimistic, then service call). Use Angular CDK DragDrop.
- **Pipeline pills / search:** filter the visible set client-side over `ContentStore.contents()` (or pass to the store's existing filter state). Search matches title + tags, debounced 300ms (already implemented).
- **Editor autosave:** unchanged — `updateField` + 3s debounce for Idea/Draft/Review.
- **AI streaming:** unchanged transport (`SignalRService`); restyle bubbles + thinking indicator.
- **Publish:** selecting/deselecting destinations updates tabs + summary live. Confirm triggers per-destination outcomes; auto ones resolve after their service calls, manual ones stay actionable.
- **Animations:** drawer slide 260ms; modal pop-in 240ms `cubic-bezier(.2,.8,.2,1)`; voice fill transition 400ms; spinner 0.7s linear. Respect `prefers-reduced-motion`.
- **Reduced/empty data:** show inspire empty state when the pipeline is empty; filtered-empty variant when filters exclude everything.

## State Management
- **List:** reuse `ContentStore` — `contents()`, `loading()`, `viewMode()` (extend to `'board' | 'grid' | 'table'`), `totalCount/page/pageSize`, filter state. Add `activeStatus` filter + search (exists).
- **Editor:** reuse `ContentEditorStore` — `content()`, `isSaving/isDirty`, chat (`chatMessages/isStreaming/currentTokens`), `applyToEditor`, `autoSave`, status transitions via `ContentService`.
- **Publish:** local component state — `selected: Platform[]`, `activeTab`, `scheduledAt`, `result`. Drive delivery from `connectedPlatforms()` + new `delivery` metadata. On confirm, call existing `ContentService.publish` / `.schedule`.

## Design Tokens

### The #1 fix — use the tokens you already have
`src/styles/_variables.scss` already defines the intended system, but components hardcode GitHub-dark values (e.g. `#0d1117`, `#161b22`, `#30363d`, `#58a6ff`, `#1f6feb`, teal buttons). **Replace those with the SCSS variables.** Recommended: expose the SCSS tokens as CSS custom properties on `:root` so component styles reference `var(--…)` (and theming/Tweaks become trivial).

### Colors (final)
```
Brand          --brand-primary #c87156   hover #d4836a   active #b5624a
               --accent-soft   rgba(200,113,86,0.13)   // brand @ ~13%
Surfaces       base #0e0e10  card #141418  elevated #1a1a20  hover #22222a
               border #2c2c36  disabled #3a3a46
               (extras used by redesign) sidebar #0b0b0d  column/inset #0c0c0e  publish-canvas #08080a
Text           primary #f0f0f5  secondary #8a8a96  muted #5a5a66
Status         Idea #8a7df0  Draft #8a8a96  Review #c87156  Approved #4ade80
               Scheduled #60a5fa  Published #34d399  Archived #5a5a66
Voice/score    >=80 #4ade80   >=60 #fbbf24   else #f87171
Delivery badge auto bg #1f3a2b/text #4ade80 · manual bg #3a2f1c/text #fbbf24 · warn bg #3a2420/text #f0935f
```
> Note: `_variables.scss` lists `$status-draft #5a5a66`; the redesign uses `#8a8a96` for the Draft dot for contrast on dark, and introduces `Idea #8a7df0`. Add these to the SCSS status map.

### Typography
```
Body / UI      'DM Sans'           400/500/600/700
Headlines      'DM Serif Display'  400 (H1 list 30, editor title 40, drawer/H2 21–25)
Mono / meta    'JetBrains Mono'    timestamps, counts, tags, scores
```
Load via Google Fonts (already the intended families). Sizes: H1 30 · editor title 40 · card title 14.5 · body prose 17.5 · labels 11 uppercase (.6–.8px tracking) · meta 11–12.

### Spacing / radius / shadow
```
Spacing   4px base (4/8/12/16/20/24/32/40/48) — matches $space-* in _variables.scss
Radius    cards 12 · card-inner 10 · controls 8 · pills/rings 99 · modal 16
Shadow    card-hover 0 6px 20px -10px rgba(0,0,0,.6)
          drawer   -20px 0 60px -20px rgba(0,0,0,.7)
          modal     0 30px 90px -30px rgba(0,0,0,.8)
```
Layout dims (from `_variables.scss`): sidebar 200–212px, header ~56–58px, sidecar/drawer 340–400px.

### NEW data: platform delivery metadata
Add to platform metadata (e.g. alongside `PlatformCapabilities` in `content.model.ts`):
```
Blog      delivery: 'auto'    connected: true   charLimit: null   fmt: "Markdown + HTML, images, full formatting"
Medium    delivery: 'manual'  connected: true   charLimit: null   fmt: "No publish API — paste into Medium"
Substack  delivery: 'auto'    connected: true   charLimit: null   fmt: "Newsletter — sends to subscribers automatically"
LinkedIn  delivery: 'auto'    connected: true   charLimit: 3000   fmt: "Posts automatically to your profile"
Twitter   delivery: 'auto'    connected: false  charLimit: 280    fmt: "Splits into a numbered thread"
```
Badge logic: `auto && connected → "⚡ Auto-publish"`; `auto && !connected → "⚡ Connect to auto-publish"`; `manual → "✋ Manual"`. **Confirmed integrations:** Blog, Substack, LinkedIn auto-publish; **Medium is manual** (no usable publishing API — author pastes the formatted post). Twitter is shown disconnected to demonstrate the "connect to auto-publish" state.

## Assets
- **No raster assets.** Avatars = initials on a gradient; platform marks = 2-letter mono codes in colored tiles (no third-party logos). If you want real platform logos, swap the code tiles for your existing icon set (the app already uses a `platformIconClass` / PrimeIcons mapping — reuse it).
- Type/section glyphs are Unicode (`¶ ▤ ⋮ ✉ ◇ ▷ ▹`, `✦ ⌕ ◴ ⚡ ✋`). Replace with PrimeIcons if preferred.
- Image placeholders use a CSS repeating-linear-gradient stripe.

## Files (in this bundle)
```
prototype/
  Content Studio.html     ← entry; <style> holds ALL final tokens, layout & component CSS
  data.jsx                ← enums, status/platform/type metadata, delivery flags, sample body, thread splitter
  studio-parts.jsx        ← AppSidebar, StudioHeader, PipelineBar, PlatformDot/Row, VoiceScore, StatusTag
  studio-views.jsx        ← Board, ContentCard, Table, EmptyState, DetailDrawer
  editor.jsx              ← Editor, VoiceMeter, AISidecar, stageActions(), AI canned replies
  publish.jsx             ← PublishOverlay, per-platform preview renderers, DeliveryBadge, ResultView
  tweaks-panel.jsx        ← prototype-only theming panel; NOT needed in production
```
Open `prototype/Content Studio.html` in a browser to interact. The `<style>` block is the source of truth for exact CSS values; the `.jsx` files show structure/behavior. Reference them, then implement in Angular per the component map above.
