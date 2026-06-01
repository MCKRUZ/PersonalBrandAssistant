# Section 06 Code Review Interview

## Auto-fixes Applied

### CRITICAL-1: Key length validation in TokenEncryptor
- Added validation in constructor: throw if key is not exactly 32 bytes
- Fail-fast at startup, not at first encrypt call

### CRITICAL-2: Twitter refresh sends client_secret in both body AND Basic auth header
- Removed client_secret from form body for Twitter (already in Basic auth header)
- LinkedIn keeps client_secret in body (body-based auth)

### CRITICAL-3: Unbounded state store
- Added size cap of 1000 entries, rejects new flows when exceeded
- NOT extracting to IOAuthStateStore (YAGNI for single-instance deployment)

### HIGH-1: RefreshTokenAsync returns Result<string> instead of raw string
- Changed IOAuthService.RefreshTokenAsync return type to Task<Result<string>>
- Success returns Result<string>.Success(newAccessToken)
- Failure returns Result<string>.Fail("reason") and deactivates credential

### HIGH-2: Null guard on EncryptedRefreshToken
- Added null/empty check at top of RefreshTokenAsync
- Returns failure Result and deactivates credential if no refresh token

### HIGH-4: Better error handling on token exchange
- Replaced EnsureSuccessStatusCode with explicit check + error body logging
- Throws InvalidOperationException with status code context

### MEDIUM-3: PKCE verifier RFC headroom
- Changed from 96 bytes (128 chars, at RFC boundary) to 64 bytes (86 chars)

### MEDIUM-4: Test HttpClient disposal
- Added HttpClient field and Dispose call in OAuthServiceTests

## Let Go

### HIGH-3: Hardcoded scopes
- LinkedIn and Twitter don't always return scopes in token response
- We requested specific scopes; storing what we asked for is acceptable
- Can revisit if provider scope reduction becomes an issue

### MEDIUM-1: Sync method with Task.FromResult
- Interface is async for future-proofing, implementation is fine
- No allocation concern for low-frequency OAuth URL generation

### MEDIUM-2: O(n) cleanup on every request
- Single-user brand assistant, state store will have ~0-2 entries typically
- Negligible performance impact

### All LOW issues
- LOW-1: OAuthTokenResponse is fine as nested type, no second OAuth provider yet
- LOW-2: Moq verify on Encrypt is sufficient, DB field assertions are redundant
- LOW-3: Crypto random guarantees uniqueness, no test needed
- LOW-4: No TimeProvider in codebase yet, consistent with existing patterns
