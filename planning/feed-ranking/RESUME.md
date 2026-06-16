# Resume — Feed Ranking Redesign (deep-plan)

## Status: PLAN COMPLETE (2026-06-16)
Deep-plan finished all steps. Research → interview → spec → plan → Opus review → integration →
TDD plan → 12 sectioned implementation files. Ready for `/deep-implement`.

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
