# Section 14: Content List Page

## Overview

This section implements the content list page -- the primary UI for browsing, filtering, and managing content items. It follows the same structural patterns established by the Idea Bank page (Ideas feature), adapted for content-specific fields: status, platform, content type, voice score, and tags.

### Dependencies

- **Section 12 (Angular Models and Service):** Provides `Content`, `ContentStatus`, `ContentType`, `Platform` TypeScript interfaces/enums and `ContentService` HTTP client.
- **Section 13 (Angular Stores):** Provides `ContentStore` signal store with `loadContents()`, `setFilter()`, `setPage()`, `deleteContent()`, `toggleView()` methods and state signals (`contents`, `loading`, `totalCount`, `filters`, `pagination`, `viewMode`).

These must be implemented before this section. This section creates no backend code -- it is purely Angular frontend.

---

## File Structure

All files live under `src/PersonalBrandAssistant.Web/src/app/features/content/`:

```
features/content/
  content.routes.ts                              (NEW - child routes for /content)
  content-list/
    content-list.component.ts                    (NEW - main list page)
    content-list.component.spec.ts               (NEW - tests)
    content-card/
      content-card.component.ts                  (NEW - card for grid/list views)
      content-card.component.spec.ts             (NEW - tests)
    content-filter-sidebar/
      content-filter-sidebar.component.ts        (NEW - filter panel)
      content-filter-sidebar.component.spec.ts   (NEW - tests)
    view-toggle/
      view-toggle.component.ts                   (NEW - grid/list toggle)
    content-grid/
      content-grid.component.ts                  (NEW - grid layout)
    content-list-table/
      content-list-table.component.ts            (NEW - table layout)
```

**Modified files:**
```
src/PersonalBrandAssistant.Web/src/app/app.routes.ts     (MODIFY - change content route)
src/PersonalBrandAssistant.Web/src/app/features/content/content.component.ts  (DELETE or REPLACE)
```

---

## Tests FIRST

### ContentListComponent Tests

**File:** `src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-list.component.spec.ts`

```typescript
describe('ContentListComponent', () => {
  // Setup: TestBed with ContentListComponent, provideHttpClient(), provideRouter([])
  // Inject ContentStore, spy on loadContents

  it('should load contents on init');
  // Expect store.loadContents to have been called

  it('should render the two-column layout (sidebar + main)');
  // Query for data-testid="content-list-page", ".filter-sidebar", ".content-main"

  it('should render Content Studio title');
  // Query h1, expect "Content Studio"

  it('should render New Content button');
  // Query data-testid="new-content-btn", expect truthy

  it('should render search input');
  // Query data-testid="search-input", expect truthy

  it('should render content cards when contents exist');
  // Patch store state with mock contents, detectChanges, query app-content-card elements

  it('should show empty state when no contents');
  // Ensure store.contents() returns [], detectChanges, query ".empty-state"

  it('should navigate to /content/new on New Content click');
  // Spy on Router.navigate, trigger button click, expect navigation to ['/content/new']

  it('should show paginator when totalCount exceeds pageSize');
  // Patch store with totalCount > pageSize, detectChanges, query p-paginator

  it('should call store.setPage on paginator page change');
  // Spy on store.setPage, emit onPageChange event

  it('should debounce search input and call store.setFilter');
  // Type into search input, wait 300ms (fakeAsync + tick), expect setFilter called with searchText
});
```

### ContentCardComponent Tests

**File:** `src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-card/content-card.component.spec.ts`

```typescript
describe('ContentCardComponent', () => {
  // Setup: TestBed with ContentCardComponent
  // Mock content: { id, title, status: 'Draft', primaryPlatform: 'Blog', contentType: 'BlogPost',
  //   voiceScore: 85, tags: ['angular', 'typescript', 'ai', 'extra'], createdAt, updatedAt }

  it('should render title');
  // Query ".card-title", expect mock content title

  it('should render status badge with correct data-status attribute');
  // Query ".status-badge", check data-status matches content.status

  it('should render platform icon');
  // Query ".platform-icon" or "[data-platform]", check presence

  it('should render content type label');
  // Query ".content-type", check text matches

  it('should render voice score dot with correct color class');
  // voiceScore=85 -> green, voiceScore=70 -> amber, voiceScore=50 -> red

  it('should display max 3 tags and show +N more');
  // Query p-tag elements, expect 3. Query ".more-tags", expect "+1"

  it('should truncate long titles');
  // Set title to 200 chars, detectChanges, check truncation

  it('should emit edit event on edit button click');
  // Spy on edit output, click edit button, expect emit with content.id

  it('should emit delete event on delete button click');
  // Spy on delete output, click delete button, expect emit with content.id

  it('should emit duplicate event on duplicate button click');
  // Spy on duplicate output, click duplicate button, expect emit with content.id
});
```

### ContentFilterSidebarComponent Tests

**File:** `src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-filter-sidebar/content-filter-sidebar.component.spec.ts`

```typescript
describe('ContentFilterSidebarComponent', () => {
  // Setup: TestBed with ContentFilterSidebarComponent, provideHttpClient()
  // Inject ContentStore

  it('should render status filter checkboxes for all ContentStatus values');
  // Query status checkboxes, expect one per status (Idea, Draft, Review, Approved, Scheduled, Published, Archived)

  it('should render platform filter dropdown');
  // Query data-testid="platform-filter", expect truthy

  it('should render content type filter dropdown');
  // Query data-testid="type-filter", expect truthy

  it('should render date range pickers');
  // Query data-testid="date-from" and data-testid="date-to"

  it('should call store.setFilter when status checkbox toggled');
  // Spy on store.setFilter, toggle a checkbox, expect called with { status: 'Draft' }

  it('should call store.setFilter when platform dropdown changes');
  // Spy on store.setFilter, select platform, expect called

  it('should clear all filters on Clear All button click');
  // Click data-testid="clear-filters", expect store.setFilter called with all-null filter
});
```

---

## Implementation Details

### 1. Route Configuration

**Modify:** `src/PersonalBrandAssistant.Web/src/app/app.routes.ts`

Change the content route from `loadComponent` to `loadChildren`:

```typescript
// Before:
{ path: 'content', loadComponent: () => import('./features/content/content.component').then(m => m.ContentComponent) },

// After:
{ path: 'content', loadChildren: () => import('./features/content/content.routes').then(m => m.CONTENT_ROUTES) },
```

This mirrors the Ideas route pattern exactly.

### 2. Content Routes

**Create:** `src/PersonalBrandAssistant.Web/src/app/features/content/content.routes.ts`

Define child routes following the Ideas pattern:

```typescript
export const CONTENT_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./content-list/content-list.component').then(m => m.ContentListComponent),
  },
  {
    path: 'new',
    loadComponent: () =>
      import('../content-editor/content-editor.component').then(m => m.ContentEditorComponent),
  },
  {
    path: ':id',
    loadComponent: () =>
      import('../content-editor/content-editor.component').then(m => m.ContentEditorComponent),
  },
];
```

Note: The editor routes (`new` and `:id`) reference the editor component from section 15. They should be defined here even though the editor component doesn't exist yet. This will cause lazy-load failures until section 15 is implemented, but the routes need to be in place. If building incrementally, stub the editor component with a placeholder.

### 3. ContentListComponent

**Create:** `src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-list.component.ts`

Layout: two-column grid (sidebar + main area). Mirrors `IdeasComponent` structure.

Key behaviors:
- Inject `ContentStore` and call `loadContents()` in `ngOnInit()`
- Header with "Content Studio" title and "New Content" button (routerLink to `/content/new`)
- Search input with 300ms debounce (setTimeout pattern, same as Ideas)
- Grid/list view toggle via `ViewToggleComponent`
- Render content items via `ContentGridComponent` (grid mode) or `ContentListTableComponent` (list mode)
- PrimeNG Paginator at the bottom when `totalCount > pageSize`
- Confirmation dialog before delete (PrimeNG ConfirmDialog or simple window.confirm)

Template structure:
```html
<div class="content-layout" data-testid="content-list-page">
  <aside class="filter-sidebar">
    <app-content-filter-sidebar />
  </aside>
  <main class="content-main">
    <header><!-- title, new button, search, view toggle --></header>
    <!-- grid or list view based on store.viewMode() -->
    <!-- paginator -->
  </main>
</div>
```

CSS grid: `grid-template-columns: 240px 1fr` (two columns, no right sidebar unlike Ideas which has smart suggestions).

PrimeNG imports needed: `ButtonModule`, `PaginatorModule`, `InputTextModule`, `ConfirmDialogModule`.

### 4. ContentCardComponent

**Create:** `src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-card/content-card.component.ts`

Follows `IdeaCardComponent` patterns with content-specific fields.

Inputs:
- `content` -- required `Content` object (from section 12 models)

Outputs:
- `edit` -- emits content ID
- `delete` -- emits content ID
- `duplicate` -- emits content ID

Display elements:
- **Title** -- truncated to ~100 chars using a `truncate()` method
- **Status badge** -- PrimeNG `Tag` component, color-coded via `data-status` attribute:
  - Idea: blue (`#58a6ff` background)
  - Draft: yellow/amber (`#d29922` background)
  - Review: purple (`#bc8cff` background)
  - Approved: green (`#3fb950` background)
  - Scheduled: cyan (`#39d2c0` background)
  - Published: bright green (`#2ea043` background)
  - Archived: gray (`#8b949e` background)
- **Platform icon** -- PrimeNG icon or custom icon mapped by platform:
  - Blog: `pi pi-globe`
  - LinkedIn: `pi pi-linkedin`
  - Twitter: `pi pi-twitter`
  - Substack: `pi pi-envelope`
  - Reddit: `pi pi-reddit` (or fallback to `pi pi-comments`)
  - YouTube: `pi pi-youtube`
- **Content type label** -- small text, e.g., "Blog Post", "LinkedIn Post"
- **Voice score dot** -- small 8px colored circle:
  - Green (`#3fb950`): score > 80
  - Amber (`#d29922`): score 60-80
  - Red (`#f85149`): score < 60
  - Gray (`#8b949e`): no score (null)
- **Updated date** -- `DatePipe` with `shortDate` format
- **Tags** -- PrimeNG `Tag` with `severity="secondary"`, show max 3, "+N" for overflow

Actions (visible on hover via CSS `:hover` on `.card-actions`):
- Edit (pencil icon) -- emits `edit` with content ID
- Duplicate (copy icon) -- emits `duplicate` with content ID
- Delete (trash icon) -- emits `delete` with content ID

### 5. ContentFilterSidebarComponent

**Create:** `src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-filter-sidebar/content-filter-sidebar.component.ts`

Follows `IdeaFilterSidebarComponent` pattern.

Filter sections:
1. **Status** -- Checkboxes for each `ContentStatus` value (Idea, Draft, Review, Approved, Scheduled, Published, Archived). Single-select (toggling one unchecks others, same pattern as Ideas).
2. **Platform** -- PrimeNG `Select` dropdown with all `Platform` enum values. `showClear` for deselection.
3. **Content Type** -- PrimeNG `Select` dropdown with all `ContentType` enum values. `showClear` for deselection.
4. **Date Range** -- Two PrimeNG `DatePicker` components (From / To).
5. **Clear All** button at top.

Each filter change calls `store.setFilter()` with the updated filter key. The store handles resetting page to 1 and reloading.

### 6. ViewToggleComponent

**Create:** `src/PersonalBrandAssistant.Web/src/app/features/content/content-list/view-toggle/view-toggle.component.ts`

Identical pattern to Ideas `ViewToggleComponent`. Two PrimeNG buttons (grid icon, list icon). Calls `ContentStore.toggleView()`.

### 7. ContentGridComponent

**Create:** `src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-grid/content-grid.component.ts`

Identical pattern to `IdeaGridComponent`. CSS grid with `grid-template-columns: repeat(auto-fill, minmax(320px, 1fr))`. Iterates contents, renders `ContentCardComponent` for each. Empty state when no items.

Inputs: `contents` (Content array)
Outputs: `edit`, `delete`, `duplicate` (string -- content ID)

### 8. ContentListTableComponent

**Create:** `src/PersonalBrandAssistant.Web/src/app/features/content/content-list/content-list-table/content-list-table.component.ts`

Follows `IdeaListComponent` pattern. Table-like rows with columns:
- Status (dot)
- Title
- Platform
- Type
- Voice Score
- Updated Date
- Actions

Column headers are sortable (Title, Updated Date). Sorting calls `ContentStore.setSort()` (if the store supports it, otherwise defer sorting to later).

Inputs: `contents` (Content array)
Outputs: `edit`, `delete`, `duplicate` (string -- content ID)

---

## Existing Content Component

The existing `content.component.ts` at `src/PersonalBrandAssistant.Web/src/app/features/content/content.component.ts` is a placeholder shell. Since the route changes from `loadComponent` to `loadChildren`, this component is no longer directly loaded by the route. It can be deleted or repurposed. The `content.component.html` file can also be deleted.

If keeping backward compatibility is desired, leave them in place but unused. The new `ContentListComponent` in `content-list/` replaces this shell entirely.

---

## PrimeNG Components Used

- `TagModule` (from `primeng/tag`) -- status badges, tag chips
- `PaginatorModule` (from `primeng/paginator`) -- pagination
- `ButtonModule` (from `primeng/button`) -- action buttons, New Content button
- `SelectModule` (from `primeng/select`) -- platform and content type dropdowns
- `CheckboxModule` (from `primeng/checkbox`) -- status filter checkboxes
- `DatePickerModule` (from `primeng/datepicker`) -- date range filter
- `InputTextModule` (from `primeng/inputtext`) -- search input
- `ChipModule` (from `primeng/chip`) -- tags display (alternative to Tag for inline chips)
- `TooltipModule` (from `primeng/tooltip`) -- button tooltips

All of these are already used in the Ideas feature and should be available without additional npm installs.

---

## TypeScript Model Dependencies (from Section 12)

The content list page depends on these types defined in section 12:

```typescript
// From features/content/models/content.model.ts
interface Content {
  id: string;
  title: string;
  contentType: ContentType;
  status: ContentStatus;
  primaryPlatform: Platform;
  voiceScore: number | null;
  tags: string[];
  createdAt: string;
  updatedAt: string;
  scheduledAt: string | null;
  publishedAt: string | null;
}

enum ContentStatus { Idea, Draft, Review, Approved, Scheduled, Published, Archived }
enum ContentType { BlogPost, LinkedInPost, Tweet, ThreadedTweet, SubstackNewsletter, RedditPost, YouTubeVideo, YouTubeShort }
enum Platform { Blog, Substack, LinkedIn, Twitter, Reddit, YouTube }
```

---

## Store Dependencies (from Section 13)

The content list page depends on `ContentStore` from section 13:

```typescript
// From features/content/stores/content.store.ts
// Expected signals: contents, loading, totalCount, page, pageSize, viewMode, filters
// Expected methods: loadContents(), setFilter(partial), setPage(number), deleteContent(id), toggleView()
```

The `ContentFilterState` should include:
```typescript
interface ContentFilterState {
  status: ContentStatus | null;
  platform: Platform | null;
  contentType: ContentType | null;
  dateFrom: string | null;
  dateTo: string | null;
  searchText: string | null;
}
```

---

## Styling Notes

All components use the PrimeNG Aura Dark palette consistent with the existing Ideas feature:
- Background: `#0d1117` (sidebar), `#161b22` (cards)
- Border: `#21262d`
- Text primary: `#f0f6fc`
- Text secondary: `#8b949e`
- Link/accent: `#58a6ff`
- Hover background: `#161b22`

Inline styles (within `styles: [...]` in the component decorator) rather than separate CSS files, matching the established pattern.

---

## Navigation Integration

The "New Content" button in `ContentListComponent` should use `routerLink="/content/new"`. Content card edit actions should navigate to `/content/{id}`.

The existing `IdeasComponent.onCreateContent()` method (currently a no-op comment) should eventually navigate to `/content/new?fromIdea={ideaId}`, but wiring that cross-feature navigation is outside this section's scope.

---

## Implementation Notes (Actual)

### Files Created
```
features/content/content.routes.ts                                    — child routes (3 routes)
features/content/content-editor/content-editor.component.ts           — stub placeholder for section-15
features/content/content-list/content-display.utils.ts                — shared display utilities
features/content/content-list/content-list.component.ts               — main list page (2-col layout)
features/content/content-list/content-list.component.spec.ts          — 11 tests
features/content/content-list/content-card/content-card.component.ts  — card with status/platform/voice/tags
features/content/content-list/content-card/content-card.component.spec.ts — 10 tests
features/content/content-list/content-filter-sidebar/content-filter-sidebar.component.ts — 7 filters
features/content/content-list/content-filter-sidebar/content-filter-sidebar.component.spec.ts — 7 tests
features/content/content-list/content-grid/content-grid.component.ts  — responsive grid layout
features/content/content-list/content-list-table/content-list-table.component.ts — table layout
features/content/content-list/view-toggle/view-toggle.component.ts    — grid/list toggle
```

### Modified Files
```
app.routes.ts — changed content from loadComponent to loadChildren
```

### Deviations from Plan
- Added `content-display.utils.ts` for shared `formatContentType`, `voiceScoreClass`, `platformIconClass`, `truncateText` (code review: eliminate duplication between card and table)
- ContentEditorComponent stubbed as placeholder (section-15 will replace)
- content.component.ts/.html left in place (unused but not deleted for safety)

### Code Review Fixes Applied
- Fixed grid delete event binding: `(delete)` → `(onDelete)` (was silently broken)
- Extracted duplicated utility methods to shared file with consistent null handling

### Test Results
- 28/28 passing (11 list + 10 card + 7 filter)

### Review
- Review: `implementation/code_review/section-14-review.md`
