# Section 06: OAuthManager -- Code Review

**Reviewer:** code-reviewer agent
**Date:** 2026-03-15
**Verdict:** BLOCK -- Critical and High issues found

---

## Critical Issues

### [CRITICAL-01] Access token leaked in Instagram URL query strings

**Files:**
- OAuthManager.cs:361-362 (ExchangeInstagramCodeAsync)
- OAuthManager.cs:435 (RefreshInstagramTokenAsync)
- OAuthManager.cs:483 (RevokeTokenAsync, Instagram case)
- OAuthManager.cs:490 (RevokeTokenAsync, YouTube case)

**Issue:** Access tokens and client secrets are passed as URL query parameters. These will be logged in HTTP access logs, proxy logs, CDN logs, and potentially browser history. This is a token leakage vector.

The Instagram long-lived token exchange puts the short-lived access token AND the app secret in the URL (line 361). The Instagram refresh does the same with the access token (line 435).

**Mitigation:** The Instagram Graph API requires tokens in query parameters -- this is an API design limitation. However, the client_secret on line 361 is also in the URL, which compounds the risk. Additionally, YouTube revocation on line 490 puts the token in the URL.

**Fix:** For Instagram/YouTube, these are API requirements and cannot be changed. But ensure the IHttpClientFactory-created "OAuth" client does NOT have request logging enabled. Add a comment acknowledging this limitation. Configure the named "OAuth" HttpClient to suppress request URI logging (e.g., `.RemoveAllLoggers()` in .NET 8+).

---

### [CRITICAL-02] Code verifier not encrypted at rest

**File:** OAuthManager.cs:65-72

**Issue:** The `OAuthState.CodeVerifier` is stored in the database as plaintext. The PKCE code verifier is a cryptographic secret. If the database is compromised, an attacker who also intercepts the authorization code can complete the token exchange.

**Fix:** Encrypt the code verifier before storage via `_encryption.Encrypt()`, decrypt on retrieval. Store as `byte[]` (encrypted) on the OAuthState entity, or store as Base64-encoded encrypted string. Coordinate with the domain entity definition.

---

### [CRITICAL-03] ExchangeCodeAsync exposes HTTP exception messages to callers

**File:** OAuthManager.cs:145

**Issue:** `HttpRequestException.Message` can contain internal URLs, server error details, or other sensitive information. This message may propagate to an API response visible to users.

**Fix:** Return a generic error message; the details are already logged. Same issue on line 199 for RefreshTokenAsync.

---

## High Priority Issues

### [HIGH-01] File is 552 lines -- exceeds the 400-line guideline

**File:** OAuthManager.cs (552 lines)

**Issue:** Exceeds the preferred file size of 200-400 lines and approaches the 800-line hard limit. The class mixes four platform-specific implementations into one file.

**Fix:** Extract platform-specific logic into strategy classes: `IOAuthPlatformStrategy` interface with `TwitterOAuthStrategy`, `LinkedInOAuthStrategy`, `InstagramOAuthStrategy`, `YouTubeOAuthStrategy`. OAuthManager becomes ~150 lines of orchestration. This also eliminates the repeated platform switch expressions (4 occurrences).

---

### [HIGH-02] IConfiguration used directly for secrets instead of strongly-typed options

**File:** OAuthManager.cs (9+ locations)

**Issue:** Direct `IConfiguration["..."]` access for ClientId/ClientSecret is fragile -- typos in key strings return null at runtime. The null-forgiving operator (`!`) will throw NRE if the key is missing.

**Fix:** Extend `PlatformOptions` to include `ClientId` and `ClientSecret` as required init-only properties. Bind from configuration and validate at startup with `ValidateOnStart()`. This catches missing secrets at app startup instead of at runtime.

---

### [HIGH-03] No validation on code parameter in ExchangeCodeAsync

**File:** OAuthManager.cs:83-88

**Issue:** The `code` parameter is passed directly to platform token endpoints without validation. An empty or whitespace-only code should be rejected early.

**Fix:** Add `string.IsNullOrWhiteSpace(code)` guard clause returning `Result.Failure` with `ValidationFailed` error code.

---

### [HIGH-04] Race condition on OAuthState consumption

**File:** OAuthManager.cs:90-111

**Issue:** Between reading the OAuthState and deleting it, another request with the same state could read it too. This is a TOCTOU issue that could allow state replay.

**Fix:** Use an atomic delete-and-return pattern or wrap in a transaction with row-level locking. Alternatively, use `ExecuteDeleteAsync` and retrieve the CodeVerifier before deletion in a serializable transaction.

---

### [HIGH-05] Instagram revocation URL differs from plan

**File:** OAuthManager.cs:482-483

**Plan says:** `DELETE https://graph.facebook.com/{userId}/permissions`
**Implementation:** `DELETE https://graph.facebook.com/me/permissions?access_token={accessToken}`

**Issue:** Using `/me/permissions` is functionally correct. However, passing the access token as a query parameter is a leakage concern (see CRITICAL-01). The Facebook Graph API also accepts the token as a header.

---

### [HIGH-06] Missing test: ExchangeCodeAsync deletes OAuthState after use

**File:** OAuthManagerTests.cs

**Issue:** The plan specifies this test but no test verifies that `_dbContext.OAuthStates.Remove()` is called. The StoresEncryptedTokens test verifies token storage but not state cleanup.

---

### [HIGH-07] Missing test: ExchangeCodeAsync uses stored CodeVerifier for Twitter PKCE

**File:** OAuthManagerTests.cs

**Issue:** The plan specifies this test but it is missing. This is a critical security behavior -- the server-stored verifier must be used, not one supplied by the client.

---

## Warnings

### [WARN-01] OAuthTokens record contains AccessToken in structural equality

**File:** OAuthTokens.cs:7-8

**Issue:** The `ToString()` override correctly excludes the access token. However, since `OAuthTokens` is a record, structural equality includes `AccessToken`. If this record appears in log context or exception data, the default record formatting could leak the token. The current override is good -- just be aware in downstream usage.

---

### [WARN-02] SendTokenRequestAsync swallows error response body

**File:** OAuthManager.cs:501-506

**Issue:** When the HTTP response is non-2xx, the error body (which often contains `error` and `error_description`) is discarded. This makes debugging token exchange failures very difficult.

**Fix:** Log the error response body. Note: `SendTokenRequestAsync` is currently static and would need to become an instance method to access `_logger`. Verify that error responses from the four platforms never echo back tokens before enabling this logging.

---

### [WARN-03] No timeout configuration on HTTP calls

**File:** OAuthManager.cs (all `client.SendAsync` calls)

**Issue:** No timeout configured on the HTTP client. If a platform token endpoint hangs, the request blocks for the default 100 seconds.

**Fix:** Configure a 30-second timeout on the named "OAuth" client during DI registration.

---

### [WARN-04] Instagram uses Facebook OAuth v19.0 -- should be parameterized

**File:** OAuthManager.cs:272

**Issue:** The Facebook Graph API version is hardcoded and will become stale.

**Fix:** Move to configuration or a named constant.

---

## Suggestions

### [SUGGEST-01] Consider adding an expired state cleanup mechanism

The OAuthState table will accumulate expired entries over time. Consider a background job or opportunistic cleanup using `ExecuteDeleteAsync` on expired records.

---

### [SUGGEST-02] Strategy pattern would improve testability

Per HIGH-01, extracting platform-specific logic into strategies would allow testing each platform OAuth flow independently.

---

### [SUGGEST-03] Consider adding retry logic for transient HTTP failures

Token refresh and exchange calls could benefit from a Polly retry policy on the "OAuth" HttpClient.

---

### [SUGGEST-04] Test file path mismatch with implementation

**Plan says:** `src/.../Services/Platform/OAuthManager.cs`
**Implementation:** `src/.../Services/PlatformServices/OAuthManager.cs`

The folder naming should be consistent.

---

## Plan Conformance

| Plan Requirement | Status | Notes |
|-----------------|--------|-------|
| GenerateAuthUrlAsync with state + PKCE | PASS | Correctly implemented |
| ExchangeCodeAsync with state validation | PASS | Missing input validation on code param |
| Instagram two-step token exchange | PASS | Short-lived then long-lived |
| YouTube offline access | PASS | access_type=offline and prompt=consent |
| Token encryption before storage | PASS | Uses IEncryptionService |
| RefreshTokenAsync with disconnect on failure | PASS | Clears tokens on null response |
| RevokeTokenAsync per platform | PASS | LinkedIn correctly skipped |
| Never log token values | PASS | No token values in log statements |
| Result pattern | PASS | All methods return Result of T |
| Error handling with try/catch | PASS | HttpRequestException caught |
| IConfiguration for secrets | PASS | Per plan spec |
| Tests for all specified scenarios | PARTIAL | Missing 2 tests (state deletion, PKCE verifier usage) |

---

## Summary

The implementation is structurally sound and follows the plan closely. The OAuth flows for all four platforms are correctly implemented with proper state validation, PKCE for Twitter, and encrypted token storage. The OAuthTokens.ToString() override correctly excludes sensitive data.

**Must fix before merge:**
1. CRITICAL-01: Acknowledge and mitigate token-in-URL logging risk
2. CRITICAL-02: Encrypt the PKCE code verifier at rest
3. CRITICAL-03: Do not expose HttpRequestException.Message to callers
4. HIGH-03: Validate code parameter
5. HIGH-04: Address TOCTOU race on state consumption
6. HIGH-06/07: Add missing tests for state deletion and PKCE verifier usage

**Should fix:**
1. HIGH-01: Extract platform strategies to reduce file size
2. HIGH-02: Move secrets to strongly-typed options with startup validation
3. WARN-02: Log error response bodies for debuggability
4. WARN-03: Configure HTTP client timeout
