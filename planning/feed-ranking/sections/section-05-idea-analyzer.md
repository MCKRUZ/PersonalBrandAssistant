# section-05-idea-analyzer — Rewrite IdeaAnalyzer for per-pillar scoring

## Goal

Replace the single-number `IdeaAnalyzer` (current: one 0-10 "content opportunity" score against a hardcoded brand sentence) with a **per-pillar analyzer** that scores an item against an in-memory `BrandRankingProfileSnapshot`. Output is raw per-pillar sub-scores on a `{0, .25, .5, .75, 1}` scale plus anti-topic / authority flags. These raw sub-scores are stored once; pillar weights are applied later at read time (section-08), so re-weighting never re-runs the LLM.

This section delivers ONLY the analyzer contract + implementation + its unit tests. The scoring sweep that calls it (candidate selection, throttle, persistence) is section-07. The embedding service is section-06. Both consume this section's contract.

## Dependencies (already built — reference only, do not re-create)

- **section-02-brand-profile-domain** — provides the `BrandRankingProfile` + `BrandPillar` aggregate in `PBA.Domain`. Each `BrandPillar` has `Id (Guid)`, `Name`, `Description`, `Weight`, `Order`. Profile has `Positioning`, `AudiencePrimary`, `AudienceSecondary?`, `AuthorityTopics`, `AntiTopics`, `VoiceMarkers`, and `Version (int)`. **(The plan calls this `BrandProfile`; section-02 renamed it `BrandRankingProfile` to avoid colliding with the existing voice `BrandProfile`. Use the ranking name.)**
- **section-03-idea-entity-changes** — provides `PillarSubScore` storage on `Idea` (jsonb), keyed by **`BrandPillarId`** (R-C3). This section produces sub-scores keyed by pillar **name** from the LLM and is responsible for mapping name → `BrandPillarId` using the snapshot before returning.

You do NOT modify those entities here. You only consume the snapshot shape they imply.

## Existing code being replaced

- `src/PBA.Application/Common/Interfaces/IIdeaAnalyzer.cs` — old single-score contract.
- `src/PBA.Application/Common/Models/IdeaAnalysis.cs` — old `IdeaAnalysis(int Score, string Reason, string Summary, string? Category, IReadOnlyList<string> Tags)`.
- `src/PBA.Infrastructure/Services/Radar/IdeaAnalyzer.cs` — old prompt + single-int parse.
- `tests/PBA.Infrastructure.Tests/Services/Radar/IdeaAnalyzerTests.cs` — old tests (all five must be replaced; the old `IdeaAnalysis.Score` shape is gone).

**Replace, don't deprecate.** The old `IdeaAnalysis` record and `IIdeaAnalyzer` signature are deleted, not kept alongside. Callers of the old contract live in `IdeaScoringService` (rewired in section-07) — leaving the old contract would break the "one number" assumption this whole redesign removes.

`ISidecarClient.SendPromptAsync(systemPrompt, userPrompt, model?, ct)` is unchanged and still the LLM entry point. The current `OpenRouterClient` routes to OpenRouter; `model` is honored. There is no separate "structured output" method on `ISidecarClient` — structured-output is achieved by instructing the model in the prompt and parsing JSON from the returned string. Keep using `SendPromptAsync`.

## New contract

Define the new contract. Put the records in `PBA.Application` (interfaces stay in Application per Clean Architecture; the analyzer impl stays in Infrastructure).

`src/PBA.Application/Common/Interfaces/IIdeaAnalyzer.cs`:

```csharp
public interface IIdeaAnalyzer
{
    /// <summary>Score an item against the active profile snapshot. Returns per-pillar sub-scores (0..1),
    /// anti-topic / authority flags, and a one-line reason per pillar. Null on LLM/parse/guard failure.</summary>
    Task<IdeaAnalysis?> AnalyzeAsync(
        IdeaAnalysisInput input, BrandRankingProfileSnapshot profile, CancellationToken ct = default);
}
```

`src/PBA.Application/Common/Models/IdeaAnalysis.cs` (replace contents):

```csharp
public record IdeaAnalysisInput(string Title, string? Description, string? Url, string SourceName);

/// <summary>One pillar's sub-score. PillarId is resolved from the snapshot (R-C3);
/// PillarName is the LLM-returned display name. Score is on the {0,.25,.5,.75,1} scale.</summary>
public record PillarScore(Guid PillarId, string PillarName, double Score, string? Reason);

public record IdeaAnalysis(
    IReadOnlyList<PillarScore> Pillars,
    bool IsAntiTopic,
    bool IsAuthorityTopic,
    string Reason);
```

`BrandRankingProfileSnapshot` — immutable in-memory projection of the active profile, defined in `PBA.Application` (e.g. `src/PBA.Application/Common/Models/BrandRankingProfileSnapshot.cs`). It is the only profile shape the analyzer touches; it carries `Version` so the sweep (section-07) can stamp `ScoredProfileVersion` from the snapshot, not from "current" (R-H3). Minimum shape this section needs:

```csharp
public record BrandPillarSnapshot(Guid Id, string Name, string Description, double Weight, int Order);

public record BrandRankingProfileSnapshot(
    int Version,
    string Positioning,
    string AudiencePrimary,
    string? AudienceSecondary,
    IReadOnlyList<BrandPillarSnapshot> Pillars,
    IReadOnlyList<string> AuthorityTopics,
    IReadOnlyList<string> AntiTopics,
    IReadOnlyList<string> VoiceMarkers);
```

If section-06/07 author this record first, reuse theirs — do not create a duplicate. Coordinate via the file path above. It is shared infrastructure across 05/06/07.

## Tests FIRST

Rewrite `tests/PBA.Infrastructure.Tests/Services/Radar/IdeaAnalyzerTests.cs`. Mock `ISidecarClient.SendPromptAsync` with canned JSON. Build a small fixed `BrandRankingProfileSnapshot` test fixture (2-3 pillars with known `Id`/`Name`, plus authority/anti topics) and reuse it across tests.

The canned LLM JSON the mock returns must match the structured shape your `BuildSystemPrompt` asks for. Pick the shape now and keep tests and prompt in lockstep. Recommended response shape (pillars keyed by name; analyzer maps name→id):

```json
{
  "pillars": [
    {"name": "Enterprise AI Adoption", "score": 0.75, "reason": "ownable angle"},
    {"name": "Agentic Development", "score": 0.25, "reason": "tangential"}
  ],
  "isAntiTopic": false,
  "isAuthorityTopic": true,
  "reason": "strong fit for the adoption pillar"
}
```

Tests to write (from TDD plan §7 — write the assertions, these are stubs):

```
# Test: parses structured JSON into per-pillar sub-scores (0..1 on the {0,.25,.5,.75,1} scale)
# Test: maps LLM-returned pillar NAMES → BrandPillarId via the snapshot; unknown name is dropped/logged (R-C3)
# Test: extracts IsAntiTopic / IsAuthorityTopic flags and per-pillar one-line reason
# Test: system prompt is built FROM the snapshot (positioning, audience, pillar names+descriptions,
#        authority/anti topics, per-level rubric) — assert key profile strings appear in the prompt
# Test: includes the fixed few-shot anchor exemplars (same count/order every call)
# Test: LLM/parse failure → returns null (does not throw)
# Test: central-collapse guard — analysis with all-identical pillar scores is rejected + logged (R-M5)
# Test: low temperature passed to the structured call (0–0.2)
```

Test-specific notes:

- **Name→id mapping (R-C3):** assert each returned `PillarScore.PillarId` equals the snapshot pillar with the matching `Name`. Feed one LLM pillar whose name is NOT in the snapshot and assert it is dropped (not present in `result.Pillars`) and a warning is logged. Name matching should be case-insensitive and trimmed (LLMs drift on casing/whitespace).
- **Prompt-from-snapshot:** the mock captures the `systemPrompt` argument via `Callback`/`Verify` with a captured string. Assert the snapshot's `Positioning`, `AudiencePrimary`, each pillar `Name`+`Description`, the authority topics, the anti topics, and the per-level rubric wording all appear in the captured system prompt.
- **Few-shot anchors:** assert a stable marker for each exemplar appears in the prompt, and that the count/order is identical across two calls with different inputs. Exemplars are hardcoded constants (NOT derived from the snapshot), so the scale calibrates consistently.
- **Failure → null:** drive with `"not json at all"` and with a JSON object missing the `pillars` array; both return null, no throw. Preserve the existing fenced-JSON tolerance — the old analyzer stripped ```` ``` ```` fences; keep that `StripFences` behavior since the model still occasionally wraps output.
- **Central-collapse guard (R-M5):** return JSON where every pillar `score` is identical (e.g. all `0.5`). Assert the result is `null` (rejected) and a warning is logged. Rationale: an all-identical sub-score vector is the LLM punting / refusing to discriminate, which would pollute brandFit. Only guard when there are ≥2 pillars (a single-pillar profile cannot "collapse").
- **Low temperature:** `ISidecarClient.SendPromptAsync` has no temperature parameter today. Two acceptable paths — pick one and note it in the section's commit:
  1. The simplest honest approach: instruct determinism/low variance in the prompt text and drop the literal "temperature passed" assertion, OR
  2. If section-04/06 extends `ISidecarClient` or `OpenRouterClient` with a temperature/options overload, use it and assert the value is in `[0, 0.2]`.
  Do NOT fabricate a temperature parameter that the sidecar ignores. If no overload exists, the temperature test asserts the prompt instructs low-variance scoring instead. **Flag this to the user if you take path 1** — it is a real reduction in determinism control versus the plan's intent.

## Implementation

`src/PBA.Infrastructure/Services/Radar/IdeaAnalyzer.cs` — rewrite. Keep it under 150 lines; extract prompt-building helpers if needed.

Constructor: inject `ISidecarClient`, `IOptions<IdeaScoringOptions>` (for `Model`), and `ILogger<IdeaAnalyzer>` (matches the existing DI shape — no DI registration change needed since the class name and lifetime are unchanged).

`AnalyzeAsync` flow:

1. Build system prompt from `profile` (see below) + fixed few-shot anchors.
2. Build user prompt from `input` (title, source, url, truncated description — reuse the existing `BuildUserPrompt` truncation at ~1000 chars).
3. `await sidecar.SendPromptAsync(system, user, _options.Model, ct)`.
4. `StripFences` (carry over from current impl) then `JsonSerializer.Deserialize` into a private `Raw` record with `pillars[]`, `isAntiTopic`, `isAuthorityTopic`, `reason`. On `JsonException` or missing `pillars` → log warning, return null.
5. For each raw pillar: trim+case-insensitive match its `name` against `profile.Pillars[].Name`. No match → log and skip (R-C3). Clamp each `score` to `[0, 1]`. Build `PillarScore(matchedPillar.Id, matchedPillar.Name, clampedScore, reason)`.
6. **Central-collapse guard (R-M5):** if `result.Pillars.Count >= 2` and all `Score` values are equal (within a tiny epsilon), log warning, return null.
7. If after mapping zero pillars survived → return null (nothing usable).
8. Return `IdeaAnalysis(mappedPillars, isAntiTopic, isAuthorityTopic, reason ?? "")`.

System prompt construction (per §7 of the plan):

- State the role using `profile.Positioning` and `profile.AudiencePrimary` (+ secondary if present) — built from the snapshot, NOT hardcoded.
- List each pillar: `Name` + `Description` — the model scores how strong a content opportunity the item is for **each** pillar independently.
- Provide the **per-level rubric** for the `{0, .25, .5, .75, 1}` scale, e.g.: `1.0` = a strong ownable thought-leadership angle for this pillar; `0.75` = clearly relevant, postable; `0.5` = tangential, needs an angle; `0.25` = weak fit; `0` = off-topic for this pillar.
- List `AuthorityTopics` (boost candidates) and `AntiTopics` (suppress candidates); instruct the model to set `isAuthorityTopic` / `isAntiTopic` accordingly. Anti-topic detection here only sets the flag — the ×0.1 multiplier is applied at read time (section-08), not here.
- Include 2-4 **fixed** few-shot anchor exemplars (hardcoded `const` strings, identical every call, fixed order) showing an item → expected per-pillar JSON, to calibrate the scale. These are static and brand-agnostic enough to stay constant.
- Instruct: respond with ONLY a JSON object matching the exact shape, no markdown fences (but tolerate them on parse).

User prompt: title, source, url, truncated description (carry over existing `BuildUserPrompt`).

`Raw` private record (camelCase JSON, reuse the existing `JsonOptions` with `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`):

```csharp
private sealed record Raw(
    List<RawPillar>? Pillars,
    bool IsAntiTopic,
    bool IsAuthorityTopic,
    string? Reason);

private sealed record RawPillar(string? Name, double Score, string? Reason);
```

## Verification

- `dotnet build` clean.
- `dotnet test --filter IdeaAnalyzerTests` green.
- Confirm no remaining references to the OLD `IdeaAnalysis(int Score, ...)` shape compile — section-07 (`IdeaScoringService`) will be the consumer; if it currently references the old contract it will break the build, which is expected and resolved in section-07. Within THIS section's scope, only the analyzer + its tests must compile and pass; if the wider solution won't build because section-07 is not yet done, that is the documented cross-section dependency (05 blocks 07).
- 80% coverage on the new analyzer code.

## Key decisions / gotchas

- **R-C3 is the load-bearing rule:** the LLM never sees or returns ids; it returns names. Identity is the `BrandPillarId` resolved from the snapshot. Renaming a pillar later must not break stored sub-scores — that is why the sweep stores by id. The analyzer is where name→id resolution happens.
- **No new "structured output" sidecar method** exists — parse JSON from the string response. Keep fence stripping.
- **Temperature control** may require a sidecar overload that does not exist today. Do not pretend it does. Pick path 1 or 2 above explicitly and flag path 1 to the user.
- The `IdeaScoringOptions.Model` is reused for the cheap scoring model. Do not introduce a new options class for this — section-01 owns the new `EmbeddingOptions`/`RankingOptions`; the analyzer's model continues to come from `IdeaScoringOptions`.

## Relevant file paths

- `src/PBA.Application/Common/Interfaces/IIdeaAnalyzer.cs` (replace)
- `src/PBA.Application/Common/Models/IdeaAnalysis.cs` (replace — new records)
- `src/PBA.Application/Common/Models/BrandRankingProfileSnapshot.cs` (create, or reuse if 06/07 created it)
- `src/PBA.Infrastructure/Services/Radar/IdeaAnalyzer.cs` (rewrite)
- `tests/PBA.Infrastructure.Tests/Services/Radar/IdeaAnalyzerTests.cs` (rewrite)

## As built (2026-06-16)

Implemented as part of the **05+06+07 build-coupled unit** (the contract change breaks the old
`IdeaScoringService` until 07 lands; all three committed together once the build was green).

- **Temperature: path 2 chosen** (user decision). Added a `SendPromptAsync(system, user, model?,
  temperature?, ct)` overload to `ISidecarClient` (separate overload, not an inserted param, so the
  existing 4-arg positional callers keep binding `ct`). `OpenRouterClient` sends `"temperature"` in the
  chat payload (omitted when null via `WhenWritingNull`, so drafting calls are byte-for-byte unchanged);
  the CLI `SidecarClient` ignores it like it ignores `model`. Analyzer passes `ScoringTemperature = 0.1`;
  the test asserts the passed value is in `[0, 0.2]`.
- **`BrandRankingProfileSnapshot`** was authored here as the **full shared shape** for 05/06/07/08 (carries
  pillar `DescriptionEmbedding`, weights, half-life, floor, multipliers — not just the analyzer's minimum)
  with a `FromProfile(BrandRankingProfile)` factory, so 06/07/08 reuse one record.
- Central-collapse guard uses an absolute epsilon (`1e-9`) and only fires with ≥2 pillars. Unknown pillar
  names and zero-survivor results return null and log a warning.
- Files: `ISidecarClient.cs`, `OpenRouterClient.cs`, `SidecarClient.cs`, `IIdeaAnalyzer.cs`,
  `IdeaAnalysis.cs`, `BrandRankingProfileSnapshot.cs` (new), `IdeaAnalyzer.cs`, `IdeaAnalyzerTests.cs`
  (11 tests).
