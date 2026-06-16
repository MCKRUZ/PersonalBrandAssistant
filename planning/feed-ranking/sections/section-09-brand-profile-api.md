# Section 09 — Brand Ranking Profile API (read + update)

## Goal

Expose the ranking-feature Brand Profile aggregate (the editable source of truth for pillar weights, half-life, decay floor, anti/authority multipliers, pillar definitions, and topics) over HTTP so the frontend editor (section-10) can read and update it. Two **server-enforced** write modes:

- **Weights-only update** — persists weights / half-life / floor / multipliers, does **NOT** bump `Version`, triggers **no** re-score. Pillar-definition fields in the payload are **ignored** on this path.
- **Definition update** — changes pillar name/description, adds/removes a pillar, or edits authority/anti-topics → **bumps `Version`** so the background scoring sweep (section-07) re-scores stale in-window ideas.

The frontend cannot smuggle a definition change through the weights path; the server decides the mode.

## CRITICAL — naming (binding cross-section decision)

A live, unrelated voice-drafting `BrandProfile` entity already exists at `src/PBA.Domain/Entities/BrandProfile.cs`. The **ranking** aggregate this section reads/writes was renamed in section-02 to **`BrandRankingProfile`** (table `BrandRankingProfiles`), child rows `BrandPillar`, in-memory projection `BrandRankingProfileSnapshot`. Every type, DTO, query, command, validator, and endpoint in THIS section is for the **ranking** profile and must use that naming.

No `/api/brand-profile` route exists today (verified — only `/api/ideas`, `/api/analytics`, etc. are mapped in `Program.cs`). **The route `/api/brand-ranking-profile` is the safe, collision-free choice. Use it.**

## Dependencies

- **section-02-brand-profile-domain** (required): provides the `BrandRankingProfile` + `BrandPillar` entities, EF mapping, migration, the `xmin` optimistic-concurrency token (R-H3), the partial unique index `WHERE "IsActive" = true` (R-L4), and the **versioning logic** (`RequiresVersionBump` semantics) defined ON the entity. **Consume that logic; do not re-derive "did the definition change?" here.**

This section **blocks** section-10 (frontend editor).

## Existing patterns to follow (verified in repo)

- MediatR + `Result<T>` from `PBA.Domain.Common`. Queries/commands live under `PBA.Application/Features/...` as `public static class Xxx { public record Query/Command ...; public sealed class Handler(IAppDbContext db) : IRequestHandler<...> {...} }`. See `GetIdea.cs` for the exact shape (NotFound via `Result<T>.NotFound(...)`, `AsNoTracking()`, `IAppDbContext`).
- Endpoints are static extension classes mapped in `src/PBA.Api/Program.cs`. `IdeaEndpoints.MapIdeaEndpoints` does `app.MapGroup("/api/ideas").WithTags("Ideas")` then `group.MapGet/MapPut(...)`, returning `result.ToApiResult()`.
- `ToApiResult()` (`src/PBA.Api/Extensions/ResultExtensions.cs`) maps `ResultFailureType.Conflict → Results.Conflict(...)`, `Validation → BadRequest(errors)`, `NotFound → NotFound(...)`. **Use `Result<T>.Conflict(...)` for the optimistic-concurrency stale-token case** (HTTP 409). Use FluentValidation for invalid input → 400.
- FluentValidation validators are auto-discovered and run through `ValidationBehavior<,>`.
- Register the new endpoint group in `Program.cs` alongside the existing `app.MapXEndpoints()` block.

## Where the code goes

- `src/PBA.Application/Features/BrandRankingProfile/Dtos/BrandRankingProfileDto.cs`
- `src/PBA.Application/Features/BrandRankingProfile/Queries/GetActiveBrandRankingProfile.cs`
- `src/PBA.Application/Features/BrandRankingProfile/Commands/UpdateBrandRankingProfile.cs` (command + handler)
- `src/PBA.Application/Features/BrandRankingProfile/Commands/UpdateBrandRankingProfileValidator.cs`
- `src/PBA.Api/Endpoints/BrandRankingProfileEndpoints.cs`
- Register in `src/PBA.Api/Program.cs` (add `app.MapBrandRankingProfileEndpoints();`).
- Tests under `tests/PBA.Application.Tests/Features/BrandRankingProfile/...` and `tests/PBA.Api.Tests/...` (WebApplicationFactory).

## Tests FIRST

xUnit, EF `UseInMemoryDatabase(Guid)`, `Options.Create()`, `NullLogger<T>.Instance`. Naming `Method_Scenario_ExpectedResult`.

Note on InMemory + `xmin`: EF InMemory does **not** implement the Npgsql `xmin` token. For the concurrency test, force a `DbUpdateConcurrencyException` (test double / save that throws) or cover via Testcontainers (section-12). The handler must catch `DbUpdateConcurrencyException` → `Result<T>.Conflict(...)`; assert that translation.

### `GetActiveBrandRankingProfileHandlerTests`
```
# Test: returns the active profile as BrandRankingProfileDto (pillars ordered by Order, weights, topics, voice markers)
# Test: no active profile present -> Result.NotFound
# Test: DTO carries the concurrency token (xmin / RowVersion) so the client can round-trip it on update
```

### `UpdateBrandRankingProfileHandlerTests` (two server-enforced write modes — R-H4)
```
# Test: weights-only update persists weights/half-life/floor/multipliers, does NOT bump Version, triggers no re-score, IGNORES pillar-definition fields
# Test: pillar-definition update (name/description/add/remove pillar/topics change) bumps Version (via entity RequiresVersionBump logic from section-02)
# Test: after a definition update, in-window ideas with ScoredProfileVersion < new Version become stale (assert Version bumped; the sweep itself is section-07)
# Test: a definition change submitted through the weights-only path is REJECTED/ignored on the server -- Version unchanged, definition fields untouched
# Test: optimistic concurrency -- stale xmin token -> Result.Conflict, no lost update (R-H3)
# Test: missing/unknown profile id (or no active profile) -> Result.NotFound
```

### `UpdateBrandRankingProfileValidatorTests` (FluentValidation)
```
# Test: each pillar weight in [0,1]
# Test: at least one pillar required
# Test: Positioning and AudiencePrimary non-empty
# Test: HalfLifeDays > 0
# Test: DecayFloor in [0,1]
# Test: AntiTopicMultiplier > 0 and AuthorityBoost > 0
```
Decision: do **NOT** validate that weights sum to ~1.0 — read-time renormalization (section-08, R-C2b) makes an exact-sum constraint unnecessary and brittle. Document the choice.

### `BrandRankingProfileEndpointsTests` (WebApplicationFactory)
```
# Test: GET /api/brand-ranking-profile -> 200 + DTO
# Test: PUT /api/brand-ranking-profile (weights-only) -> 200, Version unchanged
# Test: PUT with invalid weights -> 400 validation
# Test: PUT with stale concurrency token -> 409 Conflict
```

## Implementation detail

### DTOs

```csharp
public sealed record BrandRankingProfileDto
{
    public Guid Id { get; init; }
    public int Version { get; init; }
    public string Positioning { get; init; } = "";
    public string AudiencePrimary { get; init; } = "";
    public string? AudienceSecondary { get; init; }
    public double HalfLifeDays { get; init; }
    public double DecayFloor { get; init; }
    public double AntiTopicMultiplier { get; init; }
    public double AuthorityBoost { get; init; }
    public IReadOnlyList<BrandRankingPillarDto> Pillars { get; init; } = [];
    public IReadOnlyList<string> AuthorityTopics { get; init; } = [];
    public IReadOnlyList<string> AntiTopics { get; init; } = [];
    public IReadOnlyList<string> VoiceMarkers { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }
    // Concurrency token round-tripped to PUT. xmin maps to uint; serialize as string to be safe.
    public string ConcurrencyToken { get; init; } = "";
}

public sealed record BrandRankingPillarDto
{
    public Guid Id { get; init; }          // identity (R-C3); survives renames
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public double Weight { get; init; }
    public int Order { get; init; }
    // NOTE: DescriptionEmbedding is NEVER included in the DTO.
}
```

The update request body mirrors the DTO minus server-owned fields, carrying `ConcurrencyToken`. The handler decides the write mode by comparing incoming pillar definitions/topics against the persisted entity — via section-02's versioning semantics — not by trusting a client "mode" flag.

### `GetActiveBrandRankingProfile` query

Loads the single active profile (`IsActive == true`) with `.Include(p => p.Pillars)`, `AsNoTracking()`, pillars ordered by `Order`. Maps to DTO. `Result<BrandRankingProfileDto>.NotFound(...)` if none. Project the `xmin` token into `ConcurrencyToken`.

### `UpdateBrandRankingProfile` command + handler

Handler steps:
1. Load tracked active profile + pillars. None → `Result.NotFound`.
2. Set the loaded entity's concurrency token to the client-supplied one (so EF detects a stale token).
3. ALWAYS apply weights / half-life / floor / multipliers (never bump Version).
4. Ask the entity whether incoming pillar definitions + topics constitute a definition change (consume section-02's `RequiresVersionBump` — do NOT reimplement). If yes: apply definition/topic changes AND bump `Version`. If no: do NOT touch pillar Name/Description/topics even if the payload differs (server-enforced, R-H4).
5. `SaveChangesAsync`; catch `DbUpdateConcurrencyException` → `Result.Conflict(...)`.
6. Re-map to DTO (with new Version + token) and return.

Key binding decisions:
- **R-H4:** mode decided server-side; weights-only path never mutates definitions/topics; definition path bumps Version. One command; handler branches on the entity's versioning logic. No client "force bump" flag.
- **R-H3:** use the `xmin` token from section-02. Set the tracked entity's token to the client's value; `DbUpdateConcurrencyException` → `Result<T>.Conflict(...)` → 409. Never blind-overwrite.
- **R-C3:** pillars matched by `Id`, never name. Rename (same Id) = update; new Id = add; missing Id = remove. This detection feeds the version-bump decision.
- **Re-score trigger:** bumping `Version` is the whole mechanism — section-07's sweep finds `ScoredProfileVersion < active.Version`. This handler does NOT touch `Idea` rows.
- No endpoint auth in v2.

### `UpdateBrandRankingProfileValidator`

Auto-discovered, runs via `ValidationBehavior<,>`. Rules per the validator tests. Failure → `Result.ValidationFailure(errors)` → 400.

### `BrandRankingProfileEndpoints.cs`

```csharp
public static class BrandRankingProfileEndpoints
{
    public static void MapBrandRankingProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/brand-ranking-profile").WithTags("BrandRankingProfile");

        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
            (await sender.Send(new GetActiveBrandRankingProfile.Query(), ct)).ToApiResult());

        group.MapPut("/", async (UpdateBrandRankingProfileRequest body, ISender sender, CancellationToken ct) =>
            (await sender.Send(/* new UpdateBrandRankingProfile.Command(...from body...) */, ct)).ToApiResult());
    }
}
```

Register in `Program.cs`: `app.MapBrandRankingProfileEndpoints();`.

## Acceptance / verification

- `dotnet test` green; 80%+ coverage on the new query/command/validator/endpoints.
- GET returns the active profile with pillars ordered and the concurrency token populated.
- A weights-only PUT leaves `Version` unchanged and does not alter any definition or topic even when the payload differs on those fields.
- A definition-change PUT bumps `Version` exactly once (delegating to section-02) and does not mutate any `Idea` row.
- Stale token → 409; out-of-range weight → 400.
- The route does not collide with the existing voice `BrandProfile`.

## Files

Created:
- `src/PBA.Application/Features/BrandRankingProfile/Dtos/BrandRankingProfileDto.cs`
- `src/PBA.Application/Features/BrandRankingProfile/Queries/GetActiveBrandRankingProfile.cs`
- `src/PBA.Application/Features/BrandRankingProfile/Commands/UpdateBrandRankingProfile.cs`
- `src/PBA.Application/Features/BrandRankingProfile/Commands/UpdateBrandRankingProfileValidator.cs`
- `src/PBA.Api/Endpoints/BrandRankingProfileEndpoints.cs`
- `tests/PBA.Application.Tests/Features/BrandRankingProfile/GetActiveBrandRankingProfileHandlerTests.cs`
- `tests/PBA.Application.Tests/Features/BrandRankingProfile/UpdateBrandRankingProfileHandlerTests.cs`
- `tests/PBA.Application.Tests/Features/BrandRankingProfile/UpdateBrandRankingProfileValidatorTests.cs`
- `tests/PBA.Api.Tests/Endpoints/BrandRankingProfileEndpointsTests.cs`

Modified:
- `src/PBA.Api/Program.cs` (register `MapBrandRankingProfileEndpoints`)
