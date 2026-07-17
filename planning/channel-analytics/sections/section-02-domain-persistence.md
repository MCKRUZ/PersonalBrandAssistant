# section-02-domain-persistence

## Purpose

Add the domain model and persistence layer that the entire Channel Analytics feature is built on: the enums that classify credentials and snapshots, the `ChannelMetricSnapshot` entity that stores daily cumulative metric captures, the `Purpose` discriminator on `PlatformCredential` that lets one Publishing token and one Analytics token coexist per platform, the EF configurations (composite filtered unique index, jsonb metric bag, snapshot uniqueness), the `DbSet` wiring, and the EF migration plus an idempotent SQL script for prod apply.

This section is **backend-only, no external API calls, no LLM cost**. It is parallelizable with section-01 (OAuth refactor) and section-08 (runbook). It **blocks** sections 03, 04, 05, 06 — they consume these types.

## Background context (what already exists)

- **`Platform` enum** — `src/PBA.Domain/Enums/Platform.cs`:
  ```csharp
  namespace PBA.Domain.Enums;

  public enum Platform
  {
      Blog = 0,
      Substack = 1,
      LinkedIn = 2,
      Twitter = 3,
      Reddit = 4,
      YouTube = 5,
      Medium = 6
  }
  ```
  Persisted `PlatformCredential.Platform` values depend on these numbers. **Append only — never renumber.**

- **`PlatformCredential` entity** — `src/PBA.Domain/Entities/PlatformCredential.cs`. POCO with init-only `Id`/`Platform`, mutable token/expiry/scopes/`IsActive` fields. No `Purpose` yet.

- **`PlatformCredentialConfiguration`** — `src/PBA.Infrastructure/Data/Configurations/PlatformCredentialConfiguration.cs` (already exists). Today it declares a **unique filtered index on `Platform` alone** where `IsActive = true`, plus a non-unique `(Platform, IsActive)` index:
  ```csharp
  builder.HasIndex(c => new { c.Platform, c.IsActive });

  builder.HasIndex(c => c.Platform)
      .IsUnique()
      .HasFilter("\"IsActive\" = true");
  ```
  The unique index was created in migration `20260527132050_AddMultiPlatformPublishing` under the name **`IX_PlatformCredentials_Platform`**. That single-column unique index is the thing this section replaces — with it in place, you cannot have an active Publishing credential and an active Analytics credential for the same platform.

- **`ApplicationDbContext`** — `src/PBA.Infrastructure/Data/ApplicationDbContext.cs`. Exposes `DbSet`s via `Set<T>()`, applies configurations with `ApplyConfigurationsFromAssembly`, and applies pgvector mappings only under Npgsql. `IAppDbContext` — `src/PBA.Application/Common/Interfaces/IAppDbContext.cs` — is the application-layer port mirroring the `DbSet`s (used by read handlers so they never depend on Infrastructure).

- **jsonb + value-converter precedent** — `src/PBA.Infrastructure/Data/Configurations/IdeaConfiguration.cs` maps `IList<PillarSubScore>` to a `jsonb` column through an explicit `ValueConverter<T,string>` + `ValueComparer<T>`. This is the pattern to mirror for `Metrics` — a `ValueConverter`/`ValueComparer` is required so the **InMemory test provider** (which can't map a dictionary to jsonb natively) round-trips it as a JSON string while Npgsql stores it as real `jsonb`. Both `HasColumnType("jsonb")` and the converter are set on the property.

- **Migration conventions** — migrations live in `src/PBA.Infrastructure/Data/Migrations`, ProductVersion `10.0.7`, generated with `--project src/PBA.Infrastructure --startup-project src/PBA.Api --output-dir Data/Migrations`. Every migration is paired with an idempotent hand-authored `scripts/migrate-*.sql` for the Mac Mini prod apply. Example idempotent-script shape — `scripts/migrate-add-ismicrosoftsource.sql`:
  ```sql
  START TRANSACTION;

  DO $EF$
  BEGIN
      IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '<id>') THEN
      -- DDL here
      END IF;
  END $EF$;

  DO $EF$
  BEGIN
      IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '<id>') THEN
      INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
      VALUES ('<id>', '10.0.7');
      END IF;
  END $EF$;
  COMMIT;
  ```

## Dependencies

- **None.** This section stands alone. It is consumed by sections 03/04/05/06 but consumes nothing from them.

---

## Tests FIRST (write these before implementing)

Backend xUnit, `Method_Scenario_ExpectedResult` naming. Use the **real DB provider (Npgsql / testcontainer or the repo's existing DB-backed test fixture)** for the two index-enforcement tests — the InMemory provider does **not** enforce unique indexes, so those assertions are meaningless against InMemory. The jsonb round-trip and enum-value tests can run against either provider (the converter makes InMemory work). These are stubs; the implementer writes the assertions.

```
# Platform_Enum_YouTubeInstagramTikTok_HaveStableNumericValues
#   Assert (int)Platform.YouTube == 5, Instagram and TikTok are appended (7, 8),
#   and no pre-existing member (Blog..Medium 0..6) changed value. Guards persisted-value stability.

# ChannelMetricSnapshot_AccountScope_UsesEmptyStringVideoIdSentinel
#   A newly-constructed Account-scope snapshot has VideoId == "" (never null) so the
#   unique index + ON CONFLICT upsert function in PostgreSQL.

# PlatformCredentialConfiguration_AllowsActivePublishingAndAnalyticsForSamePlatform
#   (real DB) Insert two active PlatformCredentials for the same Platform, one Purpose=Publishing
#   one Purpose=Analytics, both IsActive=true -> both persist (no unique violation).

# PlatformCredentialConfiguration_RejectsTwoActiveAnalyticsCredentials_SamePlatform
#   (real DB) Two active credentials, same Platform, both Purpose=Analytics -> second SaveChanges throws
#   (composite filtered unique index (Platform, Purpose) WHERE IsActive violated).

# ChannelMetricSnapshotConfiguration_UniqueIndex_RejectsDuplicateAccountRow_SamePlatformDate
#   (real DB) Two Account-scope snapshots, same (Platform, SnapshotDate, Scope, VideoId="") -> second throws.

# ChannelMetricSnapshotConfiguration_UniqueIndex_RejectsDuplicateVideoRow_SamePlatformDateVideo
#   (real DB) Two Video-scope snapshots, same (Platform, SnapshotDate, Scope, VideoId="abc") -> second throws.

# ChannelMetricSnapshotConfiguration_MetricsBag_RoundTripsThroughJsonb
#   Persist a snapshot with Metrics { "subscribers": 1234, "views": 99 }, reload in a fresh context,
#   assert the dictionary round-trips with exact long values (proves converter + comparer wiring).

# Migration_AddChannelAnalytics_AppliesAndReverts_OnCleanDb
#   Apply the migration to a clean DB, assert ChannelMetricSnapshots table + Purpose column + the reworked
#   PlatformCredentials index exist; revert leaves the pre-migration model. (Or assert the model-snapshot
#   delta if a live-DB apply/revert is impractical in the harness.)
```

---

## Implementation

### 1. `src/PBA.Domain/Enums/Platform.cs` (modify)

Append two members after `Medium = 6`. Do not renumber existing members.

```csharp
    YouTube = 5,
    Medium = 6,
    Instagram = 7,
    TikTok = 8
```

### 2. `src/PBA.Domain/Enums/CredentialPurpose.cs` (new)

```csharp
namespace PBA.Domain.Enums;

public enum CredentialPurpose
{
    Publishing = 0,
    Analytics = 1
}
```

### 3. `src/PBA.Domain/Enums/SnapshotScope.cs` (new)

```csharp
namespace PBA.Domain.Enums;

public enum SnapshotScope
{
    Account = 0,
    Video = 1
}
```

### 4. `src/PBA.Domain/Entities/PlatformCredential.cs` (modify)

Add one init-only property. The default `Publishing` preserves the meaning of every existing row (they are all publishing credentials today).

```csharp
public CredentialPurpose Purpose { get; init; } = CredentialPurpose.Publishing;
```

### 5. `src/PBA.Domain/Entities/ChannelMetricSnapshot.cs` (new)

Init-only POCO. **Stores only cumulative, as-of-capture integer counts** (subscribers/followers, total views/likes, video counts; per-video cumulative views/likes/comments/shares). A cumulative count captured at an instant is always final — no provisional/correct-later state. All trends (growth, subscribersGained/Lost, etc.) are derived at **read time** as deltas between consecutive daily snapshots (that logic lives in section-06, not here).

**Invariant:** every value in `Metrics` is a whole integer count (or whole seconds) — no fractions. Ratios (engagement rate) and fractional analytics are computed at read time or fetched live; they are never stored here.

`VideoId` is **non-nullable**; use sentinel `""` for Account scope. This is load-bearing: PostgreSQL treats NULLs as distinct, so a nullable `VideoId` would let duplicate Account rows slip past the unique index and would degrade `ON CONFLICT` to a plain INSERT. The empty-string sentinel makes the unique index and the poller's upsert (section-05) genuinely enforce one Account row per platform-day.

```csharp
namespace PBA.Domain.Entities;

using PBA.Domain.Enums;

public class ChannelMetricSnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Platform Platform { get; init; }

    // Host-LOCAL capture date (same clock as ChannelAnalytics:RunAtLocalTime in the poller).
    public DateOnly SnapshotDate { get; init; }

    public SnapshotScope Scope { get; init; }

    // NON-NULLABLE. Sentinel "" for Account scope so the unique index + ON CONFLICT upsert work.
    public string VideoId { get; init; } = string.Empty;

    // Denormalized for display (Video scope only).
    public string? VideoTitle { get; init; }

    // metric name -> cumulative integer value; stored as jsonb.
    public IReadOnlyDictionary<string, long> Metrics { get; init; }
        = new Dictionary<string, long>();

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
}
```

**Rationale for a jsonb metric bag rather than fixed columns:** the three platforms expose different, evolving metric sets (Instagram deprecates/renames metrics; TikTok is a small fixed set; YouTube is large). Column-per-metric would force a migration on every platform change. The bag keeps the schema stable; the service layer (section-04) picks the known keys per platform. Trends are a range-fetch of ~90 rows + in-memory delta extraction, so the `(Platform, SnapshotDate)` btree index is sufficient — **no GIN index on the jsonb is needed.**

### 6. `src/PBA.Infrastructure/Data/Configurations/PlatformCredentialConfiguration.cs` (modify)

Replace the **single-column unique index** with a **composite filtered unique index `(Platform, Purpose)` where `IsActive = true`**, and add the `Purpose` enum->int conversion. Keep the existing non-unique `(Platform, IsActive)` index and all existing property mappings unchanged.

The `.HasFilter("\"IsActive\" = true")` string is PostgreSQL-specific (quoted identifier) — keep it byte-identical to the existing filter. Result: one active Publishing **and** one active Analytics credential can coexist per platform, but never two active credentials of the same purpose.

Add:
```csharp
builder.Property(c => c.Purpose)
    .HasConversion<int>();

builder.HasIndex(c => new { c.Platform, c.Purpose })
    .IsUnique()
    .HasFilter("\"IsActive\" = true");
```
and remove the old `builder.HasIndex(c => c.Platform).IsUnique().HasFilter(...)`.

### 7. `src/PBA.Infrastructure/Data/Configurations/ChannelMetricSnapshotConfiguration.cs` (new)

Mirror the `IdeaConfiguration` jsonb pattern for the `Metrics` dictionary. Key points:

- `HasKey(s => s.Id)`.
- `Platform`, `Scope` -> `HasConversion<int>()`.
- `SnapshotDate` maps to `DateOnly` (Npgsql maps `DateOnly` to `date` natively).
- `VideoId` -> `IsRequired()`, a sensible `HasMaxLength` (e.g. 128). `VideoTitle` -> `HasMaxLength` (e.g. 500).
- **`Metrics`** -> `HasColumnType("jsonb")` **plus** a `ValueConverter<IReadOnlyDictionary<string,long>, string>` (serialize/deserialize with default `JsonSerializerOptions`) **and** a `ValueComparer<IReadOnlyDictionary<string,long>>` (compare + hash + snapshot via serialize, exactly like the `subScore` comparer in `IdeaConfiguration`). Deserialize fallback to an empty dictionary.
- **Unique index** `(Platform, SnapshotDate, Scope, VideoId)` — `IsUnique()`. Because `VideoId` is non-nullable, this enforces one Account row per platform-day and one row per video-day, and enables a real `ON CONFLICT` upsert in section-05.
- **Range index** `(Platform, SnapshotDate)` — non-unique, for trend/range queries in section-06.

Stub:
```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PBA.Domain.Entities;

namespace PBA.Infrastructure.Data.Configurations;

public class ChannelMetricSnapshotConfiguration : IEntityTypeConfiguration<ChannelMetricSnapshot>
{
    public void Configure(EntityTypeBuilder<ChannelMetricSnapshot> builder)
    {
        // HasKey(Id); Platform/Scope HasConversion<int>();
        // VideoId IsRequired + max length; VideoTitle max length.
        // Metrics: HasColumnType("jsonb") + ValueConverter<..,string> + ValueComparer (mirror IdeaConfiguration).
        // Unique index (Platform, SnapshotDate, Scope, VideoId).
        // Index (Platform, SnapshotDate).
    }
}
```

### 8. `src/PBA.Infrastructure/Data/ApplicationDbContext.cs` (modify)

Add the DbSet:
```csharp
public DbSet<ChannelMetricSnapshot> ChannelMetricSnapshots => Set<ChannelMetricSnapshot>();
```

### 9. `src/PBA.Application/Common/Interfaces/IAppDbContext.cs` (modify)

Add the matching port so read handlers (section-06) can query snapshots without depending on Infrastructure:
```csharp
DbSet<ChannelMetricSnapshot> ChannelMetricSnapshots { get; }
```

### 10. Migration `AddChannelAnalytics`

Generate with:
```
dotnet ef migrations add AddChannelAnalytics --project src/PBA.Infrastructure --startup-project src/PBA.Api --output-dir Data/Migrations
```
It should produce: the `Purpose` column on `PlatformCredentials`, the **drop of the old `IX_PlatformCredentials_Platform` unique index** and creation of the new composite `(Platform, Purpose)` filtered unique index, and the new `ChannelMetricSnapshots` table with its two indexes. Inspect the generated `Up`/`Down` to confirm the old index is dropped (verify by name).

### 11. `scripts/migrate-add-channel-analytics.sql` (new, idempotent)

Hand-author to match the existing `scripts/migrate-*.sql` style (single `START TRANSACTION` … `COMMIT`, guarded by `__EFMigrationsHistory` checks keyed on the generated `<timestamp>_AddChannelAnalytics` MigrationId, ProductVersion `10.0.7`). The script must, in order:

1. **Drop the old unique index by name** — `DROP INDEX IF EXISTS "IX_PlatformCredentials_Platform";` — so a re-apply never leaves both the old and new indexes side by side.
2. `ALTER TABLE "PlatformCredentials" ADD "Purpose" integer NOT NULL DEFAULT 0;` (0 = Publishing, back-compat).
3. Create the new composite filtered unique index `(Platform, Purpose)` where `IsActive = true` (use `CREATE UNIQUE INDEX IF NOT EXISTS`).
4. `CREATE TABLE IF NOT EXISTS "ChannelMetricSnapshots"` with columns `Id uuid PK`, `Platform integer`, `SnapshotDate date`, `Scope integer`, `VideoId text NOT NULL`, `VideoTitle text NULL`, `Metrics jsonb NOT NULL`, `CapturedAt timestamptz NOT NULL`.
5. Create the unique index `(Platform, SnapshotDate, Scope, VideoId)` and the range index `(Platform, SnapshotDate)` on `ChannelMetricSnapshots` (`IF NOT EXISTS`).
6. Insert the `__EFMigrationsHistory` row (guarded), matching the two-`DO $EF$`-block pattern in the reference script.

Use the exact quoted-identifier / DDL EF emits in the migration's `Up` so the script and the migration converge on the same schema.

## Verification

- `dotnet build` clean.
- `dotnet test` green, including the two real-DB index-enforcement tests and the jsonb round-trip.
- Confirm the generated migration drops `IX_PlatformCredentials_Platform` and creates the composite index; confirm `Down` reverts cleanly.
- Confirm the enum-stability test passes (existing `Platform` values 0–6 unchanged, Instagram/TikTok appended).

## Out of scope (belongs to other sections)

- OAuth providers / `?purpose=analytics` threading -> section-03.
- The `IChannelAnalyticsService` implementations that produce metric bags -> section-04.
- The poller that writes snapshots -> section-05.
- Read queries that compute deltas/KPIs/trends -> section-06.

Do not build any of these here — this section only lands the types, config, DbSets, migration, and SQL script.

---

## Implementation Outcome (as built)

Implemented as planned. Build clean; all invariants land and converge across entity / EF config / migration / idempotent script (reviewer-verified).

### Files created
- `src/PBA.Domain/Enums/CredentialPurpose.cs`, `src/PBA.Domain/Enums/SnapshotScope.cs`
- `src/PBA.Domain/Entities/ChannelMetricSnapshot.cs`
- `src/PBA.Infrastructure/Data/Configurations/ChannelMetricSnapshotConfiguration.cs`
- `src/PBA.Infrastructure/Data/Migrations/20260717191429_AddChannelAnalytics.cs` (+ `.Designer.cs`) and updated `ApplicationDbContextModelSnapshot.cs`
- `scripts/migrate-add-channel-analytics.sql` (idempotent, MigrationId `20260717191429_AddChannelAnalytics`, ProductVersion `10.0.7`)
- `tests/PBA.Infrastructure.Tests/Data/ChannelAnalyticsModelTests.cs` (InMemory-runnable)
- `tests/PBA.Infrastructure.Tests/Data/ChannelAnalyticsPersistenceTests.cs` (real-DB, Testcontainers)

### Files modified
- `src/PBA.Domain/Enums/Platform.cs` — appended `Instagram = 7`, `TikTok = 8`.
- `src/PBA.Domain/Entities/PlatformCredential.cs` — added `Purpose` init-only (default Publishing).
- `src/PBA.Infrastructure/Data/Configurations/PlatformCredentialConfiguration.cs` — replaced single-column unique index with composite `(Platform, Purpose)` filtered unique index; added `Purpose` int conversion.
- `src/PBA.Infrastructure/Data/ApplicationDbContext.cs` + `src/PBA.Application/Common/Interfaces/IAppDbContext.cs` — added `ChannelMetricSnapshots` DbSet. (`ApplicationDbContext` is the only `IAppDbContext` implementer — no test doubles to update.)

### Deviations / notes
- **SQL script uses `character varying(128)`/`(500)` not `text`** — the plan's step-11 prose said `text NOT NULL`, but the plan also instructs "use the exact DDL EF emits." EF emitted `character varying(128)` (from `HasMaxLength`), so the script matches the migration. Right call, not a divergence.
- **`Purpose` has a persistent DB `DEFAULT 0`** (model declares no default) — required to add a NOT NULL column to existing rows; matches the `AddIsMicrosoftSource` precedent.
- **Real-DB test isolation** relies on disjoint Platform/date keys (no per-test cleanup) — intentional to avoid tripling container cost. **Convention for future real-DB tests: pick fresh Platform/date keys** so they don't collide with these.

### Review fixes applied (see `implementation/code_review/section-02-interview.md`)
- Added stability-guard tests for `CredentialPurpose` and `SnapshotScope` (persisted + index-critical, same risk class as `Platform`).
- Added a comment on `metricsComparer` documenting its key-order sensitivity and why it's acceptable.
- Added `ChannelMetricSnapshotConfiguration_UniqueIndex_AllowsAccountAndVideoRows_SamePlatformDate` (positive coexistence — proves `Scope` discriminates).

### Tests
- InMemory-runnable: **6 passing** in `ChannelAnalyticsModelTests` (enum stability x3, sentinel, Purpose default, jsonb round-trip). Full Infrastructure suite minus Docker tests: **386 passing**, no regressions from the enum/Purpose additions. Full solution builds clean.
- **Docker-gated (written, unverified this session — no Docker):** 5 real-DB tests in `ChannelAnalyticsPersistenceTests` (2 credential-index + 2 snapshot-index-reject + 1 coexistence + jsonb) and `ChannelAnalyticsMigrationTests.Migration_AddChannelAnalytics_AppliesAndReverts_OnCleanDb`. Reviewer confirmed they are written correctly (mirror the existing `CutoverTests` Testcontainers pattern) and would pass on real Postgres. **Run before prod apply.** Same environmental status as the pre-existing `CutoverTests`.
