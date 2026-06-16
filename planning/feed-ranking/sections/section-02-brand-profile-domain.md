# section-02-brand-profile-domain

## Goal

Create the **Brand Ranking Profile** aggregate — the editable source of truth the feed ranker scores every idea against. One active profile at a time. Pillars carry per-pillar `vector(1536)` embeddings (for the brand-fit pre-filter); topic/marker lists are `jsonb`. Versioning distinguishes query-time-only edits (weights, decay, multipliers → no `Version` bump) from re-score-triggering edits (pillar definitions, topics → bump `Version`). Optimistic concurrency via Npgsql `xmin`. Single-active enforced by a partial unique index. An idempotent startup seeder inserts the v1 profile from `brand-profile-v0.md`.

This section produces: the domain entities, EF configurations, the seeder, the migration, and DbContext/IAppDbContext wiring. It does **not** touch `Idea` (section-03), the analyzer (section-05), the API (section-09), or ranking math (section-08).

## CRITICAL — naming collision (read first)

A `BrandProfile` entity **already exists** and is unrelated to ranking:

- `src/PBA.Domain/Entities/BrandProfile.cs` — fields `Personality`, `Tone`, `Topics`, `Vocabulary`, `AvoidWords`, `ExamplePosts`, `LearningLog`. This is the **content-drafting voice profile**.
- Consumed by `DraftContent.cs`, `CheckVoice.cs`, `GenerateCrossPost.cs`, `ContentHub.cs` via `db.BrandProfiles.FirstOrDefaultAsync(...)`.
- Mapped by `BrandProfileConfiguration.cs` (table `BrandProfiles`, seeded row at GUID `00000000-0000-0000-0000-000000000001` via `HasData`).
- Exposed on `IAppDbContext.BrandProfiles` and `ApplicationDbContext.BrandProfiles`.

The plan (`claude-plan.md` §4) calls the ranking aggregate `BrandProfile`, but that name is **taken by a live, consumed entity**. Do **not** rename or repurpose the existing voice profile — four content features depend on it and they are out of scope for this redesign.

**Decision: name the ranking aggregate `BrandRankingProfile` (table `BrandRankingProfiles`) and `BrandPillar` (table `BrandPillars`).** Everywhere the plan/TDD says `BrandProfile`/`BrandProfileSnapshot`/`BrandProfileConfiguration` for the *ranking* feature, read it as `BrandRankingProfile`/`BrandRankingProfileSnapshot`/`BrandRankingProfileConfiguration`. The existing voice `BrandProfile` is left fully intact. Downstream ranking sections (05, 06, 08, 09) must use these new names — this is a binding decision for the feature.

(If you genuinely prefer to keep the plan's literal `BrandProfile` name for ranking, the only correct alternative is to first rename the existing voice entity to `BrandVoiceProfile` across all 6 consuming files + migration + DbContext. That is more churn in out-of-scope code; `BrandRankingProfile` is the lower-risk choice and is the recommendation. Do not silently overwrite the existing entity.)

## Dependencies

- **section-01-foundation** (required): provides the pgvector wiring this section's vector column relies on — `Pgvector.EntityFrameworkCore` package, `HasPostgresExtension("vector")` on the context, `o.UseVector()` on `UseNpgsql`, the migration that `CREATE EXTENSION vector`, and the `CosineSimilarity` util. **Do not duplicate that wiring here.** This section adds entity configs that map a `vector(1536)` property; it assumes the extension and `UseVector()` are already registered. If section-01 is not yet merged, the migration in this section will still scaffold but the `vector` column type will not resolve at runtime until the extension exists.

Blocks: sections 05, 06, 08, 09.

## Tech context (self-contained)

- .NET 10, Clean Architecture: `PBA.Domain` (entities), `PBA.Application` (interfaces, MediatR), `PBA.Infrastructure` (EF configs, migrations, services), `PBA.Api`.
- EF Core on PostgreSQL (Npgsql). Configs live in `src/PBA.Infrastructure/Data/Configurations/` and are auto-applied via `modelBuilder.ApplyConfigurationsFromAssembly(...)` in `ApplicationDbContext.OnModelCreating` — **a new `IEntityTypeConfiguration<T>` is picked up automatically**, no manual registration.
- Existing `jsonb List<string>` precedent: `Idea.Tags` → `builder.Property(i => i.Tags).HasColumnType("jsonb")`. Reuse this for topic/marker lists.
- pgvector property type is `float[]` in the entity (`Pgvector.EntityFrameworkCore` maps `float[]` → `vector(n)` via `.HasColumnType("vector(1536)")`).
- Tests: xUnit, AAA, `Method_Scenario_ExpectedResult`. EF InMemory (`UseInMemoryDatabase(Guid.NewGuid().ToString())`) for handler/seeder logic. **InMemory cannot execute pgvector SQL or partial-index DDL** — those two facts are covered by a Postgres Testcontainers smoke test (skippable when no Docker), everything else by InMemory + pure unit tests.
- Migrations folder: `src/PBA.Infrastructure/Data/Migrations/`. Generate with `dotnet ef migrations add ... --project src/PBA.Infrastructure --startup-project src/PBA.Api`. Two deploy hosts (Mac Mini + Furious) run the same migrations.

---

## Tests FIRST

Write these before the entities. Pure/InMemory tests are the leverage; the Testcontainers ones are smoke-only.

### `BrandRankingProfileVersioningTests` (pure domain semantics — no DB)

The versioning rule is the single most important semantic. Put the decision logic on the entity (a method like `bool RequiresVersionBump(BrandRankingProfile proposed)` or a static comparer) so it is unit-testable without EF. This logic is *consumed* by section-09's update command but *defined* here.

```
# Test: editing only Weight / HalfLifeDays / DecayFloor / AntiTopicMultiplier / AuthorityBoost does NOT require a version bump
# Test: editing a pillar Name requires a version bump
# Test: editing a pillar Description requires a version bump
# Test: adding a pillar requires a version bump
# Test: removing a pillar requires a version bump
# Test: changing AuthorityTopics requires a version bump
# Test: changing AntiTopics requires a version bump
# Test: changing Positioning / AudiencePrimary / AudienceSecondary requires a version bump (these feed the analyzer prompt)
# Test: changing VoiceMarkers requires a version bump (also feeds the prompt)
# Test: no change at all → no bump
```

Implementer decides exactly which fields are "definition" (prompt/embedding-affecting → bump) vs "query-time" (ranking math knobs → no bump). The dividing line: **anything the LLM analyzer or pillar embeddings see is a definition field; anything only `ComputeRank` reads at query time is not.** Document the field classification in code next to the method.

### `BrandRankingProfileConfigurationTests` (EF mapping)

```
# Test: active profile round-trips Pillars as related rows, each with its own Id / Weight / Order (InMemory)
# Test: AuthorityTopics / AntiTopics / VoiceMarkers persist and round-trip as List<string> (InMemory)
# Test: BrandPillar.DescriptionEmbedding maps to vector(1536) and round-trips (Testcontainers smoke; [Fact(Skip=...)] or trait-gated when no Docker)
# Test: partial unique index rejects a SECOND IsActive=true profile, allows multiple IsActive=false (R-L4) — Postgres-only (Testcontainers)
# Test: optimistic concurrency token (xmin) is configured — second save against a stale row throws DbUpdateConcurrencyException (R-H3) — Postgres-only
```

Note for the jsonb round-trip test under InMemory: the provider stores `List<string>` natively, so this proves the property is tracked/round-tripped, not the literal `jsonb` column type. The literal column type is asserted by the migration smoke test below.

### `BrandRankingProfileSeederTests` (idempotent startup seeder — R-L5)

```
# Test: first run inserts the v1 profile — 5 pillars with the v0 weights (0.28/0.23/0.22/0.15/0.12), authority topics, anti topics, voice markers, Version=1, IsActive=true
# Test: second run is a no-op — does not duplicate, does not bump Version, does not touch UpdatedAt
# Test: seeder is safe when an active profile already exists (e.g. seeded by the other deploy host) — exactly one active profile remains
```

The "two hosts insert exactly one" guarantee is the partial unique index doing its job at the DB level; the seeder test asserts the seeder *checks before inserting* (`AnyAsync(p => p.IsActive)`) so a benign concurrent insert that loses the unique-index race is caught/ignored rather than crashing startup. Under InMemory the index does not exist, so test the check-before-insert path; the index race itself is a Postgres concern covered by the config test above.

### Migration smoke (folds into the section-12 cutover Testcontainers run; assert here that the migration scaffolds)

```
# Test: migration applies cleanly on Postgres — BrandRankingProfiles + BrandPillars tables exist, BrandPillars.DescriptionEmbedding is vector(1536), partial unique index present (Testcontainers; may be deferred to section-12)
```

---

## Implementation

### 1. Domain entities — `src/PBA.Domain/Entities/`

**`BrandRankingProfile.cs`** (fields per plan §4, renamed aggregate):

```csharp
namespace PBA.Domain.Entities;

public class BrandRankingProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int Version { get; set; }                 // bump only on definition/topic/prompt-field changes
    public bool IsActive { get; set; }               // exactly one active (partial unique index)
    public required string Positioning { get; set; }
    public required string AudiencePrimary { get; set; }
    public string? AudienceSecondary { get; set; }
    public double HalfLifeDays { get; set; }          // default 7   (query-time, no bump)
    public double DecayFloor { get; set; }            // default ~0.075 (query-time, no bump)
    public double AntiTopicMultiplier { get; set; }   // default 0.1 (query-time, no bump)
    public double AuthorityBoost { get; set; }        // default 1.2 (query-time, no bump)
    public List<BrandPillar> Pillars { get; set; } = [];
    public List<string> AuthorityTopics { get; set; } = [];
    public List<string> AntiTopics { get; set; } = [];
    public List<string> VoiceMarkers { get; set; } = [];
    public uint Xmin { get; set; }                    // Npgsql concurrency token (R-H3) — see EF config
    public DateTimeOffset UpdatedAt { get; set; }

    // Versioning semantic (R, plan §4) — unit-tested by BrandRankingProfileVersioningTests.
    // Returns true if applying `proposed` would change a DEFINITION field (prompt/embedding-affecting),
    // which must bump Version and trigger re-score. Weight/decay/multiplier edits return false.
    public bool RequiresVersionBump(BrandRankingProfile proposed) { /* compare definition fields */ }
}
```

**`BrandPillar.cs`**:

```csharp
namespace PBA.Domain.Entities;

public class BrandPillar
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BrandRankingProfileId { get; init; }
    public required string Name { get; set; }
    public required string Description { get; set; }    // scored by LLM + embedded for pre-filter
    public double Weight { get; set; }                  // 0..1, sum ~1.0 across pillars (query-time)
    public int Order { get; set; }
    public float[]? DescriptionEmbedding { get; set; }  // vector(1536), populated by section-06
}
```

`DescriptionEmbedding` stays nullable: this section creates the column; section-06 (`IdeaEmbeddingService`) computes and persists the vectors. `Xmin` uses Npgsql's system-column mapping (do not add a real DB column).

### 2. EF configurations — `src/PBA.Infrastructure/Data/Configurations/`

**`BrandRankingProfileConfiguration.cs`** (`IEntityTypeConfiguration<BrandRankingProfile>`, auto-applied):

- `HasKey(p => p.Id)`.
- `Property(p => p.Positioning).HasColumnType("text")`; `AudiencePrimary`/`AudienceSecondary` `HasMaxLength(...)` or `text`.
- `Property(p => p.AuthorityTopics).HasColumnType("jsonb")`; same for `AntiTopics`, `VoiceMarkers` (matches `Idea.Tags` precedent).
- `HasMany(p => p.Pillars).WithOne().HasForeignKey(pl => pl.BrandRankingProfileId).OnDelete(DeleteBehavior.Cascade)`.
- **Concurrency token (R-H3):** the idiomatic Npgsql form is `builder.UseXminAsConcurrencyToken();` (prefer that over manually mapping the property; verify the exact API against the installed Npgsql version).
- **Partial unique index (R-L4):** `builder.HasIndex(p => p.IsActive).IsUnique().HasFilter("\"IsActive\" = true")`. This enforces at most one active profile at the DB level. (No-op under InMemory.)
- Table name `BrandRankingProfiles` (explicit `ToTable` to avoid any pluralization ambiguity with the existing `BrandProfiles`).
- **Do NOT use `HasData` to seed.** Seeding is done by the idempotent startup seeder (R-L5), not a data migration — two deploy hosts must not race a `HasData` insert, and `HasData` would re-emit on every model change.

**`BrandPillarConfiguration.cs`** (`IEntityTypeConfiguration<BrandPillar>`):

- `HasKey(p => p.Id)`.
- `Property(p => p.Name).HasMaxLength(200)`; `Description` `HasColumnType("text")`.
- `Property(p => p.DescriptionEmbedding).HasColumnType("vector(1536)")` — relies on section-01's `o.UseVector()` + `vector` extension.
- Table `BrandPillars`.

### 3. DbContext + IAppDbContext wiring

- `src/PBA.Infrastructure/Data/ApplicationDbContext.cs`: add `public DbSet<BrandRankingProfile> BrandRankingProfiles => Set<BrandRankingProfile>();` (and optionally `DbSet<BrandPillar>` if any query needs the set directly — pillars are reachable via the navigation, so a top-level set is optional). Leave existing `BrandProfiles` untouched.
- `src/PBA.Application/Common/Interfaces/IAppDbContext.cs`: add `DbSet<BrandRankingProfile> BrandRankingProfiles { get; }` so the seeder and section-09 handlers can reach it through the interface. Keep existing `BrandProfiles`.

### 4. Idempotent startup seeder — `src/PBA.Infrastructure/Data/Seeding/`

**`BrandRankingProfileSeeder.cs`** — a small class (e.g. implementing an existing seeding contract if one exists, else a plain class with `Task SeedAsync(IAppDbContext db, CancellationToken ct)`), invoked at startup (alongside `db.Database.MigrateAsync()` in `Program.cs` / wherever existing startup migration runs). Check the existing startup path for how migrations/seed run today and follow that pattern rather than inventing a new hook.

Logic:
1. `if (await db.BrandRankingProfiles.AnyAsync(p => p.IsActive, ct)) return;` — idempotent guard.
2. Otherwise construct the v1 profile from `brand-profile-v0.md` (values below), `Version = 1`, `IsActive = true`, `UpdatedAt = now`, add, `SaveChangesAsync`.
3. Wrap the save so a unique-index violation (the other host won the race) is caught and swallowed — startup must not crash if a concurrent host already seeded.

**v1 seed data (verbatim from `planning/brand-strategy/brand-profile-v0.md`):**

- **Positioning:** `"I show enterprise teams what AI can actually ship — by building it myself."`
- **AudiencePrimary:** Senior engineers, EMs, architects at enterprise companies — frustrated by AI hype, want proof, patterns, shipped artifacts.
- **AudienceSecondary:** Enterprise executives / decision-makers.
- **HalfLifeDays** = 7, **DecayFloor** = 0.075, **AntiTopicMultiplier** = 0.1, **AuthorityBoost** = 1.2.
- **Pillars** (Name, Weight, Order, Description):
  1. Agent-Native Architecture — 0.28 — "Harnesses, Agent Skills, MCP, context engineering, agent memory, multi-agent patterns, plus AI economics/tokenomics/cost."
  2. Enterprise AI Adoption & Governance — 0.23 — "Exec-altitude: strategy, operating model, governance, ROI, the 95%-failure framing."
  3. Agentic SDLC & Eng Transformation — 0.22 — "How engineering orgs actually build with agents; orchestrate-not-implement; training."
  4. Claude / Anthropic Agent Engineering — 0.15 — "Bringing Anthropic/Claude-Code patterns into the enterprise; Agent Skills, Claude Code architecture, the MD-who-ships-on-Claude position."
  5. Microsoft Enterprise AI Stack — 0.12 — "Foundry, Copilot, Agent Framework, .NET/C# in enterprise AI."
  (Weights sum to 1.0.)
- **AuthorityTopics:** [".NET / C# in enterprise AI", "Anthropic Agent Skills / Claude Code architecture", "Claude-Code-style agent harnesses", "Agent Skills framework", "MCP server design", "Agentic SDLC tooling"]
- **AntiTopics:** ["Digital-human / avatar / talking-head tech", "Consumer-AI gossip / product drama", "Model-release horse-race / benchmark-leaderboard news", "Crypto / web3 / AI-token coins", "AGI philosophy / doomerism", "Funding-round / VC news with no enterprise-build angle", "Pure consumer dev tutorials with no enterprise constraint"]
- **VoiceMarkers:** ["Simple enough for execs, technical enough to not be fluff", "Contrarian / false-debate debunking", "Data > opinion — specific numbers", "Every claim has a build behind it (Mollick rule)", "No em dashes, no AI-isms, no promotional inflation"]

`DescriptionEmbedding` is left `null` on seed — section-06 backfills pillar vectors when it sees `Version` set / vectors missing.

### 5. Migration

Generate after entities + configs compile:

```
dotnet ef migrations add AddBrandRankingProfile --project src/PBA.Infrastructure --startup-project src/PBA.Api
```

Verify the generated migration:
- Creates `BrandRankingProfiles` (with `xmin` as the concurrency column — Npgsql handles this as a system column, so it should **not** appear as a created column; confirm the snapshot marks it `IsRowVersion`/`ValueGenerated.OnAddOrUpdate`).
- Creates `BrandPillars` with `DescriptionEmbedding` typed `vector(1536)`.
- Creates the partial unique index on `BrandRankingProfiles ("IsActive") WHERE "IsActive" = true`.
- Contains **no `HasData` seed insert** (seeding is runtime).
- Depends on the `vector` extension from section-01's migration — ensure migration ordering puts the extension-enabling migration first (section-12 cutover owns final apply order; just confirm this migration is generated *after* section-01's in timestamp order, or note the dependency for the cutover).

## Definition of done

- [ ] `BrandRankingProfileVersioningTests`, `BrandRankingProfileConfigurationTests`, `BrandRankingProfileSeederTests` written first and (the InMemory/pure ones) green.
- [ ] `BrandRankingProfile` + `BrandPillar` entities created; existing voice `BrandProfile` untouched.
- [ ] Both EF configs created and auto-applied; `xmin` concurrency token + partial unique index configured.
- [ ] DbSet added to `ApplicationDbContext` + `IAppDbContext`.
- [ ] Idempotent seeder created, wired into startup, seeds v1 from `brand-profile-v0.md`, no-ops on second run, race-safe.
- [ ] Migration `AddBrandRankingProfile` generated, reviewed (no HasData, vector + partial index correct), applies cleanly on Postgres.
- [ ] `dotnet build && dotnet test` green; new code ≥ 80% covered.

## Notes / gotchas

- **`xmin` under InMemory:** the InMemory provider ignores the concurrency token; the lost-update test must run on Postgres (Testcontainers). Don't assert concurrency behavior under InMemory — it will silently pass and prove nothing.
- **Partial unique index under InMemory:** not enforced. The "reject second active" test is Postgres-only; the seeder's `AnyAsync` check is the InMemory-testable guard.
- **Topic/marker lists as jsonb:** matches `Idea.Tags`. Don't introduce a child table for these — nothing queries them in SQL; they are read whole into the analyzer prompt (section-05).
- **`BrandRankingProfileSnapshot`** (immutable in-memory projection carrying `Version`) is referenced by sections 05/06/07. It is **not** built here — those sections define it. This section only defines the persisted aggregate.
- Keep entity files under the 400-line limit (trivial here) and one class per file.

## Relevant file paths

- Create `src/PBA.Domain/Entities/BrandRankingProfile.cs`
- Create `src/PBA.Domain/Entities/BrandPillar.cs`
- Create `src/PBA.Infrastructure/Data/Configurations/BrandRankingProfileConfiguration.cs`
- Create `src/PBA.Infrastructure/Data/Configurations/BrandPillarConfiguration.cs`
- Create `src/PBA.Infrastructure/Data/Seeding/BrandRankingProfileSeeder.cs` (+ startup wiring in `Program.cs`)
- Create migration under `src/PBA.Infrastructure/Data/Migrations/`
- Modify `src/PBA.Infrastructure/Data/ApplicationDbContext.cs` (add DbSet)
- Modify `src/PBA.Application/Common/Interfaces/IAppDbContext.cs` (add DbSet)
- Create tests under `tests/PBA.Application.Tests/...` (versioning, seeder) and `tests/PBA.Infrastructure.Tests/...` (config/Testcontainers) — match the existing test project layout.

---

## As-built notes (implemented 2026-06-16)

- **Naming:** shipped as `BrandRankingProfile` + `BrandPillar` (the existing voice `BrandProfile` is untouched), as planned.
- **Embedding type decision (user-approved, cross-cutting):** `BrandPillar.DescriptionEmbedding` is
  `float[]?` on the Domain entity (Domain stays free of Npgsql/pgvector). The `vector(1536)` mapping +
  `float[]<->Pgvector.Vector` value converter live in `PgVectorModelConfiguration.Apply`, called from
  `ApplicationDbContext.OnModelCreating` **only under `Database.IsNpgsql()`**. The InMemory test provider
  can't map the `Vector` provider type, so it stores `float[]` natively. Section-03's `Idea.Embedding`
  must use this same pattern (add it to `PgVectorModelConfiguration`).
- **Concurrency (R-H3):** explicit `uint Xmin { get; private set; }` mapped via
  `HasColumnName("xmin")/HasColumnType("xid")/ValueGeneratedOnAddOrUpdate/IsConcurrencyToken`. Verified
  the generated **SQL** omits `xmin` from CREATE TABLE (Npgsql system column) — `has-pending-model-changes`
  is clean. (The migration `.cs` lists it with `rowVersion:true`; that is scaffold representation, not DDL.)
- **Single active (R-L4):** partial unique index `WHERE "IsActive" = true` — confirmed in generated SQL.
- **Seeding (changed from plan):** Program.cs has no startup migrate/seed and the existing demo seeders
  are dev-only. Per the binding decision, the v1 profile is seeded by a **startup hook** (scoped,
  try/catch-guarded `IBrandRankingProfileSeedService.SeedAsync()` in `Program.cs`) — idempotent
  (`AnyAsync(p => p.IsActive)`) and race-safe (catches only `PostgresException` unique-violation). A
  dev-only `POST /api/brand-ranking-profile/seed` endpoint mirrors the sibling seeders for manual use.
  No unauthenticated prod write surface. Section-12 no longer needs to POST a seed endpoint in prod.
- **Versioning:** `RequiresVersionBump` uses ordered `SequenceEqual` for topics/voice-markers (they
  render verbatim into the analyzer prompt). Pillars compared by `Id`; add/remove/rename bumps,
  weight/order does not. **Section-09 must preserve pillar Ids on edit** or every edit over-bumps.
- **Tests:** versioning (15, pure), seeder (3, InMemory), config round-trip (1, InMemory) — all green
  (354 total Application tests). Postgres-only guarantees (partial index, xmin conflict, vector
  round-trip) deferred to section-12 Testcontainers.
- **Files:** `BrandRankingProfile.cs`, `BrandPillar.cs`, `BrandRankingProfileConfiguration.cs`,
  `BrandPillarConfiguration.cs`, `PgVectorModelConfiguration.cs`, `IBrandRankingProfileSeedService.cs`,
  `BrandRankingProfileSeedService.cs`; migration `20260616135102_AddBrandRankingProfile`; DbSet +
  `IAppDbContext` + DI + Program.cs startup hook.
