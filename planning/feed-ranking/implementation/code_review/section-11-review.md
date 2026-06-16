# Code Review — section-11 Ranked view (Angular)

Reviewer: `deep-implement:code-reviewer`. `ng build` clean, 583 frontend tests pass at review time.

## Findings
- **HIGH — `setRankedWindow` clobbered the shared `dateFrom` filter** (phantom filter chip; the window
  persisted after leaving ranked mode; clicking the chip cleared the window). → **Fixed:** the ranked window
  is now a request-time OVERLAY applied inside `loadIdeas` only when `viewMode === 'ranked'`; the shared
  `filter` is never mutated. Leaving ranked drops the overlay automatically.
- **HIGH — `setRankedTopN` coupled `pageSize`** and the paginator fought the numbered Top-N. → **Fixed:**
  `rankedTopN` is the request `size` only in ranked mode (shared `pageSize` untouched); the paginator is
  hidden in ranked mode.
- **MEDIUM — `idea-ranked` injects the global store** rather than being purely `@Input`-driven. **Kept** —
  the section spec explicitly designs it this way (it's a feature component with an intrinsic window toggle,
  not a generic reusable); the Top-N slice is belt-and-suspenders over a server that already returns ≤ N.
- **MEDIUM — "This week" = rolling 7 days, not a calendar week.** **Kept per spec** (section-11 defines the
  window as `now − 7 days`); comment clarifies it's a rolling window.
- **MEDIUM — null-score path through the ranked list untested.** → **Fixed:** added a `score: null` fixture
  test.
- **LOW — empty-state untested.** → **Fixed:** added a `ranked-empty` test.
- **NIT — stale "goes through setFilter's pipeline" comment.** → **Fixed** (the setter no longer mutates the
  filter; comment rewritten).
- **NIT — default sort `rank` set in two places** (store initialState + `ideas.component.ts`). They agree;
  the section-08 server default + store default already flipped to `rank`, so section-11 aligning the
  dropdown is intended (section-12 owns only the DEPLOYED-host appsettings flip, not the code default).

## Confirmed good
No dangling `toggleView()` callers; `IdeaSortState.field` is `string` so `'rank'` typechecks; the ranked
component trusts server rank order (no client re-sort); badge gating on nullable bools is correct.
Added a round-trip store test proving the user filter + pageSize survive entering/leaving ranked mode.
