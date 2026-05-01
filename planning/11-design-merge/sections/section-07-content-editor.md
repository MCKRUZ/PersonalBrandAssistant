# Section 07: Content Editor

## Overview

The Content Editor is the most complex screen in the PBA frontend. It provides a full content editing experience at `/content/:id/edit` and `/content/new` with auto-save, brand voice scoring, version history, agent execution history, platform preview, and sidecar draft integration.

**Dependencies**: This section depends on:
- **Section 01 (Backend Extensions)**: 4-axis `BrandVoiceScore` model, `POST /api/brand-voice/score` endpoint with hash-based caching
- **Section 04 (App Shell)**: Route configuration, core models, shared layout
- **Section 05 (Sidecar)**: `DraftApplyService` for receiving sidecar drafts, `SidecarStore` for sending prompts from action bar buttons
- **Section 03 (Design System)**: `ScoreGaugeComponent`, `AxisBarComponent`, `StatusBadgeComponent` shared atoms

**Blocks**: Section 14 (Blog Editor) extends this component with blog-specific configuration.

---

## Tests First

All tests use Jasmine/Karma via Angular TestBed. Write these tests before implementing the production code.

### File: `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/content-editor.store.spec.ts`

```typescript
// --- ContentEditorStore tests ---

// Test: ContentEditorStore loads content by ID from GET /api/content/{id}
//   Arrange: mock ContentApiService.getById to return a Content object
//   Act: call store.loadContent('abc-123')
//   Assert: store.content() matches the returned Content; store.isLoading() is false

// Test: ContentEditorStore creates new content via POST /api/content for /content/new route
//   Arrange: mock ContentApiService.create to return { id: 'new-id' }
//   Act: call store.createContent({ contentType: 'SocialPost', body: 'hello' })
//   Assert: store.content() has id 'new-id'

// Test: ContentEditorStore auto-saves with 1s debounce via PUT /api/content/{id}
//   Arrange: load existing content, mock ContentApiService.update
//   Act: call store.updateBody('changed text'), advance timer by 1000ms
//   Assert: ContentApiService.update called once with the new body and correct version

// Test: ContentEditorStore does not auto-save until content has an ID
//   Arrange: store.content() is undefined (new route, not yet POSTed)
//   Act: call store.updateBody('text')
//   Assert: ContentApiService.update NOT called even after debounce

// Test: ContentEditorStore sends Version as ETag header on save
//   Arrange: load content with version: 3
//   Act: trigger auto-save
//   Assert: PUT request includes If-Match header with value "3"

// Test: ContentEditorStore handles 409 conflict with error state
//   Arrange: mock ContentApiService.update to return 409
//   Act: trigger auto-save
//   Assert: store.saveError() is 'conflict', store.isSaving() is false

// Test: ContentEditorStore serializes save calls (no overlapping PUTs)
//   Arrange: first save is in-flight (not resolved)
//   Act: trigger a second save
//   Assert: second PUT waits for first to complete before sending

// Test: ContentEditorStore.scoreContent calls POST /api/brand-voice/score
//   Arrange: mock BrandVoiceApiService.score to return 4-axis score
//   Act: call store.scoreContent()
//   Assert: store.brandScore() has Authoritative, Pragmatic, Concise, Practitioner values

// Test: ContentEditorStore.approveAndPublish calls approve then publish in sequence
//   Arrange: mock both approve and publish endpoints
//   Act: call store.approveAndPublish()
//   Assert: approve called first, then publish; store.content().status becomes 'Published'

// Test: ContentEditorStore.scheduleContent calls POST /api/scheduling/{id}/schedule
//   Arrange: mock scheduling endpoint
//   Act: call store.scheduleContent('2026-05-15T10:00:00Z')
//   Assert: endpoint called with correct datetime, store.content().scheduledAt updated

// Test: ContentEditorStore.loadVersions populates versions array
// Test: ContentEditorStore.loadHistory populates executionHistory array
```

### File: `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/content-editor.component.spec.ts`

```typescript
// --- ContentEditorComponent tests ---

// Test: ContentEditorComponent renders platform selector, type selector, status badge
//   Arrange: provide store with loaded content (platform: LinkedIn, type: SocialPost, status: Draft)
//   Assert: platform dropdown shows LinkedIn, type dropdown shows SocialPost, StatusBadgeComponent renders 'Draft'

// Test: ContentEditorComponent shows textarea for SocialPost, Quill for BlogPost
//   Arrange: load content with contentType 'SocialPost'
//   Assert: textarea element exists, p-editor does not
//   Arrange: load content with contentType 'BlogPost'
//   Assert: p-editor element exists, textarea does not

// Test: ContentEditorComponent character counter shows current/target with color coding
//   Arrange: content body is 142 chars, target platform is TwitterX (280 limit)
//   Assert: counter shows "142 / 280", counter element has 'green' class
//   Arrange: body is 252 chars (90% of 280)
//   Assert: counter has 'yellow' class
//   Arrange: body is 300 chars
//   Assert: counter has 'red' class

// Test: Action bar Tighten button sends message to sidecar
//   Arrange: inject SidecarStore, load content
//   Act: click Tighten button
//   Assert: SidecarStore.sendMessage called with "Tighten this draft" plus content context

// Test: DraftApplyService subscription replaces editor body content on Apply
//   Arrange: inject DraftApplyService, render component with existing content
//   Act: emit draft text "new draft body" via DraftApplyService.apply$
//   Assert: store.content().body updated to "new draft body"
```

### File: `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/brand-voice-panel/brand-voice-panel.component.spec.ts`

```typescript
// --- BrandVoicePanelComponent tests ---

// Test: BrandVoicePanelComponent renders ScoreGauge with composite score
//   Arrange: provide brandScore with overallScore: 85
//   Assert: ScoreGaugeComponent input receives 85

// Test: BrandVoicePanelComponent renders 4 AxisBar components
//   Arrange: provide brandScore with Authoritative:90, Pragmatic:80, Concise:75, Practitioner:85
//   Assert: 4 AxisBarComponent instances rendered with correct labels and values

// Test: BrandVoicePanelComponent shows loading skeleton during scoring
//   Arrange: store.isScoring() returns true
//   Assert: skeleton elements visible, score gauge not rendered

// Test: BrandVoicePanelComponent shows issues list from score response
//   Arrange: provide brandScore with issues: ['Too casual', 'Missing CTA']
//   Assert: two issue items rendered with correct text

// Test: Score button triggers store.scoreContent()
//   Arrange: render component with loaded content
//   Act: click Score button
//   Assert: store.scoreContent() called once
```

### File: `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/tabs/preview-tab.component.spec.ts`

```typescript
// Test: Preview tab renders platform-specific preview (LinkedIn card, Tweet card, Blog markdown)
//   Arrange: content with targetPlatforms: ['LinkedIn']
//   Assert: LinkedIn preview card rendered with avatar, title, truncated text
```

### File: `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/tabs/history-tab.component.spec.ts`

```typescript
// Test: History tab loads agent executions from GET /api/agents/executions
//   Arrange: mock API to return 3 executions
//   Assert: 3 rows rendered with agent type, timestamp, token count, summary
```

### File: `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/tabs/versions-tab.component.spec.ts`

```typescript
// Test: Versions tab loads content versions and opens diff modal on comparison
//   Arrange: provide 3 versions
//   Act: select version 1 and version 3, click Compare
//   Assert: diff modal opens with side-by-side text
```

---

## Implementation Details

### New File Structure

All files are created under the Angular web project root:
`src/PersonalBrandAssistant.Web/src/app/pages/content-editor/`

```
pages/content-editor/
  content-editor.component.ts        # Main page component
  content-editor.component.spec.ts   # Component tests
  content-editor.store.ts            # NgRx SignalStore (feature-scoped)
  content-editor.store.spec.ts       # Store tests
  content-editor-api.service.ts      # API service wrapping content + brand voice + scheduling calls
  brand-voice-panel/
    brand-voice-panel.component.ts   # Score gauge + 4 axis bars + issues
    brand-voice-panel.component.spec.ts
  tabs/
    preview-tab.component.ts         # Platform-specific content preview
    preview-tab.component.spec.ts
    history-tab.component.ts         # Agent execution history
    history-tab.component.spec.ts
    versions-tab.component.ts        # Version list + diff modal
    versions-tab.component.spec.ts
  version-diff-modal/
    version-diff-modal.component.ts  # Side-by-side diff dialog
```

### Modified Files

- `src/PersonalBrandAssistant.Web/src/app/shared/models/workflow.model.ts` -- Update `BrandVoiceScore` interface to use 4-axis model
- `src/PersonalBrandAssistant.Web/src/app/shared/models/enums.ts` -- Update `AutonomyLevel` type to 5 levels
- `src/PersonalBrandAssistant.Web/src/app/app.routes.ts` -- Add `/content/:id/edit` and `/content/new` routes pointing to new editor component

---

### Model Changes

#### Updated `BrandVoiceScore` (in `workflow.model.ts`)

The existing `BrandVoiceScore` interface uses 3 axes (`toneScore`, `vocabularyScore`, `personaScore`). Replace with the 4-axis model from the design:

```typescript
export interface BrandVoiceScore {
  readonly contentId: string;
  readonly overallScore: number;       // 0-100, weighted average of 4 axes
  readonly authoritative: number;      // 0-100
  readonly pragmatic: number;          // 0-100
  readonly concise: number;            // 0-100
  readonly practitioner: number;       // 0-100
  readonly issues: readonly string[];
  readonly ruleViolations: readonly string[];
  readonly scoredAt: string;
}
```

This is a breaking change to the interface. The existing `BrandVoicePanelComponent` at `features/content/components/brand-voice-panel.component.ts` references the old fields (`toneScore`, `vocabularyScore`, `personaScore`). That component is being replaced by the new one in this section, so the old file can remain until the content list page migration (section 10) removes references to it.

#### Updated `AutonomyLevel` (in `enums.ts`)

```typescript
export type AutonomyLevel = 'Manual' | 'Suggest' | 'Draft' | 'AutoPublish' | 'FullAuto';
```

Replaces the current `'Manual' | 'Assisted' | 'SemiAuto' | 'Autonomous'`.

---

### ContentEditorStore

**Location**: `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/content-editor.store.ts`

Feature-scoped NgRx SignalStore (provided at component level, not root). State is released on navigation away.

**State shape:**
```typescript
interface ContentEditorState {
  readonly content: Content | undefined;
  readonly brandScore: BrandVoiceScore | undefined;
  readonly versions: readonly Content[];
  readonly executionHistory: readonly AgentExecution[];
  readonly isLoading: boolean;
  readonly isSaving: boolean;
  readonly isScoring: boolean;
  readonly saveError: 'conflict' | 'network' | null;
  readonly activeTab: 'preview' | 'history' | 'versions';
}
```

**Key behaviors:**

1. **Load content**: `loadContent(id: string)` -- calls `GET /api/content/{id}`, patches `content` into state.

2. **Create content**: `createContent(request: CreateContentRequest)` -- calls `POST /api/content`, stores returned ID, then loads the full content by that ID.

3. **Auto-save with debounce**: An `effect()` watches changes to the content body. On change, it debounces for 1 second, then calls `PUT /api/content/{id}` with the current `version` as an `If-Match` ETag header. Save calls are serialized using a `concatMap` (queue, don't overlap). If the content has no ID yet (new, unsaved), the auto-save effect is a no-op.

4. **ETag concurrency**: The `PUT` request includes an `If-Match` header with the content's `version` field. On success, the store increments the local version. On `409 Conflict`, the store sets `saveError: 'conflict'` and stops auto-saving. The component shows a toast prompting the user to reload.

5. **Score content**: `scoreContent()` -- calls `POST /api/brand-voice/score` with `{ contentId }`. Sets `isScoring: true` during the call. The backend caches by content body hash, so re-scoring unchanged content is cheap. The component shows a loading skeleton in the brand voice panel during evaluation.

6. **Approve and publish**: `approveAndPublish()` -- calls `POST /api/approval/{id}/approve` then `POST /api/content-pipeline/{id}/publish` in sequence. Updates content status on success.

7. **Schedule**: `scheduleContent(dateTime: string)` -- calls `POST /api/scheduling/{id}/schedule`.

8. **Load versions**: `loadVersions()` -- fetches version history for the current content.

9. **Load history**: `loadHistory()` -- calls `GET /api/agents/executions?contentId={id}` to get agent interaction history.

---

### ContentEditorApiService

**Location**: `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/content-editor-api.service.ts`

Wraps the specific API calls needed by the content editor. Extends the pattern of the existing `ContentService` but adds ETag support.

```typescript
@Injectable({ providedIn: 'root' })
export class ContentEditorApiService {
  /** GET /api/content/{id} */
  getById(id: string): Observable<Content>;

  /** POST /api/content */
  create(request: CreateContentRequest): Observable<{ id: string }>;

  /** PUT /api/content/{id} with If-Match header for optimistic concurrency */
  update(id: string, request: UpdateContentRequest, version: number): Observable<void>;

  /** POST /api/brand-voice/score -- triggers LLM evaluation */
  scoreContent(contentId: string): Observable<BrandVoiceScore>;

  /** POST /api/approval/{id}/approve */
  approve(id: string): Observable<void>;

  /** POST /api/content-pipeline/{id}/publish */
  publish(id: string): Observable<void>;

  /** POST /api/scheduling/{id}/schedule */
  schedule(id: string, scheduledAt: string): Observable<void>;

  /** GET /api/agents/executions?contentId={id} */
  getExecutionHistory(contentId: string): Observable<readonly AgentExecution[]>;
}
```

The `update` method must add the `If-Match` header to the HTTP request. The existing `ApiService` base class does not support custom headers, so either extend it with an optional headers parameter or use `HttpClient` directly for this one method.

---

### ContentEditorComponent

**Location**: `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/content-editor.component.ts`

Standalone component, lazy-loaded at `/content/:id/edit` and `/content/new`.

**Template structure:**

1. **Header bar**: Editable title input, platform `p-select` dropdown, content type `p-select` dropdown, `StatusBadgeComponent` showing current status. Save indicator (shows "Saving..." during auto-save, checkmark on success, warning on conflict).

2. **Main layout** (two-column on desktop):
   - Left (wider): Editor area + tab bar
   - Right (narrower): Brand voice panel

3. **Editor area**: Conditionally renders based on `content.contentType`:
   - `SocialPost` / `Thread` / `VideoDescription`: `<textarea>` with `[(ngModel)]` or reactive form binding
   - `BlogPost`: PrimeNG `<p-editor>` (Quill-based rich text editor)

   Below the editor: character counter showing `{current} / {target}` with color classes:
   - Green: under 90% of platform limit
   - Yellow: 90-100% of platform limit
   - Red: over platform limit

   Platform character limits: TwitterX = 280, LinkedIn = 3000, Instagram = 2200, YouTube = 5000, Reddit = 40000, PersonalBlog/Substack = no limit.

4. **Tab bar** with three tabs:
   - **Preview**: `PreviewTabComponent` -- renders content as it would appear on the target platform
   - **History**: `HistoryTabComponent` -- list of agent executions for this content
   - **Versions**: `VersionsTabComponent` -- version list with diff comparison

5. **Action bar** (bottom or floating):
   - **Tighten**: Sends "Tighten this draft" to sidecar via `SidecarStore.sendMessage()` with content body as context
   - **Repurpose**: Sends "Repurpose this for {platform}" to sidecar
   - **Approve & Publish**: Calls `store.approveAndPublish()`
   - **Schedule**: Opens a `p-calendar` datetime picker, then calls `store.scheduleContent(selectedDate)`
   - **View Diff**: Opens `VersionDiffModalComponent` comparing current version with previous

**Lifecycle:**
- `OnInit`: Read route param `id`. If `id` exists, call `store.loadContent(id)`. If route is `/content/new`, show empty editor and wait for first save to POST.
- Subscribe to `DraftApplyService.apply$` to receive draft text from the sidecar. On emission, update the editor body with the draft text.
- `OnDestroy`: Unsubscribe from `DraftApplyService`, flush any pending auto-save.

---

### BrandVoicePanelComponent (New)

**Location**: `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/brand-voice-panel/brand-voice-panel.component.ts`

Replaces the existing `features/content/components/brand-voice-panel.component.ts` with the 4-axis design.

**Inputs:**
- `score: BrandVoiceScore | undefined` -- the current brand voice score
- `isScoring: boolean` -- whether a scoring call is in-flight

**Outputs:**
- `scoreRequested: EventEmitter<void>` -- emitted when the Score button is clicked

**Template:**
- When `isScoring` is true: render a loading skeleton (PrimeNG `p-skeleton` components mimicking the gauge + bars layout)
- When `score` is defined:
  - `ScoreGaugeComponent` with `[score]="score.overallScore"` (circular/semi-circular gauge, 0-100)
  - Four `AxisBarComponent` instances:
    - `[label]="'Authoritative'" [value]="score.authoritative"`
    - `[label]="'Pragmatic'" [value]="score.pragmatic"`
    - `[label]="'Concise'" [value]="score.concise"`
    - `[label]="'Practitioner'" [value]="score.practitioner"`
  - Issues list: `@for` loop over `score.issues`, rendering each as a bullet point with warning styling
  - Rule violations list: `@for` loop over `score.ruleViolations` with danger styling
- Score button: `<p-button label="Score" icon="pi pi-chart-bar" (onClick)="scoreRequested.emit()" [loading]="isScoring" />`

This component depends on `ScoreGaugeComponent` and `AxisBarComponent` from Section 03 (Design System). If those are not yet built, stub them as simple components that render the value as text.

---

### Tab Components

#### PreviewTabComponent

**Location**: `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/tabs/preview-tab.component.ts`

**Inputs:** `content: Content`

Renders a platform-specific preview based on `content.targetPlatforms[0]`:
- **LinkedIn**: Card with avatar placeholder, content title, truncated body with "...see more" after 150 chars, hashtags extracted from body
- **TwitterX**: Tweet card with 280-char limit display, link preview placeholder
- **PersonalBlog/Substack**: Rendered markdown (use a simple markdown pipe or PrimeNG's text display)
- **Default**: Plain text preview

#### HistoryTabComponent

**Location**: `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/tabs/history-tab.component.ts`

**Inputs:** `executions: readonly AgentExecution[]`

Renders a list/table of agent interactions. Each row shows: agent type badge, timestamp (relative, e.g., "2 hours ago"), token count (`inputTokens + outputTokens`), output summary text, cost formatted as `$X.XX`.

#### VersionsTabComponent

**Location**: `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/tabs/versions-tab.component.ts`

**Inputs:** `versions: readonly Content[]`

Renders a list of content versions sorted by version number descending. Each row shows: version number, updatedAt timestamp, body length. Two-selection mode: user selects two versions, then a "Compare" button opens `VersionDiffModalComponent`.

#### VersionDiffModalComponent

**Location**: `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/version-diff-modal/version-diff-modal.component.ts`

PrimeNG `p-dialog` showing side-by-side text comparison of two content versions. A simple line-by-line diff is sufficient -- split both texts by newline and highlight added/removed/changed lines with green/red background colors. No need for a full diff library in v1.

---

### DraftApplyService Integration

The `DraftApplyService` is created in Section 05 (Sidecar). It exposes an `apply$: Observable<string>` that emits when the user clicks "Apply" on a draft card in the sidecar.

The `ContentEditorComponent` subscribes to `DraftApplyService.apply$` in its `ngOnInit` and updates the editor body when a draft arrives. This subscription is only active when the component is mounted (i.e., on an editor route).

If Section 05 is not yet implemented, create a minimal stub:

```typescript
// src/app/shell/sidecar/draft-apply.service.ts
@Injectable({ providedIn: 'root' })
export class DraftApplyService {
  private readonly applySubject = new Subject<string>();
  readonly apply$ = this.applySubject.asObservable();

  applyDraft(text: string): void {
    this.applySubject.next(text);
  }
}
```

---

### Routing

Add the content editor routes to the app routing configuration. The route at `/content/:id/edit` lazy-loads `ContentEditorComponent`. The route at `/content/new` also loads `ContentEditorComponent` (the component checks for the absence of an `id` param to know it's in "create" mode).

Route data should include:
- `title: 'Content Editor'` (for the topbar)
- `sidecarContext: 'content-editor'` (for sidecar quick prompts)

---

### Character Limit Map

Define a constant map for platform character limits, used by the character counter:

```typescript
// In content-editor.component.ts or a shared constants file
export const PLATFORM_CHAR_LIMITS: Readonly<Record<string, number | null>> = {
  TwitterX: 280,
  LinkedIn: 3000,
  Instagram: 2200,
  YouTube: 5000,
  Reddit: 40000,
  PersonalBlog: null,  // no limit
  Substack: null,       // no limit
};
```

---

### Error Handling

Follow the project-wide error patterns:
- **Auto-save conflict (409)**: Set `saveError: 'conflict'` in store. Component shows PrimeNG toast: "Content was modified elsewhere. Reload?" with a Reload button that calls `store.loadContent(id)`.
- **Scoring timeout/failure**: `isScoring` resets to false. Brand voice panel shows "Scoring failed -- Retry" button.
- **Publish failure**: Status reverts to Approved in the store. Toast with error detail.
- **Network error on load**: `isLoading` resets to false, content remains undefined. Component shows empty state with retry.

---

## Implementation Notes (What Was Actually Built)

### Files Created
- `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/content-editor-api.service.ts`
- `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/content-editor.store.ts`
- `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/content-editor.component.ts`
- `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/content-editor.component.html`
- `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/content-editor.component.scss`
- `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/brand-voice-panel/brand-voice-panel.component.ts`
- `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/tabs/preview-tab.component.ts`
- `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/tabs/history-tab.component.ts`
- `src/PersonalBrandAssistant.Web/src/app/pages/content-editor/tabs/versions-tab.component.ts`

### Files Modified
- `src/PersonalBrandAssistant.Web/src/app/features/content/content.routes.ts` — Updated `new` and `:id/edit` routes to point to new ContentEditorComponent

### Test Files (29 tests total)
- `content-editor.store.spec.ts` — 10 tests (load, create, auto-save debounce, 409 conflict, scoring, approve+publish, schedule, apply draft)
- `content-editor.component.spec.ts` — 7 tests (create, init load, draft subscription, field changes, platform/type options, char limits)
- `brand-voice-panel/brand-voice-panel.component.spec.ts` — 3 tests (create, defaults, score emit)
- `tabs/preview-tab.component.spec.ts` — 3 tests (create, truncate long, truncate short)
- `tabs/history-tab.component.spec.ts` — 4 tests (create, empty state, relative date minutes, relative date hours)
- `tabs/versions-tab.component.spec.ts` — 2 tests (create, empty state)

### Deviations from Plan
1. **Auto-save uses `Subject` + `debounceTime` instead of `effect()`** — The plan suggested an `effect()` watching content body changes. Implementation uses `Subject<UpdateContentRequest>` piped through `debounceTime(1000) → concatMap` for more explicit control over serialization and error handling.
2. **`VersionDiffModalComponent` deferred** — Not built in this section. Will be added in a later section or as needed.
3. **`loadVersions()` deferred** — No versions endpoint exists yet. The versions tab renders from store state but the API call is stubbed.
4. **Model types used existing models** — Used existing `ContentItem`, `BrandVoiceScore`, `AgentExecution` from `core/models/` instead of creating new types. No breaking model changes were needed.
5. **Routes updated in `content.routes.ts`** not `app.routes.ts` — The content feature already has its own route file; routes were updated there.

### Code Review Fixes Applied
- Added `takeUntilDestroyed(destroyRef)` to all 7 bare `.subscribe()` calls in the store
- Fixed tautology test `expect(emitted || true)` → `expect(emitted)`
- Added `DecimalPipe` import to `HistoryTabComponent` (standalone components need explicit pipe imports)
- Fixed pipe precedence: `(exec.inputTokens + exec.outputTokens) | number`
- Fixed `$score-good` → `$score-success` in SCSS (variable doesn't exist in design system)
