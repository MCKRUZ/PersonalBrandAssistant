# Section 13: Angular Signal Stores

## Overview

This section creates two NgRx signal stores:

1. **ContentStore** -- manages content list page state (pagination, filtering, loading)
2. **ContentEditorStore** -- manages content editor page state (editing, dirty tracking, auto-save, AI chat streaming)

Both follow the IdeaStore pattern: plain `signalStore` with `withState`, `withComputed`, `withMethods`, `patchState`, and `rxMethod`.

## Dependencies

- **Section 12 (Angular Models and Services):** ContentService, SignalRService, all TypeScript interfaces/enums.
- **Reference:** `src/PersonalBrandAssistant.Web/src/app/features/ideas/store/idea.store.ts`

---

## Files to Create

```
features/content/stores/content.store.ts
features/content/stores/content.store.spec.ts
features/content/stores/content-editor.store.ts
features/content/stores/content-editor.store.spec.ts
```

---

## Tests First

### ContentStore Tests

| Test | Description |
|------|-------------|
| `has correct initial state` | contents=[], loading=false, page=1, pageSize=20, filters null |
| `loadContents fetches from service` | service.list called, state updated |
| `setFilter updates filter and reloads` | filter patched, page reset to 1, list called |
| `setPage updates pagination and reloads` | page patched, list called |
| `deleteContent calls service and reloads` | service.delete called, then list called |
| `handles loading errors` | loading=false, error set |
| `computes totalPages correctly` | totalCount=45, pageSize=20 -> 3 |
| `toggleView switches list/grid` | starts 'list', toggle -> 'grid' |

### ContentEditorStore Tests

| Test | Description |
|------|-------------|
| `has correct initial state` | content=null, isDirty=false, chatMessages=[] |
| `loadContent fetches and sets content` | service.get called, content set |
| `updateField marks dirty` | isDirty=true after field change |
| `autoSave calls service with lastUpdatedAt` | service.update called with correct request |
| `autoSave clears dirty on success` | isDirty=false, isSaving=false |
| `autoSave sets error on failure` | error set, isDirty still true |
| `appendToken accumulates tokens` | currentTokens grows |
| `completeGeneration finalizes message` | isStreaming=false, chatMessages grows |
| `applyToEditor replaces body and marks dirty` | body updated, isDirty=true |
| `reset clears all state` | back to initial |

---

## Implementation

### ContentStore

**File:** `content.store.ts`

`signalStore` with `{ providedIn: 'root' }`.

**State:** `contents: Content[]`, `totalCount`, `page`, `pageSize`, `filters: ContentFilterState`, `sortBy`, `sortDirection`, `viewMode`, `loading`, `error`.

**Computed:** `totalPages`, `hasNextPage`, `hasPreviousPage`.

**Methods:**
- `loadContents()` -- rxMethod calling contentService.list() with tapResponse
- `setFilter(key, value)` -- patches filter, resets page to 1, reloads
- `setPage(page)` -- patches page, reloads
- `deleteContent(id)` -- calls service.delete, reloads on success
- `toggleView()` -- toggles list/grid

### ContentEditorStore

**File:** `content-editor.store.ts`

`signalStore` (NOT providedIn root -- component-scoped via `providers: [ContentEditorStore]` on editor component).

**State:** `content: ContentDetail | null`, `isDirty`, `isSaving`, `chatMessages: ChatMessage[]`, `isStreaming`, `currentTokens`, `loading`, `error`.

**ChatMessage type:** `{ role: 'user' | 'assistant', content: string, timestamp: string }`

**Computed:** `canAutoSave`, `hasContent`, `statusActions`.

**Methods:**
- `loadContent(id)` -- fetches content, resets dirty/chat state
- `updateField(field, value)` -- patches content with spread, sets isDirty=true
- `autoSave()` -- guards on isDirty, calls service.update with lastUpdatedAt, clears dirty on success
- `addChatMessage(message)` -- adds user message to chatMessages
- `appendToken(token)` -- appends to currentTokens, sets isStreaming=true
- `completeGeneration(fullText)` -- adds assistant message, clears currentTokens/isStreaming
- `applyToEditor(text)` -- patches content.body, sets isDirty=true
- `reset()` -- returns to initial state

**Design notes:**
- Auto-save debouncing lives in the component (section 15), not the store
- SignalR subscriptions live in the component (section 16), not the store -- store just gets called with tokens
- ContentStore is root-scoped (persists across navigation). ContentEditorStore is component-scoped (fresh per editor instance).

---

## Key Patterns from IdeaStore

1. `rxMethod<void>` for async operations with `tapResponse`
2. `patchState` for all mutations (never mutate signals directly)
3. Error handling via `tapResponse` next/error callbacks
4. Methods that modify filters call `loadContents()` after patching
5. Spy-based testing with `jasmine.createSpyObj`

## Exports

Export `ContentFilterState` and `ChatMessage` types for component consumption.

---

## Implementation Notes (Actual)

### Files Created
```
features/content/stores/content.store.ts          — 82 lines
features/content/stores/content.store.spec.ts      — 120 lines, 8 tests
features/content/stores/content-editor.store.ts    — 120 lines
features/content/stores/content-editor.store.spec.ts — 185 lines, 18 tests
```

### Deviations from Plan
- `ContentStore.setFilter` uses key-value signature `(key, value)` instead of IdeaStore's partial-object pattern — better for template bindings.
- `ContentEditorStore` uses `rxMethod<string>` for `loadContent` (takes content ID) instead of `rxMethod<void>`.
- Added `canAutoSave`, `statusActions` computed signals as planned.
- Added 5 extra tests beyond spec minimum: loadContent error, canAutoSave, statusActions (3 cases).
- `autoSave` uses raw `.subscribe()` matching IdeaStore convention for fire-and-forget operations.

### Test Results
- 26/26 passing (8 ContentStore + 18 ContentEditorStore)

### Code Review
- No critical issues. Two important notes (autoSave subscribe pattern accepted per IdeaStore convention; ContentEditorStore component-scoped by design — section-15 must provide it).
- Review: `implementation/code_review/section-13-review.md`
