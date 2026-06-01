# Section 06 Review: Encryption and OAuth

## Critical Issues

**[CRITICAL-1] No encryption key length validation -- invalid keys silently produce broken crypto**
`TokenEncryptor.cs` constructor (line 14-16 in diff): `Convert.FromBase64String(options.Value.Key)` accepts any base64 string. If the key is not exactly 32 bytes (256 bits), `AesGcm` constructor throws a `CryptographicException` at first use, not at startup. If no key is configured at all, `options.Value.Key` throws `NullReferenceException` since it is `required`, but there is no validation that the key is the correct length.

Fix: Validate key length at construction time, fail fast at startup.

```csharp
public TokenEncryptor(IOptions<EncryptionOptions> options)
{
    _key = Convert.FromBase64String(options.Value.Key);
    if (_key.Length != 32)
        throw new ArgumentException(
            $"Encryption key must be exactly 32 bytes (256-bit). Got {_key.Length} bytes.",
            nameof(options));
}
```

Better: Add `IValidateOptions<EncryptionOptions>` or use `ValidateOnStart()` so the app will not start with a bad key.

**[CRITICAL-2] Twitter refresh sends client_secret in both request body AND Basic auth header**
`OAuthService.cs` lines 101-118 (diff lines 212-229): For Twitter refresh, `client_secret` is included in the form body (line 106) AND in the Basic auth header (lines 117-118). Twitter PKCE flow uses confidential client auth via Basic header only -- the `client_secret` should NOT be in the request body for Twitter. This double-sends credentials and may cause Twitter to reject the request, or worse, it logs `client_secret` in form-encoded body traces.

Fix: Build platform-specific parameter sets. For Twitter refresh, omit `client_secret` from the body (it is already in the Basic header). For LinkedIn, keep it in the body (LinkedIn uses body-based auth):

```csharp
var parameters = new Dictionary<string, string>
{
    ["grant_type"] = "refresh_token",
    ["refresh_token"] = refreshToken,
    ["client_id"] = clientId
};

if (credential.Platform != Platform.Twitter)
    parameters["client_secret"] = clientSecret;
```
**[CRITICAL-3] Static `StateStore` on a scoped service creates cross-request state leaks and blocks horizontal scaling**
`OAuthService.cs` line 25 (diff 136): `StateStore` is `static readonly ConcurrentDictionary` on a class registered as `AddScoped`. This means ALL scoped instances share the same static dictionary -- which is the intent for single-instance, but:
1. If the app restarts, all pending OAuth flows break silently (state lost)
2. Horizontal scaling (multiple instances) means the callback may hit a different instance that has no record of the state
3. There is no upper bound on dictionary size -- a malicious actor can trigger thousands of `GetAuthorizationUrlAsync` calls to grow memory unboundedly (only cleaned on the next legitimate call via `CleanExpiredStates`)

This is documented as "acceptable for single-instance deployment," which is currently true. Flagging as CRITICAL because:
- No size cap = memory exhaustion vector
- The static field on a scoped service is architecturally deceptive (looks scoped, acts singleton)

Fix for now: Add a size cap (e.g., 1000 entries, reject if exceeded) and make the design intent explicit by extracting state storage into a dedicated `IOAuthStateStore` singleton. This also makes the future migration to Redis/DB trivial.

## High Issues

**[HIGH-1] `RefreshTokenAsync` returns plaintext access token to caller**
`OAuthService.cs` line 151 (diff 262): The method returns `newAccessToken` as a raw string. The caller receives a plaintext token. The interface `IOAuthService.RefreshTokenAsync` returns `Task<string>`, and the only signal for failure is `string.Empty`. This is a design smell:
- Callers must remember that empty = failure, non-empty = plaintext token (implicit contract, not enforced by types)
- The plaintext token is now in memory in whatever calling code holds the return value
- This project has a `Result<T>` pattern. Use it.

Fix: Change the return type to `Result<string>`. On failure, return `Result<string>.Fail("Token refresh failed")`. On success, return `Result<string>.Success(newAccessToken)`. This aligns with the codebase convention and makes the error case explicit.

**[HIGH-2] No null-check on `EncryptedRefreshToken` before calling `Decrypt` in `RefreshTokenAsync`**
`OAuthService.cs` line 86 (diff 197): `encryptor.Decrypt(credential.EncryptedRefreshToken!)` uses null-forgiving operator. If `EncryptedRefreshToken` is null (e.g., LinkedIn tokens without refresh grants, or a credential that was stored without one), this throws `ArgumentNullException` from `TokenEncryptor.Decrypt`. The `PlatformCredential` entity has `EncryptedRefreshToken` as `string?` (nullable).

Fix: Guard at the top of `RefreshTokenAsync`:

```csharp
if (string.IsNullOrEmpty(credential.EncryptedRefreshToken))
{
    logger.LogWarning("No refresh token available for {Platform}", credential.Platform);
    credential.IsActive = false;
    credential.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return string.Empty; // or Result<string>.Fail(...)
}
```

**[HIGH-3] `ExchangeCodeAsync` hardcodes scopes instead of reading from token response**
`OAuthService.cs` lines 218 and 253 (diff 329, 364): Both `ExchangeLinkedInCodeAsync` and `ExchangeTwitterCodeAsync` hardcode the `Scopes` string (`"openid profile w_member_social"` and `"tweet.read tweet.write users.read media.write offline.access"`). If the actual scopes granted by the provider differ from what was requested (e.g., user revokes one scope, or provider returns a `scope` field with reduced grants), the stored scopes will be wrong.

Fix: Parse the `scope` field from the token response if present, fall back to the requested scopes only if the provider does not return one.

**[HIGH-4] No error handling on token exchange HTTP responses beyond `EnsureSuccessStatusCode`**
`OAuthService.cs` lines 207 and 242 (diff 318, 353): Both `ExchangeLinkedInCodeAsync` and `ExchangeTwitterCodeAsync` call `response.EnsureSuccessStatusCode()`, which throws `HttpRequestException` with no useful context about what the OAuth provider actually returned (error code, error description). This makes debugging OAuth failures extremely difficult.

Fix: Check `IsSuccessStatusCode`, read the response body on failure, log the provider error response, then throw a descriptive exception:

```csharp
if (!response.IsSuccessStatusCode)
{
    var errorBody = await response.Content.ReadAsStringAsync(ct);
    logger.LogError("Token exchange failed for {Platform}: {Status} {Body}", platform, response.StatusCode, errorBody);
    throw new InvalidOperationException($"Token exchange failed: {response.StatusCode}");
}
```

## Medium Issues

**[MEDIUM-1] `GetAuthorizationUrlAsync` is synchronous but returns `Task<string>`**
`OAuthService.cs` lines 28-42 (diff 139-153): The method does no async work -- it builds a URL and returns `Task.FromResult(url)`. This allocates a `Task` wrapper for no reason. The interface declares it as `Task<string>` presumably for future-proofing, but the current implementation could use `ValueTask<string>` to avoid the allocation, or the interface could be made sync if no implementer needs async.

This is low-impact but worth noting. If the interface stays `Task<string>`, the current implementation is fine. Just do not mark it `async` with no `await`.

**[MEDIUM-2] `CleanExpiredStates` iterates the entire dictionary on every auth URL request**
`OAuthService.cs` lines 259-265 (diff 370-377): Every call to `GetAuthorizationUrlAsync` iterates the full `StateStore` dictionary to clean expired entries. Under normal load this is negligible, but it is O(n) per request for a cleanup operation that could run on a timer instead.

Fix: Use a `Timer` or check only if the dictionary exceeds a size threshold. Or accept it and document the tradeoff -- for a single-user brand assistant, this is fine.

**[MEDIUM-3] PKCE `code_verifier` may exceed RFC 7636 max length**
`OAuthService.cs` lines 172-173 (diff 283-284): The code verifier is generated as `Convert.ToBase64String(RandomNumberGenerator.GetBytes(96))` then base64url-encoded. 96 bytes -> 128 base64 chars -> after trim/replace, the resulting string is 128 characters. RFC 7636 section 4.1 specifies code_verifier must be 43-128 characters. 128 is exactly at the boundary. This works, but any change to the byte count could silently break compliance.

Fix: Add a comment documenting the RFC constraint, or use 64 bytes (producing 86 characters) for more headroom.

**[MEDIUM-4] Test `HttpClient` is not disposed**
`OAuthServiceTests.cs` line 44 (diff 515): `new HttpClient(_httpHandler.Object)` is created but never disposed. The test class implements `IDisposable` but only disposes `_dbContext`. Minor in tests, but still a resource leak.

Fix: Store the `HttpClient` as a field and dispose it in `Dispose()`.

## Low Issues

**[LOW-1] `OAuthTokenResponse` record is a nested private type -- consider extracting**
`OAuthService.cs` lines 274-279 (diff 385-390): The `OAuthTokenResponse` record is a private nested type. This is fine for encapsulation, but if other services ever need to parse OAuth responses (e.g., a future Substack connector), this will need duplication.

No action needed now. Flag for extraction if a second OAuth provider is added.

**[LOW-2] Tests do not verify the exact encrypted values stored in the database**
`OAuthServiceTests.cs` line 94 (diff 602-604): The `ExchangeCodeAsync_LinkedIn_StoresEncryptedTokens` test verifies that `Encrypt` was called on the correct values via Moq, and that a record exists in the DB, but does not assert the actual `EncryptedAccessToken` value on the saved entity.

Fix: Add explicit assertions on the DB entity encrypted field values to ensure the full encrypt-then-store pipeline is correct.

**[LOW-3] No test for concurrent `GetAuthorizationUrlAsync` calls (state uniqueness)**
The state is generated via `RandomNumberGenerator.GetBytes(32)`, which is cryptographically random, so collisions are astronomically unlikely. But there is no test proving that two concurrent calls produce different states. Low priority since the crypto guarantees this.

**[LOW-4] `DateTimeOffset.UtcNow` is called directly instead of through an abstraction**
Multiple places in `OAuthService.cs` (lines 71, 72, 76, 127, 148, 247): `DateTimeOffset.UtcNow` is called directly. This makes time-dependent tests fragile (e.g., testing token expiry). The codebase does not appear to use `TimeProvider` yet, so this is consistent with existing patterns, but it limits testability.

## Approved Items

- AES-256-GCM implementation is correct: nonce/ciphertext/tag layout, random nonce per encryption, proper tag size
- PKCE implementation uses S256 code challenge method with proper base64url encoding
- `ConcurrentDictionary` is appropriate for the in-memory state store given single-instance deployment
- Interface segregation is clean: `ITokenEncryptor` and `IOAuthService` are minimal and focused
- DI registration is correct: singleton for stateless encryptor, scoped for DB-dependent OAuth service
- Test coverage is solid: 7 encryptor tests cover round-trip, uniqueness, wrong key, null input, corruption, empty string, long tokens; 9 OAuth tests cover URL generation, PKCE, state validation, token storage, refresh, deactivation, unsupported platform
- The `Encrypt_ProducesDifferentCiphertextEachTime` test correctly verifies nonce uniqueness
- `Decrypt_WithWrongKey_ThrowsCryptographicException` properly validates authenticated encryption
- OAuth state uses 32 cryptographically random bytes (256 bits of entropy) -- sufficient for CSRF protection
- Configuration uses the Options pattern with `required` properties, consistent with codebase conventions

**Verdict: BLOCK** -- Three critical issues (key validation, Twitter double-credential send, unbounded state store) and four high issues (plaintext token return, null refresh token, hardcoded scopes, missing error context on exchange) must be addressed before merge. The crypto implementation itself is sound, but the surrounding orchestration has security and correctness gaps.
