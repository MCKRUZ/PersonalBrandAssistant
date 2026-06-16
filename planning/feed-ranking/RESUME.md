# Resume — Feed Ranking Redesign (deep-plan)

## Status: IMPLEMENTATION COMPLETE — all 12 sections SHIPPED (2026-06-16)
Deep-plan + `/deep-implement` complete. All 12 sections implemented, reviewed, committed on `v2-rebuild`.
**Backend 844 tests green + frontend 586 tests green.** Code review trail for every section in
`implementation/code_review/`; usage guide at `implementation/usage.md`.

**ONLY remaining work = the manual production rollout** (section-12 runbook, gated at step 4): apply
migrations on both hosts → verify seed → backfill ~3,800 embeddings → **flip `IdeaScoring:ScoringEnabled`
true + restart (IRREVERSIBLE token spend — needs explicit go-ahead)** → verify → promote to Furious →
R-L6 grep the two deployed appsettings. Nothing else to `/deep-implement`.

### ⏸ ROLLOUT PAUSED (2026-06-16) — two blockers found at step 0 (read-only SSH recon, NO changes made)
- **Mac Mini = `192.168.50.189`** (key SSH works); the `.103` in older memory is stale/unreachable. Repo
  `/Users/matthewkruczek/personal-brand-assistant` on `v2-rebuild` @ `5f50c71c` (behind origin). Docker over
  SSH needs a login shell (`ssh … 'bash -lc "docker ps"'`). pba-db/api/web all running.
- **BLOCKER 1 — no pgvector in prod:** `db` image = `postgres:17-alpine` (vanilla). `CREATE EXTENSION vector`
  (section-01 migration) will FAIL. Must back up (`pg_dump`) then swap to a pgvector image on the same
  `pgdata` volume (`pgvector/pgvector:pg17` matches PG17 but is glibc vs current musl → possible collation
  REINDEX; an Alpine pgvector image or installing the extension is lower-risk). User-gated DB change.
- **BLOCKER 2 — migration apply mechanism unknown:** NO `Database.Migrate()` anywhere in `src/` → not
  auto-applied on API startup. Check the api Dockerfile/entrypoint on the host (or it's a manual
  `dotnet ef database update`). Resolve before deploying.
- Full detail + resume steps in the auto-memory `project_feed_ranking_rollout.md`.

### Section commits (this rebuild)
01 95589d7 · 02 196eb9b · 03 eedf22b · 04 afdfdad · 05-07 27f241d (build-coupled) · 08 bba00c6 ·
09 30c98e0 · 10 e8a3908 · 11 24583f0 · 12 9cfd92e · usage 669d5f8

### Sections 05-07 (27f241d) — the build-coupled unit, now SHIPPED
Done as one commit (05 changes the IIdeaAnalyzer contract that 07's sweep consumes; compiles only
together). Key as-built facts the later sections depend on:
- **`BrandRankingProfileSnapshot`** (`PBA.Application/Common/Models`) is the FULL shared shape (pillars
  w/ id+name+desc+weight+order+DescriptionEmbedding, topics, half-life, floor, multipliers, Version) with
  a `FromProfile(BrandRankingProfile)` factory. **Section-08 ComputeRank consumes this.**
- **`BrandFit`** (`PBA.Application/Common`) holds the only two brand-fit formulas: `EmbeddingWeightedSum`
  (pre-filter) and `RenormalizedSubScore(subScoresByPillarId, (Id,Weight)[])` (query-time). **Section-08
  must reuse `RenormalizedSubScore`, not re-derive it.**
- Derived `Score` is display-only (R-M1): renormalized LLM brandFit above threshold, raw embedding brandFit
  below. Authoritative ranking = `PillarSubScores` + section-08 `ComputeRank`.
- `ISidecarClient` gained a `SendPromptAsync(...,double? temperature,...)` overload (path 2); analyzer @ 0.1.
- Temperature, snapshot-once (R-H3/R-L2), ScoreAttempts cap=3 (R-M5), dedup gated (R-H1) + deterministic
  primary all in place. Services use `DateTimeOffset.UtcNow` (consistent w/ existing radar services).
- **Open follow-up (review L1):** no embed-attempt cap → a permanently-unembeddable in-window idea can
  wedge the dedup gate; needs an `Idea` schema field. Deferred.
- Review trail: `implementation/code_review/section-05-07-{review,interview,diff}.md`.

### Done (committed on v2-rebuild)
- **01 foundation** (95589d7): pgvector wiring, CosineSimilarity (10 tests), Embedding/RankingOptions,
  EnablePgVectorExtension migration. R-M4 gate PASSED live (text-embedding-3-small @ 1536).
- **02 brand-profile-domain** (196eb9b): BrandRankingProfile+BrandPillar, EF configs, xmin concurrency,
  partial unique index, startup seeder, AddBrandRankingProfile migration.
- **03 idea-entity-changes** (eedf22b): Idea embedding+sub-score fields, jsonb converter,
  AddIdeaEmbeddingAndSubScores migration (PillarSubScores defaults '[]'::jsonb for the populated table).
- **04 embed-async** (afdfdad): ISidecarClient.EmbedAsync via OpenRouter, index-mapped results, R-H2 guards.

### CRITICAL resume note — sections 05, 06, 07 are a BUILD-COUPLED UNIT
Section-05 changes the `IIdeaAnalyzer` contract + `IdeaAnalysis` record shape, which the old
`IdeaScoringService` consumes. That service is only rewritten in section-07. **The solution will NOT
compile (no tests can run) until 05->06->07 all land together.** Implement them as one unit, then commit
once the build is green. No cheap shim exists — the old single-score sweep must be fully rewritten (= §07).

### Carried-forward review flags for sections 05-07
- **§05 temperature:** `ISidecarClient.SendPromptAsync` has no temperature param. Either instruct
  low-variance in the prompt (flag to user) or add an overload. Decide during 05.
- **§06 (from §04 review):** EmbedAsync batches at 128 and is all-or-nothing per call + has no retry.
  Call it in manageable groups and wrap per-group try/catch so a 429 leaves items Embedding==null for
  retry without wasting a whole 128-batch. EmbedAsync drops empty inputs (result length may be < input
  length) — map vectors to ideas by filtering empties up front, never by positional index.
- **§06/§07 brand-fit helper:** expose ONE shared brand-fit weighted-sum (in §06) that §07 reuses.
- **§09 (from §02 review):** UpdateBrandRankingProfile must preserve pillar Ids on edit or
  RequiresVersionBump over-bumps.

### Established patterns (reuse in 05-12)
- Embeddings are `float[]` on Domain entities (Domain pure); `vector(1536)` mapping + float[]<->Vector
  converter live in `PgVectorModelConfiguration.Apply`, gated by `Database.IsNpgsql()` in OnModelCreating.
- Complex-type jsonb collections (e.g. PillarSubScores) need an explicit System.Text.Json converter +
  ValueComparer (InMemory can't map them); primitive `List<string>` jsonb works natively.
- NOT NULL columns added to the populated Ideas table need a store default (e.g. `'[]'::jsonb`, `0`).
- Tests live in PBA.Application.Tests + PBA.Infrastructure.Tests; Postgres-only guarantees (vector
  round-trip, partial index, xmin conflict) deferred to section-12 Testcontainers.
- Ranking aggregate is `BrandRankingProfile` (NOT the existing voice `BrandProfile`).

---

## Original plan-complete note
Deep-plan finished all steps. Research → interview → spec → plan → Opus review → integration →
TDD plan → 12 sectioned implementation files.

## Files (all in planning/feed-ranking/)
- `claude-plan.md` — the blueprint. **§13 = authoritative review-integrated fixes.**
- `claude-plan-tdd.md` — TDD test-stub plan mirroring the blueprint.
- `sections/index.md` — SECTION_MANIFEST + dependency graph (12 sections, 5 logical batches).
- `sections/section-01..12-*.md` — self-contained implementation sections (each: deps, tests-first, impl, verify).
- `claude-spec.md`, `claude-research.md`, `claude-interview.md` — inputs.
- `claude-integration-notes.md`, `reviews/iteration-1-opus.md` — review trail.
- Design source of truth: `../../docs/feed-ranking-redesign.md` + `../brand-strategy/brand-profile-v0.md`.

## Sections (build order)
1. foundation — R-M4 embed gate, pgvector enable, CosineSimilarity, EmbeddingOptions/RankingOptions
2. brand-profile-domain — **BrandRankingProfile** aggregate (renamed; see decision below), EF, seeder, migration
3. idea-entity-changes — Idea embedding + sub-scores (jsonb by BrandPillarId) + flags + migration
4. embed-async — ISidecarClient.EmbedAsync via OpenRouter
5. idea-analyzer — per-pillar structured scoring + name→id mapping
6. embedding-service — embed ideas/pillars + brand-fit pre-filter
7. scoring-sweep-dedup — rewired IdeaScoringService + IdeaDedupService (replaces IdeaClusterer)
8. composite-rank-listideas — ComputeRank + default rank sort + extended IdeaDto
9. brand-profile-api — GET/PUT /api/brand-ranking-profile, two write modes
10. frontend-editor — Angular Brand Profile editor (auto-apply weights vs confirm re-score)
11. frontend-ranked-view — store 'ranked' mode + idea-ranked component
12. cutover — migration order, backfill, gated first LLM run, R-L6 dead-config removal

## KEY BINDING DECISION made during planning (must hold in implementation)
**Naming collision:** a live voice-drafting `BrandProfile` entity already exists
(`src/PBA.Domain/Entities/BrandProfile.cs`, used by DraftContent/CheckVoice/etc). The ranking
aggregate the plan calls `BrandProfile` was therefore renamed **`BrandRankingProfile`** (table
`BrandRankingProfiles`), snapshot `BrandRankingProfileSnapshot`, API route
`/api/brand-ranking-profile`. The existing voice profile is untouched.

## Open items flagged by section writers (resolve during implement)
- **Temperature control (section-05):** `ISidecarClient.SendPromptAsync` has no temperature param.
  Either instruct low-variance in the prompt (path 1, flag to user) or add an overload (path 2).
- **EmbedAsync failed-batch return contract (section-04↔06):** section-04 throws on a failing batch;
  section-06 must wrap per-chunk so failures leave items Embedding==null for retry. Verify the seam.
- **pgvector property type:** `float[]` vs `Pgvector.Vector` — confirm against installed package version.
- **Scoring gate:** section-12 expects an `IdeaScoring:ScoringEnabled` flag gating the first prod LLM run
  (replaces dead BackfillEnabled). Confirm section-07 added it or add in 12.

## How to implement
Run `/deep-implement` against `planning/feed-ranking/sections/`. Sections are dependency-ordered;
respect the batch graph in `index.md`. TDD: write the section's tests first (each section lists them).

## Windows workaround (deep-plan/deep-implement scripts have a transcript-path + cp1252 bug)
When a script needs the transcript, run with:
```
export PYTHONUTF8=1
export CLAUDE_TRANSCRIPT_PATH="C:/Users/kruz7/.claude/projects/C--Users-kruz7-OneDrive-Documents-Code-Repos-MCKRUZ-personal-brand-assistant/<NEW_SESSION_ID>.jsonl"
```
After /clear the session id changes — use the newest *.jsonl in that projects folder.
Also: the SubagentStop hook did NOT auto-write section files on this run; they were written manually
by the orchestrator from subagent output. If a future batch run shows empty sections/, do the same.

## Pre-implementation gate (before any migration)
Verify OpenRouter proxies `openai/text-embedding-3-small` at native dim 1536 via
`GET /api/v1/embeddings/models` (review item R-M4 — section-01 owns this).
