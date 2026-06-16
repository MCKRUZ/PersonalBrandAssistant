# Code Review — sections 05+06+07 (build-coupled unit)

Reviewer: `deep-implement:code-reviewer` subagent. Build green, 797 solution tests pass at review time.

These three sections were implemented and reviewed as one unit because section-05 changes the
`IIdeaAnalyzer` contract that the old `IdeaScoringService` (rewritten in 07) consumes — the solution
cannot compile until all three land.

## Findings (severity-ordered)

### HIGH
- **H1 — derived `Score` uses two different brandFit sources across the pre-filter boundary.**
  Above-threshold items use `BrandFit.RenormalizedSubScore` (Σw·s / Σw, a true 0..1 average);
  below-threshold items use `BrandFit.EmbeddingWeightedSum` (raw Σw·cos, not renormalized). Different
  scales unless Σweight == 1.
- **H2 — dedup primary selection uses the quantized int `Score` and breaks ties on EF load order**
  (non-deterministic primary); section-07 spec asked to recompute brandFit from `PillarSubScores`.

### MEDIUM
- **M1 — no injected `TimeProvider`; both services use `DateTimeOffset.UtcNow` directly.**
- **M2 — `IdeaEmbeddingService.ComputeEmbeddingBrandFit` is dead production code** (the sweep called
  `BrandFit.EmbeddingWeightedSum` directly), reachable only from its own unit tests.
- **M3 — single `SaveChangesAsync` after the whole embed-chunk loop**: a crash mid-backfill loses all
  embedded chunks and re-spends embedding tokens.

### LOW / NIT
- **L1** — dedup R-H1 gate spans the full window; one perpetually-unembeddable in-window idea wedges
  dedup forever (no embed-attempt cap).
- **L2** — central-collapse guard fires only on exact all-pillar equality (meets spec).
- **L3** — the below-threshold partition is uncapped per sweep (bounded in practice by the 30-day window).
- **N1** — few-shot anchor placeholder pillar names can't map (intentional, commented).
- **N2** — no explicit reflection test for removed `BackfillEnabled`/`MinScore` (the build is the guard).

## Confirmed good
- Brand-fit formula exists in exactly ONE place (`BrandFit`); both paths route through it.
- EmbedAsync index-alignment seam handled correctly (filter empties before the call, align by filtered
  order, `Count` mismatch guard).
- R-H2 zero/NaN/Infinity rejection + per-chunk failure isolation correct.
- R-C3 name→id mapping (case-insensitive, trimmed, unknown-drop, zero-survivor null) correct + tested.
- R-M5 ScoreAttempts increments before the call and analyzer-null still counts; cap excluded in query.
- Temperature path 2 wired honestly end-to-end (overload, OpenRouter sends it, CLI ignores it).
