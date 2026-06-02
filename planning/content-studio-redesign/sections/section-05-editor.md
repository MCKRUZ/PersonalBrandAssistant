# section-05-editor — Editor flow

The editor flow for Content Studio: a clean TipTap prose studio replacing the split markdown editor, with a stage tracker, a side-panel voice meter, and an inline restyled AI sidecar. The existing status action bar logic is reused.

All work is in the Angular 19 web app at `src/PersonalBrandAssistant.Web/`. Tests are colocated `*.spec.ts`. Run them with:

```
cd src/PersonalBrandAssistant.Web && npx ng test --watch=false --browsers=ChromeHeadless
```

Frontend stack: Angular 19 standalone components, `@ngrx/signals`, PrimeNG 20, `ngx-markdown` (exposes `marked`). **No backend changes. `signalr.service.ts` is untouched.**

---

## Dependencies (already built — do not re-implement)

This section runs in parallel with `section-04-content-list`, after these are complete:

- **section-01-foundation** — the CSS custom-property token system (`_tokens.scss` defines `--brand-primary #c87156`, `--surface-base #0e0e10`, `--surface-card #141418`, `--surface-elevated #1a1a20`, `--surface-inset #0c0c0e`, `--surface-border #2c2c36`, `--text-primary #f0f0f5`, `--text-secondary #8a8a96`, `--text-muted #5a5a66`, `--accent-soft rgba(200,113,86,.13)`, status colors `--status-idea #8a7df0` / `--status-draft #8a8a96` / `--status-review #c87156` / `--status-approved #4ade80` / `--status-scheduled #60a5fa` / `--status-published #34d399` / `--status-archived #5a5a66`, voice bands `--voice-high #4ade80` / `--voice-mid #fbbf24` / `--voice-low #f87171`, radius `--r 12px` / `--r-inner 10px` / `--r-control 8px` / `--r-pill 99px`, fonts `--font-body` / `--font-display` (DM Serif Display) / `--font-mono`). **TipTap deps are already installed** in `package.json` by section-01 (`@tiptap/core`, `@tiptap/pm`, `@tiptap/starter-kit`, and a markdown serialization extension such as `tiptap-markdown`); they are verified to build under the ESM toolchain. `@angular/cdk@^19.2.0` is installed. Use `var(--…)` throughout — never reintroduce GitHub-dark hexes (`#0d1117`/`#161b22`/`#30363d`/`#58a6ff`/`#1f6feb`/`#21262d`/`#8b949e`/`#c9d1d9`/`#f0f6fc`/`#f85149`/`#3fb950`/`#d29922`).

- **section-02-shared-atoms-store** — presentational atoms in `features/content/shared/` and helpers in `content-list/content-display.utils.ts`. This section CONSUMES, never redefines:
  - `voice-score-ring.component.ts` — selector `app-voice-score-ring`, `@Input() score: number | null; @Input() size = 40` → conic ring + mono value (dashed-empty when null). Used in `editor-top-bar` at size 32.
  - `platform-dot.component.ts` — selector `app-platform-dot`, `@Input() platform: Platform; @Input() variant: 'dot'|'tile'`. Used in the action bar.
  - `status-tag.component.ts` — selector `app-status-tag`, `@Input() status: ContentStatus` → dot + label in status color.
  - `content-display.utils.ts` exports: `voiceBandColor(score: number | null): string` (boundary `>=80` high → `--voice-high`, `>=60` mid → `--voice-mid`, else low → `--voice-low`, null → `--text-muted`); `STATUS_META: Record<ContentStatus, { color: string; label: string; order: number }>`; `TYPE_GLYPH: Record<ContentType, string>`.

- **section-03-publish-overlay** — the bespoke `publish-modal.component.ts` rewrite (1080px modal, destinations, previews, schedule, result). The editor action bar OPENS this modal. Its public contract is unchanged from what `content-editor.component.ts` already uses today: `[visible]`, `[content]`, `[connectedPlatforms]`, `[mode]` (`'publish' | 'schedule'`), `(confirm)` emitting `{ platforms: Platform[]; scheduledAt?: string }`, `(cancel)`. Keep wiring it exactly as the current component does — do not change the modal's API here. (If section 03 changed the contract, follow whatever it settled on — coordinate.)

---

## Background: what exists today

`content-editor.component.ts` is a single ~500-line component at `src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/`. Current layout (top to bottom): a `header.editor-top-bar` with PrimeNG `p-select` platform/type pickers, a `p-tag` status badge, an inline tags input, a `p-knob` voice gauge, and a save indicator → an `<app-editor-toolbar>` → an `<app-platform-targets>` strip → a **`p-splitter`** holding `<app-markdown-editor>` on the left and a `<markdown>` preview on the right → a `footer.editor-action-bar` with a `@switch(status)` of `p-button`s → a floating chat toggle → `<app-sidecar-chat>` (a `p-drawer`) → `<app-publish-modal>`.

### Store: `ContentEditorStore` (`stores/content-editor.store.ts`) — DO NOT CHANGE shape

A `@ngrx/signals` store, provided per-component. Surface used by this section (all already exist):

- State signals: `content: Signal<ContentDetail | null>`, `isDirty`, `isSaving`, `loading`, `chatMessages`, `isStreaming`, `currentTokens`, `error`.
- `loadContent(id)` (rxMethod), `updateField<K extends keyof ContentDetail>(field, value)` (sets `isDirty: true`), `autoSave()` (calls `ContentService.update`, sends `lastUpdatedAt: content.updatedAt` as the concurrency token), `addChatMessage`, `appendToken`, `completeGeneration`, `applyToEditor(text)` (sets `body` + `isDirty`), `reset()`.

The store body field is `body` (markdown string). **No new persisted fields.** Section-02 already extended the *list* store (`content.store.ts`); the *editor* store needs no schema change here.

### Models (`models/content.model.ts`) — for reference, do not edit

```ts
enum ContentStatus { Idea, Draft, Review, Approved, Scheduled, Published, Archived } // string-valued
enum ContentType  { BlogPost='Blog', LinkedInPost, Tweet, ThreadedTweet, SubstackNewsletter,
                    RedditPost, YouTubeVideo, YouTubeShort }
enum Platform     { Blog, Medium, Substack, LinkedIn, Twitter, Reddit, YouTube }
const PUBLISHABLE_PLATFORMS = [Blog, Medium, Substack, LinkedIn, Twitter];
const PLATFORM_CHAR_LIMITS  = { Twitter: 280, LinkedIn: 3000 };

interface ContentDetail extends Content {
  body: string; voiceScore: number | null; sourceIdeaId: string | null; tags: string[];
  status: ContentStatus; contentType: ContentType; primaryPlatform: Platform;
  targetPlatforms: Platform[]; updatedAt: string; scheduledAt: string | null; /* … */
}
interface VoiceCheckResult { score: number; feedback: string; }
interface CreateContentRequest { title; contentType; primaryPlatform; sourceIdeaId?; tags;
                                 targetPlatforms?; }
```

### Service (`services/content.service.ts`) — for reference, already complete

- `create(req: CreateContentRequest): Observable<string>` (returns new id)
- `getPlatforms(): Observable<PlatformConnectionStatus[]>`
- `voiceCheck(id: string): Observable<VoiceCheckResult>` (GET `/{id}/voice-check`)
- status transitions: `draft(id, {action})`, `approve`, `submitForReview`, `requestChanges`, `schedule(id,{scheduledAt})`, `unschedule`, `publish(id,{targetPlatforms})`, `unpublish`, `restore`, `crossPost`.

---

## Tests first (write these before implementation)

The TDD source mandates writing the **prose-editor round-trip test FIRST** — it gates the whole section's editing approach. Author the spec for each component before its implementation. Tests are behavioral, not exhaustive; assert WHY behavior matters.

### `content-editor.component.spec.ts` (UPDATE — remove splitter assertions)

The existing spec already builds a `mockContent()` factory, a `mockStore` with jasmine spies, a SignalR mock, and a `ContentService` spy (`getPlatforms.and.returnValue(of([]))`, `create.and.returnValue(of('new-id-1'))`). Reuse that harness. Replace/add:

```
# Test: template has NO p-splitter and NO app-markdown-editor; app-manuscript-surface IS present.
# Test: ngOnInit on /content/new reads topic/type/sourceIdeaId query params and passes them into
#       ContentService.create() (title seeded from topic, contentType from type, sourceIdeaId when
#       present) — not the old fixed 'Untitled' Blog stub.
# Test: autosave still fires only for Idea/Draft/Review (scheduleAutoSave) and not for Approved+.
# Test: Assistant toggle (panelOpen signal) shows/hides the side panel.
```

The current spec drives `ActivatedRoute` via a `snapshot.paramMap` mock; for the new-content test, provide `snapshot.queryParamMap` returning `topic`/`type`/`sourceIdeaId`.

### `prose-editor.spec.ts` (NEW — highest-risk; round-trip test FIRST, gates the section)

```
# Test (round-trip, GATES the section): for the supported mark set — h1, h2, h3, bold, italic,
#       links, bullet lists, ordered lists, inline code — markdown -> setContent -> serialize ->
#       markdown is STABLE (string-equal modulo normalized whitespace). If a specific mark fails to
#       round-trip, EXCLUDE it from the supported set and DOCUMENT it (see Fallback below).
# Test: valueChange emits the debounced serialized markdown on edit.
# Test: setContent is NOT re-applied when incoming `value` equals the editor's last serialized
#       output (no caret reset), AND is skipped while the editor is focused.
# Test: readOnly=true blocks edits (editor.isEditable === false).
# Test: pasting HTML is sanitized — no <script>/<style>; only allowlisted marks survive.
```

### `manuscript-surface.spec.ts` (NEW)

```
# Test: title edit calls store.updateField('title', …); body edit calls updateField('body', md) and
#       triggers the host's scheduleAutoSave (assert via an (bodyChange) output the host wires up).
# Test: status===Idea renders the dashed "still just an idea" panel + a Start draft button that
#       calls onStartDraft; Draft+ renders the prose editor (app-prose-editor present).
# Test: the derived subtitle is display-only — it is NEVER passed to updateField / never lands in an
#       UpdateContentRequest.
```

### `stage-tracker.spec.ts` (NEW — pure presentational)

```
# Test: active dot index per status — Idea 0, Draft 1, Review 2, Approved 3, Scheduled 4,
#       Published 5.
# Test: Archived renders an all-muted terminal state (no active dot) with an "Archived" label.
# Test: dots before the active index are filled (completed); dots after are empty (border only).
```

### `editor-top-bar.spec.ts` (NEW)

```
# Test: the "← Studio" back control navigates to /content (router.navigate(['/content'])).
# Test: stage-tracker receives the current status input.
# Test: saved indicator reflects isSaving()/isDirty() — Saving / Unsaved / Saved.
# Test: voice-score-ring receives the voiceScore.
# Test: the Assistant toggle button emits its toggle output.
```

### `voice-meter.spec.ts` (NEW)

```
# Test: displayed value + color comes from voiceScore via voiceBandColor (>=80 / >=60 / else); the
#       one-line band note text differs per band (>=80 confident / >=60 close / else flat).
# Test: the re-check action calls ContentService.voiceCheck(id) and updates the displayed
#       score + feedback from the VoiceCheckResult.
```

### `sidecar-chat.spec.ts` (UPDATE)

```
# Test: assistant vs user bubble styling differs (assistant --surface-elevated + border; user brand
#       bg + dark text).
# Test: a 3-dot "thinking" blink animation is shown while isStreaming (NOT the old skeleton shimmer).
# Test: quick-action chips differ for empty body (Draft from idea / Draft from scratch) vs has-body
#       (Refine / Shorten / Expand / Change tone).
# Test: Apply writes via store.applyToEditor; Copy puts text on the clipboard.
# Test: SignalR wiring UNCHANGED — sendChatMessage called on send; tokens$ appended via appendToken;
#       generationComplete$ clears the stream via completeGeneration.
```

### `platform-targets.spec.ts` (UPDATE)

```
# Test: selecting/deselecting a platform emits the updated targetPlatforms (selection logic
#       unchanged) — this is a restyle-only change.
```

---

## Implementation

Component directory: `src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/`. All new components are standalone, `ChangeDetectionStrategy` default. Token-driven styles only.

### 4.1 `content-editor.component.ts` (MODIFY — relayout)

New vertical layout column: **`editor-top-bar` (58px height)** → **body** (manuscript scroll region + optional 340px side panel) → **action bar**.

- **Remove** the `p-splitter`, `<app-markdown-editor>`, the `<markdown>` preview pane, and the `SplitterModule` import. Also remove the old inline `header.editor-top-bar` markup, the `p-select` platform/type pickers (the type·platform meta moves into `editor-top-bar` as display text; if in-editor type/platform editing is still needed it lives in the manuscript chip row — keep it out of the top bar), the `p-knob`, the floating chat toggle button, and `<app-editor-toolbar>` (its draft/cross-post actions move into the sidecar quick-actions + action bar respectively — the `editor-toolbar` component is NOT deleted by this section unless you confirm no other importer; default: leave it on disk, just stop importing it here).
- **Keep:** `scheduleAutoSave()` (3s debounce, fires only for Idea/Draft/Review), `canEdit()` (Idea/Draft/Review → editable; Approved+ → read-only), every status handler (`onStartDraft`, `onApprove`, `onSubmitForReview`, `onRequestChanges`, `onUnpublish`, `onUnschedule`, `onRestore`, `onPublish`, `onSchedule`, `onPublishConfirm`, `onTargetPlatformsChange`, `doStatusAction`), and the publish-modal open/wiring.
- Add a `panelOpen = signal(true)` toggling the side panel. Wire the top-bar Assistant toggle and the in-panel close to it.
- Replace the old `voiceColor` computed (strict `>80`) usage with the shared `voiceBandColor` (`>=80`) — the editor must standardize on `>=80` so the top-bar ring and the voice-meter agree.

**New-content seeding (the load-bearing fix):** today `ngOnInit`'s no-`id` branch auto-creates a fixed `{ title:'Untitled', contentType: BlogPost, primaryPlatform: Blog }` and **ignores query params**, which makes the empty-state idea cards' pre-seed dead. Change the no-`id` branch to read `topic` / `type` / `sourceIdeaId` from `this.route.snapshot.queryParamMap` and pass them into the existing `ContentService.create(...)` call:

```ts
// no-id branch of ngOnInit — replaces the fixed 'Untitled' stub
const q = this.route.snapshot.queryParamMap;
const topic = q.get('topic');
const type = q.get('type') as ContentType | null;
const sourceIdeaId = q.get('sourceIdeaId');
this.contentService.create({
  title: topic?.trim() || 'Untitled',
  contentType: type ?? ContentType.BlogPost,
  primaryPlatform: Platform.Blog,
  tags: [],
  ...(sourceIdeaId ? { sourceIdeaId } : {}),
}).subscribe({
  next: (newId) => this.router.navigate(['/content', newId]),
  error: () => this.router.navigate(['/content']),
});
```

New template skeleton (prose, not the full file):

```html
<div class="editor-page" data-testid="content-editor-page">
  @if (store.loading()) { <div class="loading-overlay" data-testid="loading">Loading…</div> }

  <app-editor-top-bar
    [status]="store.content()?.status ?? null"
    [contentType]="store.content()?.contentType ?? null"
    [primaryPlatform]="store.content()?.primaryPlatform ?? null"
    [voiceScore]="store.content()?.voiceScore ?? null"
    [isSaving]="store.isSaving()"
    [isDirty]="store.isDirty()"
    [panelOpen]="panelOpen()"
    (togglePanel)="panelOpen.set(!panelOpen())" />

  <div class="editor-body">
    <main class="manuscript-scroll">
      @if (store.content()) {
        <app-manuscript-surface
          [content]="store.content()!"
          [canEdit]="canEdit()"
          (titleChange)="onTitleChange($event)"
          (bodyChange)="onBodyChange($event)"
          (startDraft)="onStartDraft()" />
      }
    </main>

    @if (panelOpen()) {
      <aside class="side-panel">
        <app-voice-meter [contentId]="store.content()?.id ?? ''"
                         [voiceScore]="store.content()?.voiceScore ?? null"
                         [feedback]="voiceFeedback()" />
        <app-sidecar-chat [contentId]="store.content()?.id ?? ''" />
      </aside>
    }
  </div>

  <footer class="editor-action-bar" data-testid="action-bar">
    <!-- left: Targets label + app-platform-dot per target; right: @switch(status) buttons -->
  </footer>

  @if (store.content()) {
    <app-publish-modal
      [visible]="publishModalVisible()" [content]="store.content()!"
      [connectedPlatforms]="connectedPlatforms()" [mode]="publishMode()"
      (confirm)="onPublishConfirm($event)" (cancel)="publishModalVisible.set(false)" />
  }
</div>
```

`onTitleChange(t)` → `store.updateField('title', t)` + `scheduleAutoSave()`. `onBodyChange(md)` → `store.updateField('body', md)` + `scheduleAutoSave()` (this is the existing handler — unchanged). `voiceFeedback` is a small signal the editor holds; the voice-meter's re-check updates it (or the meter owns it internally — see 4.6, owning it internally is simpler and preferred).

Note: `sidecar-chat` no longer takes `[visible]`/`(visibleChange)` (the drawer is gone) — it renders inline whenever the panel is open.

Layout CSS (tokens): `.editor-page { display:flex; flex-direction:column; height:100%; }` `.editor-body { display:flex; flex:1; min-height:0; }` `.manuscript-scroll { flex:1; overflow-y: auto; }` `.side-panel { width:340px; flex-shrink:0; border-left:1px solid var(--surface-border); background:var(--surface-inset); display:flex; flex-direction:column; overflow:hidden; }` `.editor-action-bar { border-top:1px solid var(--surface-border); padding:8px 16px; }`. Recolor the loading overlay to `var(--surface-base)`/`var(--text-secondary)`.

### 4.2 `prose-editor/prose-editor.component.ts` (NEW — TipTap ↔ signals wrapper)

**The risky, core component.** A markdown-backed rich prose surface with no toolbar (headless; StarterKit + a markdown serialization extension). Contract:

```ts
/** A markdown-backed rich prose surface. Loads markdown into a TipTap doc, emits markdown out.
 *  - value: markdown string. Set once on load / on external change; NOT re-applied on every
 *    keystroke (that would reset the caret).
 *  - readOnly: disables editing (Approved+).
 *  - valueChange: debounced markdown serialization of the current doc. */
@Input() value: string;
@Input() readOnly: boolean;
@Output() valueChange: EventEmitter<string>;
```

Implementation notes (no full code here):

- Create the TipTap `Editor` in `ngOnInit` (or an effect): `StarterKit` constrained to the supported mark set + the markdown extension; mount on a host element ref. `setContent(value)` once.
- On TipTap `update`, serialize doc → markdown and emit `valueChange` **debounced** (~300ms). Track the last serialized output in a private field.
- **Caret guard (concrete mechanism):** only call `editor.commands.setContent(value)` when the incoming `value` differs from the editor's last serialized output AND the editor is NOT focused. This prevents both caret-jump and a serialize→input→serialize update loop. Drive this from an `ngOnChanges`/input setter.
- `editor.setEditable(!readOnly)` — Approved+ is read-only.
- Sanitize paste to a plain / marked allowlist (configure the editor's paste handling so HTML `<script>`/`<style>` and non-allowlisted nodes are stripped).
- Destroy the editor in `ngOnDestroy`.

**Supported mark set (round-trip target):** h1, h2, h3, bold, italic, links, bullet lists, ordered lists, inline code.

**Fallback decision (state this explicitly so a failing round-trip test mid-implementation does not strand the work):**
1. If a specific mark fails the round-trip test, **constrain StarterKit to drop that mark** and document the exclusion in a code comment + the spec.
2. If even the constrained subset drifts unacceptably, **fall back to a token-styled raw-markdown `<textarea>`** (same `@Input/@Output` contract: load markdown, debounced emit). Naming this fallback up front means the round-trip spec is a gate, not a blocker.

### 4.3 `manuscript-surface/manuscript-surface.component.ts` (NEW)

Centered, `max-width: 680px`, padding `46px 32px 120px`. Inputs: `content: ContentDetail`, `canEdit: boolean`. Outputs: `titleChange: string`, `bodyChange: string`, `startDraft: void`.

Renders, top to bottom:
1. **Editable tag chips** (the existing tags; chip removal / add — reuse the current add/remove logic from `content-editor.component.ts`, or keep tags in the top area; minimum: render them).
2. **Editable title** — `DM Serif Display` (`var(--font-display)`) 40px, single-line contenteditable or an `input`. On change → emit `titleChange` (host → `updateField('title')`).
3. **Derived subtitle** — 19px, **display-only** (e.g. the first non-heading line of the body, or a transient field held in component state). **Never** emit it / never send to `updateField`. The spec asserts this.
4. **Byline** — avatar + `"{author} · {status}"`. There is no `author` field on `ContentDetail`; use a static studio author label (e.g. "You" / "Solo studio" to match the sidebar footer block) and `content.status`.
5. **Body** — `<app-prose-editor [value]="content.body" [readOnly]="!canEdit" (valueChange)="bodyChange.emit($event)" />`.

**Idea state:** when `content.status === ContentStatus.Idea`, REPLACE the prose area with a dashed panel — "This is still just an idea." + a "Start draft" button that emits `startDraft` (host → `onStartDraft()`, the existing handler). Draft+ renders `app-prose-editor`.

### 4.4 `stage-tracker/stage-tracker.component.ts` (NEW — pure presentational)

6 dots joined by 18px connector lines. `@Input() status: ContentStatus | null`. Active-dot index mapping (0-based, dots = `[Idea, Draft, Review, Approved, Scheduled, Published]`):

| status | active index |
|---|---|
| Idea | 0 |
| Draft | 1 |
| Review | 2 |
| Approved | 3 |
| Scheduled | 4 |
| Published | 5 |
| Archived | none (off the linear path) |

- Dots **before** the active index → "completed" (filled `var(--text-muted)`).
- The **active** dot → enlarged to 12px + its status color (from `STATUS_META[status].color`).
- Dots **after** → empty (border only).
- **Archived** → render all dots muted + a small "Archived" terminal label, no active dot.

Trivially unit-testable: assert the active index per enum value. Compute the index from a constant ordered array; map `Archived` → `-1`/null.

### 4.5 `editor-top-bar/editor-top-bar.component.ts` (NEW — 58px header)

Inputs: `status`, `contentType`, `primaryPlatform`, `voiceScore`, `isSaving`, `isDirty`, `panelOpen`. Output: `togglePanel: void`. Contents left→right:

- **"← Studio" back** — a button that `router.navigate(['/content'])` (inject `Router`).
- `<app-stage-tracker [status]="status" />`.
- **type·platform meta** — 12.5px display text, e.g. `"{contentType} · {primaryPlatform}"` (`var(--text-secondary)`).
- **"Saved" indicator** — inline `@if` reading the inputs (NOT a shared component): `@if (isSaving) { Saving… } @else if (isDirty) { Unsaved } @else { Saved }`, `var(--font-mono)`, `var(--text-muted)`. This mirrors the same `@if` that lived in the old top bar.
- `<app-voice-score-ring [score]="voiceScore" [size]="32" />`.
- **"✦ Assistant" toggle button** — shown when `panelOpen` is false (i.e. as a re-open affordance); clicking emits `togglePanel`. (The in-panel header also has a close that calls the same toggle in the parent.)

CSS: `height:58px; display:flex; align-items:center; gap:12px; padding:0 16px; border-bottom:1px solid var(--surface-border); flex-shrink:0;`.

### 4.6 `voice-meter/voice-meter.component.ts` (NEW — side-panel top)

Inputs: `contentId: string`, `voiceScore: number | null`, optional `feedback: string | null`. Inject `ContentService`.

Renders: a "Voice" label → a big mono value (`var(--font-mono)`) colored by `voiceBandColor(score)` → a track + fill bar (`width: score%`, fill colored by band, `transition: width 400ms`) → a one-line note keyed to band:
- `>= 80` → confident note (e.g. "Sounds like you.")
- `>= 60` → close note (e.g. "Close — tighten the voice.")
- else / null → flat note (e.g. "Doesn't sound like you yet.")
Prefer `VoiceCheckResult.feedback` text when present (after a re-check) over the canned note.

**Re-check affordance:** a small button that calls `contentService.voiceCheck(contentId)`; on the emitted `VoiceCheckResult` update the displayed score (`result.score`) and feedback (`result.feedback`). Hold `displayScore`/`displayFeedback` in internal signals seeded from the inputs so the meter can self-update without round-tripping through the store. (Backend `VoiceCheckResult.score` is the same 0–100 scale used by `voiceScore` — verify the real range before shipping; if it returns 0–1, scale ×100 for display and band logic. The service spec mock uses `{ score: 0.85 }` — confirm against the live API.)

### 4.7 `sidecar-chat/sidecar-chat.component.ts` (MODIFY — inline + restyle)

**Drop the `p-drawer` chrome entirely.** Render as an inline panel (the component now lives inside the 340px `.side-panel` below the voice meter; bg `var(--surface-inset)`). Remove the `visible` / `visibleChange` inputs/outputs and the `DrawerModule` import. Keep everything else.

Restyle:
- Assistant bubbles → `var(--surface-elevated)` + `1px solid var(--surface-border)`.
- User bubbles → brand bg (`var(--brand-primary)`) + dark text (`#1a0f0a`).
- **Replace the skeleton shimmer** (`.skeleton-shimmer` / `@keyframes shimmer`) with a 3-dot "blink" thinking animation shown during `store.isStreaming()` (three dots, staggered opacity blink).
- Keep the quick-action chips (empty body → "Draft from idea" / "Draft from scratch"; has body → "Refine" / "Shorten" / "Expand" / "Change tone"), the input + send, and the `✓ Apply to draft` (`store.applyToEditor`) / `⧉ Copy` (`navigator.clipboard.writeText`) actions.
- Recolor all hardcoded hexes to tokens.

**SignalR wiring UNCHANGED:** keep `signalRService.connect()/disconnect()`, the `tokens$` → `appendToken`, `generationComplete$` → `completeGeneration`, `generationError$` handling, and `sendChatMessage(contentId, text)`. Do not touch `signalr.service.ts`.

### 4.8 Action bar + `platform-targets/platform-targets.component.ts` (MODIFY — restyle, logic kept)

The action bar (`footer.editor-action-bar` in `content-editor.component.ts`):
- **Left:** a "Targets" label + an `<app-platform-dot>` per selected target. Reuse the existing `platform-targets` selection logic for which platforms are chosen; the dots are a restyled read-render of the targets (the full checkbox `platform-targets` strip can stay as the editing surface, restyled — `platform-targets.spec.ts` is a restyle-only update, selection logic unchanged).
- **Right:** the existing `@switch(store.content()?.status)` `p-button` block — **keep every handler exactly** (`onStartDraft` / `autoSave` / `onApprove` / `onSubmitForReview` / `onRequestChanges` / `onSchedule` / `onPublish` / `onUnschedule` / `onUnpublish` / `onRestore`). Restyle: primary buttons `background: var(--brand-primary); color: #1a0f0a;`; ghost buttons `background: transparent; border: 1px solid var(--surface-border);`. Approved/Scheduled "Publish"/"Schedule" open the publish modal (section-03) via the existing `onPublish`/`onSchedule`.

`platform-targets.component.ts` itself: recolor its hardcoded hexes to tokens; do not change `togglePlatform`, `isSelected`, `isConnected`, `isPrimary`, or the `targetPlatformsChange` emit.

### 4.9 Delete `markdown-editor/` (after 4.1 removes the importer)

`content-editor.component.ts` is the only importer of `<app-markdown-editor>`. After 4.1 removes that import + the template usage, **delete** the component and its spec: `content-editor/markdown-editor/markdown-editor.component.ts` + `.spec.ts`.

Then check whether `@acrodata/code-editor` / `@codemirror/*` have **any other users** in `src/PersonalBrandAssistant.Web/src/` (grep the imports). If `markdown-editor` was the only consumer and nothing else imports them, remove those deps from `package.json`. If anything else imports them, leave the deps in place and note it.

---

## Acceptance checklist

- `prose-editor` round-trip spec is green for the supported mark set (or excluded marks are documented; or the documented `<textarea>` fallback is in place). This test was written FIRST.
- `content-editor` template has no `p-splitter`, no `<app-markdown-editor>`, no `<markdown>` preview; `<app-manuscript-surface>` is present.
- `/content/new?topic=…&type=…&sourceIdeaId=…` seeds `create()` from the query params (no longer the fixed `Untitled` Blog stub).
- Autosave still fires only for Idea/Draft/Review; never Approved+.
- `stage-tracker` shows the correct active dot per status; Archived is the all-muted terminal state.
- `voice-meter` colors by `voiceBandColor` (`>=80`/`>=60`/else) and re-check calls `ContentService.voiceCheck` and updates the display.
- `sidecar-chat` is an inline panel (no `p-drawer`), uses the 3-dot thinking animation, and its SignalR wiring is unchanged. `signalr.service.ts` was not touched.
- Action bar: every status handler preserved; primary/ghost restyled to tokens.
- `markdown-editor/` deleted; orphaned codemirror deps removed only after confirming no other importer.
- No GitHub-dark hexes remain in any editor file; all colors are `var(--…)`.
- `ng test` (the PROJECT_CONFIG command) is green; `ng build` is clean.
