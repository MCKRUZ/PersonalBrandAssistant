# Section 09 -- Calendar

## Overview

This section replaces the existing Calendar feature page with the new obsidian/terracotta design. The calendar provides a publishing schedule view showing content slots across platforms with week/month grid views, platform filtering, slot creation, content assignment, and auto-fill capabilities.

**Depends on:** section-04-app-shell (shell layout, routing, core models)

**Existing code being replaced:** The current calendar feature lives in `src/PersonalBrandAssistant.Web/src/app/features/calendar/` with a working `CalendarViewComponent`, `CalendarGridComponent`, `CalendarStore`, `CalendarService`, and three dialog components (slot detail, create slot, create series). The backend endpoints at `/api/calendar` are already functional and remain unchanged.

**Backend endpoints consumed (no backend changes in this section):**
- `GET /api/calendar?from={start}&to={end}` -- returns `CalendarSlot[]`
- `POST /api/calendar/slot` -- creates a manual slot
- `PUT /api/calendar/slot/{id}/assign` -- assigns content to a slot
- `POST /api/calendar/auto-fill` -- auto-fills empty slots (requires SemiAuto+ autonomy)
- `POST /api/calendar/series` -- creates a content series

---

## Tests First

All tests use Jasmine/Karma. Write these before implementing.

### CalendarStore Tests

File: `src/PersonalBrandAssistant.Web/src/app/pages/calendar/calendar.store.spec.ts`

```typescript
// Test: CalendarStore loads slots from GET /api/calendar with from/to params
//   Arrange: mock CalendarApiService.getSlots to return 3 slots
//   Act: store.loadSlots({ from, to })
//   Assert: store.slots() has 3 items, store.loading() is false

// Test: CalendarStore.createSlot calls POST /api/calendar/slot
//   Arrange: mock CalendarApiService.createSlot to return new slot
//   Act: store.createSlot({ scheduledAt, platform })
//   Assert: CalendarApiService.createSlot called with correct body, store reloads slots

// Test: CalendarStore.assignContent calls PUT /api/calendar/slot/{id}/assign
//   Arrange: mock CalendarApiService.assignContent to return void
//   Act: store.assignContent(slotId, contentId)
//   Assert: CalendarApiService.assignContent called with correct slotId and contentId

// Test: CalendarStore.autoFill calls POST /api/calendar/auto-fill
//   Arrange: mock CalendarApiService.autoFill to return void
//   Act: store.autoFill()
//   Assert: CalendarApiService.autoFill called with current dateRange, store reloads slots

// Test: CalendarStore viewMode defaults to 'week'
//   Assert: store.viewMode() === 'week'

// Test: CalendarStore.setViewMode toggles between 'week' and 'month'
//   Act: store.setViewMode('month')
//   Assert: store.viewMode() === 'month'

// Test: CalendarStore.slotsByDate computed groups slots by date key
//   Arrange: load 3 slots, 2 on same day, 1 on different day
//   Assert: slotsByDate map has 2 keys, first key has 2 slots

// Test: CalendarStore.platformFilter filters slots client-side
//   Arrange: load slots with mixed platforms
//   Act: store.filterByPlatform('LinkedIn')
//   Assert: store.filteredSlots() contains only LinkedIn slots

// Test: CalendarStore navigateWeek/navigateMonth shifts dateRange correctly
//   Act: store.navigate(1) in week mode
//   Assert: dateRange shifts forward by 7 days
```

### CalendarComponent Tests

File: `src/PersonalBrandAssistant.Web/src/app/pages/calendar/calendar.component.spec.ts`

```typescript
// Test: CalendarComponent renders week view with day columns and hour rows
//   Arrange: provide CalendarStore with slots
//   Assert: 7 day column headers rendered, hour row labels present

// Test: CalendarComponent renders content cards in assigned slots
//   Arrange: provide slots with contentId assigned
//   Assert: slot cards show content title, platform icon, status badge

// Test: Empty slots show "+" button
//   Arrange: provide slots with status 'Open' and no contentId
//   Assert: "+" button rendered in those slots

// Test: Platform filter toggles show/hide platform-specific slots
//   Arrange: render with mixed-platform slots
//   Act: toggle LinkedIn filter off
//   Assert: LinkedIn slots hidden, other platform slots visible

// Test: CalendarComponent switches between week and month view
//   Act: click month view toggle
//   Assert: month grid rendered with day cells (similar to existing CalendarGridComponent)

// Test: Clicking a slot card opens the slot detail dialog
//   Act: click on a filled slot card
//   Assert: SlotDetailDialogComponent opens with slot data

// Test: Clicking "+" on empty slot opens create slot dialog with pre-filled date
//   Act: click "+" on an empty slot at a specific time
//   Assert: CreateSlotDialogComponent opens with that date/time pre-filled

// Test: CalendarComponent renders navigation controls (prev/next, month label)
//   Assert: prev/next buttons present, current date range label displayed
```

### CalendarApiService Tests

File: `src/PersonalBrandAssistant.Web/src/app/pages/calendar/calendar-api.service.spec.ts`

```typescript
// Test: getSlots sends GET /api/calendar with from and to query params
// Test: createSlot sends POST /api/calendar/slot with CalendarSlotRequest body
// Test: assignContent sends PUT /api/calendar/slot/{id}/assign with { contentId }
// Test: autoFill sends POST /api/calendar/auto-fill
// Test: createSeries sends POST /api/calendar/series with ContentSeriesRequest body
```

---

## Implementation Details

### File Structure

All new files go under the `pages/calendar/` directory following the design-merge convention (the existing `features/calendar/` code is being replaced):

```
src/PersonalBrandAssistant.Web/src/app/
  pages/
    calendar/
      calendar.component.ts          # Main calendar page component
      calendar.component.spec.ts     # Component tests
      calendar.store.ts              # NgRx SignalStore (feature-scoped)
      calendar.store.spec.ts         # Store tests
      calendar-api.service.ts        # API service for calendar endpoints
      calendar-api.service.spec.ts   # API service tests
      components/
        calendar-week-grid.component.ts    # Week view grid
        calendar-month-grid.component.ts   # Month view grid (port from existing CalendarGridComponent)
        slot-card.component.ts             # Individual slot card within the grid
        slot-detail-dialog.component.ts    # Slot detail / assign dialog (port from existing)
        create-slot-dialog.component.ts    # Create manual slot dialog (port from existing)
        create-series-dialog.component.ts  # Create series dialog (port from existing)
```

### CalendarApiService

File: `src/PersonalBrandAssistant.Web/src/app/pages/calendar/calendar-api.service.ts`

This is a direct port of the existing `CalendarService` at `features/calendar/services/calendar.service.ts`, relocated to the new file structure. The API contract is identical -- the service wraps the existing `ApiService` base class with typed methods for each endpoint.

Methods:
- `getSlots(from: string, to: string): Observable<CalendarSlot[]>` -- `GET /api/calendar?from=&to=`
- `createSlot(request: CalendarSlotRequest): Observable<CalendarSlot>` -- `POST /api/calendar/slot`
- `assignContent(slotId: string, contentId: string): Observable<void>` -- `PUT /api/calendar/slot/{id}/assign`
- `autoFill(from: string, to: string): Observable<void>` -- `POST /api/calendar/auto-fill`
- `createSeries(request: ContentSeriesRequest): Observable<ContentSeries>` -- `POST /api/calendar/series`

This service is **not** `providedIn: 'root'` -- it is provided at the component level so it is released on navigation away from the calendar route.

### CalendarStore

File: `src/PersonalBrandAssistant.Web/src/app/pages/calendar/calendar.store.ts`

Feature-scoped NgRx SignalStore (provided at route component level, not root). This is a redesign of the existing `CalendarStore` at `features/calendar/store/calendar.store.ts` to add view mode toggling, platform filtering, and week-level navigation.

**State interface:**

```typescript
interface CalendarState {
  readonly slots: readonly CalendarSlot[];
  readonly dateRange: { readonly from: string; readonly to: string };
  readonly viewMode: 'week' | 'month';
  readonly platformFilter: PlatformType | null;  // null = show all
  readonly selectedSlot: CalendarSlot | undefined;
  readonly loading: boolean;
}
```

**Initial state:** `viewMode: 'week'`, `dateRange` computed for the current week (Monday to Sunday), `platformFilter: null`.

**Computed signals:**
- `slotsByDate` -- `Map<string, CalendarSlot[]>` grouping slots by ISO date key (port from existing store)
- `filteredSlots` -- applies `platformFilter` to `slots` before grouping
- `dateLabel` -- formatted string for the current range ("Apr 28 - May 4, 2026" for week, "May 2026" for month)

**Methods (via `withMethods`):**
- `loadSlots(range)` -- `rxMethod` that calls `CalendarApiService.getSlots`, patches state on response
- `setViewMode(mode)` -- patches `viewMode`, recomputes `dateRange` (week range vs month range), triggers `loadSlots`
- `navigate(offset)` -- shifts `dateRange` by 1 week or 1 month depending on `viewMode`, triggers `loadSlots`
- `filterByPlatform(platform | null)` -- patches `platformFilter` (client-side filtering, no API call)
- `createSlot(request)` -- calls `CalendarApiService.createSlot`, reloads slots on success
- `assignContent(slotId, contentId)` -- calls `CalendarApiService.assignContent`, reloads on success
- `autoFill()` -- calls `CalendarApiService.autoFill` with current `dateRange`, reloads on success
- `selectSlot(slot)` -- patches `selectedSlot`

**Date range helpers:** Provide utility functions `getWeekRange(date: Date)` and `getMonthRange(date: Date)` that return `{ from: string, to: string }` -- the month range function can be ported directly from the existing store.

### CalendarComponent

File: `src/PersonalBrandAssistant.Web/src/app/pages/calendar/calendar.component.ts`

Main page component. Standalone, lazy-loaded at `/calendar`. Provides `CalendarStore` and `CalendarApiService` at the component level.

**Template structure:**

1. **Header row** -- Page title "Calendar", view mode toggle (week/month buttons), navigation arrows with date label, action buttons (New Series, New Slot, Auto-Fill)

2. **Platform filter row** -- Horizontal row of toggle chips for each platform (LinkedIn, Twitter/X, Instagram, YouTube, Reddit, Blog, Substack). Each chip toggles that platform's visibility. An "All" chip resets the filter. Use PrimeNG `p-togglebutton` or `p-selectbutton` for this.

3. **Grid area** -- Conditionally renders either `CalendarWeekGridComponent` or `CalendarMonthGridComponent` based on `store.viewMode()`. Uses `@if` control flow.

4. **Dialogs** -- Three dialog components rendered at the bottom of the template (same pattern as existing): `SlotDetailDialogComponent`, `CreateSlotDialogComponent`, `CreateSeriesDialogComponent`. These are ports of the existing dialogs with updated styling to match the obsidian design system.

**Lifecycle:** `ngOnInit` calls `store.loadSlots(store.dateRange())`.

**Event handling:**
- `slotClicked` event from grid -> opens slot detail dialog
- `emptySlotClicked` event from grid -> opens create slot dialog with pre-filled date/time
- Nav arrows -> calls `store.navigate(-1)` or `store.navigate(1)`
- View mode toggle -> calls `store.setViewMode('week' | 'month')`
- Auto-Fill button -> calls `store.autoFill()`

### CalendarWeekGridComponent

File: `src/PersonalBrandAssistant.Web/src/app/pages/calendar/components/calendar-week-grid.component.ts`

New component (the existing calendar only has a month grid). Renders a 7-column x time-row grid.

**Inputs (signal inputs):**
- `dateRange: { from: string; to: string }` -- the week boundaries
- `slotsByDate: Map<string, CalendarSlot[]>` -- grouped slots

**Outputs:**
- `slotClicked: CalendarSlot` -- user clicked a filled slot
- `emptySlotClicked: Date` -- user clicked "+" on an empty time

**Template:** 7 day columns with day name and date header. Within each column, slots are rendered as `SlotCardComponent` instances positioned vertically by their `scheduledAt` time. Empty areas show a subtle "+" button. The grid uses CSS grid with `grid-template-columns: repeat(7, 1fr)`.

**Time axis:** Display hour labels on the left (6 AM to 10 PM by default). Slots render at their hour position. If a day has no slots at a particular hour, show a subtle click target for creating a new slot.

### CalendarMonthGridComponent

File: `src/PersonalBrandAssistant.Web/src/app/pages/calendar/components/calendar-month-grid.component.ts`

Port of the existing `CalendarGridComponent` from `features/calendar/components/calendar-grid.component.ts`. Same logic for building weeks array from date range, same computed `weeks` signal. Updated styling to use obsidian design tokens (`var(--p-surface-800)` for borders, `var(--p-surface-900)` for cells, `var(--p-primary-color)` for today highlight, etc.).

The key change from the existing component is that slot chips now render as `SlotCardComponent` instances instead of raw `PlatformChipComponent`, giving richer visual information (title, status badge, time).

### SlotCardComponent

File: `src/PersonalBrandAssistant.Web/src/app/pages/calendar/components/slot-card.component.ts`

Small presentational component rendering a single calendar slot.

**Inputs:**
- `slot: CalendarSlot` -- the slot data

**Template:** A compact card showing:
- Platform icon (from `StatusBadgeComponent` or a PrimeIcon mapped from `slot.platform`)
- Content title (if `contentId` is assigned -- will need content title lookup or the API should include it; for now show "Assigned" vs "Open")
- Status badge using the shared `StatusBadgeComponent` from section-03
- Scheduled time formatted as "HH:mm"

**Styling:** Background color coded by platform using CSS classes (e.g., `.platform-linkedin { border-left: 3px solid #0077B5; }`). Hover effect with `var(--p-primary-color)` border.

### Dialog Components (Ports)

The three dialog components (`SlotDetailDialogComponent`, `CreateSlotDialogComponent`, `CreateSeriesDialogComponent`) are ported from the existing `features/calendar/components/` directory with these changes:

1. **Import path updates** -- point to the new `CalendarApiService` in `pages/calendar/` instead of `CalendarService` in `features/calendar/services/`
2. **Model imports** -- use core models from `core/models/` (from section-04) instead of `shared/models/`
3. **Styling updates** -- update any inline styles to use obsidian design tokens. The PrimeNG dialog and form components automatically pick up the theme overrides from section-03.
4. **Platform list expansion** -- Add Reddit, PersonalBlog, and Substack to the platform options in `CreateSlotDialogComponent` and `CreateSeriesDialogComponent` (currently only has 4 platforms).

The logic and form structure remain identical to the existing implementations.

### Routing

File: `src/PersonalBrandAssistant.Web/src/app/app.routes.ts` (modified by section-04)

The calendar route should be configured as:

```typescript
{
  path: 'calendar',
  loadComponent: () => import('./pages/calendar/calendar.component').then(m => m.CalendarComponent),
  data: { title: 'Calendar', sidecarContext: 'calendar' }
}
```

This replaces the current `loadChildren` approach with `loadComponent` to match the design-merge convention. Route data includes `title` (for the topbar) and `sidecarContext` (for sidecar quick prompts defined in section-05).

### Styling

The calendar components use the design system from section-03. Key styling details:

- **Grid borders:** `var(--p-surface-700)` (subtle dark borders between cells)
- **Cell backgrounds:** `var(--p-surface-900)` for normal cells, `var(--p-surface-800)` on hover
- **Today highlight:** `var(--p-primary-color)` with low opacity background
- **Day headers:** `var(--p-surface-800)` background, DM Sans font
- **Slot cards:** `var(--p-surface-800)` background with platform-colored left border
- **Empty slot "+" buttons:** `var(--p-text-muted-color)` icon, `var(--p-surface-800)` background on hover
- **Other-month cells:** `opacity: 0.3` (month view only)

### Models Used

From section-04 core models (these already exist in the codebase at `shared/models/`):

- `CalendarSlot` -- `id`, `scheduledAt`, `platform`, `contentSeriesId?`, `contentId?`, `status`, `isOverride`, `overriddenOccurrence?`, `createdAt`, `updatedAt`
- `CalendarSlotStatus` -- `'Open' | 'Filled' | 'Published' | 'Skipped'`
- `CalendarSlotRequest` -- `scheduledAt`, `platform`
- `ContentSeries` -- `id`, `name`, `description?`, `recurrenceRule`, `targetPlatforms`, `contentType`, `themeTags`, `timeZoneId`, `startsAt`, `endsAt?`, `isActive`, `createdAt`, `updatedAt`
- `ContentSeriesRequest` -- same as `ContentSeries` minus `id`, `isActive`, timestamps
- `PlatformType` -- union of all platform string literals

### Error Handling

Follow the global error state patterns from the plan:
- API failures show PrimeNG toast (error severity) via `MessageService`
- Auto-fill failure (403 when autonomy too low) shows toast with "Autonomy level must be SemiAuto or higher"
- Loading state: show skeleton/spinner while `store.loading()` is true (use existing `LoadingSpinnerComponent` or PrimeNG `p-skeleton`)

### What Is NOT in This Section

- **Backend changes** -- No backend modifications. All calendar endpoints already exist and work.
- **Sidecar integration** -- Quick prompts for the calendar route ("Find gaps this week", "Suggest content for empty slots", "Optimize posting times") are defined in section-05's `QUICK_PROMPTS` map, not in this section.
- **Design system atoms** -- `StatusBadgeComponent` and other shared atoms come from section-03. This section consumes them.
- **App shell** -- Sidebar nav, topbar, and layout grid come from section-04. This section provides the routed content area.

---

## Implementation Notes

**Files created:** 14 new files + 1 modified (app.routes.ts)
**Tests:** 22 tests across 3 spec files (API service: 5, store: 9, component: 8)

**Deviations from plan:**
1. Store uses `switchMap` + `tapResponse` chains (not nested subscribes) for `createSlot`/`assignContent`/`autoFill` — fixed during code review
2. Date grouping uses local date formatting (`getFullYear/getMonth/getDate`) instead of `toISOString().split('T')[0]` to prevent timezone-related misgrouping
3. No separate `viewMode` signal in component — binds directly to `store.viewMode()` to prevent state drift
4. Dialog error handlers show toast notifications (not silent swallowing)
5. Route uses `loadComponent` instead of `loadChildren` (matches design-merge convention)
6. Platform lists in dialogs expanded to all 7 platforms (Reddit, PersonalBlog, Substack added)
7. Week grid starts on Monday (not Sunday) to match international standard and month grid behavior
