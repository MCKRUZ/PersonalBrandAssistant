# Content Studio Redesign — Usage & Handover

Branch: `feature/content-studio-redesign` (off `v2-rebuild`). Frontend-only. 7 commits
(1 planning docs + 5 sections + 1 fix folded in). No backend/API/DB changes; `signalr.service.ts`
untouched.

## What shipped
A "terracotta-on-obsidian" Content Studio for `src/PersonalBrandAssistant.Web`:

1. **Token foundation** — `src/styles/_tokens.scss` exposes the design system as `:root` CSS custom
   properties; wired into `src/styles.scss`. Sidebar restyled with a user-footer block. The whole
   content feature recolored from GitHub-dark to `var(--…)`.
2. **Shared atoms + store** — `features/content/shared/` (`app-status-tag`, `app-voice-score-ring`,
   `app-platform-dot`); `content-display.utils.ts` (STATUS_META, TYPE_GLYPH, voiceBandColor,
   LEGAL_TRANSITIONS, nextStatus, relativeTime). `ContentStore` is load-all + client-side
   filtering/counts/grouping with a `transition(id, target)` state-machine dispatcher.
3. **Publish overlay** — bespoke 1080px `publish-modal` with destinations, delivery badges, char/
   thread usage, 5 platform preview renderers, schedule, and a post-confirm result view. Pure
   adapters: `markdown-blocks` (toBlocks/plainText), `thread-splitter` (280, suffix-aware).
4. **Content list** — pipeline-bar (status pills), CDK kanban `content-board` (legal-transition
   enter-predicate), `detail-drawer`, `filters-popover`, inspire/filtered `studio-empty-state`,
   refined table, `schedule-dialog`.
5. **Editor** — TipTap `prose-editor` (markdown round-trip), `manuscript-surface`, `stage-tracker`,
   `editor-top-bar`, side-panel `voice-meter`, inline restyled `sidecar-chat` (SignalR unchanged).

## Run it
```
cd src/PersonalBrandAssistant.Web
npm install
npx ng test --watch=false --browsers=ChromeHeadless   # 514 pass / 0 fail
npx ng build --configuration development               # clean
```
**Note:** the PRODUCTION build (`ng build`) fails in this environment ONLY at Angular's Google-Fonts
inlining (`unable to verify the first certificate`) — an offline/SSL-interception issue, not a code
defect. To produce a prod build behind the proxy, either allow the fonts host, self-host the fonts,
or set `optimization.fonts.inline: false` in angular.json.

## Known follow-ups (non-blocking, flagged in reviews)
- **Live verification pending:** the app was not run against a live backend (built + unit-tested
  only). Verify the board drag-drop transitions, publish result-view polling, voiceCheck scale
  (0–1 vs 0–100), and TipTap editing in a running app before merge.
- `detail-drawer` body-preview uses `subscribe` in an `effect` — consider `rxResource` (low-risk
  single-select race).
- Dead `content-card` `edit`/`onDelete`/`duplicate` outputs and the orphaned `editor-toolbar/`
  component can be cleaned up.
- `tiptap-markdown@0.9` integrates with TipTap v3.24 here; pin/watch on upgrades.
- 34 npm audit advisories (mostly transitive) predate/accompany the dep adds — triage separately.

## Commits
- planning docs + handoff
- section 01 foundation · 02 atoms/store · 03 publish · 04 list · 05 editor
