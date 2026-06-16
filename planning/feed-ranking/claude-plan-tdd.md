# TDD Plan — Feed Ranking Redesign

Companion to `claude-plan.md`. For each implementation area this lists the tests to write **before**
implementing. Stubs are prose / minimal signatures — the implementer writes the real assertions, fixtures,
and mocks. Conventions follow the existing suite (per `claude-research.md` §Tests):

- **Backend:** xUnit, Arrange-Act-Assert, naming `Method_Scenario_ExpectedResult`. EF `UseInMemoryDatabase(Guid)`
  for handler tests, `Moq` for `ISidecarClient`, `Options.Create()` / `NullLogger<T>.Instance`. Existing
  helpers: `CreateIdea()` in `ListIdeasHandlerTests`, `.Setup().ReturnsAsync()` in `IdeaAnalyzerTests`.
- **Frontend:** Jasmine/Karma, `TestBed`, `provideHttpClient()` + `provideHttpClientTesting()`,
  `httpMock.expectOne()` + `req.flush()`, `afterEach(() => httpMock.verify())`.
- **Critical constraint:** EF InMemory **cannot execute pgvector SQL**. Vector logic is tested through the
  pure `CosineSimilarity` helper + in-memory dedup grouping; one optional Testcontainers Postgres smoke test
  covers the `vector(1536)` EF mapping only.
- Target: 80% coverage on new code. Pure functions first, then services, then handlers, then UI.

---

## 4. Domain model — Brand Profile

**`BrandProfileConfigurationTests` (EF mapping, InMemory where possible / Testcontainers for vector col)**
```
# Test: active profile round-trips Pillars as related rows with their own Id/Weight/Order
# Test: AuthorityTopics / AntiTopics / VoiceMarkers persist as jsonb List<string>
# Test: BrandPillar.DescriptionEmbedding maps to vector(1536) (Testcontainers smoke — skipped under InMemory)
# Test: partial unique index rejects a second IsActive=true profile (R-L4) — Postgres-only
```
**`BrandProfileVersioningTests` (domain semantics — pure, no DB)**
```
# Test: editing only Weight/HalfLife/Floor/multipliers does NOT bump Version
# Test: editing a pillar Name or Description bumps Version
# Test: adding or removing a pillar bumps Version
# Test: changing AuthorityTopics / AntiTopics bumps Version
```
**`BrandProfileSeederTests` (idempotent startup seeder, R-L5)**
```
# Test: first run inserts the v1 profile (5 pillars + weights + topics) with Version=1, IsActive=true
# Test: second run is a no-op (does not duplicate, does not bump Version)
# Test: seeder guarded by the IsActive partial index — concurrent host start inserts exactly one
```

## 5. Idea entity changes

**`IdeaConfigurationTests` (extend existing)**
```
# Test: Embedding maps to vector(1536) and is nullable (Testcontainers smoke)
# Test: PillarSubScores persist as jsonb (chosen over join table per §5) and round-trip by BrandPillarId
# Test: IsAntiTopic / IsAuthorityTopic / ScoredProfileVersion / EmbeddedAt / ScoreAttempts are nullable & persist
# Test: existing Score (int? 0-10) column unchanged / still indexed
```

## 6. Embeddings — ISidecarClient.EmbedAsync + CosineSimilarity

**`CosineSimilarityTests` (pure static util, table-driven — WRITE FIRST)**
```
# Test: identical vectors → 1.0
# Test: orthogonal vectors → 0.0
# Test: opposite vectors → -1.0
# Test: computes full dot/(‖a‖‖b‖), does NOT assume unit norm (R-M6) — non-normalized inputs scored correctly
# Test: zero vector input → returns 0 (or guarded), never NaN (R-H2 corollary)
# Test: length mismatch → throws/guarded
```
**`OpenRouterClientEmbedTests` (fake HttpMessageHandler / Moq handler)**
```
# Test: posts to /api/v1/embeddings with { model, input[], encoding_format:"float", dimensions:1536 } (R-M3)
# Test: returns one vector per input in INPUT order even when API returns data[] out of order (sorts by index)
# Test: batches inputs in chunks of BatchSize (128) — N>128 produces multiple requests
# Test: model defaults from EmbeddingOptions when model arg null
# Test: empty / whitespace inputs are skipped or sanitized, not sent (R-H2)
# Test: a failing batch throws/propagates without corrupting other batches' order
```

## 7. New IdeaAnalyzer (per-pillar scoring)

**`IdeaAnalyzerTests` (rewrite existing; Moq ISidecarClient structured call)**
```
# Test: parses structured JSON into per-pillar sub-scores (0..1 on the {0,.25,.5,.75,1} scale)
# Test: maps LLM-returned pillar NAMES → BrandPillarId via the snapshot; unknown name is dropped/logged (R-C3)
# Test: extracts IsAntiTopic / IsAuthorityTopic flags and per-pillar one-line reason
# Test: system prompt is built FROM the BrandProfileSnapshot (positioning, audience, pillar names+descriptions,
#        authority/anti topics, per-level rubric) — assert key profile strings appear in the prompt
# Test: includes the fixed few-shot anchor exemplars (same count/order every call)
# Test: LLM/parse failure → returns null (does not throw)
# Test: central-collapse guard — analysis with all-identical pillar scores is rejected + logged (R-M5)
# Test: low temperature passed to the structured call (0–0.2)
```

## 8. Scoring pipeline + dedup (background services)

**`IdeaEmbeddingServiceTests`**
```
# Test: selects only ideas with Embedding == null, embeds title+description, stores vector + EmbeddedAt
# Test: re-embeds active-profile pillar descriptions when profile Version changes
# Test: a bad batch is caught — failed items remain Embedding == null for retry, sweep continues (R-H2)
# Test: never persists a zero/NaN vector (R-H2)
```
**`IdeaScoringServiceTests` (rewired sweep)**
```
# Test: candidate set = embedding present AND DetectedAt >= now-30d AND (never scored OR ScoredProfileVersion < snapshot.Version)
# Test: snapshots BrandProfile ONCE per sweep; ScoredProfileVersion set to the SNAPSHOT version not "current" (R-H3)
# Test: snapshots IOptionsMonitor<RankingOptions>.CurrentValue once at sweep start (R-L2)
# Test: items with embedding brandFit >= PreFilterThreshold → AnalyzeAsync called, sub-scores+flags stored, Score=round(brandFit*10)
# Test: items below threshold → embedding-only brandFit stored, ScoredProfileVersion stamped, NO LLM call
# Test: derived Score for below-threshold items = round(embeddingBrandFit*10) (R-M1)
# Test: ScoreAttempts increments; item exceeding cap (3) is skipped, no further LLM call (R-M5)
# Test: throttles between LLM calls (ThrottleMs honored)
```
**`IdeaDedupServiceTests` (embedding dedup replacing IdeaClusterer)**
```
# Test: groups in-window ideas with pairwise cosine >= DedupThreshold (in-memory O(n²), no pgvector query) (R-L3-dedup)
# Test: highest-brandFit item is primary; others get DuplicateOfId + ClusteredAt
# Test: GATED — returns early (no grouping) if any in-window idea has Embedding == null (R-H1)
# Test: IdeaClusterer LLM path is gone (no SendPromptAsync call)
```

## 9. Composite rank at read time (ListIdeas)

**`ComputeRankTests` (pure function — WRITE FIRST, table-driven)**
```
# Test: brandFit = Σ weight_p × subScore_p over pillars present in the ACTIVE profile only (R-C2a)
# Test: weights renormalized at read time (weight_p / Σ active weights) — scale stable when a weight is edited (R-C2b)
# Test: stale idea (missing some pillars) ranked over surviving pillars with renormalized weights, comparable (R-C2c)
# Test: recency = max(exp(-ln2/halfLife × ageDays), floor); age 0→1.0, age=halfLife→0.5, very old→floor
# Test: anti-topic (IsAntiTopic == true) applies antiMult (×0.1); non-flag/null → ×1.0 (R-L1)
# Test: authority (IsAuthorityTopic == true) applies authBoost (×1.2); null → ×1.0 (R-L1)
# Test: brandFit clamped [0,1], derived Score clamped [0,10] (R-M6)
# Test: unscored idea → brandFit 0, multipliers 1.0 → rank 0 (sorts to bottom) (R-L1)
```
**`ListIdeasHandlerTests` (extend existing; InMemory — ranking is in-memory so no pgvector needed)**
```
# Test: default SortBy is "rank" (no sort param supplied → rank ordering)
# Test: results ordered by descending composite rank
# Test: filters (Status, Category, Tags, date, MinScore, IncludeDuplicates) still push to SQL and apply
# Test: projection does NOT select Embedding (R-C1a) — assert DTO has no vector / query shape excludes it
# Test: IdeaDto populated with Rank, BrandFit, PillarBreakdown{Name,Score,Reason}, IsAntiTopic, IsAuthorityTopic, RecencyFactor
# Test: idea with ScoredProfileVersion < active.Version surfaces Stale = true (R-C2c)
# Test: "score" sort still works as a secondary user option (R-M2)
# Test: paging applied in memory after rank sort, returns correct page slice + totalCount
```

## 10. Brand Profile API + frontend

**`GetActiveBrandProfileHandlerTests`**
```
# Test: returns the active profile as BrandProfileDto (pillars ordered, weights, topics, markers)
# Test: no active profile → Result.NotFound
```
**`UpdateBrandProfileHandlerTests` (two server-enforced write modes — R-H4)**
```
# Test: weights-only update persists weights, does NOT bump Version, triggers no re-score, ignores pillar-def fields
# Test: pillar-definition update (name/description/topics) bumps Version and marks in-window stale items for re-score
# Test: definition change submitted through the weights path is REJECTED/ignored on the server (frontend can't bypass)
# Test: optimistic concurrency — stale xmin token → conflict result, no lost update (R-H3)
```
**`UpdateBrandProfileValidatorTests` (FluentValidation)**
```
# Test: each weight in [0,1]; at least one pillar required; positioning/audience non-empty
# Test: half-life > 0, floor in [0,1], multipliers > 0
```
**`BrandProfileEndpointsTests` (WebApplicationFactory)**
```
# Test: GET /api/brand-profile → 200 + DTO
# Test: PUT /api/brand-profile (weights-only) → 200, no version bump
# Test: PUT with invalid weights → 400 validation
```
**Frontend `idea.service.spec.ts` (extend)**
```
# Test: getBrandProfile() GETs /api/brand-profile and maps DTO
# Test: updateBrandProfile(dto) PUTs /api/brand-profile with body
# Test: list() sends sortBy=rank by default
```
**Frontend `idea.store.spec.ts` (extend)**
```
# Test: viewMode union accepts 'ranked'; setRankedWindow('today'|'week') updates state
# Test: rankedTopN defaults to 20; setting it reloads list
# Test: default sort is rank
```
**Frontend `brand-profile.component.spec.ts` (new)**
```
# Test: weight slider change auto-applies (calls updateBrandProfile weights-only + reloads list), NO confirm dialog
# Test: pillar name/description edit is staged and requires the "Save & re-score" confirm before PUT
# Test: confirm dialog warns about LLM cost before a definition save
# Test: reactive form validation blocks save when a weight is out of [0,1] or no pillars
```
**Frontend `idea-ranked.component.spec.ts` (new)**
```
# Test: renders numbered Top-N with big rank numerals from IdeaDto.Rank order
# Test: per-item breakdown shows pillars hit + reason + anti/authority badges
# Test: window toggle (Today / This week) re-requests with the right window
# Test: Stale items show a stale indicator
```

## 12. Cutover / rollout

**`CutoverTests` / migration & config-removal guards**
```
# Test: migrations apply cleanly (pgvector extension, BrandProfile/BrandPillar, Idea embedding+subscore cols) — Testcontainers
# Test: BackfillEnabled / Clustering.MinScore keys removed AND no code references remain (R-L6 — covered by a repo grep in the section, plus a build that fails if the symbols are referenced)
# Test: first prod LLM-scoring run is gated behind a manual confirm flag (no auto-spend on deploy)
```

## Test execution order (TDD sequence)

1. `CosineSimilarityTests`, `ComputeRankTests`, `BrandProfileVersioningTests` — pure, fastest, highest leverage.
2. `OpenRouterClientEmbedTests`, `IdeaAnalyzerTests` — service-level with Moq.
3. `IdeaEmbeddingServiceTests`, `IdeaScoringServiceTests`, `IdeaDedupServiceTests` — sweep logic on InMemory.
4. `ListIdeasHandlerTests`, Brand Profile handler/validator/endpoint tests — handlers on InMemory + WAF.
5. EF mapping / migration Testcontainers smoke tests (vector column, partial unique index).
6. Frontend service → store → component specs.
