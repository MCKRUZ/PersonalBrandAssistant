# Review triage & decisions — section-11

Triaged autonomously. Both HIGHs were genuine state-desync bugs the mock-heavy tests masked; fixed by a
small store refactor.

## Applied fixes
- **HIGH (filter clobber) + HIGH (pageSize coupling):** the ranked window + topN are now request-time
  OVERLAYS computed inside `loadIdeas` only when `viewMode === 'ranked'` — `{...filter, dateFrom: window}`
  and `size = rankedTopN`. The shared `filter`/`pageSize` are never mutated, so grid/list round-trips are
  lossless. `setViewMode`/`setRankedWindow`/`setRankedTopN` just patch their own state + `loadIdeas()`.
  Paginator hidden in ranked mode.
- **Tests:** rewrote the window/topN store specs to assert the overlay (request arg carries dateFrom/size;
  shared filter/pageSize untouched) and added a round-trip spec proving the user filter + pageSize survive
  entering/leaving ranked. Added `idea-ranked` empty-state and null-score specs.
- **NIT:** fixed the stale "goes through setFilter's pipeline" comment.

## Decisions kept (with rationale)
- **`idea-ranked` injects `IdeaStore`** — the section spec designs it this way (intrinsic window toggle +
  topN slice); it's a feature component, not a generic reusable. Making it purely `@Input` would push the
  window toggle and its events up to the page with no real gain.
- **"This week" = rolling 7 days** — matches the section spec's explicit `now − 7 days` definition.
- **Default sort `rank` set in both the store initialState and `ideas.component.ts`** — intended; section-08
  already flipped the server + store defaults, and section-12 owns only the DEPLOYED-host appsettings flip,
  not the code default.
- **7 fixture spec files** gained the new required `Idea` ranking fields (mechanical; the `idea.model.ts`
  change forced them). Required (not optional) is the honest contract — the backend always returns them.

## Verification after fixes
`ng test` headless: 586 specs green. `ng build` clean (the 1 warning is pre-existing in an unrelated
component).
