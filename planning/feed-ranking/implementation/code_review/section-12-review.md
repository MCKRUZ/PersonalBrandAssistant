# Code Review — section-12 Cutover (code deliverables)

Reviewer: `deep-implement:code-reviewer`. Build clean, 844 backend tests pass (incl. the Testcontainers
migration guard on real pgvector Postgres). No Critical/High.

Scope note: the production ROLLOUT (SSH to Mac Mini + Furious, migrations, ~3,800-embedding backfill, the
gated irreversible LLM-scoring run) is a manual runbook the human executes — NOT done here.

## Findings
- **MEDIUM — Test 3 asserted only the AnalyzeAsync call count**, not the load-bearing invariant that a
  gated-off sweep leaves the item reconsiderable. → **Fixed:** the test now asserts `ScoredProfileVersion`
  is null and `ScoreAttempts == 0` when gated off (and stamped when on).
- **MEDIUM — Test 1 partial-index assertion was too loose** (`indexdef ILIKE '%unique%' AND '%IsActive%'`
  would pass on a plain — non-partial — unique index, which would wrongly forbid >1 inactive profile). →
  **Fixed:** asserts `pg_index.indisunique AND indpred IS NOT NULL` (proves the index is genuinely partial).
- **LOW — gate via `above = []` reassignment** discarded a just-computed sorted/taken list. → **Fixed:**
  folded the gate into the `above` construction (`ScoringEnabled ? prefiltered… : []`).
- **LOW — no Testcontainers fixture reuse.** No prior fixture exists (Testcontainers was added in this
  section), so there is nothing to reuse; a fresh container for the single migration test is fine. No change.
- **NITs** — Test 3's embedder setup is harmless dead scaffolding; method name diverges from the spec stub
  (the theory name is better); Test 2's path-walk fails loudly out-of-tree (acceptable for an in-tree guard).
  No changes.

## Confirmed good
Gate correctness verified: with `ScoringEnabled = false`, above-threshold items are excluded from the scored
set with no `ScoreAttempts` increment and no `ScoredProfileVersion` stamp, so they stay in the candidate
query and score the moment the gate flips; embedding + below-threshold embedding-only brandFit still run.
Updating the 8 existing scoring tests to `ScoringEnabled = true` is correct (they assert the LLM path, which
by design only runs when the gate is on) — not masking a regression. R-L6 dead-config removal (done in
section-07) is guarded by Test 2; the repo grep is clean.
