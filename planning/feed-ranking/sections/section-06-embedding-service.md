# section-06-embedding-service

## What this section delivers

`IdeaEmbeddingService` — the component that turns raw ideas into stored vectors and keeps the active Brand Ranking Profile's pillar vectors fresh. This is the embedding *production* layer of the pipeline. It does **not** do LLM scoring or dedup (those live in section-07); it only ensures every relevant idea and pillar has a valid `vector(1536)` embedding and exposes the brand-fit pre-filter computation that section-07's sweep consumes.

Runtime role in the larger flow:

```
ingest idea ──► [THIS SECTION] embed (OpenRouter) ──► store vector + EmbeddedAt
                                                  └─► [THIS SECTION] brand-fit pre-filter (cosine vs pillar vectors)
                                                        ├─ above threshold ─► (section-07) LLM per-pillar score
                                                        └─ below threshold ─► (section-07) embedding-only brandFit
```

## Dependencies (already built — do not re-implement)

This section assumes the following from prior sections are present. Reference them; do not duplicate.

- **section-01-foundation:**
  - `CosineSimilarity(float[] a, float[] b)` — pure static util in `PBA.Application`. Computes the full `dot/(‖a‖‖b‖)` (does NOT assume unit norm — Matryoshka shrink breaks normalization). Returns `0` for a zero-vector input (never NaN). Throws/guards on length mismatch. **Use this; do not write your own cosine.**
  - `EmbeddingOptions { Model="openai/text-embedding-3-small", Dimensions=1536, BatchSize=128 }` bound from appsettings `"Embedding"`, wired via `IOptionsMonitor`.
  - `RankingOptions { PreFilterThreshold, DedupThreshold, ScoringWindowDays=30 }` bound from appsettings, via `IOptionsMonitor`.
  - pgvector enabled: `HasPostgresExtension("vector")` + `o.UseVector()` on `UseNpgsql`, migration enabling the `vector` extension.
- **section-02-brand-profile-domain:** `BrandRankingProfile` aggregate with `Version`, `IsActive`, `Pillars` (`IList<BrandPillar>`); each `BrandPillar` has `Name`, `Description`, `Weight`, `Order`, and `float[]? DescriptionEmbedding` mapped to `vector(1536)`. Optimistic-concurrency `xmin` token.
- **section-03-idea-entity-changes:** `Idea` now has `float[]? Embedding` (`vector(1536)`, nullable), `DateTimeOffset? EmbeddedAt`, plus `PillarSubScores`/`IsAntiTopic`/`IsAuthorityTopic`/`ScoredProfileVersion`/`ScoreAttempts`. Migration applied. (This section only writes `Embedding` and `EmbeddedAt`.)
- **section-04-embed-async:** `ISidecarClient.EmbedAsync(IReadOnlyList<string> inputs, string? model = null, CancellationToken ct = default)` returning `Task<IReadOnlyList<float[]>>`, one vector per input in input order, internally batched, empty inputs skipped/sanitized, per-batch try/catch so one bad batch doesn't abort. **This section calls `EmbedAsync`; it does not implement it.**

## Background context an implementer needs

- Backend is .NET 10, Clean Architecture (`PBA.Domain` / `PBA.Application` / `PBA.Infrastructure` / `PBA.Api`), EF Core on PostgreSQL (Npgsql), `Result<T>` from `PBA.Domain.Common`. No endpoint auth in v2.
- The existing scoring/clustering services live in `src/PBA.Infrastructure/Services/Radar/` (`IdeaScoringService.cs`, `IdeaAnalyzer.cs`, `IdeaClusterer.cs`, `IdeaClusteringService.cs`, etc.). The new embedding service belongs alongside them.
- The DbContext is the existing PBA EF context (the one `IdeaScoringService` already uses). Embeddings are written via that context.
- Tests: xUnit, Arrange-Act-Assert, `Method_Scenario_ExpectedResult` naming, EF `UseInMemoryDatabase(Guid)` for the data path, `Moq` for `ISidecarClient`, `Options.Create()` / `NullLogger<T>.Instance`. **EF InMemory cannot execute pgvector SQL** — but this service never issues a pgvector `OrderBy(CosineDistance)` query (all cosine math is in-memory via `CosineSimilarity`), so InMemory works for every test here. Look at existing `IdeaScoringServiceTests` for `CreateIdea()` helpers and mock-setup patterns to mirror.

## Files to create / modify

- **Create:** `src/PBA.Infrastructure/Services/Radar/IdeaEmbeddingService.cs`
- **Create (test, FIRST):** `tests/PBA.Infrastructure.Tests/Services/Radar/IdeaEmbeddingServiceTests.cs`
- **Modify:** the Radar/Infrastructure DI registration (`DependencyInjection.cs` / `Add*Dependencies()` in `PBA.Infrastructure`) to register `IdeaEmbeddingService`. Follow the existing pattern used to register `IdeaScoringService`.

## Tests FIRST

Write these before the implementation. They are the binding spec. Use Moq for `ISidecarClient`, InMemory EF for the data path, `Options.Create(new EmbeddingOptions{...})` and `Options.Create(new RankingOptions{...})` (or `IOptionsMonitor` test doubles), `NullLogger<IdeaEmbeddingService>.Instance`.

`IdeaEmbeddingServiceTests`:

```
# Test: selects only ideas with Embedding == null, embeds (title + " " + description), stores the returned vector + sets EmbeddedAt
# Test: ideas that already have a non-null Embedding are NOT re-embedded (not passed to EmbedAsync)
# Test: re-embeds active-profile pillar descriptions when the profile Version changes (pillar DescriptionEmbedding populated)
# Test: a bad batch is caught — failed items remain Embedding == null for retry, the sweep continues (R-H2)
# Test: never persists a zero or NaN vector — a zero/NaN result from EmbedAsync is rejected, Embedding stays null (R-H2)
# Test: EmbedAsync called with model defaulted from EmbeddingOptions (or null → service relies on EmbedAsync default)
```

Brand-fit pre-filter computation (pure-ish; exercise via a small public method on the service or a static helper — table-driven):

```
# Test: brandFit = weighted Σ over pillars of (pillar.Weight × CosineSimilarity(itemVec, pillar.DescriptionEmbedding))
# Test: a pillar whose DescriptionEmbedding is null is skipped (not counted) — does not NaN the sum
# Test: an item with a null Embedding cannot be pre-filtered (caller must guard / method returns 0 or is not called)
```

Notes on test fidelity (per the "stubs only when necessary" rule): only the brand-fit weighting and the zero/NaN-rejection assertions need exact numeric expectations. The selection/EmbeddedAt/skip-existing tests are structural — assert on what was passed to the mocked `EmbedAsync` and on the persisted entity state, not on exact float values.

## Implementation details

`IdeaEmbeddingService` responsibilities (from §8a + §6 + R-H2):

**1. Embed un-embedded ideas.**
- Query the context for ideas where `Embedding == null` (optionally scoped to the lookback/window the caller passes, but the base contract is "all null-embedding ideas").
- Build the input text per idea: `title + " " + description` (use summary once present, but title+description is the current contract). The empty/whitespace sanitization itself is `EmbedAsync`'s job (section-04) — this service just passes inputs in idea order and maps the returned vectors back by index.
- Call `EmbedAsync(inputs, model: options.Model, ct)`. Because `EmbedAsync` batches internally and isolates batch failures, a failed batch yields no vector for those items; those items must remain `Embedding == null` for retry. **Map results back to ideas carefully** — if `EmbedAsync`'s contract guarantees same-length-as-input ordering with failed entries represented, follow that contract; if it returns only successful vectors, you must track which inputs succeeded. **Confirm section-04's exact `EmbedAsync` return contract before wiring the mapping** (this is the one integration seam — verify, don't assume). Per section-04: empties are dropped from the request and a failing batch *throws*, so wrap the call in try/catch per safe chunk if needed so a failure leaves items unembedded rather than aborting the whole pass.
- **Reject zero / NaN vectors before persisting** (R-H2): cosine vs a zero vector is undefined and corrupts dedup. Guard: if a returned vector is all-zero or contains NaN/Infinity, skip it (leave `Embedding == null`). A direct scan for non-finite + non-all-zero is clearest.
- For accepted vectors: set `idea.Embedding = vec` and `idea.EmbeddedAt = now` (inject a `TimeProvider`/clock consistent with the existing services — check how `IdeaScoringService` gets "now").
- `SaveChangesAsync`.

**2. Re-embed active-profile pillar descriptions when `Version` changes.**
- Load the active `BrandRankingProfile` (`IsActive == true`) with its `Pillars`.
- Determine staleness: a pillar's `DescriptionEmbedding` is stale if null, or if the profile's `Version` advanced since the pillars were last embedded. Simplest robust rule given the entity shape: re-embed all active pillars when any pillar `DescriptionEmbedding == null` or on an explicit `Version`-change signal passed by the caller. (If section-02 added a per-pillar embedded-version stamp, key off that.) Embed each pillar's `Description` via `EmbedAsync`, store into `BrandPillar.DescriptionEmbedding`. Same zero/NaN rejection applies.

**3. Brand-fit pre-filter compute.** Expose a method the section-07 sweep calls (e.g. `double ComputeEmbeddingBrandFit(float[] itemVec, IReadOnlyList<BrandPillar> pillars)`):
- `brandFit = Σ over pillars-with-non-null-DescriptionEmbedding of (pillar.Weight × CosineSimilarity(itemVec, pillar.DescriptionEmbedding))`.
- Skip pillars whose `DescriptionEmbedding` is null (do not let them inject NaN/0-weight noise).
- This uses the section-01 `CosineSimilarity` helper. It does **not** issue any pgvector SQL — all in memory.
- **Single shared helper:** section-07's sweep must call this same method (or a shared pure helper), NOT a second copy of the weighted-sum loop. Coordinate so there is exactly one implementation of the brand-fit formula.

Stub signature (intent only; implementer fills the body):

```csharp
public sealed class IdeaEmbeddingService
{
    /// <summary>Embeds all ideas with Embedding == null (title + description) and
    /// re-embeds active-profile pillar descriptions when the profile Version changes.
    /// Never persists a zero or NaN vector (R-H2). Failed batches leave items
    /// Embedding == null for retry.</summary>
    public Task EmbedPendingAsync(CancellationToken ct);

    /// <summary>Weighted Σ over pillars of weight × cosine(itemVec, pillar.DescriptionEmbedding),
    /// skipping pillars with a null embedding. In-memory; uses CosineSimilarity from PBA.Application.</summary>
    public double ComputeEmbeddingBrandFit(float[] itemVec, IReadOnlyList<BrandPillar> pillars);
}
```

Constructor deps follow the existing Radar services: the EF DbContext, `ISidecarClient`, `IOptionsMonitor<EmbeddingOptions>`, a clock/`TimeProvider`, and `ILogger<IdeaEmbeddingService>`. Mirror `IdeaScoringService`'s constructor shape for consistency.

## Constraints / gotchas (binding)

- **R-H2 (never persist zero/NaN vectors):** enforce at the persist boundary, with a test proving a zero/NaN result leaves `Embedding == null`.
- **R-H2 (batch isolation):** one bad batch must not abort the whole embed pass; failed items stay `null` for a later retry. Don't catch-and-swallow the *whole* operation.
- **Idempotency:** re-running `EmbedPendingAsync` must not re-embed already-embedded ideas (selection filter `Embedding == null`), and must not duplicate pillar work when `Version` is unchanged.
- **No pgvector SQL in this service.** All cosine math is in-memory via the section-01 helper. (The only place pgvector SQL would appear is an EF mapping smoke test, which lives in section-02/03, not here.)
- Use the section-01 `CosineSimilarity` — do not hand-roll a normalized/unit-norm shortcut.

## Verify before done

- `dotnet build`
- `dotnet test --filter IdeaEmbeddingServiceTests` (all green)
- 80% coverage on the new code.
- Confirm DI registration resolves the service (it will be constructor-injected by section-07's sweep).
