# Section 11 - API Endpoints: Code Review

**Reviewer:** code-reviewer agent
**Date:** 2025-03-16
**Verdict:** BLOCK - Critical and High issues found

---

## Critical Issues

### [CRITICAL-01] No authentication on any endpoint group

**Files:** All 6 endpoint files
**Issue:** None of the MapGroup() calls chain RequireAuthorization(). The test file proves auth is expected (tests for 401 on missing API key exist and presumably pass due to middleware), but the endpoint registrations themselves carry no authorization metadata. If the global auth middleware is ever removed, reordered, or scoped differently, every endpoint becomes publicly accessible. Defense in depth requires explicit authorization at the endpoint level.

**Fix:** Chain RequireAuthorization() on every group:

```csharp
var group = app.MapGroup("/api/content-pipeline")
    .WithTags("ContentPipeline")
    .RequireAuthorization();
```

This is the standard Minimal API pattern and ensures endpoints are protected regardless of middleware ordering.

---

### [CRITICAL-02] Unvalidated POST query parameters in AutoFill

**File:** CalendarEndpoints.cs lines 158-173
**Issue:** The AutoFill endpoint accepts from and to as query string parameters on a POST request. POST bodies are the standard for mutation operations. More critically, the from/to parameters have no validation -- no range check like GetSlots has, no check that from is before to. A caller could pass invalid ranges and the behavior depends entirely on the downstream service.

**Fix:** Accept a request body record instead of query parameters for POST endpoints. Apply the same validation that GetSlots already does:

```csharp
public record AutoFillRequest(DateTimeOffset From, DateTimeOffset To);

private static async Task<IResult> AutoFill(
    IContentCalendarService calendar,
    IApplicationDbContext db,
    AutoFillRequest request,
    CancellationToken ct)
{
    if (request.From >= request.To)
        return Results.Problem(statusCode: 400, detail: "from must be before to.");
    // ... autonomy check, then service call
}
```
---

## High Issues

### [HIGH-01] Duplicated autonomy check logic (DRY violation)

**Files:** CalendarEndpoints.cs lines 165-169, TrendEndpoints.cs lines 357-361
**Issue:** The autonomy gate pattern is copy-pasted across two endpoints and will likely spread to more. Both endpoints directly inject IApplicationDbContext and query AutonomyConfigurations with identical fallback logic. This is a cross-cutting concern that belongs in a shared location.

**Fix:** Extract to a reusable IEndpointFilter called AutonomyGateFilter that checks GlobalLevel and returns 422 if Manual. Apply via AddEndpointFilter<AutonomyGateFilter>(). This also removes the layer violation of endpoints querying the DB context for policy decisions.

```csharp
// Usage:
group.MapPost("/auto-fill", AutoFill).AddEndpointFilter<AutonomyGateFilter>();
group.MapPost("/refresh", RefreshTrends).AddEndpointFilter<AutonomyGateFilter>();
```

---

### [HIGH-02] RepurposingEndpoints route collision with ContentEndpoints

**File:** RepurposingEndpoints.cs line 13 vs ContentEndpoints.cs line 16
**Issue:** Both RepurposingEndpoints and ContentEndpoints register under /api/content. While the specific sub-routes currently do not overlap, this is fragile. Adding a GET endpoint to either group could collide. Two endpoint groups sharing the same route prefix with different Swagger tags creates confusing API documentation.

**Fix:** Move repurposing under its own prefix (/api/repurposing), or merge into ContentEndpoints with a shared tag.

---

### [HIGH-03] Duplicate functionality: ValidateVoice vs BrandVoice GetScore

**Files:** ContentPipelineEndpoints.cs lines 227-234, BrandVoiceEndpoints.cs lines 75-82
**Issue:** Both call brandVoice.ScoreContentAsync(id, ct). Two routes (POST validate-voice, GET score) do identical work with different HTTP semantics (POST vs GET). One is a pipeline step, the other a standalone query -- but the implementation is identical.

**Fix:** The pipeline endpoint should delegate to pipeline.ValidateVoiceAsync(id, ct) which internally scores AND records the validation step in the pipeline state. Or remove one route if they are truly the same operation.

---

### [HIGH-04] Incorrect HTTP status code for autonomy denial

**Files:** CalendarEndpoints.cs line 169, TrendEndpoints.cs line 361
**Issue:** Both autonomy checks return 403 Forbidden. HTTP 403 implies identity/permission. Autonomy level is system configuration, not user permission. Clients would misinterpret the error.

**Fix:** Use 422 Unprocessable Entity or 409 Conflict instead:

```csharp
return Results.Problem(statusCode: 422,
    detail: "Operation requires SemiAuto or higher autonomy level. Current level: Manual.");
```

---

### [HIGH-05] RefreshTrends returns error details in Accepted response body

**File:** TrendEndpoints.cs line 364
**Issue:** When RefreshTrends fails, errors are returned inside a 202 Accepted response. A 202 indicates success (request accepted for processing). This misleads clients into thinking the operation succeeded and leaks internal error messages.

**Fix:**

```csharp
if (!result.IsSuccess)
    return result.ToHttpResult();

return Results.Accepted(value: "Refresh triggered");
```
---

## Warnings

### [WARN-01] No input validation on request DTOs

**Files:** RepurposingEndpoints.cs (RepurposeRequest), CalendarEndpoints.cs (AssignContentRequest), ContentPipelineEndpoints.cs (ContentCreationRequest)
**Issue:** Inline request records have no FluentValidation validators. These endpoints bypass MediatR so the ValidationBehavior pipeline does not apply. Empty TargetPlatforms arrays pass through unvalidated.

**Fix:** Add FluentValidation validators with an endpoint filter, or add inline guards:

```csharp
if (request.TargetPlatforms is null or { Length: 0 })
    return Results.Problem(statusCode: 400,
        detail: "At least one target platform is required.");
```

---

### [WARN-02] Missing CancellationToken on WorkflowEndpoints (existing pattern)

**File:** WorkflowEndpoints.cs lines 21-28
**Issue:** Existing WorkflowEndpoints does not pass CancellationToken. New endpoints correctly do. Note for follow-up fix (not introduced by this diff).

---

### [WARN-03] Test file is 343 lines -- approaching the boundary

**File:** ContentEngineEndpointsTests.cs
**Issue:** 343 lines with 18 tests covering 6 endpoint groups. Will become unwieldy as groups grow.

**Fix:** Split into one test class per endpoint group sharing a common TestFactory base.

---

### [WARN-04] Mock objects rebuilt per test via WithWebHostBuilder

**File:** ContentEngineEndpointsTests.cs lines 420-433
**Issue:** Each test rebuilds the test host via WithWebHostBuilder. Performance will degrade as test count grows.

**Fix:** Consider shared client approach or accept overhead for mock isolation.

---

### [WARN-05] Hardcoded connection string in test factory

**File:** ContentEngineEndpointsTests.cs lines 719-720
**Issue:** Hardcoded PostgreSQL connection string in tests (Host=localhost;Database=test_content_engine;Username=test;Password=test). Since all services are mocked, this exists only to satisfy DI registration.

**Fix:** Use in-memory database or SQLite provider instead.
---

## Suggestions

### [SUGGEST-01] Add rate limiting to LLM-backed endpoints

**Files:** ContentPipelineEndpoints.cs, TrendEndpoints.cs
**Issue:** GenerateOutline, GenerateDraft, RefreshTrends trigger expensive LLM calls with no rate limiting.

**Fix:** Add RequireRateLimiting("llm-operations") to LLM-triggering endpoints.

---

### [SUGGEST-02] Consider adding response type metadata for OpenAPI

**Issue:** No endpoints declare Produces<T>() or ProducesValidationProblem(). Swagger docs will lack response schemas.

---

### [SUGGEST-03] Analytics GetTopContent should validate date range

**File:** AnalyticsEndpoints.cs lines 32-42
**Issue:** No validation that from is before to, or that the range is reasonable. Could cause expensive queries.

**Fix:** Apply same validation pattern as CalendarEndpoints.GetSlots.

---

### [SUGGEST-04] Consider endpoint group-level tag consistency

**Issue:** Mixed tag naming: PascalCase compound (ContentPipeline, BrandVoice) vs single words (Calendar, Trends). Cosmetic but affects docs.

---

## Test Coverage Analysis

| Endpoint Group | Routes | Tests | Coverage |
|---|---|---|---|
| ContentPipeline | 5 | 5 | Good - create (201), outline (200), draft (200), submit-404, auth-401 |
| Repurposing | 3 | 3 | Good - repurpose (200), tree-404, auth-401 |
| Calendar | 5 | 2 | **GAP** - missing validation, AssignContent, AutoFill tests |
| BrandVoice | 1 | 2 | Good - 200 and 404 |
| Trends | 4 | 3 | Partial - missing DismissSuggestion, RefreshTrends autonomy/success |
| Analytics | 3 | 3 | Good - all routes covered |

**Missing test scenarios (10 gaps):**

1. CalendarEndpoints.GetSlots with from >= to -- should return 400
2. CalendarEndpoints.GetSlots with >90 day range -- should return 400
3. CalendarEndpoints.AutoFill with Manual autonomy -- should return 403/422
4. CalendarEndpoints.AutoFill success case
5. CalendarEndpoints.AssignContent -- no test at all
6. CalendarEndpoints.CreateManualSlot -- no test at all
7. TrendEndpoints.RefreshTrends with Manual autonomy -- should return 403/422
8. TrendEndpoints.RefreshTrends success case
9. TrendEndpoints.DismissSuggestion -- no test at all
10. RepurposingEndpoints.GetSuggestions -- no test at all

---

## Summary

| Priority | Count |
|---|---|
| Critical | 2 |
| High | 5 |
| Warnings | 5 |
| Suggestions | 4 |

**Verdict: BLOCK**

The two critical issues (missing RequireAuthorization() on endpoint groups and unvalidated POST query parameters) must be resolved before merge. The high-priority items (duplicated autonomy logic, route collision, duplicate functionality, incorrect status codes, error-in-202) should also be addressed in this pass. The test coverage gaps for Calendar and Trend endpoints are significant enough to warrant additional tests before this section is complete.
