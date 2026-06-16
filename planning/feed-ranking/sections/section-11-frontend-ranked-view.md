# section-11-frontend-ranked-view

**Frontend section. Test runner: `ng test` (Angular 19 / Jasmine / Karma).** This is NOT a `dotnet test` section.

## Goal

Add a third "Ranked" view to the Idea Bank that surfaces a numbered Top-N of ideas by composite brand-anchored rank, with a Today/This-week window toggle and a per-item brand-fit breakdown (pillars hit + reason + anti/authority badges + stale indicator). This is the read-side UI for the ranking redesign.

Three deliverables:
1. Extend the idea store with a `'ranked'` view mode + ranked window/top-N state, default sort `rank`.
2. Add a third button to the view toggle.
3. New `idea-ranked` component, wired into `ideas.component.ts` via an `@else if` branch.

## Dependency (reference only — do NOT re-implement)

This section consumes the extended `IdeaDto` from **section-08-composite-rank-listideas**. The frontend `Idea` interface must mirror them using **these exact field names** (camelCased on the wire):

- `rank: number` — composite rank (descending sort key)
- `brandFit: number` — `[0,1]` weighted brand fit
- `pillarBreakdown: PillarBreakdown[]` — `{ name: string; score: number; reason: string }`
- `isAntiTopic: boolean | null`
- `isAuthorityTopic: boolean | null`
- `recencyFactor: number`
- `stale: boolean` — true when `ScoredProfileVersion < active profile Version` (R-C2c)

`idea.service.ts.list()` already sends `sortBy=<sort.field>` and maps response JSON directly onto `Idea[]` with no per-field transform, so adding fields to the interface is sufficient — no mapper change needed. Tests mock the store/service; do not block on the backend being live.

## Background — existing code you are extending

- **Store:** `src/.../features/ideas/store/idea.store.ts` — NgRx signal store. Current `viewMode: 'grid' | 'list'`, default `'list'`. `sort` defaults `{ field: 'detectedAt', direction: 'desc' }`. `toggleView()` flips list↔grid only. `loadIdeas` is an `rxMethod<void>` calling `ideaService.list(filter, page, pageSize, sort)`.
- **View toggle:** `src/.../components/view-toggle/view-toggle.component.ts` — two `p-button`s (`data-testid="grid-toggle"`, `"list-toggle"`) bound to `store.viewMode()`, calling `store.toggleView()`.
- **Ideas page:** `src/.../ideas.component.ts` — has `@if (store.viewMode() === 'grid') { app-idea-grid } @else { app-idea-list }`. Has `sortOptions` + `sortField` + `onSortChange()`.
- **Model:** `src/.../models/idea.model.ts` — `Idea` interface; `IdeaSortState { field; direction }`.
- **Reusable badge:** `src/.../shared/score-badge/score-badge.component.ts` — keep using it; driven by the derived `score`, unaffected.

The `toggleView()` two-state flip cannot represent three modes. **Replace it with an explicit `setViewMode(mode)` setter** and update the existing toggle buttons + spec. This is the cleaner long-term shape and makes the third button trivial.

## Tests FIRST

Jasmine/Karma, `TestBed`, `provideHttpClient()` + `provideHttpClientTesting()`, `data-testid` selectors. The store is `{ providedIn: 'root' }` so `TestBed.inject(IdeaStore)` returns the real store. Drive UI state via store setters where possible. `afterEach(() => httpMock.verify())` when using `provideHttpClientTesting`.

### `idea.store.spec.ts` (extend existing if present, else new)
```
# Test: viewMode union accepts 'ranked' — setViewMode('ranked') sets store.viewMode() === 'ranked'
# Test: setRankedWindow('today') and setRankedWindow('week') update store.rankedWindow()
# Test: rankedWindow change reloads the list (loadIdeas runs / list() called)
# Test: rankedTopN defaults to 20
# Test: setRankedTopN(n) updates state and reloads the list
# Test: default sort field is 'rank' (initialState.sort.field === 'rank')
# Test: setViewMode('grid'|'list') still works (regression on the toggle replacement)
```

### `view-toggle.component.spec.ts` (extend existing)
```
# Test: renders a ranked toggle button (data-testid="ranked-toggle")
# Test: clicking ranked-toggle sets store.viewMode() === 'ranked'
# Test: existing grid/list toggle tests still pass after switching to setViewMode
```
Keep the existing "default to list mode" / "toggle to grid on grid button click" specs green when swapping `toggleView()` → `setViewMode()`.

### `idea-ranked.component.spec.ts` (new)
```
# Test: renders a numbered Top-N list with big rank numerals, ordered by IdeaDto.rank descending
# Test: each item shows its brand-fit breakdown — pillar name + score + reason for each pillarBreakdown entry
# Test: an item with isAntiTopic === true shows an anti-topic badge (data-testid="anti-badge")
# Test: an item with isAuthorityTopic === true shows an authority badge (data-testid="authority-badge")
# Test: an item with stale === true shows a stale indicator (data-testid="stale-badge")
# Test: window toggle — clicking 'Today' / 'This week' calls store.setRankedWindow('today'|'week')
# Test: respects rankedTopN — only the first store.rankedTopN() items are rendered
```

## Implementation

### 1. Model — `idea.model.ts`
```ts
export interface PillarBreakdown { name: string; score: number; reason: string; }
```
Add to `Idea`: `rank: number; brandFit: number; pillarBreakdown: PillarBreakdown[]; isAntiTopic: boolean | null; isAuthorityTopic: boolean | null; recencyFactor: number; stale: boolean;` — all populated by the backend DTO. No service mapper change.

### 2. Store — `idea.store.ts`
- Widen `viewMode` to `'grid' | 'list' | 'ranked'`.
- Add state: `rankedWindow: 'today' | 'week'`, `rankedTopN: number`.
- `initialState`: `viewMode: 'list'` (unchanged), `rankedWindow: 'today'`, `rankedTopN: 20`, and change `sort` default to `{ field: 'rank', direction: 'desc' }`.
- Replace `toggleView()` with `setViewMode(mode): void` → `patchState(store, { viewMode: mode })`.
- `setRankedWindow(window): void` — patch state, then apply the corresponding `dateFrom` filter and reload. "Today" = start of today (local) → now; "This week" = now − 7 days → now. Reuse the existing filter pipeline (`setFilter({ dateFrom })` resets page→1 and reloads) using ISO strings.
- `setRankedTopN(n): void` — `patchState(store, { rankedTopN: n })`, also patch `pageSize: n` (so the server returns at least N for the single numbered page), then `loadIdeas()`.

Keep `loadIdeas`, `setFilter`, `setSort`, `setPage`, `selectIdea`, `saveIdea`, `dismissIdea`, `setError` as-is.

### 3. View toggle — `view-toggle.component.ts`
- Switch the two existing buttons from `store.toggleView()` to `store.setViewMode('grid')` / `('list')`.
- Add a third `p-button` `data-testid="ranked-toggle"`, icon `pi pi-sort-amount-down`, primary severity when `store.viewMode() === 'ranked'`, `(onClick)="store.setViewMode('ranked')"`.

### 4. New component — `components/idea-ranked/idea-ranked.component.ts`
Standalone:
```ts
@Component({ selector: 'app-idea-ranked', standalone: true, imports: [/* ButtonModule, ScoreBadgeComponent, ... */], template: `...` })
export class IdeaRankedComponent {
  readonly store = inject(IdeaStore);
  @Input({ required: true }) ideas: Idea[] = [];
  @Output() save = new EventEmitter<string>();
  @Output() dismiss = new EventEmitter<string>();
  @Output() createContent = new EventEmitter<string>();
}
```
Template:
- Window toggle row: "Today" / "This week" buttons calling `store.setRankedWindow(...)`, highlighting `store.rankedWindow()`.
- Numbered list: `@for (idea of ideas.slice(0, store.rankedTopN()); track idea.id; let i = $index)` rendering big numeral (`i + 1`) + title/sourceName/`score` badge.
- Per item, render `pillarBreakdown` (name, score, reason).
- Badges: `@if (idea.isAntiTopic)` → `data-testid="anti-badge"`; `@if (idea.isAuthorityTopic)` → `data-testid="authority-badge"`; `@if (idea.stale)` → `data-testid="stale-badge"`.
- Reuse `score-badge` for the derived `score`. Wire `save`/`dismiss`/`createContent` outputs to the same page handlers grid/list use.

Style: big rank numerals (e.g. `font-size: 32px; font-weight: 700`), obsidian/PrimeNG tokens consistent with existing cards. Keep under 400 lines.

### 5. Ideas page — `ideas.component.ts`
- Import `IdeaRankedComponent`, add to `imports`.
- Three-way view block:
  ```
  @if (store.viewMode() === 'grid') { app-idea-grid ... }
  @else if (store.viewMode() === 'ranked') {
    <app-idea-ranked [ideas]="store.ideas()" (save)="onSave($event)" (dismiss)="onDismiss($event)" (createContent)="onCreateContent($event)" />
  }
  @else { app-idea-list ... }
  ```
- Add `{ label: 'Best fit', value: 'rank' }` to `sortOptions`; set initial `sortField = 'rank'` to match the new default sort.

## Verify
- `cd src/PersonalBrandAssistant.Web && ng test --watch=false --browsers=ChromeHeadless` — all specs above green, existing view-toggle/store specs still green.
- `ng build` — no type errors from the widened `viewMode` union or new `Idea` fields.
- 80% coverage on the new component + store additions.

## Out of scope
- Backend `IdeaDto`/`ListIdeas`/`ComputeRank` → section-08. Brand Profile editor + service methods → section-10. Cutover/default-sort flip on deployed hosts → section-12.
