# Opus Review

**Model:** claude-opus-4 (deep-plan opus-plan-reviewer)
**Generated:** 2026-06-15

---

Overall: plan is solid and grounded in the codebase. Query-time-weights-over-stored-sub-scores is the
right backbone. MediatR/Result<T>/BackgroundService/IOptions/signal-store used idiomatically;
jsonb-vs-table calls correct. Risks below are about correctness under change and concurrency, not
architecture. None require redesign; all require the plan to stop saying "or" and start saying "this."

## CRITICAL

**C1 — ListIdeas in-memory rank/sort/page (§9).** Must pull all *filtered* rows, compute rank in memory,
sort, page in memory. (a) Projection MUST explicitly exclude `Embedding` or EF ships ~23 MB/query.
(b) totalCount is post-filter pre-rank — fine (anti-topic ×0.1 still included, not excluded). Add a
concrete revisit threshold (e.g. ~25k rows or ListIdeas p95 > 200ms) for the stored-brandFit escape hatch.

**C2 — brandFit "over pillars present" is a landmine (§9).** (1) Stale sub-scores: items scored under an
old `Version` with renamed/removed pillars, never re-entering the 30-day candidate set, rank against new
weights using old sub-scores forever (decay floor 0.075 ≠ never). ComputeRank must only weight sub-scores
whose pillar exists in the *active* profile, keyed by `BrandPillarId`; stale items either get brandFit
renormalized over surviving pillars or are flagged. Pick one. (2) Weight renormalization: validator
"weights in [0,1]" does NOT enforce sum=1; normalize weights at read time (`weight/Σweights`) so rank
scale doesn't drift each edit.

**C3 — Sub-scores keyed by PillarName vs BrandPillarId is ambiguous and load-bearing (§5,§7).** Store by
`BrandPillarId` (survives renames). LLM returns names → analyzer maps name→id against the snapshot before
persisting. Prerequisite for C2.

## HIGH

**H1 — Embedding backfill must precede dedup; nothing enforces ordering (§8,§12).** Three racing
BackgroundServices. Dedup mid-backfill can pick the wrong primary → wrong `DuplicateOfId` → hides the
better item (ListIdeas `DuplicateOfId == null` filter). Gate dedup: skip sweep while any in-window idea
has `Embedding == null` (cheap `AnyAsync`).

**H2 — Embedding batch partial-failure unspecified (§6,§8a).** OpenRouter rejects whole batch on one bad
input; empty title+null desc → empty string → 400. Specify: sanitize/skip empty inputs; per-batch
try/catch so one bad batch doesn't abort the sweep; failed items stay `Embedding == null` for retry;
NEVER store a zero/NaN vector (cosine vs zero vector is undefined, corrupts dedup).

**H3 — Profile-version race + no optimistic concurrency (§4,§8b,§10a).** Version logic is mostly
self-healing IF the snapshot is taken once per sweep and `ScoredProfileVersion` = snapshot version (plan
does this). BUT add an EF concurrency token (`xmin`/rowversion) on BrandProfile — weight auto-apply PUT
firing while a pillar edit is mid-save makes concurrent PUTs likely.

**H4 — Two editor write paths can clobber (§10b).** Weights-only auto-apply PUT must NOT round-trip
staged pillar-definition edits (would apply a definition change without re-score confirm and without
version bump → permanently stale). Server must enforce: weights-only mode ignores pillar-definition
fields; definition changes only via the confirm path. Don't trust the client.

## MEDIUM

**M1 — Derived `Score=round(brandFit×10)` loses recency (§5,R6).** Badge can show 9 on a bottom-ranked
(old) item. Acceptable (brandFit is substance) but flag as intended; ensure `score` sort vs `rank` sort
visibly differ. Specify whether below-threshold items' embedding-only brandFit feeds the badge (different
scale than LLM brandFit) — pick one scale for `Score`.

**M2 — jsonb sub-scores correct; `Score` index becomes dead weight (§5).** Drop the `Score` index if
`score` sort is demoted, or keep only if server-side `score` sort retained. Pillars-as-child-table correct
(each needs `vector(1536)`). Topics as jsonb List<string> correct.

**M3 — EmbeddingOptions.Dimensions is dead config unless passed to API (§6).** Model and `vector(N)`
column are coupled. Either pass `dimensions` in the request to force it, or document the coupling
(changing model/dim requires a migration). Don't ship config that lies.

**M4 — OpenRouter embedding slug/dim must be verified BEFORE the migration (§6).** Pre-implementation
step: `GET /api/v1/embeddings/models`, confirm `openai/text-embedding-3-small` proxied + native dim 1536,
then write the `vector(1536)` migration. 1-min check prevents migration redo.

**M5 — Structured-output has no poison-item guard (§7,R2).** Schema-valid garbage (all pillars 0.5 =
central collapse) or a refusal → null → re-queried every sweep → infinite LLM burn on a poison item. Add
a `ScoreAttempts` cap; reject/log analyses where all pillars are identical.

**M6 — Cosine helper must do full dot/(‖a‖‖b‖), not assume normalized (§6,§8b).** Matryoshka-shrunk
vectors aren't unit-norm. Clamp brandFit to [0,1] and derived Score to [0,10] (anti-correlated items can
yield negative weighted cosine).

## LOWER / POLISH

**L1** — ComputeRank on `bool?` needs `== true`; null (unscored) → multiplier 1.0, brandFit 0 → rank 0
(mirrors `Score ?? -1`). Make explicit.
**L2** — Sweep should snapshot `IOptionsMonitor.CurrentValue` once at sweep start, not per item.
**L3** — Add ListIdeas test: duplicate-heavy filtered set pages correctly in-memory (totalCount excludes
duplicates AND page N returns right rank-ordered slice). Add a Testcontainers test that exercises
`CosineDistance` ordering (mapping round-trip ≠ proving the OrderBy SQL translates). Decide dedup cosine
in-DB (needs Testcontainers) vs in-memory (O(n²) over ~40-item window — fine, say so).
**L4** — Add partial unique index `WHERE IsActive = true` to enforce single-active-profile (relied on
everywhere, never enforced).
**L5** — Seeding: use a startup seeder guarded by the L4 index (two deploy hosts — Mac Mini + Furious —
make a data migration / racing seeder risky), not a data migration.
**L6** — Removing `BackfillEnabled`/`Clustering.MinScore`: grep-gate §12 to confirm zero references before
deletion (two live deployments).

## Change before coding (priority)
1. C3 (key by BrandPillarId) → prereq for C2. 2. C2 stale-version + weight-normalization. 3. C1 exclude
Embedding + dup-paging test. 4. H1 dedup gate + H2 batch-failure (no zero vectors). 5. H3 concurrency
token + H4 server-enforced PUT modes. 6. M4 pre-flight slug/dim. 7. M5 poison cap. 8. L4 partial index.
