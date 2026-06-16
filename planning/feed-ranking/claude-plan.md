# Implementation Plan — Feed Ranking Redesign

## 1. Background and goal

The Personal Brand Assistant ingests content "ideas" from RSS / Hacker News / GitHub sources into a
PostgreSQL database (~3,800 rows today). A background service scores each idea 0-10 for "content
opportunity" using one LLM call against a **single hardcoded sentence** describing the owner's brand,
and the Ideas page can sort by that score.

That score is coarse (11 buckets), opaque (one number, no breakdown), recency-blind, and anchored to a
vague target. This plan replaces it with a **brand-anchored, multi-factor composite rank** computed
against an **editable Brand Profile**, and surfaces the rank on the Ideas page as the default ordering
and a dedicated "Ranked" view.

The composite rank is:

```
rank = brandFit × recencyDecay × antiTopicMultiplier × authorityBoost
```

where `brandFit` is a weighted sum of per-pillar sub-scores (0-1 each), `recencyDecay` is an
exponential 7-day-half-life gate with a floor, anti-topic items are near-killed (×0.1), and
authority-topic items are nudged (×1.2). Crucially, **the LLM produces raw per-pillar sub-scores that
are stored once; the pillar weights are applied at query time**, so re-weighting re-ranks instantly with
zero LLM calls. The LLM is only re-run when pillar *definitions* change.

## 2. Tech context (confirmed)

- Backend: .NET 10, Clean Architecture (`PBA.Domain` / `PBA.Application` / `PBA.Infrastructure` / `PBA.Api`),
  MediatR + `Result<T>` (`PBA.Domain.Common`), EF Core on **PostgreSQL (Npgsql)**, FluentValidation via
  `ValidationBehavior<,>` pipeline, options bound from `appsettings.json`. No endpoint auth in v2.
- LLM access: `ISidecarClient` → `OpenRouterClient` (OpenRouter HTTP, default chat model
  `google/gemini-2.5-flash`). **OpenRouter now exposes a native embeddings endpoint** (`/api/v1/embeddings`,
  OpenAI-compatible) — embeddings stay inside the sidecar abstraction.
- Vector storage: **pgvector** (`Pgvector.EntityFrameworkCore`), `vector(1536)` column, **exact cosine,
  no ANN index** (premature at 3,800 rows).
- Frontend: Angular 19 standalone components, NgRx **signal store**, PrimeNG, lazy feature routes.
- Tests: xUnit + EF InMemory + Moq (backend), Jasmine/Karma + HttpTestingController (frontend).
  **InMemory cannot execute pgvector SQL** — see §11.

## 3. Deliverables overview (and the data-flow they form)

1. `BrandProfile` aggregate + EF mapping + migration + seed (the source of truth).
2. `ISidecarClient.EmbedAsync` + OpenRouter embeddings implementation.
3. `Idea` embedding column + per-pillar sub-score storage + profile-version stamp + migration.
4. New `IdeaAnalyzer` that scores an item against a profile → per-pillar sub-scores (structured output).
5. Embedding service + a brand-fit pre-filter that decides which items earn an LLM call.
6. Rewired `IdeaScoringService` (embed → pre-filter → LLM-score 30-day window) and **embedding-based
   dedup replacing the LLM clusterer**.
7. Query-time composite-rank computation + `ListIdeas` default sort + extended `IdeaDto`.
8. Brand Profile read/update API (MediatR queries/commands) + endpoints.
9. Angular: Brand Profile editor page + "Ranked" view mode + service/store wiring.
10. Cutover backfill (embed all, LLM-score the last 30 days).

Runtime data flow once built:
```
ingest idea ──► embed (OpenRouter) ──► store vector
                                   └─► brand-fit pre-filter (cosine vs pillar vectors)
                                         ├─ above threshold & in 30-day window ─► LLM per-pillar score ─► store sub-scores
                                         └─ below threshold ──────────────────► embedding-only brandFit
dedup: cosine similarity over vectors (replaces LLM clusterer)
read (ListIdeas): composite rank = weighted sub-scores × recencyDecay × antiTopic × authority  (default sort)
```

## 4. Domain model — Brand Profile

New aggregate in `PBA.Domain/Entities/`. One active profile; `Version` increments on a pillar-definition
change. Pillars/topics are owned child rows (or a single jsonb document — see decision below).

```csharp
// BrandProfile.cs  (fields only — no methods shown)
public class BrandProfile
{
    public Guid Id { get; init; }
    public int Version { get; set; }              // bump when pillar DEFINITIONS change
    public bool IsActive { get; set; }            // exactly one active
    public string Positioning { get; set; }
    public string AudiencePrimary { get; set; }
    public string? AudienceSecondary { get; set; }
    public double HalfLifeDays { get; set; }      // default 7
    public double DecayFloor { get; set; }        // default ~0.075
    public double AntiTopicMultiplier { get; set; } // default 0.1
    public double AuthorityBoost { get; set; }      // default 1.2
    public IList<BrandPillar> Pillars { get; set; }
    public IList<string> AuthorityTopics { get; set; }
    public IList<string> AntiTopics { get; set; }
    public IList<string> VoiceMarkers { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

// BrandPillar.cs
public class BrandPillar
{
    public Guid Id { get; init; }
    public Guid BrandProfileId { get; init; }
    public string Name { get; set; }
    public string Description { get; set; }       // text the LLM + embeddings score against
    public double Weight { get; set; }            // 0..1, weights across pillars sum ~1.0
    public int Order { get; set; }
    public float[]? DescriptionEmbedding { get; set; } // pillar vector for pre-filter (vector(1536))
}
```

**Storage decision:** map `Pillars` as a related table (`BrandPillars`) because each pillar needs its own
embedding vector column; map `AuthorityTopics` / `AntiTopics` / `VoiceMarkers` as `jsonb` `List<string>`
(consistent with how `Idea.Tags` is already mapped). Configuration class
`PBA.Infrastructure/Data/Configurations/BrandProfileConfiguration.cs` + `BrandPillarConfiguration.cs`.

**Versioning rule (single most important semantic):**
- Editing a **weight**, half-life, floor, multipliers → does NOT bump `Version` (query-time only).
- Editing a pillar **Name/Description**, adding/removing a pillar, or changing authority/anti-topics →
  bumps `Version` and marks affected ideas' sub-scores stale (those scored under an older version).

**Seed:** a migration (or idempotent seeder run at startup) inserts the v1 profile from
`planning/brand-strategy/brand-profile-v0.md` (5 pillars + weights + topics) as the single active profile,
`Version = 1`.

## 5. Idea entity changes

Add to `PBA.Domain/Entities/Idea.cs`:

```csharp
public float[]? Embedding { get; set; }              // vector(1536), nullable until embedded
public DateTimeOffset? EmbeddedAt { get; set; }
public IList<PillarSubScore> PillarSubScores { get; set; } // raw 0..1 per pillar, stored once
public bool? IsAntiTopic { get; set; }
public bool? IsAuthorityTopic { get; set; }
public int? ScoredProfileVersion { get; set; }       // which BrandProfile.Version produced the sub-scores
// Idea.Score (existing int? 0-10) is RETAINED as a derived display value = round(brandFit*10)
```

`PillarSubScore` is a child row (`Idea.Id`, `PillarName` or `BrandPillarId`, `Score double 0..1`,
`Reason string?`). Mapped table `IdeaPillarSubScores`. (Alternative: a `jsonb` document on `Idea`;
prefer the table only if we need to query/aggregate sub-scores in SQL — we don't, brandFit is computed
in the read handler, so **`jsonb` on `Idea` is acceptable and simpler**. Plan picks **jsonb** to avoid a
join; document the trade-off in the section.)

EF config (`IdeaConfiguration.cs`): map `Embedding` as `vector(1536)`, register
`modelBuilder.HasPostgresExtension("vector")` in the context, enable `o.UseVector()` on `UseNpgsql`.
Migration `AddIdeaEmbeddingAndSubScores`. No vector index (exact scan).

## 6. Embeddings — ISidecarClient extension

Add to `PBA.Application/Common/Interfaces/ISidecarClient.cs`:

```csharp
/// <summary>Returns one embedding vector per input, in input order. Batches internally if needed.</summary>
Task<IReadOnlyList<float[]>> EmbedAsync(
    IReadOnlyList<string> inputs, string? model = null, CancellationToken ct = default);
```

Implement in `OpenRouterClient`: POST `/api/v1/embeddings` with `{ model, input: string[], encoding_format:"float" }`,
deserialize `data[]` ordered by `index`. Batch inputs in chunks (e.g. 128/request). Model from a new
`EmbeddingOptions` (default `openai/text-embedding-3-small`, 1,536-dim). Honors the route-through-sidecar rule.

New options class `PBA.Infrastructure/Configuration/EmbeddingOptions.cs`:
```
EmbeddingOptions { Model="openai/text-embedding-3-small", Dimensions=1536, BatchSize=128 }
```
appsettings section `"Embedding"`.

Helper `CosineSimilarity(float[] a, float[] b)` lives in a pure static utility in `PBA.Application`
(unit-testable without a DB).

## 7. New IdeaAnalyzer (per-pillar scoring)

Replace the body/prompt of `PBA.Infrastructure/Services/Radar/IdeaAnalyzer.cs`. New contract:

```csharp
public interface IIdeaAnalyzer
{
    /// <summary>Score an item against the active profile. Returns per-pillar sub-scores (0..1),
    /// anti-topic / authority flags, and a one-line reason per pillar. Null on LLM/parse failure.</summary>
    Task<IdeaAnalysis?> AnalyzeAsync(IdeaAnalysisInput input, BrandProfileSnapshot profile, CancellationToken ct);
}

public record IdeaAnalysisInput(string Title, string? Description, string? Url, string SourceName);
public record PillarScore(string PillarName, double Score, string? Reason);   // Score in {0,.25,.5,.75,1}
public record IdeaAnalysis(IReadOnlyList<PillarScore> Pillars, bool IsAntiTopic, bool IsAuthorityTopic, string Reason);
```

Prompt construction (per research Topic 4):
- System prompt built **from the BrandProfile**: positioning, audience, the pillar names+descriptions,
  authority topics, anti-topics, and a **per-level rubric** ({0,.25,.5,.75,1} meaning per pillar).
- **Fixed few-shot anchor exemplars** (2-4, identical every call, fixed order) to calibrate the scale.
- **JSON-schema structured output** (gemini-2.5-flash structured mode) — do not regex free text.
- **Low temperature (0-0.2).**
- `BrandProfileSnapshot` is an immutable in-memory projection of the active profile (avoids re-querying
  per item; carries `Version`).

## 8. Scoring pipeline + dedup (background services)

### 8a. Embedding step
New `IdeaEmbeddingService` (or fold into `IdeaScoringService`): finds ideas with `Embedding == null`,
calls `EmbedAsync` in batches over `title + " " + description` (or summary once present), stores vector +
`EmbeddedAt`. Also (re)embeds active-profile pillar descriptions whenever the profile `Version` changes.

### 8b. Brand-fit pre-filter + LLM scoring (rewire `IdeaScoringService`)
Per sweep:
1. Candidates = ideas with embedding present, `DetectedAt >= now-30d`, and either never scored or
   `ScoredProfileVersion < activeProfile.Version` (stale).
2. Compute embedding brandFit = weighted Σ over pillars of `weight × cosine(itemVec, pillarVec)`.
3. Items with brandFit ≥ `PreFilterThreshold` → call `IIdeaAnalyzer.AnalyzeAsync` → store
   `PillarSubScores`, flags, `ScoredProfileVersion = activeProfile.Version`, `ScoredAt`, and derived
   `Score = round(brandFit*10)`. Throttle between LLM calls (existing `ThrottleMs`).
4. Items below threshold → store the embedding-only brandFit (no LLM), set `ScoredProfileVersion` so they
   aren't reconsidered until the profile changes.

The `BackfillEnabled` flag is superseded by the 30-day window; remove it (and its appsettings key).

### 8c. Dedup via embeddings (replace `IdeaClusterer` + `IdeaClusteringService` LLM call)
Replace the LLM clusterer with cosine-similarity dedup: within the lookback window, group ideas whose
pairwise cosine ≥ `DedupThreshold` (~0.85); pick the highest-brandFit as primary, set others'
`DuplicateOfId`, stamp `ClusteredAt`. Delete `IdeaClusterer.cs` and the LLM call; keep a (renamed)
background service that runs the embedding dedup. Remove `Clustering.MinScore` (gate is now embedding-based).

New options (IOptionsMonitor for the runtime-tunable ones): `RankingOptions { PreFilterThreshold,
DedupThreshold, ScoringWindowDays=30 }`. Half-life/floor/multipliers live on the BrandProfile (DB), not config.

## 9. Composite rank at read time (`ListIdeas`)

In `PBA.Application/Features/Ideas/Queries/ListIdeas.cs`:
- Load the active `BrandProfile` (weights, half-life, floor, multipliers) once per query.
- Add a `ComputeRank(idea, profile, now)` pure function:
  `brandFit = Σ weight_p × subScore_p` (over pillars present); `recency = max(exp(-ln2/halfLife × ageDays), floor)`;
  `rank = brandFit × recency × (IsAntiTopic ? antiMult : 1) × (IsAuthority ? authBoost : 1)`.
- **Where computed:** brandFit needs the per-pillar sub-scores + current weights. Because weights are
  query-time and decay needs `now`, rank cannot be a stored column. Options:
  - (a) compute in-DB via a projection (hard with jsonb sub-scores + exp), or
  - (b) **fetch the candidate page's rows and compute rank in memory** in the handler, then order.
  Pure in-memory ranking over the full filtered set is fine at 3,800 rows but breaks SQL paging. **Plan
  picks: push down all *filters* to SQL, pull the filtered set (ids + sub-scores + flags + DetectedAt),
  compute rank in memory, sort, then page in memory.** Document the 3,800-scale justification; revisit if
  the corpus grows (then precompute brandFit per profile-version as a stored column and only apply decay live).
- Add `"rank"` to `ApplySort`; make it the **default** `SortBy` (replace `"detectedat"` default in both
  the `Query` record and `IdeaEndpoints` param defaults). Keep `"score"`, `"detectedat"`, etc.
- Extend `IdeaDto` with: `Rank (double)`, `BrandFit (double)`, `PillarBreakdown (IReadOnlyList<{Name,Score,Reason}>)`,
  `IsAntiTopic`, `IsAuthorityTopic`, `RecencyFactor`. The Ranked view + "why #1 beat #2" use these.

## 10. Brand Profile API + frontend

### 10a. Backend
- `GetActiveBrandProfile` query → `Result<BrandProfileDto>`.
- `UpdateBrandProfile` command → validates weights, persists; **if only weights/half-life/floor/multipliers
  changed → no version bump, no re-score**; **if pillar definitions/topics changed → bump `Version`,
  return a flag indicating a re-score will run** (the background sweep picks up stale items). FluentValidation
  validator (weights in [0,1], at least one pillar, etc.).
- Endpoints in a new `BrandProfileEndpoints.cs`: `GET /api/brand-profile`, `PUT /api/brand-profile`.

### 10b. Frontend — Brand Profile editor
- Route in `features/ideas/ideas.routes.ts`: `{ path: 'brand-profile', loadComponent: ... }`.
- New `features/ideas/pages/brand-profile/brand-profile.component.ts` (standalone) + a small signal store or
  reuse a service. Reactive Form: positioning, audience, half-life/floor sliders, multipliers, a pillar
  list (name, description, **weight slider**), authority/anti-topic chip editors, voice markers.
- **Weight slider changes auto-apply**: on change, PUT (weights-only) then trigger the Ideas list to reload
  (re-rank is server-side query-time, instant). **Pillar-definition edits** are staged and require an
  explicit "Save & re-score" confirm dialog (warns LLM cost) before PUT.
- `idea.service.ts`: add `getBrandProfile()`, `updateBrandProfile(dto)`.

### 10c. Frontend — Ranked view
- `idea.store.ts`: extend `viewMode: 'grid' | 'list' | 'ranked'`; add `rankedWindow: 'today' | 'week'`
  and `rankedTopN` (default 20) to state; `setRankedWindow()`. `list()` already sends sort — ensure default
  sort is `rank`.
- `view-toggle.component.ts`: add a third p-button (e.g. `pi pi-sort-amount-down`).
- New `features/ideas/components/idea-ranked/idea-ranked.component.ts`: numbered Top-N, big rank numerals,
  window toggle, and per-item brand-fit breakdown (pillars hit + reason + anti/authority badges) from the
  new `IdeaDto` fields. `ideas.component.ts`: add `@else if (store.viewMode() === 'ranked')` branch.
- `score-badge` stays (driven by the derived `Score`); score-distribution unaffected.

## 11. Testing strategy (TDD)

- **Pure functions first** (no DB): `CosineSimilarity`, `ComputeRank` (brandFit weighting, exponential
  decay + floor, anti/authority multipliers), weight-validation. xUnit, table-driven.
- **IdeaAnalyzer**: Moq `ISidecarClient.SendPromptAsync`/structured call returns canned JSON; assert
  parsing into per-pillar sub-scores, flag extraction, profile-driven prompt content, failure → null.
- **EmbedAsync**: Moq HTTP / fake handler; assert batching, order-by-index, request shape.
- **ListIdeas**: EF InMemory (filters + in-memory rank/sort/paging path — no pgvector needed here since
  ranking is in-memory); assert default sort = rank, rank ordering, DTO breakdown populated.
- **pgvector similarity / dedup query**: **cannot** use EF InMemory. Either (a) unit-test the dedup grouping
  over in-memory vectors with the cosine helper, or (b) a Postgres **Testcontainers** integration test for
  the `CosineDistance` query. Plan: do (a) for logic + one (b) smoke test for the EF mapping.
- **Frontend**: `idea.service.spec.ts` extend for brand-profile calls + default sort param; store spec for
  `viewMode='ranked'`/window; component specs for editor (auto-apply vs confirm) and ranked view rendering.
- Coverage target 80% on new code.

## 12. Cutover / rollout

1. Migrations: add pgvector extension, BrandProfile/BrandPillar tables, Idea embedding + sub-score columns.
2. Seed the v1 BrandProfile.
3. Deploy. Embedding service backfills all ~3,800 vectors (cents of cost) + pillar vectors.
4. Scoring sweep LLM-scores the last-30-day window against v1; older items rank ~0 via decay.
5. Default sort flips to composite rank; Ranked view + editor become available.
6. Old `IdeaClusterer` + `BackfillEnabled` + `Clustering.MinScore` removed.

This is reversible up to the migration; the production push of LLM-scoring spends real (small) tokens —
gate the first prod run behind a manual confirm, consistent with existing Radar cost caution.

## 13. Review-integrated refinements (AUTHORITATIVE — overrides the above where they conflict)

These resolve correctness/concurrency hazards from the Opus review. Each is binding.

**Identity & versioning**
- **R-C3:** `PillarSubScore` is keyed by **`BrandPillarId`**, never by name. The analyzer maps the
  LLM-returned pillar *names* → ids against the `BrandProfileSnapshot` before persisting. Names are
  display only; ids are identity (survive renames).
- **R-H3:** `BrandProfile` gets an EF **optimistic-concurrency token** (Npgsql `xmin` via
  `.IsRowVersion()`/`UseXminAsConcurrencyToken`). The scoring sweep takes the profile **snapshot once**
  per sweep; `ScoredProfileVersion` is always set to the *snapshot's* version, not "current".
- **R-L4:** enforce single active profile with a **partial unique index** `WHERE "IsActive" = true`.
- **R-L5:** seed v1 via an **idempotent startup seeder** guarded by that index (two deploy hosts:
  Mac Mini + Furious), not a data migration.

**Ranking math (§9 ComputeRank)**
- **R-C2a:** weight only sub-scores whose `BrandPillarId` exists in the **active** profile.
- **R-C2b:** **renormalize weights at read time** (`weight_p / Σ active weights`) so the brandFit scale
  never drifts when weights are edited and so stale items (missing some pillars) stay comparable.
- **R-C2c:** an idea with `ScoredProfileVersion < active.Version` is ranked with renormalized brandFit
  over its surviving pillars AND surfaced with a `Stale = true` flag in `IdeaDto`.
- **R-M6:** `CosineSimilarity` computes the full `dot/(‖a‖‖b‖)` (do not assume unit-norm — Matryoshka
  shrink breaks normalization). Clamp `brandFit` to [0,1] and derived `Score` to [0,10].
- **R-L1:** nullable flags use `== true`; an unscored idea → multipliers 1.0, brandFit 0 → rank 0
  (sorts to bottom, mirroring the existing `Score ?? -1` convention).

**ListIdeas read path (§9)**
- **R-C1a:** the projection **MUST NOT select `Embedding`** (would ship ~23 MB/query). Select only ids,
  sub-scores, flags, `DetectedAt`, `ScoredProfileVersion`, display fields.
- **R-C1b:** filters push to SQL; rank + sort + paging happen **in memory** over the filtered set
  (justified at ~3,800 rows). **Revisit trigger:** ~25k rows OR ListIdeas p95 > 200 ms → precompute a
  stored `brandFit`-per-version column and apply only decay live.
- **R-M2:** keep the `Score` index and retain a server-side `score` sort as a secondary user option.

**Scoring sweep & embeddings (§6, §8)**
- **R-M4 (pre-implementation):** before writing the `vector(1536)` migration, call
  `GET /api/v1/embeddings/models` and confirm `openai/text-embedding-3-small` is proxied with native
  dim 1536. Also pass `dimensions: 1536` in the embeddings request body (**R-M3**) so model↔column stay coupled.
- **R-H2:** `EmbedAsync` sanitizes/skips empty inputs; wraps each batch in try/catch so one bad batch
  doesn't abort the sweep; failed items remain `Embedding == null` for retry. **Never persist a zero or
  NaN vector** (cosine vs zero is undefined and corrupts dedup).
- **R-H1:** the dedup sweep is **gated** — it returns early if any in-window idea still has
  `Embedding == null` (cheap `AnyAsync`), so dedup never runs mid-backfill and picks a wrong primary.
- **R-L3-dedup:** dedup cosine is computed **in memory** over the small (~40-item) lookback window
  (O(n²) acceptable); it does not issue a pgvector `OrderBy(CosineDistance)` query.
- **R-M5:** add `ScoreAttempts (int)` to `Idea`; the sweep caps attempts (e.g. 3) so a poison item
  (refusal / unparseable) doesn't burn an LLM call every sweep forever. Reject + log any analysis whose
  pillar scores are all identical (central-collapse guard).
- **R-L2:** the sweep snapshots `IOptionsMonitor<RankingOptions>.CurrentValue` once at sweep start.
- **R-M1:** the derived `Score` badge is fed by the **LLM** brandFit for LLM-scored items; below-threshold
  items (embedding-only) use `round(embeddingBrandFit×10)`. Documented as intended; the UI labels `score`
  and `rank` sorts distinctly so a high-Score/low-rank (old) item doesn't read as a bug.

**Brand Profile API (§10a)**
- **R-H4:** two **server-enforced** write modes. A weights-only update ignores pillar-definition fields,
  does not bump `Version`, triggers no re-score. A definition update bumps `Version` and lets the sweep
  re-score stale in-window items. The frontend cannot apply a definition change through the weights path.

**Cutover (§12)**
- **R-L6:** before deleting `BackfillEnabled` / `Clustering.MinScore`, grep the repo (and note the two
  deployed appsettings) to confirm zero remaining references.

## 14. Out of scope
Learned/performance-based brand signal; source-authority factor (cut); multi-profile; ANN vector index.
