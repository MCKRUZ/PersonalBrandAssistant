# Code Review — section-08 composite-rank + ListIdeas

Reviewer: `deep-implement:code-reviewer`. Build green, 815 solution tests pass at review time.

## Findings
- **C1 (Critical, claimed) — projecting value-converted `PillarSubScores` into `RankRow` may not translate
  on Npgsql; InMemory tests can't catch it.** → **Resolved as a false alarm** (see interview). The EF Core
  documented limitation is "cannot query *into* value-converted properties" (reference their members in the
  query); projecting the *whole* property out is supported and materializes via the converter.
- **C1a — no test proves the generated SQL excludes the `vector(1536)` Embedding column.** Valid gap;
  R-C1a holds by construction (EF emits only projected columns) and section-12 Testcontainers gives the
  real-Postgres proof.
- **H1 — whole-filtered-set materialization; the real trigger is the default *unfiltered* browse (O(table)),
  not just "25k rows".** Valid framing; per R-C1b this is an accepted decision. Comment sharpened.
- **H2 — default-sort flip to `rank` silently re-orders existing callers.** Intentional (R-M2 / section-08);
  full suite passed, no caller broke. By design.
- **M1/M2 — string sorts moved from SQL collation to in-memory current-culture comparison (non-deterministic
  across server locales; null-ordering shift for category).** Valid → fixed with a fixed ordinal comparer.
- **M3 — spec lists a derived-Score clamp test that doesn't exist here.** Correct: section-07 owns `Score`
  (it does `round(Clamp(...,0,1)*10)`); section-08 must not re-derive it. Spec is internally contradictory.
- **L1 — snapshot loads pillar vectors unused on the read path** (5 pillars, negligible). No change.
- **L2 — `ComputeRank` uses `int? scoredProfileVersion` vs spec's `int`.** Implementation is more correct
  (unscored ideas have null); spec is stale.
- **L4 — no TimeProvider at the handler** (consistent with codebase; pure function takes `now`).

## Confirmed good
Rank formula, half-life decay + floor, read-time renormalization (reuses `BrandFit.RenormalizedSubScore`),
stale flag, multipliers, clamp, and the DetectedAt tie-break are all correct and unit-tested at the pure
level (`ComputeRankTests`, 11 cases). Filters still push to SQL; rank/sort/page in memory (R-C1b).
