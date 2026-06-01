# Section 07 Review: Medium Connector

## Critical Issues

**[CRITICAL-1] Mutating `HttpClient.DefaultRequestHeaders` is not thread-safe and leaks auth state across requests**
`MediumConnector.cs` lines 37, 105 (diff lines 43, 111): `httpClient.DefaultRequestHeaders.Authorization` is set on every call to `PublishAsync` and `ValidateCredentialsAsync`. `DefaultRequestHeaders` is mutable shared state on the `HttpClient` instance. Problems:
1. If `HttpClient` is shared (via `IHttpClientFactory` or singleton registration), the bearer token leaks to all consumers of that client instance.
2. `DefaultRequestHeaders` is explicitly documented as not thread-safe. Concurrent calls to `PublishAsync` could corrupt the header collection.
3. Even on a dedicated client, the token persists after the method returns -- any subsequent use of the client (including in tests or after credential rotation) carries the stale token.

The existing `BlogConnector` avoids this entirely because it does not use HTTP. This is the first HTTP-based connector, so there is no established pattern to follow -- but the correct pattern is clear.

Fix: Set the `Authorization` header per-request, not on the shared client:

```csharp
public async Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct)
{
    try
    {
        var token = await GetDecryptedTokenAsync(ct);
        var userId = await GetUserIdAsync(token, ct);
        // ...
        using var postRequest = new HttpRequestMessage(HttpMethod.Post, $"/v1/users/{userId}/posts")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        postRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // ...
    }
}

private async Task<string?> GetUserIdAsync(string token, CancellationToken ct)
{
    if (_cachedUserId is not null)
        return _cachedUserId;

    using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/me");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    var response = await httpClient.SendAsync(request, ct);
    // ...
}
```

This eliminates the shared mutable state. The token stays scoped to the individual HTTP request.

**[CRITICAL-2] Exception message exposed to caller in error path leaks internal details**
`MediumConnector.cs` line 97 (diff 103): The catch-all handler returns `ex.Message` directly in the `PlatformPublishResult.ErrorMessage`. Exception messages from `HttpClient`, `JsonSerializer`, `DbContext`, and `ITokenEncryptor` can contain:
- Internal URLs and paths
- Serialization details (type names, property names)
- Database connection info (if EF Core throws)
- Encryption implementation details

This result eventually reaches the frontend/API caller. The existing `BlogConnector` has the same pattern (`ex.Message` at line 62), so this is a codebase-wide issue, but it should not be replicated in new code.

Fix: Return a generic user-facing message, log the full exception:

```csharp
catch (Exception ex)
{
    logger.LogError(ex, "Failed to publish to Medium");
    return new PlatformPublishResult(false, null, null,
        "An unexpected error occurred while publishing to Medium. Check logs for details.");
}
```

## High Issues

**[HIGH-1] `IOptions<MediumOptions>` used instead of `IOptionsMonitor<MediumOptions>` -- inconsistent with codebase convention**
`MediumConnector.cs` line 18 (diff 24): Uses `IOptions<MediumOptions>` and captures `.Value` immediately in the primary constructor body (line 28, diff 34: `_options = options.Value`). Every other connector and service in the Infrastructure layer uses `IOptionsMonitor<T>`:
- `BlogConnector` uses `IOptionsMonitor<BlogConnectorOptions>`
- `BlogFormatter` uses `IOptionsMonitor<BlogConnectorOptions>`
- `ContentTransformer` uses `IOptionsMonitor<TransformerOptions>`
- `RssPollingService` uses `IOptionsMonitor<RssPollingOptions>`

`IOptions<T>` captures the config value at construction time and never updates. `IOptionsMonitor<T>` supports runtime reloads. The codebase convention is `IOptionsMonitor<T>` and the global `CLAUDE.md` explicitly calls for `IOptionsMonitor<T>`.

Fix: Change to `IOptionsMonitor<MediumOptions>` and access `_options.CurrentValue` at point of use, not at construction.

Note: `MediumOptions` is currently only used to check `Enabled` and `DefaultPublishStatus`, neither of which is accessed at all in the connector code. That leads to HIGH-2.

**[HIGH-2] `MediumOptions` is injected but never used**
`MediumConnector.cs` line 18 (diff 24): `IOptions<MediumOptions> options` is injected, and line 28 captures `_options = options.Value`, but `_options` is never referenced anywhere in the class. Neither `Enabled` nor `DefaultPublishStatus` is read.

This is dead code. Two possibilities:
1. `DefaultPublishStatus` was intended to be used as a fallback in the `publishStatus` switch (line 50-55), but was forgotten.
2. The options injection is premature -- added for future use.

Fix: Either use it (e.g., replace the `_ => "draft"` fallback with `_ => _options.DefaultPublishStatus`) or remove the injection entirely. Dead dependencies obscure the real dependency graph.

**[HIGH-3] `_cachedUserId` is not thread-safe and has no invalidation path**
`MediumConnector.cs` line 29 (diff 35): `private string? _cachedUserId` is a plain field with no synchronization. If two concurrent calls to `PublishAsync` race through `GetUserIdAsync`, both will issue the `/v1/me` request and both will write to `_cachedUserId` (benign in this case since the value is idempotent, but still a code smell).

More importantly: there is no way to invalidate the cached user ID. If the Medium integration token is rotated (pointing to a different user account), the connector will continue using the stale user ID for the lifetime of the DI scope. Given the connector is registered as `AddKeyedScoped`, this is per-request in web scenarios, making the caching effectively useless -- it only saves a second call within the same request scope.

Fix: If the connector is scoped (one instance per request), the cache provides negligible value. Remove it for simplicity. If the connector is intended to be singleton, add proper synchronization (`SemaphoreSlim` or `Lazy<Task<string?>>`) and an invalidation mechanism.

**[HIGH-4] `GetUserIdAsync` silently returns null on any non-success status, not just 401**
`MediumConnector.cs` lines 130-137 (diff 136-143): `GetUserIdAsync` returns `null` for any non-success HTTP status. The caller at line 46-48 (diff 52-54) interprets this as "invalid or expired token." But a 500 from Medium, a network timeout that surfaces as a non-success status, or a 403 (forbidden) would all produce the same "reconfigure in Settings" user message. This conflates transient failures with auth failures.

Fix: Differentiate status codes:

```csharp
private async Task<string?> GetUserIdAsync(CancellationToken ct)
{
    // ... send request ...
    if (response.StatusCode == HttpStatusCode.Unauthorized)
        return null; // Auth failure -- token invalid

    if (!response.IsSuccessStatusCode)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogError("Medium /v1/me failed: {Status} {Body}", response.StatusCode, body);
        throw new HttpRequestException($"Medium user lookup failed ({response.StatusCode})");
    }

    // ... deserialize ...
}
```

This way auth failures return null (and the caller returns a user-friendly message), while transient failures propagate as exceptions and hit the catch block generic error handler.

## Medium Issues

**[MEDIUM-1] `PublishMode.Schedule` silently downgrades to "draft" with no user notification**
`MediumConnector.cs` lines 50-55 (diff 56-61): When the user requests `PublishMode.Schedule`, the connector maps it to `"draft"`. The `PlatformCapabilities.SupportsScheduling` is `false`, so callers *should* know scheduling is not supported. But the connector returns `Success = true` with no indication that the post was published as draft instead of scheduled. The user thinks their post is scheduled; it is actually sitting as a draft.

Fix: Either return a success result with a warning in `ErrorMessage` (which is poorly named for this -- but that is the existing model), or add an explicit check:

```csharp
if (request.Mode == PublishMode.Schedule)
    return new PlatformPublishResult(false, null, null,
        "Medium does not support scheduled publishing. Post as draft or publish immediately.");
```

The caller should be responsible for checking capabilities before calling, but defense-in-depth applies.

**[MEDIUM-2] SVG regex replacement is overly aggressive**
`MediumFormatter.cs` line 176 (diff 182): The `SvgToPng()` regex pattern is `\.svg\)` -- it matches `.svg)` anywhere in the text. This will incorrectly transform:
- Inline text like `"I use .svg) format"` (unlikely but possible in code blocks)
- URLs where `.svg` appears mid-path (malformed but possible)
- Multiple SVG extensions in the same image reference

More importantly, it only matches `.svg)` -- the closing paren from markdown image syntax `![alt](url.svg)`. But what about HTML `<img>` tags with SVG sources, or bare URLs not wrapped in markdown image syntax? The scope is implicitly "only markdown image links ending in .svg" but the regex does not enforce the full markdown image pattern.

Fix: Tighten the regex to match the full markdown image pattern, or accept the current behavior with a code comment explaining the intentional scope.

**[MEDIUM-3] `ResolveRelativeImages` uses naive string replacement -- vulnerable to substring collisions**
`MediumFormatter.cs` lines 192-204 (diff 198-210): `body.Replace(...)` does a plain string replace wrapping `image.OriginalPath` in parens. If `image.OriginalPath` is a substring of another image path (e.g., `images/photo.png` and `images/photo.png-large`), or if the same path string appears in non-image contexts (e.g., in a code block referencing the path), the replacement will be incorrect.

This is unlikely in practice for image references, but it is a correctness gap.

Fix: Use a more specific match pattern that includes the markdown `](` prefix from markdown link/image syntax, reducing false positives.

**[MEDIUM-4] Test helper `SetupMeAndPostWithCapture` returns captured body via closure -- but is never used**
`MediumConnectorTests.cs` lines 325-357 (diff 331-363): The `SetupMeAndPostWithCapture()` helper method is defined but never called in any test. Every test that needs body capture duplicates the full setup inline instead. This is 30 lines of dead code.

Fix: Either use the helper in the tests that duplicate its logic (5 tests duplicate the same pattern), or delete it.

**[MEDIUM-5] Massive test code duplication -- 5 tests repeat identical 20-line HTTP mock setup**
`MediumConnectorTests.cs`: Tests `PublishAsync_Draft_SendsCorrectPayload`, `PublishAsync_Public_SendsCorrectStatus`, `PublishAsync_TruncatesTags_ToMax3`, `PublishAsync_IncludesCanonicalUrl`, and `PublishAsync_ReturnsPublishedUrlFromResponse` all contain near-identical HTTP mock setup code. The `SetupMeAndPostWithCapture` helper was presumably written to solve this, then never integrated.

Fix: Refactor to use the helper or extract a shared setup. This reduces 100+ lines of duplication.

## Low Issues

**[LOW-1] `MediumOptions.DefaultPublishStatus` has no validation**
`MediumOptions.cs` line 222 (diff 228): `DefaultPublishStatus` defaults to `"draft"` but accepts any string. Medium only supports `"public"`, `"draft"`, and `"unlisted"`. An invalid value would produce a 400 from the Medium API with no clear indication of the misconfiguration. Low priority since the property is not currently used (see HIGH-2).

**[LOW-2] Internal record types use positional constructors -- fragile against Medium API changes**
`MediumConnector.cs` lines 149-151 (diff 155-157): `MediumResponse<T>`, `MediumUser`, and `MediumPost` are positional records. If Medium adds a new required field or changes the response shape, deserialization will silently produce null/default values. No action needed. Just noting the tradeoff -- if the Medium response model grows, switch to init-only properties.

**[LOW-3] No test for tag length truncation (each tag max 25 chars)**
`MediumConnectorTests.cs`: The `PublishAsync_TruncatesTags_ToMax3` test (diff line 445) verifies max 3 tags but does not test that individual tags exceeding 25 characters are truncated. The connector has this logic at line 53 (diff 59): `t.Length > 25 ? t[..25] : t`. This path is untested.

Fix: Add a test case with a tag like `"ThisIsAVeryLongTagNameThatExceedsTheLimit"` and assert it is truncated to 25 chars.

**[LOW-4] Tests do not verify the URL path of the POST request**
`MediumConnectorTests.cs`: No test asserts that the publish request is sent to `/v1/users/{userId}/posts`. The mock captures the body but does not verify the request URI contains the correct user ID. A bug in the URL template would pass all current tests.

Fix: In the mock callback, add `Assert.Contains("/v1/users/user123/posts", req.RequestUri?.PathAndQuery)`.

**[LOW-5] `ValidateCredentialsAsync` swallows all exceptions silently**
`MediumConnector.cs` lines 109-112 (diff 115-118): The bare `catch` block returns `false` for any exception, including `InvalidOperationException` from `GetDecryptedTokenAsync` (no credential found) and decryption failures. This is acceptable behavior for a "validate" method, but the total lack of logging means credential misconfiguration is invisible.

Fix: Add a log statement:

```csharp
catch (Exception ex)
{
    logger.LogWarning(ex, "Medium credential validation failed");
    return false;
}
```

## Nitpicks

**[NITPICK-1]** `MediumConnector.cs` line 74 (diff 80): `System.Net.HttpStatusCode.TooManyRequests` uses the fully-qualified name while `HttpStatusCode` is not imported via `using`. Add `using System.Net;` to the imports and use `HttpStatusCode.TooManyRequests` for consistency with the rest of the file.

**[NITPICK-2]** `MediumFormatter.cs` line 178 (diff 184): `new Uri(content.CanonicalUrl).Host` -- if `CanonicalUrl` is not a valid URI, this throws `UriFormatException`. The `!string.IsNullOrEmpty` guard does not validate URI format. Use `Uri.TryCreate` for robustness.

**[NITPICK-3]** `MediumConnectorTests.cs` line 249 (diff 255): `_mediumOptions` is initialized but `DefaultPublishStatus` is never asserted against in any test. Since the options are unused in the connector (HIGH-2), this is consistent but worth noting.

## Approved Items

- Interface compliance is correct: `IPlatformConnector` and `IPlatformFormatter` contracts are fully implemented
- Token decryption via `ITokenEncryptor` follows the established pattern from section-06
- `PlatformCredential` lookup correctly filters by `Platform.Medium` and `IsActive`
- The `sealed` modifier on both classes prevents inheritance -- correct for concrete implementations
- `GeneratedRegex` attribute on the SVG pattern is the correct source-generator approach for .NET 10
- `MediumFormatter.FormatAsync` is pure (no side effects, no I/O) -- good separation of concerns
- Tag truncation logic (max 3, max 25 chars each) correctly implements Medium API limits
- JSON serialization options are `static readonly` -- avoids per-call allocation
- `CamelCase` naming policy matches Medium API JSON convention
- Test coverage for formatter is solid: canonical URL injection, null canonical URL, SVG conversion, relative image resolution, markdown preservation, platform identity
- Test coverage for connector covers happy path, rate limiting, invalid token, credential validation, and capabilities
- `using var postRequest` properly disposes the `HttpRequestMessage`
- The `PlatformPublishResult` record usage is consistent with `BlogConnector`

**Verdict: BLOCK** -- Two critical issues (shared mutable auth header, exception message leakage) and four high issues (wrong options interface, dead dependency, unsafe cache, conflated error handling in user ID lookup) must be addressed before merge. The formatter is clean. The connector core logic is sound but the HTTP client usage pattern and error handling need rework.
