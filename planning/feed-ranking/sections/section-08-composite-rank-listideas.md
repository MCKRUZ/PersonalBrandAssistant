# Section 08 — Composite Rank + ListIdeas Read Path

## Goal

Replace the coarse `Score`-based ordering on the Ideas page with a **brand-anchored, multi-factor composite rank** computed at read time. This section delivers:

1. A pure `ComputeRank(idea, profile, now)` function (no DB, no LLM — fully unit-testable).
2. A rewired `ListIdeas` query handler that pushes filters to SQL, projects **without the embedding vector**, then computes rank + sorts + pages **in memory**.
3. A `rank` sort option (the new **default**), with the existing `score` sort retained.
4. An extended `IdeaDto` carrying the rank, brand-fit, per-pillar breakdown, topic flags, recency factor, and a stale flag.

The composite rank formula is:

```
rank = brandFit × recencyDecay × antiTopicMultiplier × authorityBoost
```

where:
- `brandFit` = renormalized weighted sum of stored per-pillar sub-scores (each 0-1), clamped to [0,1].
- `recencyDecay` = `max(exp(-ln2 / halfLife × ageDays), floor)`.
- `antiTopicMultiplier` = `antiMult` (e.g. ×0.1) if the idea is flagged anti-topic, else 1.0.
- `authorityBoost` = `authBoost` (e.g. ×1.2) if the idea is flagged authority-topic, else 1.0.

**Key design fact:** the LLM produces raw per-pillar sub-scores that are stored **once** on the idea. The pillar **weights are applied at query time**, so re-weighting re-ranks instantly with zero LLM calls. This section only *consumes* stored sub-scores and the active profile — it never calls the LLM or the embedding service.

## Naming decision (BINDING — from section-02)

The ranking aggregate that the original plan calls `BrandProfile` was **renamed to `BrandRankingProfile`** (table `BrandRankingProfiles`) because a live, unrelated voice-drafting `BrandProfile` entity already exists at `src/PBA.Domain/Entities/BrandProfile.cs`. The in-memory projection is `BrandRankingProfileSnapshot`. Throughout this section, wherever the plan text says `BrandProfile` / `BrandProfileSnapshot` for the RANKING feature, use `BrandRankingProfile` / `BrandRankingProfileSnapshot`. Do not touch the existing voice `BrandProfile`.

## Dependencies (provided by other sections — reference only)

This section depends on **section-02** and **section-03** being complete.

**From section-02 (`BrandRankingProfile` domain aggregate in `PBA.Domain`):**
- `BrandRankingProfile` with: `Version (int)`, `IsActive (bool)`, ranking knobs — `HalfLifeDays`, `DecayFloor`, `AntiTopicMultiplier`, `AuthorityBoost` — and a collection of `BrandPillar` children.
- `BrandPillar` with at least: `Id (Guid)`, `Name (string)`, `Weight (double, 0-1)`.
- A `BrandRankingProfileSnapshot` in-memory projection (active profile + active pillars). Reuse it; do not duplicate.

**From section-03 (extended `PBA.Domain/Entities/Idea.cs`):**
- `Embedding` — `vector(1536)`, nullable. **MUST NOT be selected by this section's projection.**
- `PillarSubScores` — jsonb, **keyed by `BrandPillarId` (Guid)** (R-C3), value carries the sub-score (0-1) and a short `Reason` string for display. Names are resolved from the active profile at read time.
- `IsAntiTopic`, `IsAuthorityTopic` — nullable bool.
- `ScoredProfileVersion` — int.
- Existing `Score (int?)` column + its index are **retained** (R-M2).

## Tests FIRST (TDD)

Backend: xUnit, AAA, `Method_Scenario_ExpectedResult`. Use EF `UseInMemoryDatabase(Guid)` for the handler tests (ranking is in-memory so **no pgvector is needed**). Reuse the existing `CreateIdea()` helper pattern in `ListIdeasHandlerTests`.

### `ComputeRankTests` (pure function — WRITE FIRST, table-driven)

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

### `ListIdeasHandlerTests` (extend existing; EF InMemory)

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

## Implementation

### 1. Extend `IdeaDto`

File: `src/PBA.Application/Features/Ideas/Dtos/IdeaDto.cs` (adjust to actual location). Add the ranking fields to the existing record (keep all current fields):

```csharp
public double Rank { get; init; }
public double BrandFit { get; init; }
public double RecencyFactor { get; init; }
public IReadOnlyList<PillarBreakdownDto> PillarBreakdown { get; init; } = [];
public bool? IsAntiTopic { get; init; }
public bool? IsAuthorityTopic { get; init; }
public bool Stale { get; init; }
```

New small DTO `PillarBreakdownDto`:

```csharp
public record PillarBreakdownDto
{
    public string Name { get; init; } = string.Empty;   // display name from active pillar
    public double Score { get; init; }                   // stored sub-score 0-1
    public string? Reason { get; init; }                 // short LLM reason, display only
}
```

`PillarBreakdown` lists only pillars present in the active profile that the idea has a stored sub-score for (R-C2a). `Name` comes from the active pillar (resolved by `BrandPillarId`), so renames reflect without re-scoring (R-C3).

### 2. `ComputeRank` pure function

Add to `ListIdeas.cs` as a static method (or a sibling static helper class — must be pure: no DB, no DI, deterministic).

```csharp
internal static RankResult ComputeRank(
    IReadOnlyDictionary<Guid, PillarSubScore> subScores,  // keyed by BrandPillarId (R-C3)
    bool? isAntiTopic,
    bool? isAuthorityTopic,
    int scoredProfileVersion,
    DateTimeOffset detectedAt,
    BrandRankingProfileSnapshot profile,
    DateTimeOffset now);

internal readonly record struct RankResult(
    double Rank, double BrandFit, double RecencyFactor, bool Stale,
    IReadOnlyList<PillarBreakdownDto> Breakdown);
```

**Algorithm (binding — §13 overrides §9 where they differ):**

1. **Active pillars only (R-C2a):** consider only sub-scores whose `BrandPillarId` exists in the active profile's pillars.
2. **Renormalize weights at read time (R-C2b):** let `S = Σ weight_p` over the active pillars the idea has a sub-score for. Effective weight = `weight_p / S`. If `S == 0` → `brandFit = 0`. `brandFit = Σ (weight_p / S) × subScore_p`, then **clamp to [0,1]** (R-M6).
3. **Recency:** `ageDays = max(0, (now - detectedAt).TotalDays)`; `recency = max(exp(-ln2 / halfLife × ageDays), floor)`. Use `Math.Log(2)`. `halfLife`/`floor` from profile.
4. **Multipliers (R-L1):** `antiMult = (isAntiTopic == true) ? profile.AntiTopicMultiplier : 1.0`; `authBoost = (isAuthorityTopic == true) ? profile.AuthorityBoost : 1.0`.
5. **Rank:** `rank = brandFit × recency × antiMult × authBoost`.
6. **Stale (R-C2c):** `stale = scoredProfileVersion < profile.Version`. Still ranked over surviving pillars + surfaced `Stale=true`.
7. **Unscored (R-L1):** no sub-scores → `brandFit=0`, multipliers 1.0 → `rank=0` (sorts to bottom, mirrors `Score ?? -1`).
8. **Derived `Score` clamp (R-M6):** clamp any derived 0-10 Score to [0,10]. Do not re-derive `Score` here (section-07 owns it); keep the existing stored `Score`.

### 3. Rewire `ListIdeas.Handler`

Load the active `BrandRankingProfile` + pillars once per query (`db.BrandRankingProfiles.Include(pillars).AsNoTracking().FirstOrDefault(p => p.IsActive)` or the section-02 snapshot). If none active, rank everything 0 (degrade gracefully) — the seeder guarantees one in practice.

Read path (R-C1a / R-C1b):
1. **Filters push to SQL** — keep all existing `Where` clauses unchanged.
2. `totalCount = await query.CountAsync(ct)` over the filtered set.
3. **Projection MUST NOT select `Embedding`** (R-C1a). Project to a lightweight intermediate selecting only ids + display fields + ranking inputs (`PillarSubScores`, `IsAntiTopic`, `IsAuthorityTopic`, `ScoredProfileVersion`, `DetectedAt`). **Never** include `i.Embedding` in the `Select`.
4. **Materialize** (`ToListAsync(ct)`) — pulls the full filtered set into memory. Justified at ~3,800 rows (R-C1b). **Revisit trigger:** ~25k rows OR p95 > 200 ms → precompute a stored `brandFit`-per-version column and apply only decay live. Leave a comment noting this.
5. **Compute rank in memory** for each row via `ComputeRank(...)` with `now`. Build the full `IdeaDto`.
6. **Sort + page in memory:** apply `ApplySort` over the ranked DTOs, then `Skip((Page-1)*PageSize).Take(PageSize)`. Return `PagedResult<IdeaDto>` with the in-memory page slice and the SQL `totalCount`.

### 4. `rank` sort + default flip (R-M2)

`Query` record default: `public string SortBy { get; init; } = "rank";`. Update `ApplySort` to operate over the in-memory `IEnumerable<IdeaDto>` and add `rank`; keep `score` (`order by Score ?? -1`), `detectedat`, `title`, `sourcename`, `category`, `status`. Default → rank. Respect `SortDirection` (desc default).

Endpoint default (`src/PBA.Api/Endpoints/IdeaEndpoints.cs` ~line 33): change `SortBy = p.SortBy ?? "detectedat"` → `?? "rank"`.

### 5. Keep the `Score` index (R-M2)

Do not drop the `Score` column/index. `score` sort remains a valid user option. No migration in this section.

## Files to create / modify

| File | Action |
|------|--------|
| `src/PBA.Application/Features/Ideas/Dtos/IdeaDto.cs` | Add ranking fields |
| `src/PBA.Application/Features/Ideas/Dtos/PillarBreakdownDto.cs` | New record `{ Name, Score, Reason }` |
| `src/PBA.Application/Features/Ideas/Queries/ListIdeas.cs` | Add `ComputeRank`; load active profile; project without `Embedding`; in-memory rank/sort/page; default sort `rank` |
| `src/PBA.Api/Endpoints/IdeaEndpoints.cs` | Change `SortBy` fallback to `"rank"` |
| `tests/.../Features/Ideas/ComputeRankTests.cs` | New table-driven pure-function tests |
| `tests/.../ListIdeasHandlerTests.cs` | Extend with rank/projection/sort/stale/paging tests |

## Binding review constraints recap (§13)

- **R-C1a** — projection MUST NOT select `Embedding`.
- **R-C1b** — filters→SQL; rank+sort+paging in memory (revisit at ~25k rows or p95 > 200 ms).
- **R-C2a/b/c** — active-pillar-only weighting; read-time renormalization; stale flag.
- **R-C3** — sub-scores keyed by `BrandPillarId`; names display-only, resolved from active pillar.
- **R-L1** — nullable flags use `== true`; unscored → rank 0.
- **R-M2** — keep the `Score` index; retain `score` sort.
- **R-M6** — clamp `brandFit` [0,1], derived `Score` [0,10].

## Out of scope

- Embedding / pre-filter (section-06); LLM scoring + sub-score persistence + Score derivation (section-05/07); the aggregate/EF/migration (section-02/03); the API (section-09); the Ranked view UI (section-11).

## Verify

`dotnet test` (pure + EF InMemory). Confirm default sort `rank`, descending order, filters applied, DTO breakdown populated, stale flag, `score` sort works, paging slice + total correct, and the query never references `Embedding`.

## As built (2026-06-16)

- **`ComputeRank` reuses `BrandFit.RenormalizedSubScore`** (the shared helper from section-06) for the
  renormalized brandFit — no re-derived weighted sum. Pure, in `ListIdeas.cs`; `RankResult` record struct.
- **Read path:** filters → SQL; `CountAsync`; load active `BrandRankingProfile` once → `…Snapshot`; project
  to a private `RankRow` (display fields + ranking inputs, **never `Embedding`**, R-C1a); `ToListAsync`;
  compute rank in memory; `ApplySort` over the DTOs; `Skip/Take` page. `PagedResult` = in-memory page slice
  + SQL `totalCount`.
- **Default sort flipped to `rank`** (Query default + endpoint fallback, R-M2). The `rank` sort tie-breaks on
  `DetectedAt desc`, so with no active profile (all ranks 0) it degrades to recency order — which keeps the
  pre-existing default-sort test passing. `score` sort + index retained.
- **Signature note:** `ComputeRank` takes `int? scoredProfileVersion` (not the spec's `int`) — unscored ideas
  have a null version; `Stale = version is { } v && v < profile.Version`.
- **Review fixes:** in-memory string sorts use a fixed `StringComparer.OrdinalIgnoreCase` (deterministic
  across server cultures); materialization comment names the real O(table) trigger (default unfiltered view).
- **Verification gap (review C1a):** the "projection excludes the `vector(1536)` column" guarantee holds by
  construction but is only InMemory-tested here; **section-12 Testcontainers** must confirm the generated SQL
  on real Postgres. (EF *can* project the whole value-converted `PillarSubScores` property — the EF limit is
  querying *into* a converted property, which this code never does.)
- Files: `IdeaDto.cs` (+rank fields), `PillarBreakdownDto.cs` (new), `ListIdeas.cs` (rewrite),
  `IdeaEndpoints.cs` (`?? "rank"`), `ComputeRankTests.cs` (new, 11), `ListIdeasHandlerTests.cs` (+6).
