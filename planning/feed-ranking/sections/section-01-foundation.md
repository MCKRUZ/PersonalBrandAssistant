# section-01-foundation — Feed Ranking Redesign

## What this section delivers

This is the **root section** of the Feed Ranking Redesign. Everything else depends on it (it blocks sections 02, 03, 04, 06, 08). It establishes the four foundational pieces that the rest of the work assumes already exist:

1. **R-M4 pre-implementation embedding gate** — confirm OpenRouter actually proxies `openai/text-embedding-3-small` at native dim 1536 *before* committing to a `vector(1536)` column. This is a verification step that gates the whole effort.
2. **pgvector enablement** — add the `Pgvector.EntityFrameworkCore` package, register the Postgres `vector` extension on the EF model, enable `o.UseVector()` on the Npgsql data source, and add a migration that enables the `vector` extension in the database.
3. **`CosineSimilarity(float[], float[])` pure static utility** in `PBA.Application` — full `dot/(‖a‖‖b‖)` (R-M6), unit-tested without a DB. Used by the pre-filter, dedup, and brand-fit math in later sections.
4. **Two config classes + appsettings sections + DI wiring** — `EmbeddingOptions` (model, dim 1536, batch 128) and `RankingOptions` (`PreFilterThreshold`, `DedupThreshold`, `ScoringWindowDays=30`), the latter consumed via `IOptionsMonitor`.

### Background you need (do not assume prior reading)

- **Stack:** .NET 10, Clean Architecture: `PBA.Domain` / `PBA.Application` / `PBA.Infrastructure` / `PBA.Api`. EF Core on **PostgreSQL (Npgsql)**. MediatR + `Result<T>` (`PBA.Domain.Common`). Options bound from `appsettings.json`. Tests: xUnit + EF InMemory + Moq.
- **Why vectors:** The redesign embeds each "idea" (RSS/HN/GitHub item) into a `vector(1536)` and computes brand-fit by cosine similarity against per-pillar brand vectors. Storage is **pgvector**, `vector(1536)` column, **exact cosine, no ANN index** (premature at ~3,800 rows).
- **Why a pure cosine helper:** Brand-fit pre-filtering, in-memory dedup, and read-time ranking all need cosine similarity over `float[]` arrays held in memory (not via SQL). EF InMemory **cannot execute pgvector SQL**, so all vector *logic* is tested through this pure helper. The pgvector column mapping itself is later covered by an optional Testcontainers smoke test (not in this section).
- **Matryoshka caveat (R-M6):** `text-embedding-3-small` truncated/shrunk to 1536 dims is **not guaranteed unit-norm**. The helper must compute the full `dot/(‖a‖‖b‖)` — do **not** shortcut to a bare dot product assuming normalized inputs.

### Existing code to follow as patterns

- DI registration entry point: `src/PBA.Infrastructure/DependencyInjection.cs`. The `AddDbContext<ApplicationDbContext>` block builds an `NpgsqlDataSourceBuilder`, calls `.EnableDynamicJson()`, then `options.UseNpgsql(dataSource, npgsql => npgsql.MigrationsAssembly(...))`. **This is exactly where `o.UseVector()` must be added.**
- Options-class convention: `src/PBA.Infrastructure/Configuration/*Options.cs`. Each is a `sealed class` with a `public const string SectionName`, `init`-only properties with defaults. Example — `ClusteringOptions.cs`:
  ```csharp
  public sealed class ClusteringOptions
  {
      public const string SectionName = "Clustering";
      public int IntervalMinutes { get; init; } = 30;
      // ...
  }
  ```
  Registered with `services.Configure<ClusteringOptions>(configuration.GetSection(ClusteringOptions.SectionName));`.
- appsettings file: `src/PBA.Api/appsettings.json` (sibling sections like `"Clustering"`, `"BlogConnector"` already present).
- `PBA.Application` has a `Common/` folder (`PBA.Application/Common/...`) — put the cosine helper under `PBA.Application/Common/`.

---

## Tests first (TDD)

Write these **before** implementing. Backend = xUnit, Arrange-Act-Assert, naming `Method_Scenario_ExpectedResult`. Put the test class in the existing application test project (the one that already tests pure `PBA.Application` code — search for an existing `*Tests` class under `tests/` that targets `PBA.Application`; create the file alongside it).

### `CosineSimilarityTests` (pure static util, table-driven — WRITE FIRST)

This is the **highest-leverage test in the whole plan** (run order #1). Table-driven where possible.

```
# Test: identical vectors → 1.0
# Test: orthogonal vectors → 0.0
# Test: opposite vectors → -1.0
# Test: computes full dot/(‖a‖‖b‖), does NOT assume unit norm (R-M6) — non-normalized inputs scored correctly
#        e.g. a = [3,0,0], b = [6,0,0] → 1.0 (scale-invariant)
# Test: zero vector input → returns 0 (or guarded), never NaN (R-H2 corollary)
# Test: length mismatch → throws/guarded (ArgumentException)
```

Assertions should use a tolerance (e.g. `Assert.Equal(expected, actual, precision: 6)` or an epsilon). The "does NOT assume unit norm" test is load-bearing: pick at least one pair of clearly non-unit-length vectors whose true cosine is a known non-trivial value (e.g. `a=[1,2,3]`, `b=[2,4,6]` → 1.0; `a=[1,0]`, `b=[1,1]` → `1/√2 ≈ 0.7071`) so a naive dot-product-only implementation would fail.

The zero-vector case encodes R-H2: cosine against a zero vector is mathematically undefined (`0/0 = NaN`) and a NaN would silently corrupt dedup/pre-filter scoring downstream. The contract is "return 0, never NaN."

### Config wiring — no dedicated test class required

The `EmbeddingOptions` / `RankingOptions` classes are plain POCOs with defaults; their binding is exercised implicitly by later sections (the embedding service, scoring sweep) and by app startup. Do **not** write a contrived options-binding unit test unless you find an existing pattern for it in the suite. The defaults themselves are the contract; assert them only if a later section's test needs them pinned.

### pgvector mapping / migration — out of scope for this section's tests

EF InMemory cannot execute pgvector SQL, and there is no `vector` column on any entity *yet* (that arrives in sections 02/03). This section only enables the extension and the EF plumbing. The Testcontainers smoke test for the actual `vector(1536)` column mapping belongs to sections 02/03/12, not here. Verify this section's migration by running `dotnet ef database update` against a real Postgres (or letting the build/migration apply) — see "Verification" below.

---

## Implementation

### 1. R-M4 — Pre-implementation embedding gate (DO THIS FIRST, before any code)

Before writing the migration or `vector(1536)` column anywhere, **confirm the embedding model is actually available at the expected dimension.** This is a verification gate, not code.

- Call `GET /api/v1/embeddings/models` on the OpenRouter endpoint the app uses (the `OpenRouterOptions` base URL — see `src/PBA.Infrastructure/Configuration/OpenRouterOptions.cs`). Confirm `openai/text-embedding-3-small` is listed and proxied with native dim **1536**.
- If the model is **not** present at 1536, STOP and surface this to the user — the entire plan's `vector(1536)` choice depends on it. Do not silently pick a different model or dimension.
- Record the confirmation (model id + dimension) in this section's commit message so the coupling between model and column is auditable. R-M3 (passing `dimensions: 1536` in the embed request body) is implemented in section-04, but the *coupling decision* is locked here.

You can do this gate via a quick authenticated `curl`/HTTP call against the configured OpenRouter endpoint. Treat a non-1536 result as a hard blocker.

### 2. pgvector enablement

**Add the package** to `PBA.Infrastructure`:
```
dotnet add src/PBA.Infrastructure package Pgvector.EntityFrameworkCore
```
(Research note from the plan: `Pgvector.EntityFrameworkCore` 0.3.0, EF Core 9/10 compatible.)

**Register the extension on the EF model.** In `ApplicationDbContext` (`src/PBA.Infrastructure/Data/ApplicationDbContext.cs`), inside `OnModelCreating`, add:
```csharp
modelBuilder.HasPostgresExtension("vector");
```
Add this near the top of `OnModelCreating` (before/independent of entity configs). No entity uses a vector column yet — that's sections 02/03 — but enabling the extension now means their migrations don't have to.

**Enable `UseVector()` on the data source.** In `src/PBA.Infrastructure/DependencyInjection.cs`, the existing `AddDbContext` block is:
```csharp
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        dataSource,
        npgsql => npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));
```
Add `.UseVector()` to the Npgsql builder:
```csharp
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        dataSource,
        npgsql => npgsql
            .UseVector()
            .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));
```
Pgvector also needs the data source to know about the `vector` type. The existing `NpgsqlDataSourceBuilder` (lines 26-28) calls `.EnableDynamicJson()`. Add the pgvector registration on that builder per the `Pgvector.EntityFrameworkCore` docs — verify the exact call name against the installed package version (it is typically `dataSourceBuilder.UseVector();` on `NpgsqlDataSourceBuilder`). **Confirm the method name from the package** rather than guessing; the data-source-level registration is what lets Npgsql map the `vector` type at runtime.

Check the **design-time** factory too: `src/PBA.Infrastructure/Data/DesignTimeDbContextFactory.cs` builds its own `UseNpgsql("Host=localhost;Database=pba_design_time")`. Add `.UseVector()` there as well so `dotnet ef migrations` works.

**Add the migration** that enables the extension in the database:
```
dotnet ef migrations add EnablePgVectorExtension --project src/PBA.Infrastructure --startup-project src/PBA.Api
```
The generated migration should contain `migrationBuilder.AlterDatabase().Annotation("Npgsql:PostgresExtension:vector", ...)` (produced automatically by `HasPostgresExtension("vector")`). If EF does not emit the extension annotation, fall back to a raw `migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");` in `Up` and `DROP EXTENSION IF EXISTS vector;` in `Down`. Verify the generated migration actually creates the extension before considering this done.

### 3. `CosineSimilarity` pure static utility

Create `src/PBA.Application/Common/VectorMath.cs` (or `CosineSimilarity.cs` — match whichever naming the existing `Common/` folder favors; a single static class is fine). Signature:

```csharp
namespace PBA.Application.Common;

public static class VectorMath
{
    /// <summary>
    /// Cosine similarity = dot(a,b) / (||a|| * ||b||). Computes the FULL norm — does NOT
    /// assume unit-normalized inputs (Matryoshka-shrunk embeddings are not unit-norm, R-M6).
    /// Returns 0 when either vector is the zero vector (cosine undefined; never NaN, R-H2).
    /// Throws ArgumentException on length mismatch.
    /// </summary>
    public static double CosineSimilarity(float[] a, float[] b);
}
```

Implementation notes (keep it simple, no SIMD micro-optimization — YAGNI at this scale):
- Guard: `a.Length != b.Length` → throw `ArgumentException`.
- Accumulate `dot`, `normA`, `normB` in a single loop (use `double` accumulators to avoid float drift).
- If `normA == 0` or `normB == 0` → return `0.0` (the zero-vector guard; never divide by zero).
- Return `dot / (Math.Sqrt(normA) * Math.Sqrt(normB))`.

### 4. `EmbeddingOptions`

Create `src/PBA.Infrastructure/Configuration/EmbeddingOptions.cs`, following the `ClusteringOptions` pattern:

```csharp
namespace PBA.Infrastructure.Configuration;

public sealed class EmbeddingOptions
{
    public const string SectionName = "Embedding";

    public string Model { get; init; } = "openai/text-embedding-3-small";
    public int Dimensions { get; init; } = 1536;
    public int BatchSize { get; init; } = 128;
}
```

### 5. `RankingOptions`

Create `src/PBA.Infrastructure/Configuration/RankingOptions.cs`. This one is consumed via `IOptionsMonitor` at runtime (the scoring sweep snapshots `CurrentValue` once per sweep — R-L2 — in section-07), so the values are deliberately runtime-tunable. **Half-life / floor / multipliers do NOT live here** — those live on the `BrandProfile` (DB), per the plan. This class is only the embedding-pipeline thresholds:

```csharp
namespace PBA.Infrastructure.Configuration;

public sealed class RankingOptions
{
    public const string SectionName = "Ranking";

    public double PreFilterThreshold { get; init; } = 0.30; // brand-fit cutoff for earning an LLM call
    public double DedupThreshold { get; init; } = 0.85;     // cosine >= this => duplicate
    public int ScoringWindowDays { get; init; } = 30;       // rolling window for LLM scoring
}
```

(Pick sensible defaults; the `PreFilterThreshold` / `DedupThreshold` values above are reasonable starting points — `DedupThreshold ~0.85` is specified in the plan; tune later. Document that they're runtime-tunable.)

### 6. DI registration

In `src/PBA.Infrastructure/DependencyInjection.cs`, alongside the other `services.Configure<...>` calls (near the Radar block around lines 78-80), add:
```csharp
services.Configure<EmbeddingOptions>(configuration.GetSection(EmbeddingOptions.SectionName));
services.Configure<RankingOptions>(configuration.GetSection(RankingOptions.SectionName));
```
`services.Configure<T>` already makes both `IOptions<T>`, `IOptionsSnapshot<T>`, and `IOptionsMonitor<T>` resolvable — no extra registration needed for the `IOptionsMonitor<RankingOptions>` that section-07 consumes.

### 7. appsettings sections

In `src/PBA.Api/appsettings.json`, add two top-level sections (mirroring the existing `"Clustering"` / `"BlogConnector"` style):
```json
"Embedding": {
  "Model": "openai/text-embedding-3-small",
  "Dimensions": 1536,
  "BatchSize": 128
},
"Ranking": {
  "PreFilterThreshold": 0.30,
  "DedupThreshold": 0.85,
  "ScoringWindowDays": 30
}
```
Note the two deployed hosts (Mac Mini + Furious) each have their own `appsettings` — those are updated at cutover (section-12), **not here**. This section only touches the in-repo `src/PBA.Api/appsettings.json`. If an `appsettings.Development.json` exists, no override needed unless a local value must differ.

---

## File checklist

| Action | Path |
|--------|------|
| Verify (no code) | OpenRouter `GET /api/v1/embeddings/models` confirms `openai/text-embedding-3-small` @ 1536 |
| Add package | `Pgvector.EntityFrameworkCore` → `src/PBA.Infrastructure/PBA.Infrastructure.csproj` |
| Edit | `src/PBA.Infrastructure/Data/ApplicationDbContext.cs` — add `modelBuilder.HasPostgresExtension("vector")` |
| Edit | `src/PBA.Infrastructure/DependencyInjection.cs` — `.UseVector()` on Npgsql + data-source builder; `Configure<EmbeddingOptions>` / `Configure<RankingOptions>` |
| Edit | `src/PBA.Infrastructure/Data/DesignTimeDbContextFactory.cs` — `.UseVector()` |
| Create | `src/PBA.Application/Common/VectorMath.cs` — `CosineSimilarity` |
| Create | `src/PBA.Infrastructure/Configuration/EmbeddingOptions.cs` |
| Create | `src/PBA.Infrastructure/Configuration/RankingOptions.cs` |
| Edit | `src/PBA.Api/appsettings.json` — `"Embedding"` + `"Ranking"` sections |
| Create migration | `EnablePgVectorExtension` (extension-enable only) |
| Create test | `CosineSimilarityTests` in the `PBA.Application` test project |

## Dependencies

- **Depends on:** nothing (root section, Batch 1).
- **Blocks:** section-02 (brand-profile domain — needs the `vector` extension + `UseVector()` for `BrandPillar.DescriptionEmbedding`), section-03 (Idea `Embedding` column), section-04 (embeddings client — uses `EmbeddingOptions`), section-06 (embedding service — uses `CosineSimilarity` + `EmbeddingOptions`), section-08 (composite rank — uses `CosineSimilarity`). Section-07 consumes `RankingOptions` via `IOptionsMonitor`.

## Verification (before declaring done)

1. `dotnet build` succeeds (package added, `UseVector()` resolves).
2. `dotnet test` — `CosineSimilarityTests` green (all 6 cases), no regressions.
3. The R-M4 gate result is recorded (model confirmed at 1536) — this is a hard prerequisite; if it failed, the section is blocked, not done.
4. The `EnablePgVectorExtension` migration applies cleanly against a real Postgres (`dotnet ef database update` against a local pgvector-capable Postgres, or confirm the generated migration creates the extension). Do **not** rely on EF InMemory here — it can't run the extension SQL.
5. `dotnet ef migrations` runs without error (design-time factory has `.UseVector()`).
