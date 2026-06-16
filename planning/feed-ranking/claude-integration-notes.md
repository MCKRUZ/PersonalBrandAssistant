# Integration Notes — Opus review (iteration 1)

The review found correctness/concurrency hazards, not architecture problems. Integrating nearly all of
it. The unifying fix is to replace every "or" in the plan with a decision.

## Integrating (and how)

| ID | Decision integrated into plan |
|----|------|
| **C1** | §9: ListIdeas projection MUST exclude `Embedding`; pull filtered set, rank+sort+page in memory. Added revisit trigger (~25k rows or p95 > 200ms → precompute stored brandFit per version). |
| **C2** | §9: ComputeRank weights only sub-scores whose pillar exists in the active profile; **renormalize over surviving pillars**; **normalize weights at read time** (`w/Σw`) so scale never drifts. Stale items (ScoredProfileVersion < active) get renormalized brandFit AND a `stale` flag in the DTO. |
| **C3** | §5/§7: `PillarSubScore` keyed by **`BrandPillarId`** (not name). Analyzer maps LLM-returned names → ids against the snapshot before persist. Resolved before C2. |
| **H1** | §8c: dedup sweep **gated** — skips while any in-window idea has `Embedding == null` (`AnyAsync` guard). |
| **H2** | §8a: sanitize/skip empty inputs; per-batch try/catch; failed items stay `Embedding == null` for retry; **never store zero/NaN vectors**. |
| **H3** | §4/§10a: add EF **optimistic concurrency token** (`xmin`) on BrandProfile. Snapshot taken once per sweep; `ScoredProfileVersion` = snapshot version. |
| **H4** | §10a: **server-enforced** write modes — weights-only command ignores pillar-definition fields (no version bump); definition changes only via the confirm path (version bump + re-score). Not client-trusted. |
| **M1** | §5/R6: documented as intended; specify the **LLM brandFit** (not embedding-only) feeds the derived `Score`; below-threshold items keep `Score = round(embeddingBrandFit×10)` only if never LLM-scored — flagged. `score` vs `rank` sort labeled distinctly in UI. |
| **M2** | §5/§12: drop the `Score` index in migration if `score` sort demoted (keep `score` sort server-side as a secondary option, so keep index — decided: **keep index + keep score sort**). |
| **M3** | §6: pass `dimensions` in the embeddings request to force 1536; document Model↔column coupling. |
| **M4** | §12: **pre-implementation step** — `GET /api/v1/embeddings/models`, confirm slug + native dim before writing the `vector(1536)` migration. |
| **M5** | §8b: add `ScoreAttempts` cap on Idea; reject+log analyses where all pillars are identical (collapse guard). |
| **M6** | §6: `CosineSimilarity` does full `dot/(‖a‖‖b‖)`; clamp brandFit→[0,1], derived Score→[0,10]. |
| **L1** | §9: `bool?` flags use `== true`; null → multiplier 1.0, brandFit 0 → rank 0. |
| **L2** | §8: sweep snapshots `IOptionsMonitor.CurrentValue` once at sweep start. |
| **L3** | §11: add duplicate+in-memory-paging ListIdeas test; add a Testcontainers test exercising `CosineDistance` ordering; **dedup cosine computed in-memory** over the ~40-item window (O(n²) acceptable, stated). |
| **L4** | §4/§12: partial unique index `WHERE IsActive = true`. |
| **L5** | §4/§12: **startup seeder** guarded by the L4 index (two deploy hosts), not a data migration. |
| **L6** | §12: grep-gate confirming zero references to `BackfillEnabled`/`Clustering.MinScore` before deletion. |

## Not integrating / deferring
- Nothing rejected outright. The review's two genuinely optional points were resolved as judgment calls:
  - **M2 Score index:** keep it (we retain a server-side `score` sort as a secondary user option), rather
    than drop it. Trade-off: a near-idle index vs. preserving a cheap sort. Keeping is lower-risk.
  - **L3 dedup location:** chose **in-memory cosine** over the ~40-item clustering window (not in-DB),
    because the window is tiny and it keeps dedup testable without Testcontainers. The Testcontainers test
    covers only the ListIdeas similarity/mapping path, not dedup.

All other items folded into `claude-plan.md`.
