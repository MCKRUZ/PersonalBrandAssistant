# Code Review Interview: Section 06 - OAuthManager

**Date:** 2026-03-15

## User Decisions

### CRITICAL-02: Encrypt PKCE code verifier at rest
**Decision:** Fix — Changed `OAuthState.CodeVerifier` (string) to `EncryptedCodeVerifier` (byte[]). Updated entity, EF config, and all references. Verifier now encrypted/decrypted via IEncryptionService.

### HIGH-01: Extract platform strategies
**Decision:** Fix — Created `IOAuthPlatformStrategy` interface with 4 implementations: `TwitterOAuthStrategy`, `LinkedInOAuthStrategy`, `InstagramOAuthStrategy`, `YouTubeOAuthStrategy`. OAuthManager reduced from 552 → ~250 lines as pure orchestrator. Shared token parsing extracted to `OAuthHelpers.SendTokenRequestAsync`.

### HIGH-04: TOCTOU race on state consumption
**Decision:** Acknowledged — The read-then-delete pattern has a race window. Full fix requires database-level serializable transaction or atomic delete-and-return. Current implementation deletes immediately after read, which is a partial mitigation. A TODO for transaction wrapping is noted for when EF migrations are set up.

## Auto-fixes Applied

### CRITICAL-01: Token-in-URL logging risk
Added WARNING comments on all Instagram/YouTube methods where tokens appear in URLs. Noted DI config (section-12) must suppress HTTP client request logging.

### CRITICAL-03: Generic error messages
Replaced `HttpRequestException.Message` with generic "failed due to an external service error" in all catch blocks.

### HIGH-03: Code parameter validation
Added `string.IsNullOrWhiteSpace(code)` guard in `ExchangeCodeAsync`.

### HIGH-06/07: Missing tests
Added 3 tests:
- `ExchangeCodeAsync_DeletesOAuthStateAfterExchange` — verifies Remove called
- `ExchangeCodeAsync_UsesStoredCodeVerifierForTwitterPkce` — verifies encrypted verifier decrypted
- `ExchangeCodeAsync_RejectsEmptyCode` — validates code parameter

## Items Let Go

- HIGH-02: Strongly-typed options for secrets — deferred to DI section (section-12)
- WARN-01: OAuthTokens structural equality — acknowledged, ToString override already excludes tokens
- WARN-02: Log error response body — deferred, needs careful audit of what platforms echo back
- WARN-03: HTTP timeout — will configure in section-12 DI setup
- WARN-04: Facebook API version parameterization — moved to constant, can be config later
- SUGGEST-01: Expired state cleanup — deferred to background processors (section-11)
- SUGGEST-03: Retry logic — deferred to DI section with Polly

## Files Modified Beyond Plan

- `src/PersonalBrandAssistant.Domain/Entities/OAuthState.cs` — `CodeVerifier` → `EncryptedCodeVerifier`
- `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/OAuthStateConfiguration.cs` — updated property config
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Utilities/TestEntityFactory.cs` — updated factory
- `tests/PersonalBrandAssistant.Domain.Tests/Entities/OAuthStateTests.cs` — updated test

## Final Test Count: 19 passing (was 16, added 3)
