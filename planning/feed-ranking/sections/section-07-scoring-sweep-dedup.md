# section-07-scoring-sweep-dedup

## Goal

Rewire the background scoring pipeline to the new brand-anchored model and replace the LLM-based deduplication with in-memory cosine dedup. Concretely:

1. Rewire `IdeaScoringService` so each sweep: snapshots the active `BrandRankingProfile` and `RankingOptions` **once**; builds a candidate set (embedded ideas in the 30-day window that are never-scored or stale); computes an embedding brand-fit pre-filter; sends items above `PreFilterThreshold` to the per-pillar `IIdeaAnalyzer` (storing sub-scores, flags, `ScoredProfileVersion`, and a derived `Score`); stores embedding-only brand-fit for items below threshold; and caps per-idea `ScoreAttempts`.
2. Replace `IdeaClusterer` (LLM call) with **in-memory cosine-similarity dedup**, gated on full embedding coverage of the window.
3. Delete `IdeaClusterer.cs` + `IIdeaClusterer` + `IdeaClustererTests`; remove `BackfillEnabled` and `Clustering.MinScore`.

This is **Batch 4** work, parallelizable with sections 10 and 11. It is the last backend behavioral change before cutover (section-12).

## Dependencies (already built — reference only, do not re-implement)

- **section-01-foundation:** `CosineSimilarity(float[] a, float[] b)` pure static util in `PBA.Application` (full `dot/(‖a‖‖b‖)`, returns 0 for zero-vector, throws on length mismatch — **R-M6**). `RankingOptions { double PreFilterThreshold; double DedupThreshold; int ScoringWindowDays = 30; }` config class registered via `IOptionsMonitor<RankingOptions>` from the `"Ranking"` appsettings section. `EmbeddingOptions`. `vector` extension enabled on the context.
- **section-03-idea-entity-changes:** `Idea` now has `float[]? Embedding`, `DateTimeOffset? EmbeddedAt`, `IList<PillarSubScore> PillarSubScores` (jsonb, keyed by `BrandPillarId` — **R-C3**), `bool? IsAntiTopic`, `bool? IsAuthorityTopic`, `int? ScoredProfileVersion`, `int ScoreAttempts`. Existing `Score (int?)`, `ScoredAt`, `DuplicateOfId`, `ClusteredAt`, `DetectedAt` retained.
- **section-05-idea-analyzer:** new `IIdeaAnalyzer` contract:
  ```csharp
  Task<IdeaAnalysis?> AnalyzeAsync(IdeaAnalysisInput input, BrandRankingProfileSnapshot profile, CancellationToken ct);
  // record IdeaAnalysisInput(string Title, string? Description, string? Url, string SourceName);
  // record PillarScore(Guid PillarId, string PillarName, double Score, string? Reason);  // already mapped to BrandPillarId
  // record IdeaAnalysis(IReadOnlyList<PillarScore> Pillars, bool IsAntiTopic, bool IsAuthorityTopic, string Reason);
  ```
  The analyzer maps LLM pillar **names → `BrandPillarId`** against the snapshot and returns `null` on failure (incl. central-collapse guard).
- **section-06-embedding-service:** `IdeaEmbeddingService` already embeds ideas + pillar descriptions and exposes the brand-fit pre-filter computation as a reusable helper (`ComputeEmbeddingBrandFit(...)`). **Decision:** consume that helper rather than re-deriving the weighted-sum formula. If section-06 did not expose it as reusable, lift the weighted-sum loop into a pure helper in `PBA.Application` so both call the same code. Do not have two divergent copies of the formula.

`BrandRankingProfileSnapshot` (from section-02/05) is the immutable in-memory projection of the active profile carrying `Version`, the pillars (id, name, description, weight, `DescriptionEmbedding`), topics, half-life, floor, multipliers.

## Background the implementer needs

The PBA ingests "ideas" (RSS/HN/GitHub) into Postgres (~3,800 rows). Two `BackgroundService`s run today:

- `IdeaScoringService` — scores each idea 0-10 with one LLM call against a hardcoded brand sentence. **Being rewired.**
- `IdeaClusteringService` + `IdeaClusterer` — LLM-call dedup of similar ideas. **The `IdeaClusterer` LLM call is being deleted; `IdeaClusteringService` is being rewired to in-memory cosine dedup** (or renamed `IdeaDedupService` — the TDD spec names the test `IdeaDedupServiceTests`; rename the service to `IdeaDedupService` for clarity and update DI + tests accordingly).

Existing files (read before editing):

- `src/PBA.Infrastructure/Services/Radar/IdeaScoringService.cs` — current sweep (rewire).
- `src/PBA.Infrastructure/Services/Radar/IdeaClusteringService.cs` — current cluster sweep (rewire to in-memory cosine dedup, rename to `IdeaDedupService`).
- `src/PBA.Infrastructure/Services/Radar/IdeaClusterer.cs` — LLM clusterer (**delete**).
- `src/PBA.Application/Common/Interfaces/IIdeaClusterer.cs` — clusterer interface + `ClusterInput` record (**delete** — verify no other consumer first).
- `src/PBA.Infrastructure/Configuration/IdeaScoringOptions.cs` — remove `BackfillEnabled`.
- `src/PBA.Infrastructure/Configuration/ClusteringOptions.cs` — remove `MinScore` (dedup gate is now embedding-based; keep `IntervalMinutes`, `LookbackHours`, `MaxItemsPerSweep`; remove unused `Model`).
- `src/PBA.Infrastructure/DependencyInjection.cs` — lines 78-87 register the options, `IIdeaClusterer`, and the two hosted services. Remove the `IIdeaClusterer` registration; update hosted-service registration if renamed.
- `tests/PBA.Infrastructure.Tests/Services/Radar/IdeaScoringServiceTests.cs`, `IdeaClustererTests.cs`, `IdeaClusteringServiceTests.cs` — rewrite per the tests below; **delete** `IdeaClustererTests.cs`.

**Current `IdeaScoringService` behavior to replace** (`ScoreBatchAsync`): selects `Ideas.Where(i => i.ScoredAt == null)`, optionally gated by `BackfillEnabled` cutoff, takes `BatchSize` ordered by `DetectedAt desc`, calls the old single-score `AnalyzeAsync`, writes `Score/ScoreReason/Summary/Category/Tags/ScoredAt`, throttles `ThrottleMs`. The new sweep keeps the `BackgroundService` loop shape (delay 1 min, then loop with `IntervalMinutes` delay, scope-per-sweep, `ThrottleMs` between LLM calls) but changes the candidate selection and the per-idea logic entirely.

**Current `IdeaClusteringService` behavior to replace** (`ClusterBatchAsync`): selects candidates `ClusteredAt == null && ScoredAt != null && Score >= MinScore && DuplicateOfId == null && DetectedAt >= now-LookbackHours`, ordered by `Score desc`, takes `MaxItemsPerSweep`, sends titles+summaries to the LLM clusterer, applies returned groups by setting `DuplicateOfId` on non-primary members, stamps `ClusteredAt`. The new sweep groups by **cosine over embeddings**, picks **highest-brandFit as primary**, and is **gated on full embedding coverage**.

## Binding refinements (from claude-plan §13 — these override anything that conflicts)

- **R-H3:** snapshot the `BrandRankingProfile` **once** per sweep. `ScoredProfileVersion` is always set to the **snapshot's** version, never re-read "current". The profile has an EF optimistic-concurrency token (`xmin`) from section-02 — read it via the snapshot; do not re-query mid-sweep.
- **R-L2:** snapshot `IOptionsMonitor<RankingOptions>.CurrentValue` **once** at sweep start; use that captured value for the whole sweep.
- **R-M5:** the sweep **increments `ScoreAttempts`** on each LLM attempt and **skips** any idea whose `ScoreAttempts >= cap` (cap = 3) — a poison item (refusal / unparseable / central-collapse) must not burn an LLM call every sweep forever. The central-collapse guard (all-pillar-identical rejection) lives in the analyzer (section-05); when the analyzer returns `null`, this sweep still increments `ScoreAttempts` so retries are bounded.
- **R-M1:** the derived `Score` badge is fed by the **LLM** brandFit for LLM-scored items; **below-threshold** items use `round(embeddingBrandFit × 10)`. Both are intended; the UI distinguishes `score` vs `rank` sorts.
- **R-H2:** never persist a zero/NaN vector (enforced upstream in section-06). This sweep must treat `Embedding == null` as "not yet embedded" and exclude it from candidates and from the dedup window.
- **R-H1:** the dedup sweep is **gated** — it returns early (does no grouping) if **any** in-window idea still has `Embedding == null`. Use a cheap `AnyAsync(i => i.Embedding == null && i.DetectedAt >= since)` before pulling candidates, so dedup never runs mid-backfill and picks a wrong primary.
- **R-L3-dedup:** dedup cosine is computed **in memory** over the small (~40-item) lookback window via the `CosineSimilarity` helper. **Do not** issue a pgvector `OrderBy(CosineDistance)` query (EF InMemory can't run it, and O(n²) over ~40 items is trivial).

## Configuration changes

`IdeaScoringOptions.cs` — **remove** `BackfillEnabled`. Keep `IntervalMinutes`, `BatchSize`, `ThrottleMs`, `Model`. Recommended: `BatchSize` bounds the **LLM-scored** subset; pull all candidates, partition by threshold, LLM-score up to `BatchSize` of the above-threshold set ordered by descending embedding brandFit, stamp the rest. Document the choice.

`ClusteringOptions.cs` — **remove** `MinScore` and `Model` (no LLM). Keep `IntervalMinutes`, `LookbackHours`, `MaxItemsPerSweep`. `DedupThreshold` comes from `RankingOptions` (section-01), not here. **Prefer keeping the `"Clustering"` section name** to minimize cutover churn on the two deployed hosts (Mac Mini + Furious) unless section-12 coordinates a rename. Flag the choice in the section commit message.

`RankingOptions` already supplies `PreFilterThreshold`, `DedupThreshold`, `ScoringWindowDays = 30`.

Remove the now-dead `BackfillEnabled` / `Clustering:MinScore` keys from `src/PBA.Api/appsettings.json` (and note the two deployed appsettings for section-12's R-L6 grep — do not edit deployed files here).

DI (`DependencyInjection.cs`): remove `services.AddScoped<IIdeaClusterer, ...IdeaClusterer>();` (line 83). Keep both hosted-service registrations (rename the cluster one if you renamed the class). The dedup logic now runs in-memory inside the service, so no separate injected collaborator is needed (it uses `ApplicationDbContext` + `CosineSimilarity` + `RankingOptions`). Also register `IdeaEmbeddingService` if section-06 left it unregistered.

---

## Tests (write FIRST)

Backend: xUnit, Arrange-Act-Assert, `Method_Scenario_ExpectedResult`, `UseInMemoryDatabase(Guid.NewGuid().ToString())` for the DB, `Moq` for `IIdeaAnalyzer`, `Options.Create()` / `IOptionsMonitor` test double for `RankingOptions`, `NullLogger<T>.Instance`. **InMemory cannot execute pgvector SQL** — this is fine because both the pre-filter brand-fit and the dedup grouping run **in memory** over `float[]` vectors already loaded into the entities. Seed `Idea.Embedding` and `BrandPillar.DescriptionEmbedding` as plain `float[]` in the test arrange.

Reuse the existing `IdeaScoringServiceTests` fixture helpers (idea factory) where present; extend rather than duplicate.

### `IdeaScoringServiceTests` (rewrite `tests/PBA.Infrastructure.Tests/Services/Radar/IdeaScoringServiceTests.cs`)

```
# Test: candidate set = Embedding != null AND DetectedAt >= now - ScoringWindowDays
#        AND (ScoredProfileVersion == null OR ScoredProfileVersion < snapshot.Version)
#        — ideas outside the 30-day window, unembedded, or already scored at the current version are excluded
# Test: snapshots BrandRankingProfile ONCE per sweep; ScoredProfileVersion is set to the SNAPSHOT version,
#        not a value re-read after the sweep started (R-H3)
# Test: snapshots IOptionsMonitor<RankingOptions>.CurrentValue once at sweep start; a mid-sweep options
#        change does not affect the in-flight sweep (R-L2)
# Test: item with embedding brandFit >= PreFilterThreshold → IIdeaAnalyzer.AnalyzeAsync IS called,
#        PillarSubScores (keyed by BrandPillarId) + IsAntiTopic + IsAuthorityTopic + ScoredProfileVersion + ScoredAt stored,
#        derived Score = round(brandFit * 10)
# Test: item with embedding brandFit < PreFilterThreshold → AnalyzeAsync NOT called,
#        ScoredProfileVersion stamped (so not reconsidered until profile changes),
#        derived Score = round(embeddingBrandFit * 10) (R-M1)
# Test: ScoreAttempts increments on each LLM attempt; an item already at the cap (3) is skipped — no AnalyzeAsync call (R-M5)
# Test: AnalyzeAsync returning null still increments ScoreAttempts (bounded retry, no infinite re-scoring)
# Test: BatchSize bounds the number of LLM-scored (above-threshold) items per sweep
# Test: ThrottleMs delay honored between LLM calls (assert via a fake delay/clock or call-count timing seam)
# Test: empty candidate set → no LLM call, no SaveChanges side effects, no throw
```

### `IdeaDedupServiceTests` (rewrite `tests/PBA.Infrastructure.Tests/Services/Radar/IdeaClusteringServiceTests.cs` → rename)

```
# Test: groups in-window ideas whose pairwise CosineSimilarity(embedding_a, embedding_b) >= DedupThreshold,
#        computed IN MEMORY (no pgvector query) (R-L3-dedup)
# Test: within a group the highest-brandFit idea is primary; others get DuplicateOfId = primary.Id + ClusteredAt
#        (changed from old highest-Score primary)
# Test: GATED — if ANY in-window idea has Embedding == null, the sweep returns early and sets NO DuplicateOfId (R-H1)
# Test: ideas already deduped (DuplicateOfId != null) or already ClusteredAt are not re-grouped
# Test: window respects LookbackHours and MaxItemsPerSweep
# Test: no ISidecarClient / SendPromptAsync interaction at all (LLM dedup path is gone)
```

### Delete `tests/PBA.Infrastructure.Tests/Services/Radar/IdeaClustererTests.cs`

The unit it tested (`IdeaClusterer`) is deleted. Remove the file. Confirm the test project still compiles (no lingering `IIdeaClusterer`/`ClusterInput` references).

### Build-guard test (supports R-L6, finalized in section-12)

```
# Test: IdeaScoringOptions has no BackfillEnabled member; ClusteringOptions has no MinScore member
#        (reflection assertion in a test, or simply that the build fails if the symbols are referenced —
#         the rewrite removing the members IS the guard)
```

---

## Implementation

### 1. `IdeaScoringService` rewire

Keep the `BackgroundService` skeleton (1-min initial delay, loop with `IntervalMinutes`, per-sweep DI scope, try/catch swallowing non-`OperationCanceledException` with `LogError`). Replace `ScoreBatchAsync`'s body. Inject `IOptionsMonitor<RankingOptions>` (capture `.CurrentValue` once per sweep — **R-L2**) alongside the existing `IOptions<IdeaScoringOptions>`.

Per-sweep algorithm:

1. **Snapshot once (R-H3):** load the active `BrandRankingProfile` and build the `BrandRankingProfileSnapshot` (or resolve a snapshot provider if section-05 introduced one — reuse it; do not re-query per item). Capture `var ranking = _rankingOptions.CurrentValue;` once.
2. **Candidate query (SQL):**
   ```
   var windowStart = now.AddDays(-ranking.ScoringWindowDays);
   db.Ideas.Where(i =>
       i.Embedding != null &&
       i.DetectedAt >= windowStart &&
       (i.ScoredProfileVersion == null || i.ScoredProfileVersion < snapshot.Version) &&
       i.ScoreAttempts < AttemptCap)   // AttemptCap = 3 (R-M5)
   ```
   Pull these candidates (their `Embedding` is loaded — needed for the in-memory pre-filter). This is a bounded set.
3. **Pre-filter (in memory):** for each candidate compute `embeddingBrandFit` via the shared brand-fit helper (section-06) over the snapshot's pillar vectors + weights.
4. **Partition by `ranking.PreFilterThreshold`:**
   - **Above threshold:** take up to `BatchSize`, ordered by descending `embeddingBrandFit`. For each: `idea.ScoreAttempts += 1;` then `await analyzer.AnalyzeAsync(new IdeaAnalysisInput(idea.Title, idea.Description, idea.Url, idea.SourceName), snapshot, ct)`. If non-null: store `idea.PillarSubScores` (already carries `BrandPillarId` per R-C3), `idea.IsAntiTopic`, `idea.IsAuthorityTopic`, `idea.ScoreReason` (from `IdeaAnalysis.Reason`), `idea.ScoredProfileVersion = snapshot.Version`, `idea.ScoredAt = now`. Set `idea.Score = round(Clamp(llmBrandFit, 0, 1) * 10)` clamped to [0,10] (R-M6), where `llmBrandFit` is the renormalized weighted sum over active pillars (the same query-time weighting section-08 uses). If analyzer returns `null`: leave sub-scores empty, do **not** stamp `ScoredProfileVersion` (retries next sweep, bounded by the incremented `ScoreAttempts`). Honor `ThrottleMs` between calls.
   - **Below threshold:** **no LLM call.** Set `idea.ScoredProfileVersion = snapshot.Version` (so not reconsidered until version changes), `idea.ScoredAt = now`, `idea.Score = round(Clamp(embeddingBrandFit, 0, 1) * 10)` (R-M1, R-M6). Leave `PillarSubScores` empty and flags null.
5. `SaveChangesAsync` once at the end if anything changed. Log scored/skipped counts.

Notes:
- `AttemptCap` (3) can be a const in the service. Document the choice.
- The derived `Score` is intentionally lossy/display-only (R-M1). The authoritative ranking lives in `PillarSubScores` + `ComputeRank` (section-08).

### 2. `IdeaClusteringService` → `IdeaDedupService` rewire

Rename the class and file to `IdeaDedupService.cs` (update DI hosted-service registration). Keep the `BackgroundService` skeleton (2-min initial delay, loop with `IntervalMinutes`). Remove the `IIdeaClusterer` dependency. Inject `IOptionsMonitor<RankingOptions>` for `DedupThreshold` (snapshot once per sweep) plus the existing `ClusteringOptions` for `LookbackHours`/`MaxItemsPerSweep`.

Per-sweep algorithm (`DedupBatchAsync`):

1. `var since = now.AddHours(-_clusteringOptions.LookbackHours);`
2. **Gate (R-H1):** `if (await db.Ideas.AnyAsync(i => i.DetectedAt >= since && i.Embedding == null, ct)) { log + return; }` — do not dedup mid-backfill.
3. Pull candidates: `Embedding != null && DetectedAt >= since && DuplicateOfId == null && ClusteredAt == null`, take `MaxItemsPerSweep`. (No `MinScore` gate — removed.) Load their `Embedding` and the brand-fit signal needed to pick a primary (recompute brandFit from `PillarSubScores`, falling back to `Score` for embedding-only items).
4. If `< 2` candidates, return.
5. **In-memory grouping (R-L3-dedup):** union-find / greedy grouping over pairwise `CosineSimilarity(a.Embedding, b.Embedding) >= ranking.DedupThreshold`. O(n²) over ~`MaxItemsPerSweep` (~40) is fine.
6. For each group of `>= 2`: choose the **highest-brandFit** member as primary; set the others' `DuplicateOfId = primary.Id`.
7. Stamp `ClusteredAt = now` on all processed candidates (so they aren't re-grouped). `SaveChangesAsync`. Log counts. No `ISidecarClient` use anywhere.

### 3. Deletions

- Delete `src/PBA.Infrastructure/Services/Radar/IdeaClusterer.cs`.
- Delete `src/PBA.Application/Common/Interfaces/IIdeaClusterer.cs` (and the `ClusterInput` record it defines) — **first** grep the whole solution to confirm no other consumer; if `ClusterInput` is referenced elsewhere, stop and reconcile before deleting.
- Delete `tests/PBA.Infrastructure.Tests/Services/Radar/IdeaClustererTests.cs`.
- Remove the `IIdeaClusterer` DI registration (`DependencyInjection.cs` line 83).
- Remove `BackfillEnabled` from `IdeaScoringOptions.cs`; remove `MinScore` (and unused `Model`) from `ClusteringOptions.cs`.
- Remove the corresponding keys from `src/PBA.Api/appsettings.json`. (Deployed appsettings on Mac Mini + Furious are handled in section-12's R-L6 grep — do not touch them here.)

## Verification

1. `dotnet build` — must fail-fast if any code still references `BackfillEnabled`, `Clustering.MinScore`, `IIdeaClusterer`, `ClusterInput`, or `IdeaClusterer` (this is the cheap R-L6 build guard).
2. `dotnet test` — all rewritten tests green; `IdeaClustererTests` removed; coverage ≥ 80% on the two rewired services.
3. Confirm no `ISidecarClient.SendPromptAsync` call remains in any dedup path (grep `Services/Radar`).
4. Confirm the scoring sweep computes brand-fit through the **single shared helper** (no duplicated weighted-sum loop vs section-06).

## Relevant file paths

- `src/PBA.Infrastructure/Services/Radar/IdeaScoringService.cs` (rewire)
- `src/PBA.Infrastructure/Services/Radar/IdeaClusteringService.cs` (rewire → rename `IdeaDedupService.cs`)
- `src/PBA.Infrastructure/Services/Radar/IdeaClusterer.cs` (delete)
- `src/PBA.Application/Common/Interfaces/IIdeaClusterer.cs` (delete after consumer check)
- `src/PBA.Infrastructure/Configuration/IdeaScoringOptions.cs` (remove `BackfillEnabled`)
- `src/PBA.Infrastructure/Configuration/ClusteringOptions.cs` (remove `MinScore`/`Model`)
- `src/PBA.Infrastructure/DependencyInjection.cs` (lines 78-87: remove `IIdeaClusterer`, update hosted-service name)
- `src/PBA.Api/appsettings.json` (remove dead keys)
- `tests/PBA.Infrastructure.Tests/Services/Radar/IdeaScoringServiceTests.cs` (rewrite)
- `tests/PBA.Infrastructure.Tests/Services/Radar/IdeaClusteringServiceTests.cs` (rewrite → `IdeaDedupServiceTests.cs`)
- `tests/PBA.Infrastructure.Tests/Services/Radar/IdeaClustererTests.cs` (delete)

## As built (2026-06-16)

Final third of the **05+06+07 build-coupled unit**; the whole unit was committed once the build was green.

- **`IdeaScoringService` rewired**: each sweep calls `embedder.EmbedPendingAsync` first (so candidates see
  fresh vectors), snapshots the active `BrandRankingProfile` ONCE (R-H3, stamps `ScoredProfileVersion`
  from the snapshot) and `IOptionsMonitor<RankingOptions>.CurrentValue` ONCE (R-L2). Candidate query:
  `Embedding != null && DetectedAt >= now-ScoringWindowDays && (ScoredProfileVersion == null ||
  < snapshot.Version) && ScoreAttempts < 3`. Pre-filter via the shared helper; above-threshold items are
  LLM-scored up to `BatchSize` ordered by descending fit (`ScoreAttempts++` before each call, analyzer-null
  leaves it unstamped for bounded retry); below-threshold items get the embedding-only badge. Derived
  `Score` is **two intentional sources per R-M1** (renormalized LLM brandFit above, raw embedding brandFit
  below).
- **`IdeaClusteringService` → `IdeaDedupService`** (renamed file + class + hosted-service registration):
  in-memory union-find cosine grouping (R-L3-dedup, no pgvector SQL), gated on full in-window embedding
  coverage (R-H1). Primary = highest `Score`, tie-broken deterministically on oldest `DetectedAt` then `Id`
  (review fix H2 — no dependence on EF load order). No `ISidecarClient` use anywhere.
- **Deletions:** `IdeaClusterer.cs`, `IIdeaClusterer.cs` (+ `ClusterInput`), `IdeaClustererTests.cs`,
  `IdeaClusteringServiceTests.cs`. Removed `IdeaScoringOptions.BackfillEnabled`, `ClusteringOptions.MinScore`
  + `ClusteringOptions.Model`, and the matching keys from `src/PBA.Api/appsettings.json` (deployed
  appsettings left for section-12's R-L6 grep). `"Clustering"` section name kept (minimize cutover churn).
- **Tests:** `IdeaScoringServiceTests.cs` rewritten (9 tests incl. a `CountingMonitor` proving R-L2
  single-read), `IdeaDedupServiceTests.cs` new (5 tests). Build is the R-L6 guard for the removed symbols
  (no explicit reflection test added). Uses `DateTimeOffset.UtcNow` (see review M1).
- **Open follow-up (review L1):** no embed-attempt cap exists, so a permanently-unembeddable in-window
  idea can wedge the dedup gate; needs an `Idea` schema field (section-03) — deferred.
