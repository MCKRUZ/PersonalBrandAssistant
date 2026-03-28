# Integration Notes — External Review Feedback

## Integrating

### 1. OAuth state parameter validation
**Suggestion:** Persist `state` server-side with TTL, bind to user + platform, validate on callback.
**Why:** Legitimate CSRF risk. The plan mentioned `state` but didn't specify server-side validation.
**Action:** Add server-side state storage (DB with TTL) to OAuth Manager section. Validate on callback.

### 2. PKCE code_verifier in distributed cache
**Suggestion:** Use DB table with TTL instead of in-memory cache for `code_verifier`.
**Why:** The plan already noted "in-memory cache or short-lived DB entry" — clarify as DB-only for multi-instance safety.
**Action:** Specify DB table with TTL for PKCE verifier storage.

### 3. Idempotency for publishing
**Suggestion:** Add idempotency key to ContentPlatformStatus to prevent duplicate posts on retry.
**Why:** Real risk with retries + background processors. Good catch.
**Action:** Add `IdempotencyKey` field to ContentPlatformStatus. Check before publishing.

### 4. Concurrency control on ContentPlatformStatus
**Suggestion:** Use optimistic concurrency (xmin) on ContentPlatformStatus.
**Why:** Multiple workers (manual + retry processor) could publish simultaneously. Follows existing xmin pattern on Platform entity.
**Action:** Add `Version` (xmin) to ContentPlatformStatus. Acquire lease atomically.

### 5. IHttpClientFactory with typed clients
**Suggestion:** Use typed HttpClients + Polly policies per platform.
**Why:** Correct — raw HttpClient causes socket exhaustion. Standard .NET pattern.
**Action:** Register typed HttpClients in DI section. Add Polly transient fault handling.

### 6. Signed URLs for media instead of UseStaticFiles
**Suggestion:** Replace static file serving with HMAC-signed expiring URLs.
**Why:** Direct static file exposure is a security risk. Signed URLs are simple to implement.
**Action:** Replace UseStaticFiles approach with a media controller that validates HMAC token + expiry.

### 7. PartiallyPublished status
**Suggestion:** Add explicit `PartiallyPublished` overall status.
**Why:** "Published if ANY succeeded" is ambiguous. Explicit status is clearer.
**Action:** Add `PartiallyPublished` to content status logic.

### 8. Richer rate limit response
**Suggestion:** Return `RateLimitDecision { Allowed, RetryAt, Reason }` instead of bool.
**Why:** Pipeline needs `RetryAt` to set `NextRetryAt` on ContentPlatformStatus.
**Action:** Update IRateLimiter.CanMakeRequestAsync return type.

### 9. OAuth scopes tracking
**Suggestion:** Store granted scopes, validate on profile check.
**Why:** Missing scopes are #1 integration failure. Worth tracking.
**Action:** Add `GrantedScopes` field to Platform entity. Define required scopes per platform in config.

### 10. PublishingInProgress status for async uploads
**Suggestion:** Add status for IG container processing / YT upload processing.
**Why:** Instagram video containers need polling before publish. Can't block request thread.
**Action:** Add `Processing` status to PlatformPublishStatus enum. Pipeline sets this for async uploads.

## NOT Integrating

### 1. Multi-tenant / per-user platform connections
**Why not:** This is a single-user personal brand assistant. The Platform entity is already scoped to one user. Multi-tenant adds unnecessary complexity. If needed later, it's a schema migration.

### 2. Move rate limiting out of JSONB
**Why not:** Single-user app = no write contention. The JSONB approach on Platform entity is simple and sufficient. Only one instance running.

### 3. Full OIDC/JWT auth for platform endpoints
**Why not:** The app uses API key auth consistently. Adding OIDC for this single-user app is overengineering. API key middleware is already in place.

### 4. Capabilities model
**Why not:** Nice-to-have but adds complexity for V1. Each adapter already exposes what it supports via its interface. Can add later.

### 5. Audit table for publish attempts
**Why not:** Structured logging already captures this. A dedicated table is overkill for single-user.

### 6. Redirect OAuth to backend instead of Angular
**Why not:** The frontend-initiated flow works fine and is a common pattern. The plan already specifies using the exact same callback URL in both auth request and token exchange.

### 7. Token encryption key rotation
**Why not:** ASP.NET Data Protection handles key rotation automatically. No custom implementation needed.

### 8. Separate access vs refresh token encryption
**Why not:** Both are already stored as separate `byte[]` fields with the same DPAPI encryption. Separate keys add complexity without benefit for single-user.
