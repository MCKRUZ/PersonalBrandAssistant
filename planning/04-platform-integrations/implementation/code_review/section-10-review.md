# Section 10: API Endpoints -- Code Review

**Reviewer:** code-reviewer agent
**Date:** 2026-03-15
**Scope:** PlatformEndpoints.cs, Program.cs modification, PlatformEndpointsTests.cs
**Verdict:** BLOCK -- 2 HIGH issues, several MEDIUM findings

---

## HIGH Severity

### HIGH-01: Test-post endpoint is a production footgun with no safeguards

**File:** PlatformEndpoints.cs:95-111

The TestPost endpoint publishes real content to a live social media account with zero guardrails. There is no rate limiting, no confirmation, no authorization check beyond the global API key, and no way to distinguish test posts from real ones on the platform side. Any API key holder can spam social accounts.

**Recommendation:**

1. Add a request body with an optional custom message, or at minimum a confirmation flag.
2. Add rate limiting specific to this endpoint (e.g., 1 test post per platform per 5 minutes).
3. Consider requiring a confirm=true query parameter or similar explicit opt-in.
4. Tag the test with metadata so it can be identified/cleaned up later.

Suggested fix -- add rate limiting and a confirmation guard:

```csharp
group.MapPost("/{type}/test-post", TestPost)
    .RequireRateLimiting("test-post");

public record TestPostRequest(string? CustomMessage, bool Confirm = false);

private static async Task<IResult> TestPost(
    string type, TestPostRequest? body, IEnumerable<ISocialPlatform> adapters, CancellationToken ct)
{
    if (body?.Confirm != true)
        return Results.BadRequest("Set confirm=true to publish a test post.");
    // ...
}
```

---

### HIGH-02: Tests verify mocks, not endpoint behavior -- coverage is illusory

**File:** PlatformEndpointsTests.cs (entire file)

Every test calls mock methods directly rather than issuing HTTP requests against the actual endpoint pipeline. This means:

- Route binding is untested
- Middleware (auth, error handling) is untested
- The `ToHttpResult()` mapping is untested
- The adapter resolution via `IEnumerable<ISocialPlatform>` is untested

The section plan explicitly calls for integration tests using `CustomWebApplicationFactory`. The current tests are unit tests of Moq itself, not of the endpoints.

**Recommendation:** Rewrite using `WebApplicationFactory<Program>` following the pattern established by ContentEndpointsTests.

---

## MEDIUM Severity

### MED-01: GetStatus endpoint is incomplete vs. section plan

**File:** PlatformEndpoints.cs:75-93

The section plan specifies that `GET /{type}/status` should return `tokenExpiresAt`, `lastSyncAt`, `grantedScopes`, and `rateLimit` (from `IRateLimiter`). The implementation only returns `IsConnected`, `DisplayName`, and `Type` -- missing most of the specified fields and the `IRateLimiter` dependency entirely.

**Recommendation:** Inject `IRateLimiter` and include all planned fields (tokenExpiresAt, lastSyncAt, grantedScopes, rateLimit).

---

### MED-02: ListPlatforms returns incomplete data

**File:** PlatformEndpoints.cs:30-42

The section plan says the list should include `LastSyncAt` and `GrantedScopes`. The implementation only projects `Type`, `IsConnected`, and `DisplayName`. Add `LastSyncAt` to the projection.

---

### MED-03: postId route parameter has no input validation

**File:** PlatformEndpoints.cs:113-125

The `postId` parameter in `GetEngagement` is passed directly to the adapter with no validation. Add a basic sanity check (non-empty, max length 256) to prevent unnecessary calls to external APIs.

---

### MED-04: OAuthCallbackRequest has no FluentValidation

**File:** PlatformEndpoints.cs:134

The `OAuthCallbackRequest` record carries security-sensitive data and has no validation. Per project coding standards, all DTOs should use FluentValidation. Add validators for Code (NotEmpty, MaxLength 2048), State (NotEmpty, MaxLength 512), and CodeVerifier (MaxLength 128 when not null).

---

### MED-05: DELETE for disconnect uses non-standard REST convention

**File:** PlatformEndpoints.cs:24

The route `/{type}/disconnect` includes a verb. Consider `DELETE /{type}/connection` instead (deleting the connection resource).

---

## LOW Severity

### LOW-01: Mutable dictionary in test post content

**File:** PlatformEndpoints.cs:107

Per project coding standards (immutability), prefer `ImmutableDictionary<string, string>.Empty` over `new Dictionary<string, string>()`.

---

### LOW-02: Anonymous types in endpoint responses create fragile API contracts

**File:** PlatformEndpoints.cs:33-38, 87-92

Both `ListPlatforms` and `GetStatus` return anonymous types. Define explicit response records for OpenAPI documentation and versioning.

---

### LOW-03: Missing CancellationToken on ListPlatforms

**File:** PlatformEndpoints.cs:30

The `ListPlatforms` handler does not accept a `CancellationToken`, unlike all other handlers in the file. Add it and pass to `ToListAsync(ct)`.

---

### LOW-04: Missing test cases per plan

**File:** PlatformEndpointsTests.cs

Beyond the structural issue (HIGH-02), these test scenarios from the section plan are absent:

- Invalid platform type returns 400
- 401 when API key is missing
- Callback with invalid state returns 400
- Disconnect returns 204 and updates DB state
- Status endpoint returns all expected fields

---

### LOW-05: MediaEndpoints tests are missing from the diff

The section plan defines `MediaEndpointsTests.cs` with HMAC validation tests. These are not present in the diff. Track if not implemented elsewhere.

---

## Summary

| Severity | Count | Items |
|----------|-------|-------|
| HIGH     | 2     | Test-post safeguards, mock-only tests |
| MEDIUM   | 5     | Incomplete status endpoint, missing list fields, no postId validation, no DTO validation, REST convention |
| LOW      | 5     | Mutable dictionary, anonymous types, missing CancellationToken, missing test cases, missing media tests |

**Decision: BLOCK**

The two HIGH issues must be addressed before merge:

1. The test-post endpoint needs rate limiting and a confirmation mechanism to prevent abuse.
2. The tests must be rewritten as integration tests that exercise the HTTP pipeline, following the WebApplicationFactory pattern established by other endpoint test files.

The MEDIUM issues (especially MED-01 and MED-04) should also be resolved in this pass since they represent incomplete implementation relative to the section plan and missing input validation on a security-sensitive OAuth callback.
