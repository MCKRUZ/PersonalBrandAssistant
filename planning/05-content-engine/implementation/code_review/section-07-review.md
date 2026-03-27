# Section 07: Brand Voice Validation -- Code Review

**Verdict:** Warning

**Files reviewed:**
- src/PersonalBrandAssistant.Application/Common/Interfaces/IBrandVoiceService.cs
- src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs
- src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/BrandVoiceService.cs
- src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/StubBrandVoiceService.cs
- src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
- tests/PersonalBrandAssistant.Application.Tests/Features/BrandVoice/BrandVoiceServiceTests.cs

---

## CRITICAL Issues (0)

No critical issues found. No hardcoded secrets, no SQL injection risks, no authentication bypasses.

---

## HIGH Issues (2)

### H1: GenerateDraftAsync result is silently discarded in regeneration loop

**File:** BrandVoiceService.cs:136

The return value of pipeline.GenerateDraftAsync(contentId, ct) is Result of string, but the result is never checked. If draft generation fails (returns a failure result), the code proceeds to re-score the old content, wasting an LLM call and potentially returning a misleading "still below threshold" error instead of surfacing the real problem.

    // Current (line 136):
    await pipeline.GenerateDraftAsync(contentId, ct);

    // Fix:
    var draftResult = await pipeline.GenerateDraftAsync(contentId, ct);
    if (!draftResult.IsSuccess)
        return Result<MediatR.Unit>.Failure(draftResult.ErrorCode, draftResult.Errors.ToArray());

### H2: No score range validation on LLM-returned values

**File:** BrandVoiceService.cs:97-103

The LLM can return any integer for OverallScore, ToneAlignment, etc. Values like -50 or 999 would be accepted and stored, potentially breaking downstream comparisons against the 0-100 threshold. The DTO deserialization applies no constraints.

    // Fix: Add validation after parsing the DTO, before constructing the score
    if (dto.OverallScore is < 0 or > 100 ||
        dto.ToneAlignment is < 0 or > 100 ||
        dto.VocabularyConsistency is < 0 or > 100 ||
        dto.PersonaFidelity is < 0 or > 100)
    {
        return Result<BrandVoiceScore>.Failure(ErrorCode.ValidationFailed,
            "LLM returned score values outside valid 0-100 range");
    }

---

## MEDIUM Issues (6)

### M1: Prompt injection via user-controlled content body

**File:** BrandVoiceService.cs:150-175

The BuildScoringPrompt method interpolates plainText (the content body) and brand profile fields directly into the prompt. A content body containing adversarial text could manipulate the LLM score. This is an inherent LLM risk, but the prompt should at minimum use clear XML-style delimiters (wrapping user content in content tags) to reduce the attack surface.

### M2: Service locator pattern via IServiceProvider for IContentPipeline

**File:** BrandVoiceService.cs:77,128

The constructor takes IServiceProvider instead of IContentPipeline directly, then resolves IContentPipeline at runtime via GetRequiredService. This is the service locator anti-pattern. It hides the dependency, makes testing less explicit, and would throw at runtime rather than at DI container validation time if the service is missing.

If this is intentional to break a circular dependency (both services are scoped and reference each other), it is acceptable but should be documented with a comment explaining why.

### M3: Regex objects are not compiled or cached in StripHtml

**File:** BrandVoiceService.cs:229-235

StripHtml calls Regex.Replace twice with string patterns. In the regeneration loop, this method is called multiple times (once per RunRuleChecks + once per BuildScoringPrompt per attempt). These regex patterns should be compiled and cached as static fields for better performance.

### M4: Substring matching for avoided/preferred terms produces false positives

**File:** BrandVoiceService.cs:50-56

normalized.Contains(term.ToLowerInvariant()) matches substrings. An avoided term "age" would flag "manage" or "coverage". Preferred term "AI" would match "fail" or "certain". Word boundary matching (using Regex with \b anchors) would be more accurate.

### M5: No null guard on text parameter in RunRuleChecks

**File:** BrandVoiceService.cs:44

RunRuleChecks is a public interface method. If text is null, StripHtml will throw a NullReferenceException from Regex.Replace. Add ArgumentNullException.ThrowIfNull for both parameters.

### M6: Missing test coverage for error paths

**File:** BrandVoiceServiceTests.cs

Several important paths are untested:
- Sidecar returns an ErrorEvent during scoring (ConsumeEventStreamAsync ErrorEvent branch, line 243)
- No active brand profile found
- Exception thrown by sidecar (catch path, line 249)
- Assisted autonomy level (enum has 4 values, only 3 tested)
- GenerateDraftAsync returning failure during regeneration (related to H1)

---

## LOW Issues (4)

### L1: MediatR.Unit fully qualified in interface and implementation

**File:** IBrandVoiceService.cs:8

MediatR.Unit is fully qualified in the method signature. Add a using MediatR directive and use Unit directly for cleaner signatures.

### L2: Double HTML stripping in ScoreContentAsync

**File:** BrandVoiceService.cs:84,86

RunRuleChecks calls StripHtml(text) internally, and then BuildScoringPrompt also calls StripHtml(contentBody). The same content body is stripped twice. Minor performance waste, but more importantly a readability concern -- the caller does not know that RunRuleChecks strips HTML internally.

### L3: BrandVoiceScoreDto uses mutable class instead of record

**File:** BrandVoiceService.cs:237-253

Per project coding style (immutability preference), the DTO could be a record with init-only properties. Since it is only used for JSON deserialization and is private, this is cosmetic but consistent with project conventions.

### L4: ContentEngineOptions is a mutable class

**File:** ContentEngineOptions.cs

Options classes bound via IOptions<T> require mutable setters by convention, so this is acceptable. No action needed -- noting for completeness that it deviates from the immutability preference by necessity.

---

## Test Coverage Assessment

**Covered (14 tests):**
- Rule-based checks: avoided terms, preferred terms, compliant content, case insensitivity, HTML stripping (5 tests)
- LLM scoring: valid JSON parsing, invalid JSON handling, NotFound, metadata storage, prompt verification (5 tests)
- Gating logic: autonomous regeneration, max attempts failure, SemiAuto advisory, Manual advisory (4 tests)

**Missing coverage:**
- Sidecar ErrorEvent during scoring
- No active brand profile scenario
- Exception thrown by sidecar (catch path)
- Assisted autonomy level
- Empty content body
- Score out-of-range values from LLM (related to H2)
- GenerateDraftAsync returning failure during regeneration (related to H1)

---

## Summary

The implementation is well-structured and closely follows the section plan. The code is clean, under file size limits (254 lines), and uses proper patterns (Result type, structured logging, immutable score record). The two HIGH issues (unchecked GenerateDraftAsync result and missing score range validation) should be fixed before merging as they represent correctness and data integrity risks. The MEDIUM issues around substring matching (M4) and regex caching (M3) are worth addressing but not blockers.
