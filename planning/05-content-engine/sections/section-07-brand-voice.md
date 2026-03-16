Now I have all the context needed. Let me produce the section content.

# Section 07 — Brand Voice Validation

## Overview

This section implements the Brand Voice system: `IBrandVoiceService` interface, `BrandVoiceService` implementation with three-layer validation (prompt injection, rule-based checks with HTML stripping, LLM-as-judge with structured JSON output), the `BrandVoiceScore` model, and autonomy-driven gating logic.

**Dependencies:**
- **Section 01 (Domain Entities):** `BrandProfile` entity already exists with `VocabularyConfig`, `ToneDescriptors`, `ExampleContent`, etc.
- **Section 03 (Agent Refactoring):** `ISidecarClient` must be available for LLM-as-judge scoring via the sidecar.
- **Section 04 (Content Pipeline):** `IContentPipeline.GenerateDraftAsync` is called during auto-regeneration in autonomous gating mode. This is a runtime dependency only; brand voice can be implemented and tested with a mock `IContentPipeline`.

**Blocks:** Section 11 (API Endpoints) depends on this section for the brand voice endpoints.

---

## Existing Codebase Context

The following types already exist and are relevant to this section:

**`BrandProfile`** (`src/PersonalBrandAssistant.Domain/Entities/BrandProfile.cs`): Entity with `ToneDescriptors`, `StyleGuidelines`, `VocabularyPreferences` (a `VocabularyConfig`), `Topics`, `PersonaDescription`, `ExampleContent`, `IsActive`.

**`VocabularyConfig`** (`src/PersonalBrandAssistant.Domain/ValueObjects/VocabularyConfig.cs`): Value object with `PreferredTerms` (List of string) and `AvoidTerms` (List of string). Note: the property is named `AvoidTerms`, not `AvoidedTerms`.

**`BrandProfilePromptModel`** (`src/PersonalBrandAssistant.Application/Common/Models/BrandProfilePromptModel.cs`): Record used for prompt injection (Layer 1). Properties: `Name`, `PersonaDescription`, `ToneDescriptors`, `StyleGuidelines`, `PreferredTerms`, `AvoidedTerms`, `Topics`, `ExampleContent`.

**`AutonomyLevel`** (`src/PersonalBrandAssistant.Domain/Enums/AutonomyLevel.cs`): Enum with values `Manual`, `Assisted`, `SemiAuto`, `Autonomous`.

**`Content`** (`src/PersonalBrandAssistant.Domain/Entities/Content.cs`): Entity with `Body`, `Metadata` (a `ContentMetadata`), `ContentType`, `Status`, etc.

**`ContentMetadata`** (`src/PersonalBrandAssistant.Domain/ValueObjects/ContentMetadata.cs`): Value object with `AiGenerationContext`, `PlatformSpecificData`, `Tags`, `SeoKeywords`, `TokensUsed`, `EstimatedCost`.

**`Result<T>`** (`src/PersonalBrandAssistant.Application/Common/Models/Result.cs`): Standard result type with `Success(T)`, `Failure(ErrorCode, ...)`, `NotFound(string)`, `ValidationFailure(IEnumerable<string>)`.

**`IApplicationDbContext`** (`src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs`): DB context interface with `DbSet<Content> Contents` and `DbSet<BrandProfile> BrandProfiles`.

---

## Tests

All tests go in a new file. The test class mocks `ISidecarClient`, `IApplicationDbContext`, and `IContentPipeline`.

**File:** `tests/PersonalBrandAssistant.Application.Tests/Features/BrandVoice/BrandVoiceServiceTests.cs`

### Rule-Based Check Tests (Layer 2)

```csharp
/// Test: RunRuleChecks strips HTML before checking
/// Setup: text = "<p>This is <b>bold</b> text</p>", profile with AvoidTerms = ["bold"]
/// Assert: "bold" is NOT flagged — it appears only in HTML tags context. After HTML strip the plain text is "This is bold text", so "bold" IS flagged.
/// Clarification: HTML tags are stripped, but the text content remains. "bold" in the body text IS matched.
/// Expected: result.Value contains violation for "bold"
```

```csharp
/// Test: RunRuleChecks detects avoided terms
/// Setup: text = "We leverage synergy to maximize impact", profile.VocabularyPreferences.AvoidTerms = ["leverage", "synergy"]
/// Assert: result.Value contains 2 violations (one for "leverage", one for "synergy")
```

```csharp
/// Test: RunRuleChecks warns when no preferred terms present
/// Setup: text = "A generic post about nothing specific", profile.VocabularyPreferences.PreferredTerms = ["AI", "automation", "branding"]
/// Assert: result.Value contains a warning about no preferred terms found
```

```csharp
/// Test: RunRuleChecks returns empty list for compliant content
/// Setup: text includes preferred terms, no avoided terms, matches tone
/// Assert: result.Value is empty list
```

```csharp
/// Test: RunRuleChecks is case-insensitive for term matching
/// Setup: text = "We LEVERAGE this", AvoidTerms = ["leverage"]
/// Assert: violation detected
```

### LLM Scoring Tests (Layer 3)

```csharp
/// Test: ScoreContentAsync sends scoring prompt to sidecar
/// Setup: Mock ISidecarClient.SendTaskAsync to return ChatEvent stream with valid JSON score
/// Assert: ISidecarClient.SendTaskAsync was called with prompt containing content body and brand profile
```

```csharp
/// Test: ScoreContentAsync parses JSON response into BrandVoiceScore dimensions
/// Setup: Sidecar returns JSON: {"overallScore":85,"toneAlignment":90,"vocabularyConsistency":80,"personaFidelity":85,"issues":[]}
/// Assert: result.Value.OverallScore == 85, ToneAlignment == 90, etc.
```

```csharp
/// Test: ScoreContentAsync handles invalid JSON from LLM gracefully
/// Setup: Sidecar returns "I think the score is about 7 out of 10" (not JSON)
/// Assert: result.IsSuccess == false, result.ErrorCode == ErrorCode.ValidationFailed
```

```csharp
/// Test: ScoreContentAsync returns NotFound when content does not exist
/// Setup: Mock DbContext returns null for content query
/// Assert: result.IsSuccess == false, result.ErrorCode == ErrorCode.NotFound
```

```csharp
/// Test: ScoreContentAsync stores score in ContentMetadata
/// Setup: Valid content, sidecar returns valid JSON score
/// Assert: content.Metadata.PlatformSpecificData contains serialized BrandVoiceScore
```

### Gating Logic Tests

```csharp
/// Test: ValidateAndGateAsync at Autonomous auto-regenerates below threshold
/// Setup: autonomy = Autonomous, sidecar returns score 50 (below default threshold 70)
/// Mock IContentPipeline.GenerateDraftAsync, then sidecar returns score 80 on second call
/// Assert: IContentPipeline.GenerateDraftAsync called once, final result is success
```

```csharp
/// Test: ValidateAndGateAsync at Autonomous fails after 3 regen attempts
/// Setup: autonomy = Autonomous, sidecar always returns score 40
/// Assert: result.IsSuccess == false, result.ErrorCode == ErrorCode.ValidationFailed
/// Assert: IContentPipeline.GenerateDraftAsync called exactly 3 times
```

```csharp
/// Test: ValidateAndGateAsync at SemiAuto returns advisory score, no blocking
/// Setup: autonomy = SemiAuto, sidecar returns score 30
/// Assert: result.IsSuccess == true (score is advisory, not blocking)
/// Assert: IContentPipeline.GenerateDraftAsync never called
```

```csharp
/// Test: ValidateAndGateAsync at Manual returns advisory score, no blocking
/// Setup: autonomy = Manual, sidecar returns score 20
/// Assert: result.IsSuccess == true
/// Assert: IContentPipeline.GenerateDraftAsync never called
```

---

## Implementation

### File 1: IBrandVoiceService Interface

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IBrandVoiceService.cs`

```csharp
public interface IBrandVoiceService
{
    /// <summary>
    /// Score content against the active brand profile using three-layer validation.
    /// Layer 1 (prompt injection) is handled upstream during generation.
    /// Layer 2 (rule checks) and Layer 3 (LLM-as-judge) run here.
    /// </summary>
    Task<Result<BrandVoiceScore>> ScoreContentAsync(Guid contentId, CancellationToken ct);

    /// <summary>
    /// Score content and apply gating based on autonomy level.
    /// Autonomous: auto-regenerate up to 3 times if below threshold, then fail.
    /// SemiAuto/Manual: return score as advisory, never block.
    /// </summary>
    Task<Result<Unit>> ValidateAndGateAsync(Guid contentId, AutonomyLevel autonomy, CancellationToken ct);

    /// <summary>
    /// Synchronous rule-based checks (Layer 2). No AI involved.
    /// Strips HTML, checks avoided terms, checks for preferred term presence.
    /// </summary>
    Result<IReadOnlyList<string>> RunRuleChecks(string text, BrandProfile profile);
}
```

### File 2: BrandVoiceScore Model

**File:** `src/PersonalBrandAssistant.Application/Common/Models/BrandVoiceScore.cs`

```csharp
/// <summary>
/// Result of brand voice validation. OverallScore 0-100.
/// ToneAlignment, VocabularyConsistency, PersonaFidelity each 0-100.
/// Issues: LLM-identified concerns. RuleViolations: from Layer 2 rule checks.
/// </summary>
public record BrandVoiceScore(
    int OverallScore,
    int ToneAlignment,
    int VocabularyConsistency,
    int PersonaFidelity,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> RuleViolations);
```

### File 3: BrandVoiceService Implementation

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/BrandVoiceService.cs`

This is the core implementation. Key design decisions:

**Constructor dependencies:**
- `IApplicationDbContext` -- to load `Content` and active `BrandProfile`
- `ISidecarClient` -- for LLM-as-judge scoring (Layer 3)
- `IContentPipeline` -- for auto-regeneration in autonomous gating mode
- `IOptions<ContentEngineOptions>` -- for `BrandVoiceScoreThreshold` and `MaxAutoRegenerateAttempts`
- `ILogger<BrandVoiceService>`

**`RunRuleChecks(string text, BrandProfile profile)` implementation:**

1. Strip HTML tags using a regex pattern (`<[^>]+>`) and decode HTML entities. This produces plain text for checking.
2. Normalize text to lowercase for case-insensitive comparison.
3. Check each term in `profile.VocabularyPreferences.AvoidTerms` -- if found in normalized text, add a violation string like `"Avoided term detected: '{term}'"`.
4. Check if at least one term from `profile.VocabularyPreferences.PreferredTerms` is present -- if none found, add a warning: `"No preferred brand terms found. Consider including: {comma-separated terms}"`.
5. Return `Result<IReadOnlyList<string>>.Success(violations)` (empty list means fully compliant).

**`ScoreContentAsync(Guid contentId, CancellationToken ct)` implementation:**

1. Load `Content` by ID from DB. Return `NotFound` if missing.
2. Load the active `BrandProfile` (where `IsActive == true`). Return `Failure` if no active profile exists.
3. Run `RunRuleChecks(content.Body, profile)` to get Layer 2 violations.
4. Build a scoring prompt for Layer 3 that includes:
   - The content body
   - The brand profile (tone descriptors, style guidelines, persona description, vocabulary config)
   - Explicit instruction to return JSON matching the `BrandVoiceScore` shape
   - Instruction to score each dimension 0-100
5. Send the prompt to `ISidecarClient.SendTaskAsync`. Collect all `ChatEvent` text fragments into a single response string.
6. Parse the JSON response. Use `System.Text.Json.JsonSerializer.Deserialize` with a DTO that maps to `BrandVoiceScore`. If parsing fails, return `Result.Failure(ErrorCode.ValidationFailed, "Failed to parse brand voice score from LLM response")`.
7. Merge Layer 2 `RuleViolations` into the score.
8. Store the score in `content.Metadata.PlatformSpecificData["BrandVoiceScore"]` as serialized JSON.
9. Save changes and return the score.

**`ValidateAndGateAsync(Guid contentId, AutonomyLevel autonomy, CancellationToken ct)` implementation:**

1. Call `ScoreContentAsync(contentId, ct)`. If it fails, propagate the error.
2. If `autonomy` is `Autonomous`:
   - Read threshold from `ContentEngineOptions.BrandVoiceScoreThreshold` (default 70).
   - Read max attempts from `ContentEngineOptions.MaxAutoRegenerateAttempts` (default 3).
   - If `score.OverallScore < threshold`, loop up to max attempts:
     - Call `IContentPipeline.GenerateDraftAsync(contentId, ct)` to regenerate.
     - Call `ScoreContentAsync(contentId, ct)` again.
     - If score now meets threshold, break and return success.
   - If still below threshold after all attempts, return `Result.Failure(ErrorCode.ValidationFailed, ...)`.
3. If `autonomy` is `SemiAuto`, `Assisted`, or `Manual`:
   - Score is advisory only. Return `Result.Success(Unit.Value)` regardless of score.
   - The score is already persisted in `ContentMetadata` from the `ScoreContentAsync` call.

### File 4: ContentEngineOptions

**File:** `src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs`

This options class may be shared with other sections (e.g., section 04, section 09). If it already exists when this section is implemented, add the brand voice properties to it. If not, create it.

```csharp
public class ContentEngineOptions
{
    public const string SectionName = "ContentEngine";

    /// <summary>Minimum brand voice score (0-100) for autonomous auto-approval. Default 70.</summary>
    public int BrandVoiceScoreThreshold { get; set; } = 70;

    /// <summary>Max regeneration attempts in autonomous mode before failing. Default 3.</summary>
    public int MaxAutoRegenerateAttempts { get; set; } = 3;

    // Other content engine options from other sections will coexist here
    public int EngagementRetentionDays { get; set; } = 30;
    public int EngagementAggregationIntervalHours { get; set; } = 4;
}
```

---

## Scoring Prompt Design

The LLM-as-judge prompt sent to the sidecar should follow this structure (not prescribing exact wording, but specifying the required elements):

1. **System instruction:** "You are a brand voice evaluator. Score the following content against the provided brand profile. Return ONLY valid JSON, no markdown fencing, no explanation."
2. **Brand profile context:** Include tone descriptors, persona description, style guidelines, preferred terms, avoided terms.
3. **Content to evaluate:** The full content body (HTML-stripped for the prompt).
4. **Expected JSON schema:**
   ```json
   {
     "overallScore": 0,
     "toneAlignment": 0,
     "vocabularyConsistency": 0,
     "personaFidelity": 0,
     "issues": []
   }
   ```
5. **Scoring guidance:** Each dimension is 0-100. `issues` is an array of strings describing specific concerns.

The JSON parsing should use a private DTO class (`BrandVoiceScoreDto`) with `System.Text.Json` attributes (`JsonPropertyName`) matching the camelCase JSON keys, then map to the public `BrandVoiceScore` record. Use `JsonSerializerOptions { PropertyNameCaseInsensitive = true }` for resilience.

If the LLM wraps the JSON in markdown code fences (common behavior), strip them before parsing. Check for and remove leading/trailing triple-backtick patterns.

---

## HTML Stripping Implementation Note

For Layer 2 rule checks, HTML stripping should:
1. Remove all tags via regex `<[^>]+>`
2. Decode common HTML entities (`&amp;` to `&`, `&lt;` to `<`, `&gt;` to `>`, `&quot;` to `"`, `&#39;` to `'`)
3. Use `System.Net.WebUtility.HtmlDecode` for comprehensive entity decoding
4. Collapse multiple whitespace into single spaces and trim

This is a private helper method within `BrandVoiceService`, not a separate utility class.

---

## Configuration

The brand voice threshold and max regen attempts are configured via `appsettings.json`:

```json
{
  "ContentEngine": {
    "BrandVoiceScoreThreshold": 70,
    "MaxAutoRegenerateAttempts": 3
  }
}
```

Bound via `services.Configure<ContentEngineOptions>(configuration.GetSection(ContentEngineOptions.SectionName))` in DI (handled by section 12).

---

## Implementation Status: COMPLETE

### Files Created
- `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/BrandVoiceService.cs` — Full 3-layer implementation (270 lines)
- `tests/PersonalBrandAssistant.Application.Tests/Features/BrandVoice/BrandVoiceServiceTests.cs` — 14 tests (namespace: Services.BrandVoice to avoid Content namespace collision)

### Files Modified
- `src/PersonalBrandAssistant.Application/Common/Interfaces/IBrandVoiceService.cs` — Added `ValidateAndGateAsync` and `RunRuleChecks` methods
- `src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs` — Added `BrandVoiceScoreThreshold` and `MaxAutoRegenerateAttempts`
- `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/StubBrandVoiceService.cs` — Updated to implement new interface methods
- `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` — Changed from StubBrandVoiceService to BrandVoiceService

### Deviations from Plan
1. **IServiceProvider instead of IContentPipeline**: Used service locator pattern to break circular dependency (BrandVoiceService -> IContentPipeline -> IBrandVoiceService). Documented with comment.
2. **No BrandProfilePromptModel usage**: Prompt built directly from BrandProfile entity fields rather than through the PromptModel.
3. **Test namespace**: Used `Services.BrandVoice` instead of `Features.BrandVoice` to avoid C# namespace collision with existing `Features.Content` namespace.

### Code Review Fixes Applied
- **H1**: Check `GenerateDraftAsync` result and propagate failure
- **H2**: Validate LLM score dimensions are 0-100 range
- **M2**: Comment documenting service locator reason
- **M3**: `[GeneratedRegex]` source generator for compiled regex patterns
- **M4**: Word boundary regex (`\b`) for term matching instead of substring Contains
- **M5**: `ArgumentNullException.ThrowIfNull` on public method parameters

### Test Count: 14 tests (all passing, 726 total across solution)