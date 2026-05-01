# Section 10: Content List

## Overview

This section implements the **Content List** page at `/content` -- the main table/grid of all content items with filters, search, pagination, and navigation to the editor. It replaces the existing `ContentListComponent` with a redesigned version matching the obsidian/terracotta design system, using PrimeNG `p-table` with lazy loading, proper filter controls (type, status, platform, search), and a "New Content" button.

## Dependencies

- **section-03-design-system**: PrimeNG Aura theme override with obsidian/terracotta tokens, shared atoms (`StatusBadgeComponent`)
- **section-04-app-shell**: Shell layout (sidebar + content + sidecar grid), routing configuration, core models (`Content`, `ContentStatus`, `ContentType`, `PlatformType`)

These must be completed before this section. No other sections depend on this one.

## Existing Code Being Replaced

The following files already exist and will be **replaced** (not extended):

| File | Current State | Action |
|------|--------------|--------|
| `src/PersonalBrandAssistant.Web/src/app/features/content/content-list.component.ts` | Groups content by platform, uses cursor-based pagination ("Load More" button), two dropdown filters (status, type) | Replace with PrimeNG `p-table` lazy loading, add platform filter + text search |
| `src/PersonalBrandAssistant.Web/src/app/features/content/store/content.store.ts` | Root-provided SignalStore with cursor-based pagination, selected content, brand voice, workflow log | Replace with feature-scoped store focused on list concerns (pagination, filters); move editor concerns to `ContentEditorStore` in section-07 |
| `src/PersonalBrandAssistant.Web/src/app/features/content/services/content.service.ts` | Full content service with CRUD, pipeline, workflow, approval, brand voice, scheduling, repurposing, content ideas | Keep as-is; extend `getAll` params if needed for platform and search filters |

## Existing Code Being Kept

These files are **not touched** by this section:

- `content.routes.ts` -- sub-routes for `/content`, `/content/new`, `/content/:id`, `/content/:id/edit`
- `content.service.ts` -- the API service already has `getAll()` with `contentType` and `status` filters
- All component files under `components/` (form, detail, pipeline, etc.)
- All models under `models/` (blog-chat, blog-publish, substack-prep)
- Shared models: `Content`, `ContentStatus`, `ContentType`, `PlatformType` (in `shared/models/enums.ts` and `shared/models/content.model.ts`)

## Tests (Write First)

### ContentStore Tests

File: `src/PersonalBrandAssistant.Web/src/app/features/content/store/content.store.spec.ts`

```typescript
// Test: ContentStore loads items with pagination from GET /api/content
//   - Arrange: mock ContentService.getAll to return PagedResult with 5 items
//   - Act: call store.loadContent({})
//   - Assert: store.items() has 5 items, loading is false

// Test: ContentStore filters by type, status, platform
//   - Arrange: mock ContentService.getAll
//   - Act: call store.loadContent({ contentType: 'BlogPost', status: 'Draft' })
//   - Assert: ContentService.getAll called with matching filter params

// Test: ContentStore paginates via loadMore using cursor
//   - Arrange: first call returns { items: [a,b], cursor: 'abc', hasMore: true }
//   - Act: call store.loadMore()
//   - Assert: ContentService.getAll called with cursor='abc', items appended

// Test: ContentStore sets loading=true during fetch and false on completion
//   - Act: call store.loadContent({})
//   - Assert: loading() is true before response, false after

// Test: ContentStore handles API error gracefully
//   - Arrange: mock ContentService.getAll to return error
//   - Act: call store.loadContent({})
//   - Assert: loading() is false, items() is empty

// Test: ContentStore search filter passes search text to API
//   - Act: call store.loadContent({ search: 'angular post' })
//   - Assert: ContentService.getAll called with search param
```

### ContentListComponent Tests

File: `src/PersonalBrandAssistant.Web/src/app/features/content/content-list.component.spec.ts`

```typescript
// Test: ContentListComponent renders PrimeNG table with lazy loading
//   - Arrange: provide store with mock items
//   - Assert: p-table element is present, rows match item count

// Test: ContentListComponent renders filter controls (type, status, platform, search)
//   - Assert: 3 p-select dropdowns present (type, status, platform)
//   - Assert: search input field present with placeholder

// Test: Clicking row navigates to /content/{id}/edit
//   - Arrange: provide items, spy on Router.navigate
//   - Act: click a table row
//   - Assert: Router.navigate called with ['/content', item.id, 'edit']

// Test: "New Content" button navigates to /content/new
//   - Arrange: spy on Router.navigate
//   - Act: click "New Content" button
//   - Assert: Router.navigate called with ['/content/new']

// Test: Filter change triggers store.loadContent with updated filters
//   - Arrange: select 'BlogPost' in type filter
//   - Assert: store.loadContent called with { contentType: 'BlogPost' }

// Test: Platform filter chips filter the visible items
//   - Arrange: provide items with various targetPlatforms
//   - Act: select 'LinkedIn' platform filter
//   - Assert: store.loadContent called with { platform: 'LinkedIn' }

// Test: Search input triggers store.loadContent with debounced search text
//   - Arrange: type 'angular' in search input
//   - Act: wait for debounce
//   - Assert: store.loadContent called with { search: 'angular' }

// Test: Table shows correct columns: title, type, platform, status, created date
//   - Assert: table headers match expected columns

// Test: Empty state shown when no items match filters
//   - Arrange: provide store with empty items
//   - Assert: empty state component is rendered

// Test: Loading spinner shown during initial load
//   - Arrange: store.loading() = true, store.hasContent() = false
//   - Assert: loading spinner component is rendered

// Test: Delete button shows confirmation dialog and removes item on confirm
//   - Arrange: spy on ConfirmationService.confirm and ContentService.remove
//   - Act: click delete button, accept confirmation
//   - Assert: ContentService.remove called, store reloads
```

## Implementation Details

### 1. ContentStore Redesign

File: `src/PersonalBrandAssistant.Web/src/app/features/content/store/content.store.ts`

The existing store is root-provided and mixes list concerns with editor concerns (selected content, brand voice score, workflow log, transitions). The redesign separates these:

**State shape:**
```typescript
interface ContentFilters {
  readonly contentType?: ContentType;
  readonly status?: ContentStatus;
  readonly platform?: PlatformType;
  readonly search?: string;
}

interface ContentListState {
  readonly items: readonly Content[];
  readonly cursor: string | undefined;
  readonly hasMore: boolean;
  readonly loading: boolean;
  readonly filters: ContentFilters;
}
```

Key changes from existing store:
- **Remove** `selectedContent`, `allowedTransitions`, `brandVoiceScore`, `workflowLog`, `saving` -- these belong in `ContentEditorStore` (section-07)
- **Add** `platform` and `search` to `ContentFilters`
- **Keep** `providedIn: 'root'` for now (the design plan says feature-scoped, but the existing routes share the store across list/detail/edit sub-routes; changing scope requires routing restructure which is out of scope for this section)
- **Keep** `loadContent`, `loadMore` methods
- **Keep** `hasContent` computed

The store retains the `rxMethod`-based pattern using `switchMap` + `tapResponse` for API calls. Add `search` and `platform` params to the `loadContent` method's filter passthrough.

### 2. ContentService Extension

File: `src/PersonalBrandAssistant.Web/src/app/features/content/services/content.service.ts`

The existing `getAll` method accepts `contentType`, `status`, `pageSize`, `cursor`. Extend the params interface to also accept:

```typescript
// Add to getAll params:
platform?: PlatformType;
search?: string;
```

And add corresponding `HttpParams` entries:
```typescript
if (params?.platform) httpParams = httpParams.set('platform', params.platform);
if (params?.search) httpParams = httpParams.set('search', params.search);
```

The backend `GET /api/content` endpoint may or may not support these params today. If it doesn't, the frontend sends them anyway (harmless) and server-side filtering can be added later. Client-side filtering in the store is the fallback.

### 3. ContentListComponent Replacement

File: `src/PersonalBrandAssistant.Web/src/app/features/content/content-list.component.ts`

Replace the existing component with a redesigned version matching the obsidian design. Key structural changes:

**Template structure:**
1. **Page header** -- `<app-page-header>` with title "Content" and "New Content" + "AI Pipeline" action buttons (keep existing actions)
2. **Filter bar** -- Row of controls:
   - `p-select` for status filter (same options as existing)
   - `p-select` for type filter (same options as existing)
   - `p-select` for platform filter (new -- options from `PlatformType` enum values with labels from `PLATFORM_LABELS`)
   - Text input (`pInputText`) for search with debounce (new)
3. **PrimeNG `p-table`** -- Replace the existing grouped-by-platform table approach with a single flat `p-table` using `[lazy]="true"` and `(onLazyLoad)` for pagination:
   - Columns: Title, Type (tag), Platform (icon + label), Status (badge), Created (relative time), Actions (view/edit/delete)
   - `[value]` bound to `store.items()`
   - Row click navigates to `/content/{id}/edit`
   - Lazy loading: `onLazyLoad` triggers `store.loadMore()` when scrolling past the last page
4. **Empty state** -- `<app-empty-state>` when no content matches filters
5. **Loading state** -- `<app-loading-spinner>` during initial load
6. **Pipeline dialog** -- Keep existing `<app-content-pipeline-dialog>` integration

**Component class changes:**
- Keep `ContentStore`, `ContentService`, `Router`, `ConfirmationService`, `MessageService` injections
- Add `platformFilter` signal for the new platform dropdown
- Add `searchText` signal with a debounced `effect()` that calls `applyFilters()` after 300ms of inactivity
- Keep `viewContent`, `editContent`, `deleteContent` methods
- Keep `statusOptions`, `typeOptions` arrays; add `platformOptions` array built from `PlatformType`/`PLATFORM_LABELS`
- Remove `groupedContent` computed -- the new design uses a flat table, not grouped by platform

**Imports to add:**
- `InputTextModule` from `primeng/inputtext` for search field
- Keep all existing imports

**Styling:**
- The component uses the design system's obsidian theme tokens from section-03
- Table styling inherits from the PrimeNG theme overrides
- Filter bar uses `flex` with `gap` for horizontal layout
- The `p-datatable-sm` style class keeps the table compact

### 4. Search Debounce Pattern

The search input uses Angular's signal-based debounce approach:

```typescript
// In component class:
searchText = signal('');

// In constructor or ngOnInit, set up debounced effect:
// Use effect() with a timer-based debounce or rxjs-interop toObservable + debounceTime
// When searchText changes, wait 300ms then call applyFilters()
```

The recommended approach is `toObservable(this.searchText).pipe(debounceTime(300))` subscribed in the constructor via `takeUntilDestroyed()`. This avoids the complexity of manual timer management.

### 5. Lazy Loading with p-table

PrimeNG's `p-table` `[lazy]="true"` mode fires `(onLazyLoad)` on initialization and whenever sorting/filtering/pagination changes. Since the backend uses cursor-based pagination (not offset-based), the integration works as follows:

- Initial load: `onLazyLoad` fires with `first=0` -- call `store.loadContent(currentFilters)`
- "Load More": Keep the existing "Load More" button pattern below the table (PrimeNG lazy loading is offset-based, which doesn't map cleanly to cursor pagination). The button calls `store.loadMore()` and appends results.
- Alternative: Use `[rows]` and `[totalRecords]` with a virtual scroll approach if the backend adds count support later.

The existing cursor-based pattern (`loadContent` resets, `loadMore` appends) is the right fit here. The `p-table` handles rendering; pagination is handled by the explicit "Load More" button.

## File Summary

| File | Action | Description |
|------|--------|-------------|
| `src/.../features/content/store/content.store.spec.ts` | Create | Store unit tests |
| `src/.../features/content/content-list.component.spec.ts` | Create | Component tests |
| `src/.../features/content/store/content.store.ts` | Modify | Add platform/search filters, remove editor concerns |
| `src/.../features/content/services/content.service.ts` | Modify | Add platform/search params to getAll |
| `src/.../features/content/content-list.component.ts` | Replace | New design with flat p-table, platform filter, search input |

All paths are relative to `src/PersonalBrandAssistant.Web/src/app/`.

## Backend API Assumptions

The content list relies on `GET /api/content` which already exists. The following query params are expected:

| Param | Type | Existing? | Notes |
|-------|------|-----------|-------|
| `contentType` | string | Yes | Filters by ContentType |
| `status` | string | Yes | Filters by ContentStatus |
| `pageSize` | number | Yes | Items per page |
| `cursor` | string | Yes | Cursor for pagination |
| `platform` | string | Maybe | Filter by target platform -- may need backend support |
| `search` | string | Maybe | Text search on title/body -- may need backend support |

If `platform` and `search` are not supported server-side, client-side filtering can be applied in the store's computed signals as a temporary measure.

## Key Design Decisions

1. **Flat table over grouped-by-platform**: The existing component groups items by platform. The new design uses a flat table with a platform column and filter dropdown instead. This is simpler, supports lazy loading cleanly, and matches the design mockup.

2. **Keep root-provided store**: The plan suggests feature-scoped stores, but the existing content routes share state across list/detail/edit sub-routes. Changing to feature-scoped requires route restructuring (providing the store at the content route level). This is preserved as-is to avoid breaking sub-route navigation.

3. **Cursor pagination over offset**: The backend uses cursor-based pagination. PrimeNG's lazy table assumes offset-based. Rather than forcing a mismatch, we keep the "Load More" button pattern which works cleanly with cursors.

4. **Search debounce at 300ms**: Standard UX practice -- prevents API spam while typing. Uses `toObservable` + `debounceTime` from rxjs-interop.
