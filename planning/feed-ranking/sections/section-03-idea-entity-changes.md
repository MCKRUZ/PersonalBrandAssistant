# section-03 — Idea Entity Changes

## Goal

Extend the existing `Idea` aggregate with the columns the new brand-anchored ranking pipeline needs: a 1536-dim embedding vector, per-pillar sub-scores (keyed by `BrandPillarId`), anti/authority flags, the profile version the scores were produced under, and a scoring-attempt counter. Add the matching EF config + migration `AddIdeaEmbeddingAndSubScores`. **The existing `Score` column and its index are retained** — `Score` becomes a derived display value (`round(brandFit*10)`) but its storage and index are unchanged.

This section only adds the schema surface. The services that **populate** these columns (embedding service, analyzer, scoring sweep, ComputeRank) live in later sections and are out of scope here.

## Dependencies

- **section-01-foundation** (required, must land first): registers `Pgvector.EntityFrameworkCore`, calls `modelBuilder.HasPostgresExtension("vector")` in `ApplicationDbContext.OnModelCreating`, enables `o.UseVector()` on the `UseNpgsql(...)` call in `PBA.Infrastructure/DependencyInjection.cs`, and ships the migration that enables the `vector` extension in Postgres. Without section-01, mapping `Embedding` as `vector(1536)` will not compile/migrate. Do **not** re-add the extension registration here — assume it exists.
- **section-02-brand-profile-domain** (reference only): defines `BrandRankingProfile` / `BrandPillar` and the `BrandPillarId` (`Guid`) used as the key inside `PillarSubScore`. This section does **not** require section-02 to be merged — `PillarSubScore` carries a raw `Guid PillarId`, not a navigation/FK to `BrandPillar` (sub-scores are stored as jsonb, so no relational FK is created). The two can develop in parallel (both are in Batch 2).

## Design decisions (binding)

- **jsonb over join table.** `PillarSubScores` is stored as a `jsonb` document column on `Ideas`, **not** as a related `IdeaPillarSubScores` table. Rationale: brandFit is computed in the read handler in memory (see section-08), never aggregated in SQL, so a join buys nothing and costs a table + FK + N-row writes per idea. Document this trade-off in the EF config with a one-line comment. (Revisit only if a future requirement needs to filter/aggregate sub-scores in SQL.)
- **R-C3 — sub-scores keyed by `BrandPillarId`, never by pillar name.** `PillarSubScore.PillarId` is a `Guid` matching `BrandPillar.Id`. Names survive only as a display field (`PillarName`), and identity is the id (survives pillar renames). The analyzer (section-05) is responsible for mapping LLM-returned pillar *names* → ids before persisting; this section just provides the storage shape.
- **R-M5 — `ScoreAttempts`.** Add `ScoreAttempts` so the sweep (section-07) can cap retries (e.g. 3) and stop burning an LLM call every sweep on a poison item. This section only adds the column with default `0`; the cap logic is in section-07.
- **No vector index.** `Embedding` gets the `vector(1536)` column but **no** pgvector index — at ~3,800 rows an exact in-memory cosine scan is fine (dedup and pre-filter are computed in memory in later sections). Do not add an HNSW/IVFFlat index here.
- **Nullable semantics.** `Embedding`, `EmbeddedAt`, `IsAntiTopic`, `IsAuthorityTopic`, and `ScoredProfileVersion` are all **nullable** — they are unset until the embedding/scoring pipeline processes the idea. `ScoreAttempts` is a non-nullable `int` defaulting to `0`. `PillarSubScores` is a non-nullable list initialized to empty.

## Tests FIRST

Create `tests/PBA.Infrastructure.Tests/Data/Configurations/IdeaConfigurationTests.cs` (new file; the directory may need creating). The "vector(1536)" and jsonb round-trip tests require a **real Postgres with the `vector` extension** — use Testcontainers (the codebase already references Testcontainers in the Infrastructure test project; mirror the existing container setup used elsewhere in `tests/PBA.Infrastructure.Tests`). The nullable-persistence test can also run against the Testcontainers Postgres. Do not use the InMemory provider for the vector/jsonb tests — InMemory does not model the `vector` column type or jsonb round-tripping.

Test stubs (write these as failing tests first):

```
# IdeaConfigurationTests (Testcontainers Postgres with vector extension)

# Test: Embedding maps to vector(1536) and is nullable
#   - persist an Idea with Embedding == null → round-trips as null
#   - persist an Idea with a 1536-length float[] → round-trips element-for-element

# Test: PillarSubScores persist as jsonb (chosen over join table per design) and round-trip by BrandPillarId
#   - persist an Idea with two PillarSubScore entries keyed by distinct Guid PillarIds
#   - reload → list preserves PillarId (Guid), Score (double 0..1), PillarName, Reason
#   - confirm NO IdeaPillarSubScores table is created (column is jsonb on Ideas)

# Test: IsAntiTopic / IsAuthorityTopic / ScoredProfileVersion / EmbeddedAt / ScoreAttempts are nullable & persist
#   - persist with all left default/null → IsAntiTopic null, IsAuthorityTopic null,
#     ScoredProfileVersion null, EmbeddedAt null, ScoreAttempts == 0
#   - persist with all set → round-trip exact values

# Test: existing Score (int? 0-10) column unchanged / still indexed
#   - Score column still present, still nullable int
#   - IX_Ideas_Score index still exists after the new migration
```

A `SchemaUpdateTests`-style lightweight assertion (InMemory provider, mirror `tests/PBA.Infrastructure.Tests/Data/SchemaUpdateTests.cs`) is acceptable as an additional cheap guard that the new properties exist on the entity and that `ScoreAttempts` defaults to `0` — but it cannot validate the `vector`/jsonb column types, so it does not replace the Testcontainers tests.

## Implementation

### 1. `PBA.Domain/Entities/Idea.cs`

Add the new members to the existing `Idea` class (do not remove or alter existing members — `Score`, `ScoreReason`, `ScoredAt`, etc. stay):

```csharp
public float[]? Embedding { get; set; }                 // vector(1536), nullable until embedded
public DateTimeOffset? EmbeddedAt { get; set; }
public IList<PillarSubScore> PillarSubScores { get; set; } = []; // raw 0..1 per pillar, keyed by BrandPillarId (R-C3)
public bool? IsAntiTopic { get; set; }
public bool? IsAuthorityTopic { get; set; }
public int? ScoredProfileVersion { get; set; }          // which BrandRankingProfile.Version produced the sub-scores (R-H3)
public int ScoreAttempts { get; set; }                  // sweep caps at 3 to avoid poison-item LLM burn (R-M5)
// Idea.Score (existing int? 0-10) is RETAINED as derived display = round(brandFit*10). Do not remove.
```

`float[]?` is the type `Pgvector.EntityFrameworkCore` maps to a `vector` column when configured via `HasColumnType("vector(1536)")` (the package supports mapping `float[]` directly; if the codebase's pgvector version requires the `Pgvector.Vector` wrapper type instead, use that type for the property — verify against the `Pgvector.EntityFrameworkCore` version pinned in section-01 before committing). State which you used in the implementation.

### 2. New value type — `PBA.Domain/Entities/PillarSubScore.cs`

Small owned/serialized value object stored inside the jsonb column (one class per file per coding-style rules):

```csharp
namespace PBA.Domain.Entities;

/// <summary>One pillar's raw fit score for an idea. Keyed by BrandPillarId (R-C3),
/// PillarName is display-only and may go stale across renames.</summary>
public sealed class PillarSubScore
{
    public Guid PillarId { get; set; }      // == BrandPillar.Id; identity, survives renames
    public string PillarName { get; set; } = string.Empty; // display only
    public double Score { get; set; }       // raw 0..1
    public string? Reason { get; set; }     // one-line LLM rationale, nullable
}
```

Use a class with mutable setters (not a record) only if jsonb serialization in the codebase needs it; a `record` with init setters also serializes fine via `EnableDynamicJson()` (already enabled in `DependencyInjection.cs`). Prefer the simplest shape that round-trips — confirm with the jsonb test.

### 3. EF config — `PBA.Infrastructure/Data/Configurations/IdeaConfiguration.cs`

Extend the existing `Configure(EntityTypeBuilder<Idea>)` (do not rewrite existing mappings). Add:

```csharp
// vector(1536), nullable, exact-scan (no index) — ~3,800 rows, brandFit/dedup computed in memory (section-07/08)
builder.Property(i => i.Embedding).HasColumnType("vector(1536)");

builder.Property(i => i.EmbeddedAt);

// jsonb document, NOT a join table: brandFit is computed in the read handler, never aggregated in SQL,
// so a relational IdeaPillarSubScores table buys nothing. Keyed by BrandPillarId (R-C3).
builder.Property(i => i.PillarSubScores).HasColumnType("jsonb");

builder.Property(i => i.IsAntiTopic);
builder.Property(i => i.IsAuthorityTopic);
builder.Property(i => i.ScoredProfileVersion);
builder.Property(i => i.ScoreAttempts).HasDefaultValue(0);
```

Leave the existing `IX_Ideas_Score` (`builder.HasIndex(i => i.Score)`) in place — do not drop it (R-M2). Do **not** add a vector index.

`EnableDynamicJson()` is already called on the `NpgsqlDataSourceBuilder` in `DependencyInjection.cs`, so the `List<PillarSubScore>` serializes to/from jsonb automatically. If the jsonb round-trip test fails on serialization, fall back to an explicit `HasConversion` using `System.Text.Json` (matching how `Tags`/other `List<string>` jsonb columns behave) — but try the dynamic-json path first since the existing `Tags` jsonb column relies on it.

### 4. Migration — `AddIdeaEmbeddingAndSubScores`

Generate via:

```
dotnet ef migrations add AddIdeaEmbeddingAndSubScores --project src/PBA.Infrastructure --startup-project src/PBA.Api
```

(The design-time factory is `src/PBA.Infrastructure/Data/DesignTimeDbContextFactory.cs`; mirror how prior migrations like `20260605165204_AddIdeaRadarFields` were generated.)

Expected `Up`:
- `AddColumn` `Embedding` type `"vector(1536)"` nullable (the `vector` extension must already be enabled by section-01's migration; ensure this migration is **ordered after** it).
- `AddColumn` `EmbeddedAt` `timestamp with time zone` nullable.
- `AddColumn` `PillarSubScores` type `"jsonb"` (nullable defaulting is fine; the entity initializes to empty list).
- `AddColumn` `IsAntiTopic` / `IsAuthorityTopic` `boolean` nullable.
- `AddColumn` `ScoredProfileVersion` `integer` nullable.
- `AddColumn` `ScoreAttempts` `integer` not null default `0`.
- No new index, no FK (sub-scores are jsonb, not relational).

`Down` drops all six columns. Verify the auto-generated migration matches this shape (EF sometimes emits a default `'{}'` for jsonb or a `Pgvector` column annotation — keep those, just confirm nothing unexpected like a vector index or an `IdeaPillarSubScores` table appears). Inspect the generated `.cs` before committing; pattern-match against `20260605165204_AddIdeaRadarFields.cs` for column/index shape.

## Verify

1. `dotnet build` — entity + config compile (depends on section-01's pgvector package + `UseVector()`).
2. `dotnet test --filter IdeaConfiguration` — all four Testcontainers tests green (vector round-trip, jsonb round-trip by `PillarId`, nullable persistence, retained `Score` index).
3. Inspect the generated migration `.cs` — confirm six columns added, `vector(1536)` + `jsonb` types correct, `ScoreAttempts` default `0`, no vector index, no `IdeaPillarSubScores` table, `IX_Ideas_Score` untouched.
4. Confirm `ApplicationDbContextModelSnapshot.cs` updated by the migration step (it regenerates automatically; do not hand-edit).

## Out of scope (later sections)

- Populating `Embedding`/`EmbeddedAt` (section-06 embedding service).
- Producing `PillarSubScores`/flags via the analyzer + name→id mapping (section-05).
- The scoring sweep that sets `ScoredProfileVersion`, increments `ScoreAttempts`, and enforces the cap (section-07).
- `ComputeRank` consuming sub-scores/flags (section-08).

---

## As-built notes (implemented 2026-06-16)

- **Embedding type:** `float[]?` on `Idea` (Domain pure), mapped to `vector(1536)` in
  `PgVectorModelConfiguration.Apply` (Npgsql-only, gated by `Database.IsNpgsql()` in OnModelCreating) —
  same pattern established in section-02.
- **PillarSubScores (key fix):** an `IList<PillarSubScore>` (collection of a COMPLEX type) is NOT mappable
  by the InMemory provider (unlike `List<string>` primitive collections), and section-08's ListIdeas
  tests need sub-scores under InMemory. So it uses an explicit **System.Text.Json string ValueConverter
  + ValueComparer** (in `IdeaConfiguration`), stored as `jsonb` on Npgsql and a string on InMemory — not
  Npgsql dynamic JSON. The converter is the sole read/write path, so default `JsonSerializerOptions` is
  self-consistent. Keyed by `BrandPillarId` (R-C3).
- **Migration default (review fix):** `PillarSubScores` is `NOT NULL` so the migration adds
  `defaultValueSql: "'[]'::jsonb"` to backfill the existing ~3,800-row `Ideas` table (a NOT NULL column
  with no default would fail on a populated table — invisible to empty-DB tests). `ScoreAttempts` is
  `NOT NULL DEFAULT 0` (same reason).
- **Indexes:** added `IX_Ideas_ScoredProfileVersion`; retained `IX_Ideas_Score` (R-M2); no vector index.
- **Migration:** `20260616151100_AddIdeaEmbeddingAndSubScores` — 6 columns + index; `Down()` drops all.
  Verified build + 356 tests + no model drift. vector(1536) round-trip + literal jsonb type deferred to
  section-12 Testcontainers.
- **Files:** `PillarSubScore.cs` (new), `Idea.cs` (+6 fields), `IdeaConfiguration.cs` (jsonb converter +
  ScoreAttempts default + ScoredProfileVersion index), `PgVectorModelConfiguration.cs` (+ Idea.Embedding),
  migration, `IdeaRankingFieldsConfigurationTests.cs` (InMemory round-trip).
