# Section 15: Content Editor Page

## Overview

This section implements the content editor -- the page where users create new content or edit existing content with AI assistance. It includes the main editor component, a CodeMirror 6 markdown editor (wrapped for swappability), an AI action toolbar, a bottom status-action bar, and auto-save with optimistic concurrency.

### Dependencies

- **Section 12 (Angular Models and Services):** `ContentDetail`, `Content`, `ContentStatus`, `ContentType`, `Platform`, `DraftContentRequest`, `UpdateContentRequest`, `ContentService` -- all TypeScript interfaces/enums and the HTTP client.
- **Section 13 (Angular Stores):** `ContentEditorStore` -- component-scoped signal store with `loadContent()`, `updateField()`, `autoSave()`, `applyToEditor()`, `reset()`, state signals (`content`, `isDirty`, `isSaving`, `isStreaming`, `chatMessages`, `currentTokens`, `error`).
- **Section 14 (Content List Page):** `content.routes.ts` already defines routes for `/content/new` and `/content/:id` that lazy-load the editor component. The editor component must export as `ContentEditorComponent`.
- **npm packages (installed in section 12):** `@acrodata/code-editor`, `@codemirror/lang-markdown`, `ngx-markdown`, `prismjs`.

**Blocks:** Section 16 (Sidecar Chat Panel) depends on this section -- the chat panel is a child of the editor.

## Implementation Notes (Actual)

### Files Created
```
content-editor/
  content-editor.component.ts              (inline template+styles, NO separate .html/.css)
  content-editor.component.spec.ts         (16 tests)
  markdown-editor/
    markdown-editor.component.ts           (inline template+styles)
    markdown-editor.component.spec.ts      (4 tests)
  editor-toolbar/
    editor-toolbar.component.ts            (inline template+styles, exports DraftActionEvent)
    editor-toolbar.component.spec.ts       (6 tests)
```

### Deviations from Plan
1. **No separate .html template files** — all components use inline templates in the `@Component` decorator, matching the codebase pattern.
2. **ChipsModule replaced with ChipModule + InputTextModule** — PrimeNG v19 does not export `ChipsModule` from `primeng/chips`. Used `ChipModule` (primeng/chip) with `[removable]="true"` + plain `input` with `(keydown.enter)` for tag add.
3. **@acrodata/code-editor binds via individual inputs** — no `[options]` input exists. Uses `[value]`, `[extensions]`, `[languages]`, `[readonly]`, `language="markdown"`, and `(change)` output (not `(valueChange)`).
4. **LanguageDescription.of()** required for the `[languages]` input — direct `markdown()` import wasn't accepted. Uses `LanguageDescription.of({ name: 'markdown', alias: ['md'], extensions: ['md'], load: () => Promise.resolve(markdown()) })`.
5. **ngx-markdown import**: Uses `MarkdownComponent as MarkdownRenderer` (standalone component import), not `MarkdownModule`.
6. **app.config.ts**: Only `provideMarkdown()` was added to existing providers array. Original providers preserved (interceptors, PrimeNG theming, animations, MessageService).
7. **EditorToolbarComponent**: Uses `p-chip` elements instead of styled `p-button` chips. Exports `DraftActionEvent` interface.
8. **Published status action bar**: Only shows "Unpublish" (no "View Published" — no published URL available yet).

### Code Review Fixes Applied
- Error handling on `create()` subscribe (new mode) — navigates to `/content` on failure
- Error handling on `doStatusAction()` — reloads content from server on failure
- Platform validation on `onCrossPostAction()` — validates against `Object.values(Platform)`
- Date validation on `onSchedule()` — validates ISO date parse before sending

### Test Coverage
- **ContentEditorComponent**: 16 tests (init modes, UI elements, auto-save debounce, status-gated save prevention, field update dispatch)
- **EditorToolbarComponent**: 6 tests (chip rendering, action emission, loading state, status disable, cross-post)
- **MarkdownEditorComponent**: 4 tests (creation, input binding, value emission, readOnly default)
- **Total**: 26 tests, all passing

---

## File Structure

All files under `src/PersonalBrandAssistant.Web/src/app/features/content/`:

```
content-editor/
  content-editor.component.ts              (NEW - main editor page)
  content-editor.component.html            (NEW - template)
  content-editor.component.spec.ts         (NEW - tests)
  markdown-editor/
    markdown-editor.component.ts           (NEW - CodeMirror 6 wrapper)
    markdown-editor.component.spec.ts      (NEW - tests)
  editor-toolbar/
    editor-toolbar.component.ts            (NEW - AI action chips)
    editor-toolbar.component.spec.ts       (NEW - tests)
```

No backend files are created in this section.

---

## Tests FIRST

### ContentEditorComponent Tests

**File:** `src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/content-editor.component.spec.ts`

```typescript
describe('ContentEditorComponent', () => {
  // Setup: TestBed with ContentEditorComponent
  // Provide: provideHttpClient(), provideRouter([{ path: 'content/:id', component: ContentEditorComponent }])
  // Provide: ContentEditorStore (component-scoped, but override in TestBed providers)
  // Provide: ContentService as jasmine.SpyObj
  // Mock ActivatedRoute with paramMap containing 'id' param

  // Mock content for edit mode:
  // { id: 'abc-123', title: 'Test Post', body: '# Hello', status: 'Draft',
  //   contentType: 'BlogPost', primaryPlatform: 'Blog', voiceScore: 85,
  //   tags: ['angular'], updatedAt: '2026-01-01T00:00:00Z', ... }

  it('should load content on init when route has id param (edit mode)');
  // ActivatedRoute.paramMap has id='abc-123'
  // Expect store.loadContent to have been called with 'abc-123'

  it('should create content on init when route is /content/new (new mode)');
  // ActivatedRoute.paramMap has no id (or special 'new' detection)
  // Expect contentService.create to have been called
  // Expect router.navigate to ['/content/', newId] after creation

  it('should render platform selector dropdown');
  // Query data-testid="platform-selector", expect truthy

  it('should render content type selector dropdown');
  // Query data-testid="type-selector", expect truthy

  it('should render status badge reflecting current status');
  // Store has content with status='Draft'
  // Query data-testid="status-badge", expect text to contain 'Draft'

  it('should render tags input');
  // Query data-testid="tags-input", expect truthy

  it('should render voice score knob when score exists');
  // Store has content with voiceScore=85
  // Query data-testid="voice-knob", expect truthy

  it('should render auto-save indicator showing Saved when not dirty');
  // store.isDirty()=false, store.isSaving()=false
  // Query data-testid="save-indicator", expect text 'Saved'

  it('should render auto-save indicator showing Saving... when saving');
  // store.isSaving()=true
  // Expect text 'Saving...'

  it('should render auto-save indicator showing Unsaved when dirty');
  // store.isDirty()=true, store.isSaving()=false
  // Expect text 'Unsaved changes'

  it('should render bottom action bar with correct buttons for Draft status');
  // Status=Draft -> expect [Save Draft] [Approve] [Submit for Review] buttons
  // Query data-testid="action-bar", check button labels

  it('should render bottom action bar with correct buttons for Approved status');
  // Status=Approved -> expect [Schedule] [Publish Now] buttons

  it('should render bottom action bar with Restore button for Archived status');
  // Status=Archived -> expect [Restore] button

  it('should call store.updateField when platform dropdown changes');
  // Spy on store.updateField, simulate dropdown change
  // Expect called with ('primaryPlatform', newValue)

  it('should trigger auto-save debounce when editor content changes');
  // fakeAsync: simulate body change, tick(3000), expect store.autoSave called

  it('should NOT auto-save when status is Published');
  // Set content.status=Published, simulate body change, tick(3000)
  // Expect store.autoSave NOT called
});
```

### MarkdownEditorComponent Tests

**File:** `src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/markdown-editor/markdown-editor.component.spec.ts`

```typescript
describe('MarkdownEditorComponent', () => {
  // Setup: TestBed with MarkdownEditorComponent
  // Note: CodeMirror has DOM dependencies. Tests focus on component I/O, not editor internals.

  it('should create the component');
  // Basic instantiation check

  it('should accept value input');
  // Set input value='# Hello', detectChanges, component should receive it

  it('should emit valueChange on content change');
  // Spy on valueChange output
  // Simulate editor content change (via component method or direct call)
  // Expect valueChange.emit called with new text

  it('should be read-only when readOnly input is true');
  // Set readOnly=true, detectChanges
  // Verify editor config has readOnly extension applied
});
```

### EditorToolbarComponent Tests

**File:** `src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/editor-toolbar/editor-toolbar.component.spec.ts`

```typescript
describe('EditorToolbarComponent', () => {
  // Setup: TestBed with EditorToolbarComponent
  // Provide: ContentService as spy

  it('should render AI action chips');
  // Query chip buttons: Draft, Refine, Shorten, Expand, Change Tone, Cross-Post

  it('should emit draftAction with correct action string on chip click');
  // Click "Refine" chip
  // Expect draftAction output emitted with { action: 'refine' }

  it('should emit draftAction with toneName for Change Tone');
  // Click "Change Tone", expect action='changeTone' with toneName selection

  it('should show loading state when isLoading input is true');
  // Set isLoading=true, detectChanges
  // Expect chips to be disabled or overlay visible

  it('should disable chips when content status is Published');
  // Set status='Published', detectChanges
  // Expect chips disabled

  it('should emit crossPostAction on Cross-Post chip click');
  // Click "Cross-Post" chip
  // Expect crossPostAction output emitted
});
```

---

## Implementation Details

### 1. ContentEditorComponent

**File:** `src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/content-editor.component.ts`

This is the main editor page component. It operates in two modes: **new** (creates empty content, then redirects to `/content/{newId}`) and **edit** (loads existing content by route param ID).

**Component metadata:**
- `standalone: true`
- `providers: [ContentEditorStore]` -- component-scoped store, fresh per editor instance
- Imports: `FormsModule`, `MarkdownEditorComponent`, `EditorToolbarComponent`, `MarkdownModule` (from ngx-markdown), PrimeNG modules (ButtonModule, SelectModule, TagModule, ChipsModule, KnobModule, SplitterModule, TooltipModule, InputTextModule)

**Injected dependencies:**
- `ContentEditorStore` -- component-scoped
- `ContentService` -- for create (new mode) and draft/approve/publish/etc. actions
- `ActivatedRoute` -- to read `:id` param
- `Router` -- to navigate after creation
- `DestroyRef` -- cleanup auto-save timer

**Initialization logic (`ngOnInit`):**
```
1. Read route paramMap
2. If param 'id' exists AND is not 'new':
   - Call store.loadContent(id) (edit mode)
3. Else (new mode):
   - Call contentService.create({ title: 'Untitled', contentType: default, primaryPlatform: default })
   - On success: navigate to /content/{newId}, then store.loadContent(newId)
```

Note: Section 14's `content.routes.ts` defines the `new` route as a separate path segment (`path: 'new'`), so the ActivatedRoute will NOT have an `id` param for new content. Detect new mode by checking if `route.snapshot.paramMap.has('id')` is false, OR by checking if the URL ends with `/new`.

**Layout structure (three areas):**

**Top bar** -- horizontal flex row containing:
- PrimeNG `Select` for Platform selection (bound to `store.content()?.primaryPlatform`)
- PrimeNG `Select` for ContentType selection (bound to `store.content()?.contentType`)
- PrimeNG `Tag` for status badge (read-only, color from status)
- PrimeNG `Chips` for tags (bound to `store.content()?.tags`)
- PrimeNG `Knob` for voice score (48px diameter, read-only, color-coded: green >80, amber 60-80, red <60). Only shown when `store.content()?.voiceScore` is non-null.
- Auto-save indicator text: "Saved" / "Saving..." / "Unsaved changes"

**Main editor area** -- PrimeNG `Splitter` with horizontal orientation, two panels:
- Left panel: `MarkdownEditorComponent` bound to `store.content()?.body`
- Right panel: `ngx-markdown` rendered preview of the body content

Above the editor area: `EditorToolbarComponent` with AI action chips.

**Bottom action bar** -- contextual buttons based on current content status:
- **Idea:** `[Start Draft]`
- **Draft:** `[Save Draft]` `[Approve]` `[Submit for Review]`
- **Review:** `[Approve]` `[Request Changes]`
- **Approved:** `[Schedule]` `[Publish Now]`
- **Scheduled:** `[Unschedule]` (show scheduled time as text)
- **Published:** `[Unpublish]` `[View Published]`
- **Archived:** `[Restore]`

Each button calls the corresponding `ContentService` method (approve, publish, schedule, etc.), then reloads the content from the store to reflect the new status.

**Status badge colors** (matching section 14 card colors):
- Idea: `info` severity
- Draft: `warn` severity
- Review: `secondary` severity (purple tint via CSS override)
- Approved: `success` severity
- Scheduled: `contrast` severity (cyan tint via CSS override)
- Published: `success` severity (brighter green)
- Archived: `secondary` severity

**Auto-save implementation:**

Auto-save fires when the editor body or title changes, debounced by 3 seconds.

```
- On editor valueChange: set a 3-second timeout
- On title input change: set the same 3-second timeout (clear previous)
- When timeout fires:
  1. Check if status is Idea, Draft, or Review (auto-saveable statuses)
  2. If not auto-saveable: skip
  3. Call store.autoSave() (which internally calls contentService.update with lastUpdatedAt)
- On component destroy: clear timeout via DestroyRef
```

Auto-save indicator logic:
```
isSaving()  -> "Saving..."
isDirty()   -> "Unsaved changes"
else        -> "Saved"
```

On optimistic concurrency conflict (409 from backend): show a toast/message "Content was modified elsewhere. Refresh to continue." The store's `autoSave()` method handles this by setting an error state rather than clearing dirty.

**AI toolbar action handling:**

When `EditorToolbarComponent` emits a `draftAction` event:
1. Set a loading flag (passed to toolbar and editor as `isLoading`)
2. Call `contentService.draft(contentId, { action, instructions?, toneName? })`
3. On success: update the store content with the response (new body, new updatedAt)
4. Clear loading flag
5. Optionally show an undo toast (stretch -- basic implementation just overwrites)

When `EditorToolbarComponent` emits a `crossPostAction` event:
1. Show a platform selection dialog (simple PrimeNG Select in a dialog)
2. Call `contentService.crossPost(contentId, { targetPlatform })`
3. On success: navigate to `/content/{childId}` to edit the new cross-post

### 2. MarkdownEditorComponent

**File:** `src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/markdown-editor/markdown-editor.component.ts`

This component wraps CodeMirror 6 via `@acrodata/code-editor`, isolating the editor dependency for easy swapping if the library breaks or a better option appears.

**Component metadata:**
- `standalone: true`
- `selector: 'app-markdown-editor'`
- Imports: `CodeEditorModule` (from `@acrodata/code-editor`)

**Inputs:**
- `value: string` -- the markdown content (input signal)
- `readOnly: boolean` -- disables editing (default false)

**Outputs:**
- `valueChange: EventEmitter<string>` -- emits on content change

**CodeMirror configuration:**
- Language: `markdown()` from `@codemirror/lang-markdown`
- Theme: custom dark theme matching PrimeNG Aura Dark palette:
  - Background: `#0d1117`
  - Text: `#f0f6fc`
  - Selection: `#1f6feb44`
  - Cursor: `#58a6ff`
  - Gutter background: `#161b22`
  - Gutter text: `#8b949e`
- Extensions: line numbers, bracket matching, code folding, search (`@codemirror/search`)
- Height: fills available space (CSS `height: 100%`)

The `@acrodata/code-editor` component provides two-way binding via `[(value)]` or `(valueChange)`. Wire the input value to the editor, and forward `valueChange` events to the parent.

**Template (minimal):**
```html
<code-editor
  [value]="value()"
  [options]="editorOptions"
  (valueChange)="onValueChange($event)"
  class="editor-container">
</code-editor>
```

Where `editorOptions` is a readonly object containing the language, theme, and extensions configuration.

### 3. EditorToolbarComponent

**File:** `src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/editor-toolbar/editor-toolbar.component.ts`

Horizontal row of chip buttons representing AI actions.

**Component metadata:**
- `standalone: true`
- `selector: 'app-editor-toolbar'`
- Imports: `ButtonModule`, `ChipModule` (or just styled buttons)

**Inputs:**
- `isLoading: boolean` -- disables chips during AI generation
- `status: ContentStatus | null` -- determines which chips are enabled
- `hasBody: boolean` -- some actions require existing body content

**Outputs:**
- `draftAction: EventEmitter<{ action: string; instructions?: string; toneName?: string }>` -- Draft, Refine, Shorten, Expand, Change Tone actions
- `crossPostAction: EventEmitter<void>` -- triggers cross-post flow

**Chips:**
| Chip | Action string | Enabled when |
|------|--------------|--------------|
| Draft | `'draft'` | Status is Idea or Draft, no body yet |
| Refine | `'refine'` | Body exists |
| Shorten | `'shorten'` | Body exists |
| Expand | `'expand'` | Body exists |
| Change Tone | `'changeTone'` | Body exists |
| Cross-Post | n/a | Body exists, status is Draft or later |

All chips disabled when `isLoading=true` or status is Published/Archived.

**Template:** horizontal flex row with PrimeNG `Button` components styled as chips (rounded, outlined, small):
```html
<div class="toolbar" data-testid="editor-toolbar">
  <p-button label="Draft" icon="pi pi-pencil" [rounded]="true" [outlined]="true"
    size="small" [disabled]="isLoading() || !canDraft()"
    (onClick)="draftAction.emit({ action: 'draft' })" />
  <p-button label="Refine" icon="pi pi-sync" ... />
  <!-- etc. -->
</div>
```

### 4. Template Structure (content-editor.component.html)

**File:** `src/PersonalBrandAssistant.Web/src/app/features/content/content-editor/content-editor.component.html`

The template is described in prose; the implementer writes the actual HTML.

**Structure:**

```
<div class="editor-page" data-testid="content-editor-page">
  <!-- TOP BAR -->
  <header class="editor-top-bar">
    Platform Select | Type Select | Status Tag | Tags Chips | Voice Knob | Save Indicator
  </header>

  <!-- AI TOOLBAR -->
  <app-editor-toolbar
    [isLoading]="isAiLoading()"
    [status]="store.content()?.status"
    [hasBody]="!!store.content()?.body"
    (draftAction)="onDraftAction($event)"
    (crossPostAction)="onCrossPostAction()" />

  <!-- MAIN EDITOR AREA -->
  <p-splitter [style]="{ height: '100%' }" [panelSizes]="[50, 50]">
    <ng-template pTemplate>
      <app-markdown-editor
        [value]="store.content()?.body ?? ''"
        [readOnly]="!canEdit()"
        (valueChange)="onBodyChange($event)" />
    </ng-template>
    <ng-template pTemplate>
      <div class="preview-panel">
        <markdown [data]="store.content()?.body ?? ''" />
      </div>
    </ng-template>
  </p-splitter>

  <!-- BOTTOM ACTION BAR -->
  <footer class="editor-action-bar" data-testid="action-bar">
    @switch (store.content()?.status) {
      @case ('Idea') { <p-button label="Start Draft" ... /> }
      @case ('Draft') { Save Draft | Approve | Submit for Review }
      @case ('Review') { Approve | Request Changes }
      @case ('Approved') { Schedule | Publish Now }
      @case ('Scheduled') { Unschedule + scheduled time display }
      @case ('Published') { Unpublish | View Published }
      @case ('Archived') { Restore }
    }
  </footer>
</div>
```

**CSS layout:**
```css
.editor-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  gap: 0;
}
.editor-top-bar {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 8px 16px;
  border-bottom: 1px solid #21262d;
  flex-shrink: 0;
}
/* Splitter takes remaining space */
:host ::ng-deep .p-splitter {
  flex: 1;
  min-height: 0;
}
.preview-panel {
  padding: 16px;
  overflow-y: auto;
  height: 100%;
}
.editor-action-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 16px;
  border-top: 1px solid #21262d;
  flex-shrink: 0;
}
```

---

## Key Behaviors

### Two-mode initialization

The component detects new vs. edit mode from the route:

- **Edit mode:** `route.paramMap` contains `id`. Call `store.loadContent(id)`.
- **New mode:** route path is `new` (no `id` param). Call `contentService.create()` with defaults, then `router.navigate(['/content', newId])` which re-triggers the route with an `id` param, loading into edit mode.

### Auto-save rules

1. Auto-save triggers on body changes and title changes, debounced to 3 seconds.
2. Only auto-saves when status is `Idea`, `Draft`, or `Review`.
3. Sends `lastUpdatedAt` from the current `store.content()?.updatedAt` for optimistic concurrency.
4. On success: store updates `updatedAt` from server response, clears `isDirty`.
5. On 409 conflict: show error message, do NOT clear dirty (user must manually refresh).
6. On component destroy: clear any pending timeout.

### Read-only states

The editor should be read-only (no typing, no toolbar actions) when the content status is `Published`, `Scheduled`, or `Archived`. The `canEdit()` computed checks:

```typescript
canEdit(): boolean {
  const status = store.content()?.status;
  return status === 'Idea' || status === 'Draft' || status === 'Review';
}
```

Approved status is a gray area -- the plan says auto-save is blocked for Approved, but the user should still be able to view the body. Make it read-only for Approved as well (to edit, the user must unschedule or request changes first).

### Status action handlers

Each bottom bar button calls the appropriate service method and reloads:

```
onApprove():    contentService.approve(id).subscribe(() => store.loadContent(id))
onPublish():    contentService.publish(id).subscribe(() => store.loadContent(id))
onSchedule():   show date picker dialog, then contentService.schedule(id, { scheduledAt })
onUnschedule(): contentService.unschedule(id).subscribe(() => store.loadContent(id))
onUnpublish():  contentService.unpublish(id).subscribe(() => store.loadContent(id))
onRestore():    contentService.restore(id).subscribe(() => store.loadContent(id))
onSubmitForReview(): contentService.submitForReview(id).subscribe(() => store.loadContent(id))
onRequestChanges():  contentService.requestChanges(id).subscribe(() => store.loadContent(id))
```

The "Start Draft" button for Idea status calls `contentService.draft(id, { action: 'draft' })` which transitions status Idea -> Draft AND generates the body.

### AI toolbar action flow

1. User clicks an action chip (e.g., "Refine").
2. `EditorToolbarComponent` emits `draftAction` with `{ action: 'refine' }`.
3. `ContentEditorComponent.onDraftAction()`:
   a. Sets `isAiLoading = true`
   b. Calls `contentService.draft(contentId, request)`
   c. On success: store updates body and updatedAt from response, clears dirty
   d. Sets `isAiLoading = false`
4. During loading: toolbar chips disabled, optional loading overlay on editor.

---

## PrimeNG Components Used

| Component | Module Import | Usage |
|-----------|--------------|-------|
| Dropdown/Select | `SelectModule` from `primeng/select` | Platform and ContentType selectors |
| Tag | `TagModule` from `primeng/tag` | Status badge |
| Chips | `ChipsModule` from `primeng/chips` | Tags input |
| Knob | `KnobModule` from `primeng/knob` | Voice score gauge |
| Splitter | `SplitterModule` from `primeng/splitter` | Editor / Preview split |
| Button | `ButtonModule` from `primeng/button` | All action buttons and toolbar chips |
| Tooltip | `TooltipModule` from `primeng/tooltip` | Button tooltips |

---

## TypeScript Model Dependencies (from Section 12)

```typescript
// From features/content/models/content.model.ts
interface ContentDetail extends Content {
  body: string;
  viralityPrediction: number | null;
  sourceIdeaId: string | null;
  parentContentId: string | null;
  platformPublishes: PlatformPublish[];
  children: ChildContent[];
}

interface DraftContentRequest {
  action: 'draft' | 'refine' | 'shorten' | 'expand' | 'changeTone';
  instructions?: string;
  toneName?: string;
}

interface UpdateContentRequest {
  title?: string;
  body?: string;
  tags?: string[];
  contentType?: ContentType;
  primaryPlatform?: Platform;
  lastUpdatedAt: string;  // required -- optimistic concurrency
}

enum ContentStatus { Idea, Draft, Review, Approved, Scheduled, Published, Archived }
enum ContentType { BlogPost, LinkedInPost, Tweet, ThreadedTweet, SubstackNewsletter, RedditPost, YouTubeVideo, YouTubeShort }
enum Platform { Blog, Substack, LinkedIn, Twitter, Reddit, YouTube }
```

## Store Dependencies (from Section 13)

```typescript
// ContentEditorStore -- component-scoped, NOT root
// Signals: content, isDirty, isSaving, isStreaming, chatMessages, currentTokens, error, loading
// Methods: loadContent(id), updateField(field, value), autoSave(), applyToEditor(text), reset()
// Computed: canAutoSave, hasContent, statusActions
```

The auto-save debounce timer lives in the **component** (ContentEditorComponent), not the store. The store's `autoSave()` is a synchronous-trigger method that guards on `isDirty` and calls the service.

---

## Styling Notes

All components use inline styles in `styles: [...]` within the component decorator, matching the established Ideas feature pattern. Color palette:

- Background: `#0d1117` (page), `#161b22` (panels)
- Border: `#21262d`
- Text primary: `#f0f6fc`
- Text secondary: `#8b949e`
- Accent: `#58a6ff`
- Voice knob colors: green `#3fb950` (>80), amber `#d29922` (60-80), red `#f85149` (<60)

---

## Platform Preview (Stretch Goal)

The plan mentions a platform preview tab component as a stretch goal. This section does NOT implement it. The right pane of the Splitter renders a generic `ngx-markdown` preview. If platform preview is desired later, add a tab component wrapping the right pane with platform-specific rendering (Blog HTML template, LinkedIn 3000-char truncation, Twitter 280-char thread splitting). This would be a separate component (`platform-preview.component.ts`) added as a future enhancement.

---

## Sidecar Chat Integration Point

Section 16 (Sidecar Chat Panel) adds a `SidecarChatComponent` as a PrimeNG Sidebar overlaying the editor. This section should include a toggle button for opening the chat panel (bottom-right floating button), but the actual chat component is built in section 16. For now, include the toggle button in the template and a `chatPanelVisible` signal, wired to nothing until section 16 provides the component.

```html
<!-- In content-editor.component.html, after the splitter -->
<p-button icon="pi pi-comments" [rounded]="true"
  class="chat-toggle-btn"
  (onClick)="chatPanelVisible.set(!chatPanelVisible())"
  pTooltip="AI Chat (Ctrl+Shift+C)" data-testid="chat-toggle" />
```

CSS for the toggle button:
```css
.chat-toggle-btn {
  position: fixed;
  bottom: 72px;  /* above action bar */
  right: 24px;
  z-index: 100;
}
```
