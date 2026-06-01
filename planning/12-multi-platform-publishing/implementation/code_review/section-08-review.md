# Section 08 Review: LinkedIn Connector

## Critical Issues

No critical issues found.

The section-07 review identified two critical issues in MediumConnector (shared mutable auth headers, exception message leakage). This implementation correctly addresses both:
- Per-request `Authorization` headers via `HttpRequestMessage.Headers` (lines 67-69, 157, 222) -- no `DefaultRequestHeaders` mutation
- Generic error messages to callers with detailed logging internally (lines 88-89, 104-106)

## High Issues

**[HIGH-1] `_options` field is assigned but never used -- dead injected dependency**
`LinkedInConnector.cs` line 31: `private readonly IOptionsMonitor<LinkedInOptions> _options = options;` is assigned from the primary constructor parameter, but `_options` is never referenced anywhere in the class. `LinkedInOptions` contains `Enabled`, `ClientId`, `ClientSecret`, and `RedirectUri` -- none of which are read during publish, validation, or capabilities.

This is the same issue flagged as HIGH-2 in the section-07 MediumConnector review. The options injection is either premature (added for a future need) or the `Enabled` flag was intended to be checked as a guard.

Fix (pick one):
1. Add an enabled guard at the top of `PublishAsync`:
```csharp
if (!_options.CurrentValue.Enabled)
    return new PlatformPublishResult(false, null, null,
        "LinkedIn publishing is not enabled. Enable it in Settings.");
```
2. Remove the injection entirely if the enabled check belongs elsewhere (e.g., `ContentPublisher` checks capabilities before dispatching).

**[HIGH-2] Image upload is specified in section plan but not implemented**
The section-08 plan specifies a two-step image upload flow with a corresponding test `PublishAsync_WithImage_UploadsImageFirst`. The implementation has:
- Internal records for image upload response types (`LinkedInImageUploadResponse`, `LinkedInImageUploadValue`) at lines 235-236
- `GetCapabilities()` returns `SupportsImages: true` at line 129
- No actual image upload logic in `PublishAsync` or any private helper

The response models suggest image upload was planned but not built. `SupportsImages: true` advertises a capability the connector cannot deliver.

Fix: Either implement the image upload flow as spec'd, or set `SupportsImages: false` and remove the dead response records.

**[HIGH-3] `ValidateCredentialsAsync` does not attempt token refresh -- near-expired tokens pass validation but fail on publish**
`LinkedInConnector.cs` lines 110-123: `ValidateCredentialsAsync` decrypts the stored token and calls `GetPersonUrnAsync` directly. It does not check `AccessTokenExpiresAt` or call `GetValidTokenAsync` (which has the 5-minute refresh window logic).

Scenario: token expires in 2 minutes. Validation passes but immediate publish triggers refresh which may fail.

Fix: Route through `GetValidTokenAsync` in `ValidateCredentialsAsync` too:
```csharp
public async Task<bool> ValidateCredentialsAsync(CancellationToken ct)
{
    try
    {
        var credential = await GetActiveCredentialAsync(ct);
        var token = await GetValidTokenAsync(credential, ct);
        if (token is null) return false;
        return await GetPersonUrnAsync(token, ct) is not null;
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "LinkedIn credential validation failed");
        return false;
    }
}
```

**[HIGH-4] Missing test for `PublishMode.Draft` and `PublishMode.Schedule` rejection**
The connector explicitly rejects `Draft` and `Schedule` modes (lines 48-51), but there is no test covering this path.

Fix: Add a `[Theory]` test with `[InlineData(PublishMode.Draft)]` and `[InlineData(PublishMode.Schedule)]` that asserts `result.Success == false` and error message contains "does not support".

## Medium Issues

**[MEDIUM-1] `SetupUserInfoAndPost` has unused `capturedBodyHolder` parameter**
`LinkedInConnectorTests.cs` line 433: The method signature includes `string? capturedBodyHolder = null` but this parameter is never used.

Fix: Remove the parameter: `private void SetupUserInfoAndPost()`

**[MEDIUM-2] `LinkedInVersion` constant will silently become stale**
`LinkedInConnector.cs` line 33: the version string is hardcoded. LinkedIn's Community Management API uses monthly version strings (YYYYMM format). Once this version ages past LinkedIn's supported window (typically 12 months), API calls will fail.

Fix: Move the version to `LinkedInOptions` so it can be updated via config. This also gives `IOptionsMonitor` a reason to exist (see HIGH-1).

**[MEDIUM-3] `FencedCodeBlock` regex may produce whitespace artifacts**
The fenced code block pattern replaces the entire code block with captured content. The `CollapseBlankLines` regex should clean up most artifacts. Acceptable as-is, but worth a targeted test case.

**[MEDIUM-4] Tests duplicate 25+ lines of HTTP mock setup across 4 tests**
Same duplication issue flagged as MEDIUM-5 in the section-07 review. The `SetupUserInfoAndPost` helper exists but only 3 of 11 tests use it.

Fix: Refactor the helper to optionally return captured data, or extract the userinfo mock separately.

**[MEDIUM-5] `SetupHttpResponses` helper method is defined but never called**
Lines 459-474: Dead test infrastructure. Fix: Either use it or remove it.

## Low Issues

**[LOW-1] `BuildPostPayload` article description truncation has no word-boundary awareness**
Lines 187-189: Article description truncated to 200 chars via `request.TransformedContent[..200]` cuts mid-word. The formatter's `Truncate` method uses word-boundary truncation, but this inline truncation does not.

Fix: Extract a shared word-boundary truncation helper.

**[LOW-2] `LinkedInUserInfo` record properties differ from spec -- implementation is safer**
Nullable `Sub` where spec has non-nullable. Better for resilience. No fix needed.

**[LOW-3] No test verifies the POST request URL path is `/rest/posts`**
A typo in the URL would pass all tests.

**[LOW-4] No test verifies the userinfo endpoint URL is `/v2/userinfo`**
Same as LOW-3 but for the person URN lookup.

## Nitpicks

**[NITPICK-1]** `_options` field is the only one explicitly captured from the primary constructor. Inconsistency resolves when HIGH-1 is fixed.

**[NITPICK-2]** Test `LinkedInOptions` values for `ClientId`/`ClientSecret` serve no purpose since `_options` is unused.

**[NITPICK-3]** `Format_Platform_ReturnsLinkedIn` is synchronous while other formatter tests are `async Task`. Correct but breaks visual pattern.

## Approved Items

- Per-request `Authorization` headers on all HTTP calls -- addresses the critical thread-safety issue from section-07 review
- Generic error messages to callers with detailed `logger.LogError` internally -- addresses the information leakage issue from section-07 review
- `IOptionsMonitor<LinkedInOptions>` used (not `IOptions<T>`) -- consistent with codebase convention
- Differentiated HTTP status handling: 401 (auth), 403 (permissions), 429 (rate limit), generic fallback
- `SetLinkedInHeaders` private helper correctly adds `Authorization`, `X-Restli-Protocol-Version`, and `LinkedIn-Version` per-request
- `GetPersonUrnAsync` correctly differentiates 401 (returns null) from other failures (throws)
- Token refresh via `IOAuthService.RefreshTokenAsync` correctly uses `Result<string>` (matching actual interface, not the outdated spec)
- 5-minute refresh window logic (`TokenRefreshWindow`) is clean and self-documenting
- `sealed` on both classes prevents inheritance -- correct for concrete implementations
- `static readonly JsonSerializerOptions` avoids per-call allocation
- `GeneratedRegex` source-generated regex on all formatter patterns -- correct .NET 10 approach
- Regex processing order (fenced code blocks first) prevents stripping markdown syntax inside code blocks
- `CollapseBlankLines` cleanup after all stripping ensures consistent whitespace
- Truncation respects word boundaries with `lastSpace > budget / 2` guard against pathological inputs
- `BuildPostPayload` correctly branches on `CanonicalUrl` for text-only vs. article-sharing payloads
- `LinkedInFormatter.FormatAsync` is pure (no side effects, no I/O)
- Interface compliance correct: `IPlatformConnector` and `IPlatformFormatter` contracts fully implemented
- `PlatformPublishRequest` correctly constructed with 6 parameters (including `ScheduledAt`) in all test sites
- `GetActiveCredentialAsync` correctly filters by `Platform.LinkedIn` and `IsActive`
- Test coverage for formatter is thorough: markdown stripping, line breaks, truncation, platform identity
- Test coverage for connector covers: token refresh, text post payload, article sharing, response headers, HTTP errors, versioned headers, credential validation, capabilities
- `IDisposable` on test class correctly disposes `HttpClient` and `DbContext`
- In-memory database with `Guid.NewGuid()` name prevents test interference

**Verdict: BLOCK** -- Four high issues must be addressed before merge:
1. **HIGH-1** (dead `_options` injection) -- resolve to match the pattern fix from section-07
2. **HIGH-2** (image upload advertised but not implemented) -- most important. Either implement or set `SupportsImages: false`
3. **HIGH-3** (validation doesn't check token expiry) -- confusing UX where validation passes but publish fails
4. **HIGH-4** (no test for draft/schedule rejection) -- leaves guard clause untested

No critical security or thread-safety issues -- the section-07 critical patterns were correctly applied.