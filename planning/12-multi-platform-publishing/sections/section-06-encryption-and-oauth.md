# Section 06: Encryption and OAuth

## Overview

This section implements two foundational security services required by all platform connectors:

1. **`ITokenEncryptor`** -- AES-256-GCM encryption/decryption for tokens stored in `PlatformCredential` entities
2. **`IOAuthService`** -- OAuth 2.0 flow orchestration for LinkedIn and Twitter, including authorization URL construction, code exchange, PKCE (Twitter), token refresh, and CSRF state validation

These services are consumed by every platform connector (sections 07-10), the OAuth API endpoints (section 12), and the frontend connections page (section 13).

## Dependencies

- **Section 02 (Domain Model Changes):** `PlatformCredential` entity must exist with `EncryptedAccessToken`, `EncryptedRefreshToken`, `EncryptedCookies`, `EncryptedIntegrationToken` fields, and the `IAppDbContext.PlatformCredentials` DbSet must be registered.

## Files to Create

| File | Layer | Purpose |
|------|-------|---------|
| `src/PBA.Application/Common/Interfaces/ITokenEncryptor.cs` | Application | Encrypt/decrypt interface |
| `src/PBA.Application/Common/Interfaces/IOAuthService.cs` | Application | OAuth flow interface |
| `src/PBA.Infrastructure/Security/TokenEncryptor.cs` | Infrastructure | AES-256-GCM implementation |
| `src/PBA.Infrastructure/Security/OAuthService.cs` | Infrastructure | OAuth flow implementation |
| `src/PBA.Infrastructure/Configuration/EncryptionOptions.cs` | Infrastructure | Encryption key config |
| `src/PBA.Infrastructure/Configuration/LinkedInOptions.cs` | Infrastructure | LinkedIn OAuth config |
| `src/PBA.Infrastructure/Configuration/TwitterOptions.cs` | Infrastructure | Twitter OAuth config |
| `tests/PBA.Infrastructure.Tests/Security/TokenEncryptorTests.cs` | Tests | Encryption tests |
| `tests/PBA.Infrastructure.Tests/Security/OAuthServiceTests.cs` | Tests | OAuth service tests |

## Files to Modify

| File | Change |
|------|--------|
| `src/PBA.Infrastructure/DependencyInjection.cs` | Register `ITokenEncryptor`, `IOAuthService`, and options bindings |

---

## Tests FIRST

All tests go in `tests/PBA.Infrastructure.Tests/Security/`. xUnit + Moq. Follow the existing pattern from `ContentPublisherTests`.

### TokenEncryptorTests

File: `tests/PBA.Infrastructure.Tests/Security/TokenEncryptorTests.cs`

```csharp
namespace PBA.Infrastructure.Tests.Security;

public class TokenEncryptorTests
{
    // Test: Encrypt_ThenDecrypt_ReturnsOriginalValue
    //   Arrange: create encryptor with valid 256-bit key, plaintext = "my-secret-token"
    //   Act: encrypt, then decrypt the ciphertext
    //   Assert: decrypted value equals original plaintext

    // Test: Encrypt_ProducesDifferentCiphertextEachTime
    //   Arrange: same plaintext encrypted twice
    //   Assert: two ciphertexts are NOT equal (unique nonce per encryption)

    // Test: Decrypt_WithWrongKey_ThrowsCryptographicException
    //   Arrange: encrypt with key A, create new encryptor with key B
    //   Act/Assert: decrypting with key B throws CryptographicException

    // Test: Encrypt_NullInput_ThrowsArgumentNullException
    //   Act/Assert: Encrypt(null) throws ArgumentNullException

    // Test: Decrypt_CorruptedCiphertext_ThrowsCryptographicException
    //   Arrange: encrypt a value, corrupt the ciphertext bytes
    //   Act/Assert: Decrypt throws CryptographicException
}
```

### OAuthServiceTests

File: `tests/PBA.Infrastructure.Tests/Security/OAuthServiceTests.cs`

```csharp
namespace PBA.Infrastructure.Tests.Security;

public class OAuthServiceTests : IDisposable
{
    // Test: GetAuthorizationUrl_LinkedIn_ReturnsCorrectUrlWithScopes
    //   Assert: URL starts with "https://www.linkedin.com/oauth/v2/authorization"
    //   Assert: contains scope=openid%20profile%20w_member_social
    //   Assert: contains client_id from LinkedInOptions
    //   Assert: contains redirect_uri from LinkedInOptions
    //   Assert: contains a state parameter

    // Test: GetAuthorizationUrl_Twitter_IncludesPKCECodeChallenge
    //   Assert: URL starts with "https://twitter.com/i/oauth2/authorize"
    //   Assert: contains code_challenge parameter
    //   Assert: contains code_challenge_method=S256

    // Test: GetAuthorizationUrl_IncludesStateParameter
    //   Assert: returned URL contains a state query parameter
    //   Assert: state is stored in the state store for later validation

    // Test: ExchangeCodeAsync_LinkedIn_StoresEncryptedTokens
    //   Arrange: mock HTTP to return { access_token, refresh_token, expires_in } from LinkedIn token endpoint
    //   Act: ExchangeCodeAsync(Platform.LinkedIn, code, state)
    //   Assert: PlatformCredential saved to DB with encrypted tokens
    //   Assert: ITokenEncryptor.Encrypt called for access and refresh tokens

    // Test: ExchangeCodeAsync_Twitter_UsesCodeVerifierForPKCE
    //   Arrange: call GetAuthorizationUrl first to store code_verifier, mock HTTP token endpoint
    //   Act: ExchangeCodeAsync(Platform.Twitter, code, state)
    //   Assert: HTTP POST to token endpoint includes code_verifier in body

    // Test: ExchangeCodeAsync_InvalidState_ThrowsSecurityException
    //   Arrange: do NOT call GetAuthorizationUrl (no state stored)
    //   Act/Assert: ExchangeCodeAsync with random state throws InvalidOperationException or SecurityException

    // Test: RefreshTokenAsync_LinkedIn_UpdatesStoredTokens
    //   Arrange: existing PlatformCredential with encrypted refresh token, mock HTTP to return new tokens
    //   Act: RefreshTokenAsync(credential)
    //   Assert: credential updated with new encrypted access token and new expiry

    // Test: RefreshTokenAsync_Twitter_HandlesShortLivedTokens
    //   Arrange: Twitter credential with 2-hour access token nearly expired
    //   Act: RefreshTokenAsync(credential)
    //   Assert: new access token stored, expiry ~2 hours from now

    // Test: RefreshTokenAsync_ExpiredRefreshToken_ReturnsFailure
    //   Arrange: mock HTTP to return 401/400 from token endpoint (refresh token expired)
    //   Act: RefreshTokenAsync(credential)
    //   Assert: returns empty string or throws, indicating re-authorization needed
}
```

---

## Implementation Details

### ITokenEncryptor Interface

File: `src/PBA.Application/Common/Interfaces/ITokenEncryptor.cs`

```csharp
namespace PBA.Application.Common.Interfaces;

public interface ITokenEncryptor
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}
```

This interface lives in the Application layer so that connectors and services can depend on it without referencing Infrastructure. The implementation uses AES-256-GCM from `System.Security.Cryptography` (built into .NET, no extra NuGet needed).

### TokenEncryptor Implementation

File: `src/PBA.Infrastructure/Security/TokenEncryptor.cs`

Algorithm: **AES-256-GCM**. This is authenticated encryption -- it provides both confidentiality and integrity verification.

Key design decisions:
- The encryption key comes from `EncryptionOptions.Key` (base64-encoded 256-bit key)
- Each encryption generates a **unique 12-byte nonce** via `RandomNumberGenerator` (this is why the same plaintext produces different ciphertext each call)
- Output format: `base64(nonce + ciphertext + tag)` -- the nonce and 16-byte auth tag are prepended/appended to the ciphertext so decryption can extract them
- Uses `AesGcm` class from `System.Security.Cryptography`
- Constructor takes `IOptions<EncryptionOptions>`
- Throws `ArgumentNullException` for null input
- Throws `CryptographicException` for decryption with wrong key or corrupted data (AES-GCM auth tag verification)

The layout of the encrypted byte array:

```
[12-byte nonce][ciphertext bytes][16-byte auth tag]
```

On decrypt, split the base64-decoded bytes back into nonce (first 12), tag (last 16), and ciphertext (everything in between).

### IOAuthService Interface

File: `src/PBA.Application/Common/Interfaces/IOAuthService.cs`

```csharp
namespace PBA.Application.Common.Interfaces;

using PBA.Domain.Entities;
using PBA.Domain.Enums;

public interface IOAuthService
{
    Task<string> GetAuthorizationUrlAsync(Platform platform, CancellationToken ct);
    Task<PlatformCredential> ExchangeCodeAsync(Platform platform, string code, string state, CancellationToken ct);
    Task<string> RefreshTokenAsync(PlatformCredential credential, CancellationToken ct);
}
```

### OAuthService Implementation

File: `src/PBA.Infrastructure/Security/OAuthService.cs`

This is the central OAuth orchestrator. It handles LinkedIn and Twitter -- the two platforms that use OAuth 2.0. Medium uses a static integration token (no OAuth flow). Substack uses cookie-based auth (handled in SubstackConnector).

Constructor dependencies:
- `IHttpClientFactory` -- for making token exchange and refresh HTTP calls
- `ITokenEncryptor` -- for encrypting tokens before storage
- `IAppDbContext` -- for persisting `PlatformCredential` records
- `IOptions<LinkedInOptions>` -- LinkedIn OAuth client config
- `IOptions<TwitterOptions>` -- Twitter OAuth client config
- `ILogger<OAuthService>`

#### State Management (CSRF Protection)

The `state` parameter prevents CSRF attacks in OAuth flows. Implementation:

- Use `ConcurrentDictionary<string, OAuthStateEntry>` as an in-memory store (single-instance deployment on Mac Mini makes this sufficient; no need for `IDistributedCache`)
- `OAuthStateEntry` holds: `Platform`, `CreatedAt`, and optionally `CodeVerifier` (for Twitter PKCE)
- Generate state via `Convert.ToHexString(RandomNumberGenerator.GetBytes(32))` -- 64-char hex string
- TTL: 10 minutes. Clean up expired entries on each `GetAuthorizationUrl` call (simple sweep)
- On `ExchangeCodeAsync`: look up state, validate it exists and hasn't expired, consume it (remove from dictionary to prevent replay)
- If state is missing or expired: throw `InvalidOperationException("Invalid or expired OAuth state")`

#### GetAuthorizationUrlAsync -- LinkedIn

Construct URL: `https://www.linkedin.com/oauth/v2/authorization` with query parameters:
- `response_type=code`
- `client_id` from `LinkedInOptions.ClientId`
- `redirect_uri` from `LinkedInOptions.RedirectUri`
- `scope=openid profile w_member_social`
- `state` (generated, stored in state dictionary)

#### GetAuthorizationUrlAsync -- Twitter

Construct URL: `https://twitter.com/i/oauth2/authorize` with query parameters:
- `response_type=code`
- `client_id` from `TwitterOptions.ClientId`
- `redirect_uri` from `TwitterOptions.RedirectUri`
- `scope=tweet.read tweet.write users.read media.write offline.access`
- `state` (generated, stored in state dictionary)
- `code_challenge` (SHA-256 hash of generated code verifier, base64url-encoded)
- `code_challenge_method=S256`

PKCE flow: generate a 128-byte random code verifier, compute `SHA256(code_verifier)`, base64url-encode the hash (no padding). Store the verifier in the state entry keyed by state value.

#### ExchangeCodeAsync -- LinkedIn

1. Validate state from state dictionary, extract and remove entry
2. POST to `https://www.linkedin.com/oauth/v2/accessToken` with form-urlencoded body:
   - `grant_type=authorization_code`
   - `code={code}`
   - `redirect_uri={LinkedInOptions.RedirectUri}`
   - `client_id={LinkedInOptions.ClientId}`
   - `client_secret={LinkedInOptions.ClientSecret}`
3. Parse response: `access_token`, `refresh_token`, `expires_in`, `refresh_token_expires_in`
4. Encrypt both tokens via `ITokenEncryptor`
5. Create or update `PlatformCredential` in DB:
   - `Platform = Platform.LinkedIn`
   - `EncryptedAccessToken` = encrypted access token
   - `EncryptedRefreshToken` = encrypted refresh token
   - `AccessTokenExpiresAt` = `DateTimeOffset.UtcNow.AddSeconds(expires_in)` (typically 60 days)
   - `RefreshTokenExpiresAt` = `DateTimeOffset.UtcNow.AddSeconds(refresh_token_expires_in)` (typically 365 days)
   - `Scopes = "openid profile w_member_social"`
   - `IsActive = true`
6. Save and return the credential

#### ExchangeCodeAsync -- Twitter

Same flow as LinkedIn but with PKCE:
1. Validate state, extract code verifier from state entry
2. POST to `https://api.twitter.com/2/oauth2/token` with form-urlencoded body:
   - `grant_type=authorization_code`
   - `code={code}`
   - `redirect_uri={TwitterOptions.RedirectUri}`
   - `client_id={TwitterOptions.ClientId}`
   - `code_verifier={codeVerifier}` (from state entry)
3. Twitter also requires `Authorization: Basic base64(client_id:client_secret)` header
4. Parse response: `access_token`, `refresh_token`, `expires_in` (7200 = 2 hours)
5. Encrypt and persist same as LinkedIn
6. `AccessTokenExpiresAt` = ~2 hours from now

#### RefreshTokenAsync

Works for both LinkedIn and Twitter:
1. Decrypt the refresh token from `credential.EncryptedRefreshToken`
2. Determine token endpoint based on `credential.Platform`:
   - LinkedIn: `https://www.linkedin.com/oauth/v2/accessToken`
   - Twitter: `https://api.twitter.com/2/oauth2/token`
3. POST with form-urlencoded body:
   - `grant_type=refresh_token`
   - `refresh_token={decryptedRefreshToken}`
   - `client_id` and `client_secret` from the appropriate options
4. If HTTP response is success:
   - Encrypt new access token, update credential
   - If response includes a new refresh token (Twitter rotates them), encrypt and update that too
   - Update `AccessTokenExpiresAt`
   - Save to DB
   - Return the new **decrypted** access token (caller needs it for immediate use)
5. If HTTP response is 401/400 (refresh token expired):
   - Set `credential.IsActive = false`
   - Save to DB
   - Return empty string to signal re-authorization is needed

#### Unsupported Platforms

If `GetAuthorizationUrlAsync` or `ExchangeCodeAsync` is called with a platform other than LinkedIn or Twitter, throw `NotSupportedException($"OAuth is not supported for {platform}")`. Medium and Substack credentials are stored directly, not through OAuth.

### Options Classes

#### EncryptionOptions

File: `src/PBA.Infrastructure/Configuration/EncryptionOptions.cs`

```csharp
namespace PBA.Infrastructure.Configuration;

public sealed class EncryptionOptions
{
    public const string SectionName = "Encryption";

    public required string Key { get; init; }
}
```

#### LinkedInOptions

File: `src/PBA.Infrastructure/Configuration/LinkedInOptions.cs`

```csharp
namespace PBA.Infrastructure.Configuration;

public sealed class LinkedInOptions
{
    public const string SectionName = "Publishing:LinkedIn";

    public bool Enabled { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required string RedirectUri { get; init; }
}
```

#### TwitterOptions

File: `src/PBA.Infrastructure/Configuration/TwitterOptions.cs`

```csharp
namespace PBA.Infrastructure.Configuration;

public sealed class TwitterOptions
{
    public const string SectionName = "Publishing:Twitter";

    public bool Enabled { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
    public required string RedirectUri { get; init; }
    public string? ApiKey { get; init; }
    public string? ApiSecret { get; init; }
}
```

### DI Registration

Add to `src/PBA.Infrastructure/DependencyInjection.cs` inside `AddInfrastructureDependencies`:

```csharp
services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));
services.Configure<LinkedInOptions>(configuration.GetSection(LinkedInOptions.SectionName));
services.Configure<TwitterOptions>(configuration.GetSection(TwitterOptions.SectionName));

services.AddSingleton<ITokenEncryptor, TokenEncryptor>();
services.AddScoped<IOAuthService, OAuthService>();
```

`TokenEncryptor` is a singleton -- it holds no state, just the key from options. `OAuthService` is scoped because it accesses `IAppDbContext` (which is scoped via the DbContext lifetime).

### Configuration in User Secrets

The encryption key and OAuth client secrets go in User Secrets for development. Generate a 256-bit key:

```
dotnet user-secrets set "Encryption:Key" "<base64-encoded-32-byte-key>"
dotnet user-secrets set "Publishing:LinkedIn:ClientId" "<value>"
dotnet user-secrets set "Publishing:LinkedIn:ClientSecret" "<value>"
dotnet user-secrets set "Publishing:LinkedIn:RedirectUri" "https://localhost:5001/api/auth/linkedin/callback"
dotnet user-secrets set "Publishing:Twitter:ClientId" "<value>"
dotnet user-secrets set "Publishing:Twitter:ClientSecret" "<value>"
dotnet user-secrets set "Publishing:Twitter:RedirectUri" "https://localhost:5001/api/auth/twitter/callback"
```

For Docker deployment on Mac Mini, these values come from environment variables or a mounted secrets file.

### appsettings.json (non-secret values only)

```json
{
  "Publishing": {
    "LinkedIn": {
      "Enabled": false
    },
    "Twitter": {
      "Enabled": false
    }
  }
}
```

The `Enabled` flags default to `false` -- each platform is opt-in. Connectors (sections 07-10) should check their respective `Enabled` flag before attempting operations.

---

## Key Design Decisions

1. **In-memory state store over IDistributedCache.** PBA is a single-instance deployment on Mac Mini. A `ConcurrentDictionary` with TTL sweep avoids adding Redis/distributed cache dependencies. If multi-instance deployment becomes relevant, swap to `IDistributedCache`.

2. **Separate interfaces (ITokenEncryptor + IOAuthService) over a monolithic auth service.** `ITokenEncryptor` is used by all connectors (Medium needs it for integration tokens, Substack for cookies). `IOAuthService` is only relevant for LinkedIn and Twitter. Keeping them separate means connectors that don't use OAuth don't depend on it.

3. **State stored with code verifier.** Rather than separate storage for PKCE code verifiers, the state entry holds the verifier. This guarantees verifier-state association and simplifies cleanup.

4. **RefreshTokenAsync returns Result<string>.** Changed from plain string to align with project conventions. Success wraps the decrypted token for immediate use by callers. Failure returns a descriptive error and deactivates the credential.

5. **No auto-retry on refresh failure.** If a refresh token is expired, the credential is deactivated and the user must re-authorize. Silently retrying auth failures creates confusing UX.

## Deviations from Plan (Code Review Fixes)

1. **TokenEncryptor key validation (CRITICAL-1):** Constructor now validates key is exactly 32 bytes, failing fast at startup instead of at first encrypt call.

2. **Twitter refresh credential handling (CRITICAL-2):** `client_secret` is only sent in form body for LinkedIn. Twitter uses Basic auth header exclusively, avoiding double-credential exposure.

3. **State store size cap (CRITICAL-3):** Added `MaxPendingStates = 1000` limit. Rejects new OAuth flows when exceeded to prevent memory exhaustion from malicious auth URL generation.

4. **Result<T> return type (HIGH-1):** `RefreshTokenAsync` returns `Task<Result<string>>` instead of `Task<string>` with empty-string failure signal. Makes the success/failure contract type-safe.

5. **Null refresh token guard (HIGH-2):** `RefreshTokenAsync` checks `EncryptedRefreshToken` for null/empty before attempting decrypt, deactivating the credential if absent.

6. **Token exchange error handling (HIGH-4):** Replaced `EnsureSuccessStatusCode()` with explicit status check that reads and logs the provider error body before throwing `InvalidOperationException`.

7. **PKCE verifier size (MEDIUM-3):** Changed from 96 bytes (128 chars, RFC max boundary) to 64 bytes (86 chars) for RFC 7636 headroom.

8. **Test HttpClient disposal (MEDIUM-4):** `OAuthServiceTests` now stores and disposes `HttpClient` in `Dispose()`.

9. **Additional tests:** Added `Encrypt_ThenDecrypt_HandlesEmptyString` and `Encrypt_ThenDecrypt_HandlesLongTokens` (not in original spec).

## Test Summary

- **TokenEncryptorTests:** 7 tests (round-trip, nonce uniqueness, wrong key, null input, corrupted data, empty string, long tokens)
- **OAuthServiceTests:** 9 tests (LinkedIn URL+scopes, Twitter PKCE, state parameter, LinkedIn token exchange, Twitter code verifier, invalid state, LinkedIn refresh, expired refresh, unsupported platform)
- **Total:** 16 new tests, all passing. Full suite: 481 tests green.
