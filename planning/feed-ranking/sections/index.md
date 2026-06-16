<!-- PROJECT_CONFIG
runtime: dotnet
test_command: dotnet test
END_PROJECT_CONFIG -->

<!-- SECTION_MANIFEST
section-01-foundation
section-02-brand-profile-domain
section-03-idea-entity-changes
section-04-embed-async
section-05-idea-analyzer
section-06-embedding-service
section-07-scoring-sweep-dedup
section-08-composite-rank-listideas
section-09-brand-profile-api
section-10-frontend-editor
section-11-frontend-ranked-view
section-12-cutover
END_MANIFEST -->

# Implementation Sections Index — Feed Ranking Redesign

Backend = .NET 10 (`dotnet test`). Frontend sections (10, 11) use Angular `ng test` — noted per section.
Authority documents: `claude-plan.md` (esp. **§13 = binding review fixes**), `claude-plan-tdd.md`,
`docs/feed-ranking-redesign.md`, `planning/brand-strategy/brand-profile-v0.md`.

## Dependency Graph

| Section | Depends On | Blocks | Parallelizable |
|---------|------------|--------|----------------|
| section-01-foundation | - | 02, 03, 04, 06, 08 | Yes (root) |
| section-02-brand-profile-domain | 01 | 05, 06, 08, 09 | Yes (with 03, 04) |
| section-03-idea-entity-changes | 01 | 05, 06, 08 | Yes (with 02, 04) |
| section-04-embed-async | 01 | 06 | Yes (with 02, 03) |
| section-05-idea-analyzer | 02, 03 | 07 | Yes (with 06, 08, 09) |
| section-06-embedding-service | 02, 03, 04 | 07 | Yes (with 05, 08, 09) |
| section-07-scoring-sweep-dedup | 05, 06 | 12 | Yes (with 10, 11) |
| section-08-composite-rank-listideas | 02, 03 | 11 | Yes (with 05, 06, 09) |
| section-09-brand-profile-api | 02 | 10 | Yes (with 05, 06, 08) |
| section-10-frontend-editor | 09 | 12 | Yes (with 07, 11) |
| section-11-frontend-ranked-view | 08 | 12 | Yes (with 07, 10) |
| section-12-cutover | all | - | No (final) |

## Execution Order (batches)

1. **Batch 1:** section-01-foundation (no dependencies — pure helpers, pgvector enable, config, R-M4 gate)
2. **Batch 2:** section-02, section-03, section-04 (parallel after 01)
3. **Batch 3:** section-05, section-06, section-08, section-09 (parallel after Batch 2)
4. **Batch 4:** section-07, section-10, section-11 (parallel after their Batch-3 deps)
5. **Batch 5:** section-12-cutover (final, after all)

## Section Summaries

### section-01-foundation
Pre-implementation embedding gate (**R-M4**: `GET /api/v1/embeddings/models` confirms
`openai/text-embedding-3-small` proxied at native dim 1536). Add `Pgvector.EntityFrameworkCore`,
register `HasPostgresExtension("vector")` + `o.UseVector()` and a migration enabling the `vector` extension.
Pure `CosineSimilarity(float[],float[])` static util in `PBA.Application` (full dot/‖a‖‖b‖, R-M6) + tests.
New `EmbeddingOptions` (model, dim 1536, batch 128) and `RankingOptions` (`PreFilterThreshold`,
`DedupThreshold`, `ScoringWindowDays=30`) config classes + appsettings sections + `IOptionsMonitor` wiring.

### section-02-brand-profile-domain
`BrandProfile` + `BrandPillar` aggregate in `PBA.Domain`. EF configs: `BrandPillars` related table with
`vector(1536)` `DescriptionEmbedding`; `AuthorityTopics`/`AntiTopics`/`VoiceMarkers` as jsonb `List<string>`.
Versioning semantics (weights/decay → no bump; pillar def/topics → bump). Optimistic concurrency token
(xmin, **R-H3**). Partial unique index `WHERE "IsActive" = true` (**R-L4**). Idempotent startup seeder for
v1 profile from `brand-profile-v0.md` (**R-L5**). Migration.

### section-03-idea-entity-changes
Extend `PBA.Domain/Entities/Idea.cs`: `Embedding` `vector(1536)` (nullable), `EmbeddedAt`,
`PillarSubScores` as jsonb (keyed by **`BrandPillarId`**, R-C3), `IsAntiTopic`/`IsAuthorityTopic` (nullable),
`ScoredProfileVersion`, `ScoreAttempts` (**R-M5**). EF config + migration `AddIdeaEmbeddingAndSubScores`.
Retain existing `Score` column + index.

### section-04-embed-async
`ISidecarClient.EmbedAsync(IReadOnlyList<string>, model?, ct)` + `OpenRouterClient` impl: POST
`/api/v1/embeddings` with `dimensions:1536` (**R-M3**), order results by `index`, batch in chunks of
`BatchSize`, sanitize/skip empty inputs and isolate batch failures (**R-H2**), default model from
`EmbeddingOptions`.

### section-05-idea-analyzer
Rewrite `IdeaAnalyzer` to per-pillar scoring against a `BrandProfileSnapshot`: JSON-schema structured
output, system prompt built from the profile, per-level rubric, fixed few-shot anchors, low temperature.
Map LLM pillar names → `BrandPillarId` (**R-C3**). Central-collapse guard (**R-M5**). Returns null on
failure. New contract (`IIdeaAnalyzer`, `IdeaAnalysisInput`, `PillarScore`, `IdeaAnalysis`).

### section-06-embedding-service
`IdeaEmbeddingService`: embed ideas with `Embedding == null` (title+description), store vector + `EmbeddedAt`;
re-embed active-profile pillar descriptions when `Version` changes; never persist zero/NaN vectors (**R-H2**).
Brand-fit pre-filter compute = weighted Σ `weight × cosine(itemVec, pillarVec)` using `CosineSimilarity`.

### section-07-scoring-sweep-dedup
Rewire `IdeaScoringService`: snapshot profile + `RankingOptions` once per sweep (**R-H3, R-L2**); candidate
set = embedded, in 30-day window, never-scored or stale; above `PreFilterThreshold` → `AnalyzeAsync` →
store sub-scores/flags/`ScoredProfileVersion`/derived `Score`; below → embedding-only brandFit; cap
`ScoreAttempts` (**R-M5**). Replace `IdeaClusterer` LLM call with in-memory cosine dedup, gated on full
embedding coverage (**R-H1, R-L3-dedup**). Delete `IdeaClusterer.cs`; remove `BackfillEnabled` &
`Clustering.MinScore`.

### section-08-composite-rank-listideas
Pure `ComputeRank(idea, profile, now)`: renormalized weighted brandFit over active pillars
(**R-C2a/b/c**), `max(exp(-ln2/halfLife×age), floor)` recency, anti/authority multipliers (**R-L1**),
clamps (**R-M6**). `ListIdeas`: filters→SQL, projection **excludes Embedding** (**R-C1a**), rank+sort+page
in memory (**R-C1b**), default `SortBy="rank"`, retain `score` sort (**R-M2**). Extend `IdeaDto` with
`Rank`, `BrandFit`, `PillarBreakdown`, flags, `RecencyFactor`, `Stale`.

### section-09-brand-profile-api
`GetActiveBrandProfile` query + `UpdateBrandProfile` command with two **server-enforced** write modes
(**R-H4**): weights-only (no version bump, no re-score) vs definition change (bump + re-score). FluentValidation
validator. Optimistic concurrency (**R-H3**). `BrandProfileEndpoints.cs`: `GET`/`PUT /api/brand-profile`.

### section-10-frontend-editor
Angular `brand-profile` route + standalone component. Reactive Form: positioning/audience, half-life/floor
sliders, multipliers, pillar list with **weight sliders** (auto-apply: weights-only PUT + list reload),
pillar-definition edits staged behind a "Save & re-score" confirm dialog (warns LLM cost). `idea.service.ts`:
`getBrandProfile()`, `updateBrandProfile()`. **Frontend: `ng test`.**

### section-11-frontend-ranked-view
`idea.store.ts`: extend `viewMode` union with `'ranked'`, add `rankedWindow`/`rankedTopN` + setters, default
sort `rank`. `view-toggle.component.ts`: third button. New `idea-ranked.component.ts`: numbered Top-N, big
rank numerals, window toggle, per-item brand-fit breakdown (pillars hit + reason + anti/authority badges +
stale indicator) from new `IdeaDto` fields. `ideas.component.ts`: `@else if ranked` branch. **Frontend: `ng test`.**

### section-12-cutover
Migration apply order (vector extension → BrandProfile/BrandPillar → Idea cols); seed v1 profile; backfill
all ~3,800 embeddings + pillar vectors; scoring sweep LLM-scores last-30-day window; flip default sort;
enable Ranked view + editor. Gate first prod LLM-scoring run behind manual confirm. Grep both deployed
appsettings to confirm `BackfillEnabled`/`Clustering.MinScore` fully removed (**R-L6**).
