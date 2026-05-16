# Section 12: Feed Card List and Card Component

## Overview

Two Angular components that render the feed as a scrollable list of type-specific cards. `FeedCardList` is a dumb/presentational container managing three visual states (loading, empty, data). `FeedCard` is the individual card with type-specific styling, icons, priority badges, and action buttons.

Both components are input-driven -- they receive data from the parent `FeedPage` (which reads from `FeedStore`) and emit events for user interactions. Neither component injects the store directly.

## Dependencies

- **Section 09 (Feed Store):** FeedStore provides the items, selectedIds, and loading state that FeedPage passes to these components.
- **Section 08 (Angular Models/Service):** TypeScript interfaces (`FeedItem`, `FeedItemType`, `FeedItemPriority`) used as input types.

## Tests (Write First)

### FeedCardList Tests

File: `src/PersonalBrandAssistant.Web/src/app/features/feed/feed-card-list/feed-card-list.component.spec.ts`

```typescript
// TestBed setup: import FeedCardListComponent, provide NO_ERRORS_SCHEMA to shallow-render child FeedCard components

// Test: renders FeedCard for each item in items input
//   - Set items input to 3 FeedItem objects, loading = false
//   - Expect 3 app-feed-card elements rendered

// Test: shows skeleton cards when loading input is true
//   - Set loading = true, items = []
//   - Expect 5 skeleton placeholder elements with pulse shimmer animation class

// Test: shows empty state when loading is false and items is empty
//   - Set loading = false, items = []
//   - Expect element containing "You're all caught up!" text and checkmark illustration

// Test: emits action event when card action triggered
//   - Set items to at least one item
//   - Subscribe to (action) output
//   - Trigger action event on the child card
//   - Expect emitted value to have shape { id: string, action: string }

// Test: emits select event when card checkbox toggled
//   - Set items to at least one item
//   - Subscribe to (select) output
//   - Trigger select event from child card
//   - Expect emitted value to be the item id (string)
```

### FeedCard Tests

File: `src/PersonalBrandAssistant.Web/src/app/features/feed/feed-card/feed-card.component.spec.ts`

```typescript
// TestBed setup: import FeedCardComponent, provideNoopAnimations()

// --- Type-specific border colors ---

// Test: renders blue left border for AgentDraft type
//   - Set item input with type = 'AgentDraft'
//   - Expect host or card element to have border-left-color #3b82f6

// Test: renders orange left border for TrendAlert type
//   - item.type = 'TrendAlert' -> border-left-color #f97316

// Test: renders purple left border for IdeaSuggestion type
//   - item.type = 'IdeaSuggestion' -> border-left-color #a855f7

// Test: renders green left border for AnalyticsHighlight type
//   - item.type = 'AnalyticsHighlight' -> border-left-color #22c55e

// --- Icons ---

// Test: renders correct icon class per type
//   - AgentDraft -> pi-bolt
//   - TrendAlert -> pi-chart-line
//   - IdeaSuggestion -> pi-lightbulb
//   - AnalyticsHighlight -> pi-chart-bar
//   - ApprovalRequest -> pi-check-circle
//   - SystemNotification -> pi-bell

// --- Priority badges ---

// Test: shows priority badge for High and Urgent, hides for Normal
//   - item.priority = 'High' -> expect Tag component visible with "High" text
//   - item.priority = 'Urgent' -> expect Tag visible with "Urgent" text
//   - item.priority = 'Normal' -> no Tag rendered

// Test: Urgent badge has pulse animation class
//   - item.priority = 'Urgent' -> Tag element has CSS class 'pulse' (or 'animate-pulse')

// --- Action buttons per type ---

// Test: shows Approve button for AgentDraft type
//   - item.type = 'AgentDraft' -> primary button text = "Approve"

// Test: shows View button for TrendAlert type
//   - item.type = 'TrendAlert' -> primary button text = "View"

// Test: shows Create Content button for IdeaSuggestion type
//   - item.type = 'IdeaSuggestion' -> primary button text = "Create Content"

// Test: all types show Dismiss button
//   - For each FeedItemType, verify a "Dismiss" button exists

// --- Read state ---

// Test: read items have reduced opacity class
//   - item.isRead = true -> card element has class like 'is-read' or style opacity: 0.7
//   - item.isRead = false -> no reduced opacity

// --- Selection ---

// Test: checkbox reflects selected state from selectedIds input
//   - Set selectedIds = [item.id] -> checkbox checked
//   - Set selectedIds = [] -> checkbox unchecked

// --- Event emission ---

// Test: click action button emits action event with id and action string
//   - Click "Approve" -> expect output { id: item.id, action: 'approve' }
//   - Click "Dismiss" -> expect output { id: item.id, action: 'dismiss' }
```

## Implementation Details

### FeedCardList Component

File: `src/PersonalBrandAssistant.Web/src/app/features/feed/feed-card-list/feed-card-list.component.ts`
Template: `feed-card-list.component.html`
Styles: `feed-card-list.component.scss`

Standalone component. Input-driven (no store injection).

**Inputs:**
- `items: FeedItem[]` -- the feed items for the current page
- `loading: boolean` -- whether items are being fetched
- `selectedIds: string[]` -- IDs of currently selected items (passed through to FeedCard)

**Outputs:**
- `action = new EventEmitter<{ id: string; action: string }>()` -- bubbles up from FeedCard
- `select = new EventEmitter<string>()` -- bubbles up from FeedCard checkbox toggle

**Three visual states (template logic):**

1. **Loading** (`loading === true`): Render 5 skeleton card placeholders. Each placeholder is a rectangular div with the same card dimensions but a CSS shimmer/pulse animation. No real data displayed.

2. **Empty** (`loading === false && items.length === 0`): A centered container with a large checkmark icon and "You're all caught up!" message. Muted text, secondary color.

3. **Data** (`loading === false && items.length > 0`): Use `@for (item of items; track item.id)` to render `<app-feed-card>` for each item. Pass item, selectedIds, and forward the action/select events.

### FeedCard Component

File: `src/PersonalBrandAssistant.Web/src/app/features/feed/feed-card/feed-card.component.ts`
Template: `feed-card.component.html`
Styles: `feed-card.component.scss`

Standalone component. Purely presentational.

**Inputs:**
- `item: FeedItem` -- the feed item to display
- `selectedIds: string[]` -- used to determine checkbox state

**Outputs:**
- `action = new EventEmitter<{ id: string; action: string }>()` -- emitted on action button click
- `select = new EventEmitter<string>()` -- emitted on checkbox toggle

**Card layout:**

```
+-- 3px colored border ------------------------------------------------+
| [x] [Icon] Type Label   [Priority Badge]         2 hours ago        |
|                                                                      |
| Title (bold, #f0f6fc)                                               |
| Summary text (secondary, #8b949e, max 2 lines, ellipsis)            |
|                                                                      |
| [Primary Action]  [Secondary...]  [Dismiss]                         |
+----------------------------------------------------------------------+
```

**Type visual treatments (implement via CSS class map or computed signal):**

| Type | Accent Color | Icon Class (PrimeNG) |
|------|-------------|---------------------|
| AgentDraft | #3b82f6 (blue) | pi-bolt |
| TrendAlert | #f97316 (orange) | pi-chart-line |
| IdeaSuggestion | #a855f7 (purple) | pi-lightbulb |
| AnalyticsHighlight | #22c55e (green) | pi-chart-bar |
| ApprovalRequest | #eab308 (amber) | pi-check-circle |
| SystemNotification | #6b7280 (gray) | pi-bell |

Use a `readonly` map in the component class to look up accent color and icon class by `FeedItemType`. A computed signal deriving the config from `item.type` keeps the template clean.

**Priority badges:**
- `Normal` or `Low`: no badge rendered
- `High`: PrimeNG `<p-tag severity="warning" value="High">`
- `Urgent`: PrimeNG `<p-tag severity="danger" value="Urgent">` with additional CSS class `pulse` that applies a keyframe pulse animation

**Action buttons per type:**

| Type | Primary Button (label, severity) | Secondary Buttons |
|------|----------------------------------|-------------------|
| AgentDraft | Approve (success) | Edit, Schedule, Dismiss |
| TrendAlert | View (info) | Dismiss |
| IdeaSuggestion | Create Content (info) | Dismiss |
| AnalyticsHighlight | View Report (info) | Dismiss |
| ApprovalRequest | Approve (success) | Edit, Dismiss |
| SystemNotification | (none) | Dismiss |

Implement as a `readonly` action config map keyed by `FeedItemType`. Each entry has a `primary` (nullable for SystemNotification) and `secondaries` array. This avoids a large `@switch` block in the template.

**Read state styling:** When `item.isRead === true`, apply an `is-read` CSS class to the card root element. Style: `opacity: 0.7` (entire card dims). Unread items have full opacity.

**Checkbox:** Top-right corner. Checked state derived from `selectedIds.includes(item.id)`. On toggle, emit `select` event with the item's ID.

**Timestamp:** Show `item.createdAt` as a relative time string (e.g., "2 hours ago"). Use a pure pipe or Angular's `DatePipe` with a utility. If you want relative formatting, create a small `RelativeTimePipe` or use a lightweight library. Keep it simple -- a helper function that returns "Xm ago", "Xh ago", "Xd ago" based on the difference from `Date.now()`.

### Dark theme SCSS notes

Cards sit on `#161b22` background. Text: `#f0f6fc` for titles, `#8b949e` for secondary/summary text. Card surface: `#0d1117` or `#161b22` with subtle `border: 1px solid #30363d`. Hover state: `background: #1c2128`.

The skeleton placeholders use the same card dimensions with a `background: linear-gradient(90deg, #21262d 25%, #30363d 50%, #21262d 75%)` animated via `background-position` keyframes.

### Pulse animation for Urgent priority

```scss
@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.5; }
}

.pulse {
  animation: pulse 2s ease-in-out infinite;
}
```

## File Structure Summary (Actual)

```
src/PersonalBrandAssistant.Web/src/app/features/feed/
  feed-card-list/
    feed-card-list.component.ts        (inline template/styles, matching codebase convention)
    feed-card-list.component.spec.ts
  feed-card/
    feed-card.component.ts             (inline template/styles, matching codebase convention)
    feed-card.component.spec.ts
  pipes/
    relative-time.pipe.ts
    relative-time.pipe.spec.ts
```

Note: Used inline templates/styles instead of separate .html/.scss files to match the established convention across all existing feed components (feed-stats-bar, feed-filter-tabs, feed-batch-toolbar, feed-page).

## Deviations from Plan

1. **Inline templates/styles** instead of separate files -- codebase consistency with sections 10-11
2. **No PrimeNG components** (Tag, Button) -- used styled HTML elements matching existing feed component patterns
3. **TYPE_MAP and ACTION_MAP made static** -- code review found 20 identical instance copies wasteful
4. **RelativeTimePipe kept pure** -- timestamps refresh on feed interaction, acceptable staleness for dashboard use
5. **Added aria-label to checkbox** -- WCAG compliance fix from code review
6. **Added type="button" to all buttons** -- defensive, prevents accidental form submission
7. **FeedPage wired with `onCardAction()`** -- delegates all actions to `store.actOnItem(id, action)`

## Test Count
- FeedCard: 26 tests (4 border colors, 6 icons, 5 priority badges, 8 action buttons, 2 read state, 2 selection, 3 event emission, plus 4 secondary button tests from review)
- FeedCardList: 6 tests (loading skeleton, empty state, data rendering, no skeleton/empty overlap, action emission, select emission)
- RelativeTimePipe: 7 tests (null, just now, minutes, hours, days, months, future dates)
- Total: 44 tests, all passing
