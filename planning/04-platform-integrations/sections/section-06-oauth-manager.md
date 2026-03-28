# Section 06: OAuth Manager

## Overview

This section implements the `OAuthManager` class -- the central service for handling OAuth lifecycle operations across all four supported platforms (Twitter/X, LinkedIn, Instagram, YouTube). It handles auth URL generation with server-side state validation, PKCE support for Twitter, authorization code exchange, token refresh, and token revocation. All tokens are encrypted via `IEncryptionService` before storage.

## Dependencies

- **Section 01 (Domain Entities):** `OAuthState` entity, `Platform` entity with `GrantedScopes` field
- **Section 02 (Interfaces & Models):** `IOAuthManager` interface, `OAuthTokens` record, `OAuthAuthorizationUrl` record, `PlatformIntegrationOptions`
- **Section 03 (EF Core Config):** `OAuthStateConfiguration` with unique index on `State`, index on `ExpiresAt`

## Existing Codebase Context

**`IEncryptionService`** at `src/PersonalBrandAssistant.Application/Common/Interfaces/IEncryptionService.cs`:
```csharp
public interface IEncryptionService
{
    byte[] Encrypt(string plaintext);
    string Decrypt(byte[] ciphertext);
}
```

**`Platform` entity** has `EncryptedAccessToken`, `EncryptedRefreshToken` (byte[]), `TokenExpiresAt`, `IsConnected`, `GrantedScopes` (string[]?, added in Section 01).

**`Result<T>`** supports `Success(T)`, `Failure(ErrorCode, errors)`, `NotFound(message)`, `ValidationFailure(errors)`.

**`PlatformType` enum:** `TwitterX`, `LinkedIn`, `Instagram`, `YouTube`.

## Files to Create

| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/OAuthManager.cs` | OAuth lifecycle orchestrator (~250 lines) |
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/OAuth/IOAuthPlatformStrategy.cs` | Strategy interface for platform-specific OAuth |
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/OAuth/OAuthHelpers.cs` | Shared token request/parsing logic |
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/OAuth/TwitterOAuthStrategy.cs` | Twitter PKCE + Basic auth |
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/OAuth/LinkedInOAuthStrategy.cs` | Standard OAuth2 flow |
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/OAuth/InstagramOAuthStrategy.cs` | Two-step token exchange (short → long-lived) |
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/OAuth/YouTubeOAuthStrategy.cs` | Google OAuth with offline access |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/OAuthManagerTests.cs` | 19 unit tests |

> **Deviation from plan:** Original plan had a single monolithic `OAuthManager.cs` (~552 lines). Code review extracted platform logic into strategy pattern, reducing OAuthManager to ~250 lines as a pure orchestrator. Folder renamed from `Services/Platform/` to `Services/PlatformServices/` to avoid namespace collision with `Domain.Entities.Platform`.

## Required OAuth Scopes

| Platform | Required Scopes |
|----------|----------------|
| Twitter/X | `tweet.read`, `tweet.write`, `users.read`, `offline.access` |
| LinkedIn | `w_member_social`, `r_liteprofile` |
| Instagram | `instagram_basic`, `instagram_content_publish`, `pages_show_list` |
| YouTube | `youtube`, `youtube.upload` |

## Tests (Write First)

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/OAuthManagerTests.cs`

Use xUnit + Moq. Mock `IApplicationDbContext` (with `AsyncQueryableHelpers`), `IEncryptionService`, `IOptions<PlatformIntegrationOptions>`, and `HttpClient` (via `HttpMessageHandler` mock).

```csharp
// --- GenerateAuthUrlAsync ---

// Test: GenerateAuthUrlAsync creates OAuthState entry in DB with TTL
//   Assert: OAuthState added with ExpiresAt ~10 minutes from now

// Test: GenerateAuthUrlAsync returns cryptographically random state parameter
//   Assert: two calls return different State values

// Test: GenerateAuthUrlAsync includes PKCE code_verifier in OAuthState for Twitter
//   Assert: OAuthState.CodeVerifier is non-null, URL contains code_challenge

// Test: GenerateAuthUrlAsync returns correct platform-specific OAuth URL
//   Assert: Twitter -> twitter.com/i/oauth2/authorize
//   Assert: LinkedIn -> linkedin.com/oauth/v2/authorization
//   Assert: Instagram -> facebook.com/v19.0/dialog/oauth
//   Assert: YouTube -> accounts.google.com/o/oauth2/v2/auth

// Test: Twitter auth URL includes PKCE challenge and required scopes
// Test: LinkedIn auth URL includes correct scopes
// Test: Instagram auth URL includes Facebook OAuth parameters
// Test: YouTube auth URL includes Google OAuth parameters and access_type=offline

// --- ExchangeCodeAsync ---

// Test: ExchangeCodeAsync validates state against DB and rejects mismatch
// Test: ExchangeCodeAsync deletes OAuthState entry after successful exchange
// Test: ExchangeCodeAsync rejects expired state entries
// Test: ExchangeCodeAsync stores encrypted tokens in Platform entity
// Test: ExchangeCodeAsync stores granted scopes in Platform entity
// Test: ExchangeCodeAsync uses code_verifier from OAuthState for Twitter PKCE

// --- RefreshTokenAsync ---

// Test: RefreshTokenAsync refreshes and updates encrypted tokens
// Test: RefreshTokenAsync handles invalid_grant for YouTube (marks disconnected)

// --- RevokeTokenAsync ---

// Test: RevokeTokenAsync calls platform revocation endpoint and clears tokens
// Test: RevokeTokenAsync sets IsConnected=false on Platform entity
```

## Implementation Details

### OAuthManager

File: `src/PersonalBrandAssistant.Infrastructure/Services/Platform/OAuthManager.cs`

**Constructor dependencies:**
- `IApplicationDbContext` -- Platform and OAuthState entities
- `IEncryptionService` -- encrypting/decrypting tokens
- `IOptions<PlatformIntegrationOptions>` -- callback URLs
- `IHttpClientFactory` -- token exchange/refresh/revoke HTTP calls
- `ILogger<OAuthManager>` -- structured logging (never log token values)

**Scoped lifetime.**

### GenerateAuthUrlAsync

1. Generate random `state` using `RandomNumberGenerator.GetBytes(32)` -> URL-safe Base64
2. For Twitter: generate PKCE `code_verifier` (43-128 char random), compute `code_challenge` = Base64Url(SHA256(code_verifier))
3. Create `OAuthState` entity: `State`, `Platform`, `CodeVerifier` (Twitter only), `CreatedAt = UtcNow`, `ExpiresAt = CreatedAt + 10min`
4. Save to DB
5. Build platform-specific auth URL:
   - **Twitter:** `https://twitter.com/i/oauth2/authorize?response_type=code&client_id={}&redirect_uri={}&scope=tweet.read%20tweet.write%20users.read%20offline.access&state={}&code_challenge={}&code_challenge_method=S256`
   - **LinkedIn:** `https://www.linkedin.com/oauth/v2/authorization?response_type=code&client_id={}&redirect_uri={}&scope=w_member_social%20r_liteprofile&state={}`
   - **Instagram:** `https://www.facebook.com/v19.0/dialog/oauth?client_id={}&redirect_uri={}&scope=instagram_basic,instagram_content_publish,pages_show_list&state={}`
   - **YouTube:** `https://accounts.google.com/o/oauth2/v2/auth?response_type=code&client_id={}&redirect_uri={}&scope=https://www.googleapis.com/auth/youtube%20https://www.googleapis.com/auth/youtube.upload&state={}&access_type=offline&prompt=consent`
6. Return `Result.Success(new OAuthAuthorizationUrl(url, state))`

### ExchangeCodeAsync

1. Look up `OAuthState` by `state`. Not found -> `Result.Failure` (CSRF)
2. Check `ExpiresAt` -- expired -> delete entry, return failure
3. For Twitter: get `CodeVerifier` from OAuthState
4. Delete OAuthState entry (single-use)
5. POST to platform token endpoint:
   - **Twitter:** `POST https://api.x.com/2/oauth2/token` with Basic auth
   - **LinkedIn:** `POST https://www.linkedin.com/oauth/v2/accessToken`
   - **Instagram:** Two-step: exchange short-lived, then exchange for long-lived
   - **YouTube:** `POST https://oauth2.googleapis.com/token`
6. Parse response, encrypt tokens, update Platform entity
7. Set `IsConnected = true`, `GrantedScopes`, `TokenExpiresAt`
8. SaveChangesAsync, return `Result.Success(new OAuthTokens(...))`

### RefreshTokenAsync

1. Load Platform by type, decrypt refresh token
2. POST with `grant_type=refresh_token` per platform
   - **Instagram special:** Uses `GET /refresh_access_token?grant_type=ig_refresh_token&access_token={current}`
3. Handle `invalid_grant` (esp. YouTube): set `IsConnected = false`, clear tokens, return failure
4. On success: encrypt new tokens, update Platform, save

### RevokeTokenAsync

1. Load Platform, decrypt access token
2. Call revocation endpoint per platform:
   - **Twitter:** `POST https://api.x.com/2/oauth2/revoke`
   - **LinkedIn:** No API -- just clear local tokens
   - **Instagram:** `DELETE https://graph.facebook.com/{userId}/permissions`
   - **YouTube:** `POST https://oauth2.googleapis.com/revoke?token={}`
3. Clear all token fields, set `IsConnected = false`, `GrantedScopes = null`
4. SaveChangesAsync

### Error Handling

- Wrap HTTP calls in try/catch for `HttpRequestException`
- Return `Result.Failure` with descriptive error
- Log operations at Info, errors at Error, with `PlatformType` context
- **NEVER log token values**

### Secret Access

ClientId/ClientSecret are NOT in `PlatformIntegrationOptions`. Access them directly from `IConfiguration`:
- `PlatformIntegrations:Twitter:ClientId`
- `PlatformIntegrations:Twitter:ClientSecret`
- `PlatformIntegrations:Instagram:AppId`
- `PlatformIntegrations:Instagram:AppSecret`
- etc.

These are stored in User Secrets (dev) / Azure Key Vault (prod).

## Implementation Deviations

### CRITICAL: Encrypted PKCE Code Verifier
- **Plan:** `OAuthState.CodeVerifier` stored as plaintext `string`
- **Actual:** Changed to `EncryptedCodeVerifier` (`byte[]`), encrypted/decrypted via `IEncryptionService`
- **Rationale:** Code verifier is a secret equivalent; must be encrypted at rest like tokens
- **Files modified beyond plan:** `OAuthState.cs`, `OAuthStateConfiguration.cs`, `TestEntityFactory.cs`, `OAuthStateTests.cs`

### Strategy Pattern Extraction
- **Plan:** Single `OAuthManager.cs` with all platform logic inline
- **Actual:** Extracted `IOAuthPlatformStrategy` with 4 implementations + `OAuthHelpers` shared helper
- **Rationale:** Original was 552 lines; strategy pattern reduces to ~250 lines and isolates platform concerns

### Security Hardening
- `HttpRequestException.Message` replaced with generic "failed due to an external service error" in all catch blocks
- WARNING comments on Instagram/YouTube methods where tokens appear in URLs (DI config must suppress HTTP client request logging)
- `string.IsNullOrWhiteSpace(code)` guard added to `ExchangeCodeAsync`

### TOCTOU Race Acknowledged
- Read-then-delete pattern on OAuthState has a race window
- Current mitigation: immediate delete after read
- Full fix (database-level atomic delete-and-return) deferred to when EF migrations are set up

### Deferred Items
- Strongly-typed options for secrets → section-12 (DI configuration)
- HTTP timeout configuration → section-12
- Expired state cleanup → section-11 (background processors)
- Retry logic with Polly → section-12
- Facebook API version parameterization → moved to constant

### Final Test Count: 19 passing
