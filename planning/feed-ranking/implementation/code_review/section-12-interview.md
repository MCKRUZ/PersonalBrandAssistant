# Review triage & decisions — section-12

Triaged autonomously. No Critical/High; two MEDIUM test-quality fixes applied.

## Applied fixes
- **MEDIUM (Test 3 weak assertion):** now asserts the gated-off sweep leaves the above-threshold idea
  un-stamped (`ScoredProfileVersion == null`, `ScoreAttempts == 0`) and stamped when enabled — encodes the
  load-bearing "reconsiderable when the gate flips" invariant, not just the call count.
- **MEDIUM (Test 1 partial-index):** tightened to `pg_index.indisunique AND indpred IS NOT NULL` so it
  genuinely proves the index is PARTIAL (a plain unique index would have failed correctly now).
- **LOW (gate readability/perf):** folded the gate into the `above` construction
  (`ScoringEnabled ? prefiltered… : []`) instead of building then discarding the sorted list.

## Decisions kept
- **No Testcontainers fixture reuse** — there was no prior fixture to reuse (this section introduced the
  dependency); a fresh `pgvector/pgvector:pg16` container for the single migration guard is fine.
- **NITs** (Test 3 harmless embedder scaffolding, theory method-name divergence, Test 2 out-of-tree
  behavior) left as-is.

## Out of scope (manual runbook — NOT executed)
The production rollout is irreversible and gated by design and was deliberately NOT run here:
- Applying migrations on Mac Mini (`v2-rebuild`) + Furious (`main`).
- Backfilling ~3,800 idea embeddings + pillar vectors.
- **The first LLM-scoring run** (flip `IdeaScoring:ScoringEnabled` → true + restart) — irreversible token
  spend, gated per the Nexus decision "Gate first production LLM run behind manual confirm".
- Editing the two DEPLOYED `appsettings.json` (R-L6 grep on the hosts).
The runbook in `section-12-cutover.md` (steps 0–7, gated at step 4) is the human's checklist for this.

## Verification after fixes
`dotnet build` clean (0 warnings). CutoverTests (4) + IdeaScoringServiceTests (9) green; full backend suite
844 green incl. the real-Postgres migration guard.
