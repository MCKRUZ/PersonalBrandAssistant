# Section 12 — Cutover / Rollout

## Purpose

This is the **final section**. Everything else (sections 01-11) is built, tested, and merged. This section is the operational runbook that takes the new brand-anchored ranking system live on the two deployed hosts, plus a small set of guard tests and a code-cleanup pass that removes the dead V1 scoring/clustering config.

It is **not** mostly new code. It is:
1. An ordered migration-apply + backfill + go-live runbook (manual steps, gated where irreversible).
2. Three guard tests that prevent regressions (`CutoverTests`).
3. A code-removal pass that deletes the now-dead `BackfillEnabled` flag and `Clustering:MinScore` key (R-L6).

Do **not** start this section until all of sections 01-11 are merged and green. It depends on every other section.

## Binding cross-section decisions (fold these in)

1. **Aggregate name.** The ranking aggregate is `BrandRankingProfile` (table `BrandRankingProfiles`), seeded by `BrandRankingProfileSeeder` (section-02). It is **distinct** from the existing voice `BrandProfile`. The v1 ranking profile is seeded at **application startup**, idempotently, on both hosts — **not** via a data migration.
2. **Migration apply order is load-bearing.** Vector columns require the pgvector extension first. Required order:
   1. section-01 `EnablePgVectorExtension`
   2. section-02 `AddBrandRankingProfile`
   3. section-03 `AddIdeaEmbeddingAndSubScores`
   Verify the migration **timestamps** sort in exactly this order before deploying (EF applies by timestamp prefix). If they don't, the deploy fails with "type vector does not exist."
3. **Dedup service rename, config name kept.** The dedup background service is `IdeaDedupService` (renamed from `IdeaClusterer`/`IdeaClusteringService`), but its appsettings section is still named `"Clustering"` deliberately — to minimize churn on the two deployed hosts. So `"Clustering"` as a *section* survives; only the `MinScore` *key inside it* is removed.
4. **R-L6 cleanup verification.** Grep **both** deployed appsettings (Mac Mini `192.168.50.103` + Furious) to confirm `BackfillEnabled` and `Clustering:MinScore` are fully gone before deleting the code paths.
5. **Embedding-model gate (R-M4)** was already cleared in section-01. No need to re-run it here.
6. **Backfill sequence.** Embed **all ~3,800 ideas + pillar vectors first**, then LLM-score **only the last 30 days**. The first production LLM-scoring run is **irreversible** (spends real tokens) and **must be gated behind a manual confirm**.

## Tech context (self-contained)

- Backend: .NET 10, Clean Architecture, EF Core on PostgreSQL (Npgsql), MediatR + `Result<T>`.
- EF migrations live in `PBA.Infrastructure/Data/Migrations/`. Applied with `dotnet ef database update` or auto-applied at startup if `Program.cs` calls `Database.Migrate()` (check).
- Background sweeps: `IdeaEmbeddingService` (section-06), rewired `IdeaScoringService` (section-07), `IdeaDedupService` (section-07).
- Two deploy hosts, both Docker Compose:
  - **Mac Mini** `192.168.50.103` — runs branch `v2-rebuild`.
  - **Furious** (local Windows 11) — runs branch `main`.
- Migration smoke test uses **Testcontainers (Postgres, pgvector-capable image)** because EF InMemory cannot run pgvector SQL.
- Dead config in `src/PBA.Api/appsettings.json`: `IdeaScoring:BackfillEnabled` (line ~52), `Clustering:MinScore` (line ~56). **Do NOT touch `HackerNews:MinScore` (line ~90)** — unrelated, must stay.

## Files to create / modify

| File | Action |
|------|--------|
| `tests/PBA.Infrastructure.Tests/Cutover/CutoverTests.cs` | **Create** — three guard tests. |
| `src/PBA.Api/appsettings.json` | **Modify** — delete `IdeaScoring:BackfillEnabled` and `Clustering:MinScore` (after R-L6 grep clears). |
| `src/PBA.Infrastructure/Configuration/ClusteringOptions.cs` | **Verify/Modify** — `MinScore` removed (section-07 may have done it). |
| `src/PBA.Infrastructure/Configuration/IdeaScoringOptions.cs` | **Verify/Modify** — `BackfillEnabled` removed (section-07 may have done it). |
| `src/PBA.Infrastructure/Services/Radar/IdeaClusterer.cs` | **Delete** (verify section-07 did it). |
| `tests/.../Services/Radar/IdeaClustererTests.cs`, `IdeaClusteringServiceTests.cs` | **Delete**. |
| Deployed `appsettings.json` on **both** hosts | **Modify** (manual, during deploy). |

Sections 01/07 may already remove some config/code. This section **verifies** removal is complete (R-L6) and finishes anything left.

## Tests FIRST (`CutoverTests`)

Create `tests/PBA.Infrastructure.Tests/Cutover/CutoverTests.cs`. Guards, not exhaustive behavior tests:

```csharp
public class CutoverTests
{
    // Test 1 — migrations apply cleanly, in order, on a real Postgres (Testcontainers, pgvector/pgvector:pg16).
    // dbContext.Database.MigrateAsync() must not throw, and assert:
    //   - "vector" extension exists,
    //   - "BrandRankingProfiles" + "BrandPillars" tables exist,
    //   - Ideas has Embedding (vector), EmbeddedAt, PillarSubScores (jsonb), ScoredProfileVersion, ScoreAttempts cols,
    //   - the partial unique index on BrandRankingProfiles WHERE "IsActive" = true exists.
    // Implicitly proves apply ORDER (extension before vector columns).
    [Fact] public async Task Migrations_ApplyCleanly_OnRealPostgres() { /* Testcontainers */ }

    // Test 2 — dead config keys gone (R-L6). Loads src/PBA.Api/appsettings.json, asserts:
    //   - "IdeaScoring:BackfillEnabled" absent, "Clustering:MinScore" absent,
    //   - "Clustering" section STILL exists (decision #3),
    //   - "HackerNews:MinScore" STILL exists (unrelated).
    [Fact] public void Appsettings_DeadScoringAndClusteringKeys_AreRemoved() { }

    // Test 3 — first prod LLM-scoring run gated behind a manual confirm flag.
    // With the gate flag false/absent, the sweep must skip the AnalyzeAsync branch entirely
    // (embedding/pre-filter still runs).
    [Fact] public void ScoringSweep_DoesNotInvokeLlm_WhenScoringGateDisabled() { }
}
```

Notes:
- Test 1 uses Testcontainers (EF InMemory can't run pgvector DDL). Reuse any Testcontainers base fixture from section-02/03 smoke tests.
- Test 2 is a plain JSON-load assertion against the committed `appsettings.json`; it fails until the keys are deleted (TDD red driving the cleanup).
- The build itself is a fourth guard (R-L6): once the options properties are removed, any lingering C# reference fails the build.

## Scoring gate (manual-confirm mechanism)

The first production LLM-scoring run spends real tokens and is irreversible; it must not fire automatically on deploy.

Mechanism (use what section-07 built; if it didn't add a gate, add it here):
- A config flag on `IdeaScoringOptions`, e.g. `bool ScoringEnabled` (default `false`). The rewired `IdeaScoringService` skips the `AnalyzeAsync` (LLM) branch when false — embedding + pre-filter still run (cheap, reversible). Only the token-spending step is gated.
- This **replaces** the old `BackfillEnabled` role. Do not resurrect `BackfillEnabled`; the 30-day window + this gate cover what it used to do.
- Go-live = a human flips `IdeaScoring:ScoringEnabled` to `true` on the target host and restarts the API, after confirming embedding backfill completed and costs are understood.

If section-07 already implemented an equivalent gate, wire Test 3 to that flag and document its name here — do not add a second one.

## Rollout runbook (ordered, gated)

Execute per host. Do **Mac Mini (`v2-rebuild`)** first as canary, then **Furious (`main`)**.

**Step 0 — Pre-flight (both hosts).** Confirm migration timestamp order (decision #2). Confirm the Postgres role can `CREATE EXTENSION vector`. Confirm `IdeaScoring:ScoringEnabled` is **false**. Take a DB backup.

**Step 1 — Apply migrations.** Deploy the new image (auto-migrate) or run `dotnet ef database update`. Verify: `vector` extension; `BrandRankingProfiles`/`BrandPillars` tables; new `Ideas` columns; partial unique index.

**Step 2 — Verify v1 ranking profile seeded.** `BrandRankingProfileSeeder` runs idempotently at startup. After first boot confirm exactly one `BrandRankingProfiles` row with `IsActive=true`, `Version=1`, 5 pillars with weights from `brand-profile-v0.md`. Redeploy must NOT create a second active profile (partial unique index enforces, R-L4/R-L5).

**Step 3 — Backfill embeddings (cheap, reversible).** `IdeaEmbeddingService` embeds all `Embedding == null` ideas (~3,800) in batches + the active profile's pillar vectors. Verify `count(*) WHERE Embedding IS NULL` trends to 0 (a few may stay null from isolated bad batches — R-H2, acceptable, they retry). Verify all 5 pillars have non-null `DescriptionEmbedding`. **Do not proceed until embedding coverage is effectively complete.**

**Step 4 — GATE: first LLM-scoring run (irreversible, spends tokens).** Manual-confirm point — a human must approve. Flip `IdeaScoring:ScoringEnabled` → `true`, restart the API. `IdeaScoringService` LLM-scores the last-30-day window of embedded, above-threshold ideas; below-threshold get embedding-only brandFit; >30-day items rank ~0 via decay. `ScoreAttempts` cap (R-M5) prevents poison items re-spending. Verify a sample has populated `PillarSubScores` (keyed by `BrandPillarId`, R-C3) + `ScoredProfileVersion=1`.

**Step 5 — Flip the read path live.** Default `SortBy` is already `"rank"` (section-08). Confirm `ListIdeas` returns rank-ordered results with `BrandFit`/`PillarBreakdown`/flags/`RecencyFactor`/`Stale`. Smoke-test the UI: Ranked view shows numbered Top-N; weight slider re-ranks instantly (weights-only PUT, no re-score); a pillar-definition edit prompts the "Save & re-score" confirm.

**Step 6 — Verify dedup.** `IdeaDedupService` (config section still `"Clustering"`, decision #3) runs in-memory cosine dedup, gated on full embedding coverage (R-H1). Verify primaries/`DuplicateOfId` look sane on a recent cluster.

**Step 7 — Promote to Furious.** Repeat Steps 0-6 on Furious (`main`). Same gate discipline at Step 4.

**Rollback.** Reversible up to Step 4. After Step 1 you can roll back the image; new columns/tables are additive and old code ignores them. After Step 4, tokens are spent and sub-scores written — rolling back code is fine but you've paid; restore the Step-0 DB backup only if scored data is harmful (it isn't — additive).

## R-L6 cleanup pass (AFTER go-live verified on both hosts)

1. **Grep the repo** for: `BackfillEnabled`; `Clustering` + `MinScore` (i.e. `Clustering:MinScore` / `ClusteringOptions.MinScore`) — **not** `HackerNews:MinScore`; `IdeaClusterer` / `IdeaClusteringService`.
2. **Grep both deployed appsettings** (Mac Mini + Furious) — SSH in, confirm neither has `BackfillEnabled` or `Clustering:MinScore`; remove if present. Don't delete C# until live configs are clean.
3. **Delete code** once grep is clean: `IdeaClusterer.cs` (if section-07 didn't); `IdeaClustererTests.cs`; `IdeaClusteringServiceTests.cs`; `BackfillEnabled` from `IdeaScoringOptions.cs`; `MinScore` from `ClusteringOptions.cs` (keep the class — `IntervalMinutes`/`LookbackHours`/`MaxItemsPerSweep` remain for `IdeaDedupService`).
4. **Delete the keys** from `src/PBA.Api/appsettings.json` (the `BackfillEnabled` line in `IdeaScoring`, the `MinScore` line in `Clustering`). Leave `HackerNews:MinScore`.
5. **Build** — the compiler is the guard: any lingering reference fails (R-L6 belt-and-suspenders).
6. Run `CutoverTests` — Test 2 now passes.

## Dependencies

- **All sections 01-11** must be merged and green. Relies specifically on: section-01 (pgvector extension migration + `RankingOptions`), section-02 (`BrandRankingProfiles` + seeder), section-03 (`AddIdeaEmbeddingAndSubScores`), section-06 (`IdeaEmbeddingService`), section-07 (rewired `IdeaScoringService`, `IdeaDedupService`, scoring gate, dead-config removal), section-08 (default sort = rank, extended `IdeaDto`), sections 10/11 (editor + Ranked view UI).

## Verify (definition of done)

- `dotnet build` clean (no dead-config references).
- `dotnet test` green, including the three `CutoverTests`.
- Migrations applied + verified on **both** hosts in correct order.
- Exactly one active `BrandRankingProfile` (Version 1) per host; no duplicate from redeploy.
- Embedding backfill complete; LLM-scoring run completed **only after manual gate flip**.
- Default sort = rank; Ranked view + Brand Profile editor live and smoke-tested.
- R-L6 grep clean across repo and both deployed appsettings; `IdeaClusterer.cs` + tests deleted; `BackfillEnabled` / `Clustering:MinScore` removed; `HackerNews:MinScore` untouched.
