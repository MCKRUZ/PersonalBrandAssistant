# Section 10: API Endpoints

## Overview

This section implements the Minimal API endpoints for platform connection management and media serving. Two endpoint classes are created: `PlatformEndpoints` for OAuth flows, platform status, test posts, and engagement retrieval; and `MediaEndpoints` for serving media files with HMAC signature validation.

These endpoints follow the existing codebase conventions established by `ContentEndpoints`, `AgentEndpoints`, and others -- static classes with extension methods on `IEndpointRouteBuilder`, using `MapGroup` with tags, and leveraging `Result<T>.ToHttpResult()` for consistent error responses.

## Dependencies

- **Section 06 (OAuth Manager):** `IOAuthManager` with `GenerateAuthUrlAsync`, `ExchangeCodeAsync`, `RevokeTokenAsync`
- **Section 09 (Publishing Pipeline):** `IPublishingPipeline` for test-post functionality
- **Section 04 (Media Storage):** `IMediaStorage` for media file serving and HMAC-signed URL validation
- **Section 02 (Interfaces/Models):** `ISocialPlatform`, `RateLimitStatus`, `EngagementStats`, `OAuthAuthorizationUrl`, `OAuthTokens`
- **Section 05 (Rate Limiter):** `IRateLimiter` for status endpoint
- **Existing infrastructure:** `Result<T>`, `ResultExtensions.ToHttpResult()`, `ApiKeyMiddleware` (`X-Api-Key` header), `ApplicationDbContext`, `PlatformType` enum

## Route Structure

Platform endpoints group:
```
MapGroup("/api/platforms").WithTags("Platforms")

GET    /                          -> List all platforms with connection status
GET    /{type}/auth-url           -> Generate OAuth URL (returns { url, state })
POST   /{type}/callback           -> Exchange code for tokens (body: { code, codeVerifier?, state })
DELETE /{type}/disconnect         -> Revoke tokens, set IsConnected=false
GET    /{type}/status             -> Token validity, rate limits, last sync, granted scopes
POST   /{type}/test-post          -> Publish test post to verify connection
GET    /{type}/engagement/{postId}-> Get engagement stats for a published post
```

Media endpoints group:
```
MapGroup("/api/media").WithTags("Media")

GET    /{fileId}                  -> Serve media file (validates HMAC token + expiry from query params)
```

All platform endpoints require API key auth (handled by existing `ApiKeyMiddleware`). The media endpoint uses HMAC signature validation instead of API key auth.

## Files to Create

### `src/PersonalBrandAssistant.Api/Endpoints/PlatformEndpoints.cs`

Static class following existing conventions. Key implementation details:

- `MapPlatformEndpoints` extension method on `IEndpointRouteBuilder`
- `{type}` route parameter is a string parsed to `PlatformType` enum (return 400 if invalid)
- Each handler method is a private static async method injecting services via parameter injection
- Use `Result<T>.ToHttpResult()` from existing `ResultExtensions` for error responses

**Endpoint handler details:**

1. **GET /** -- Inject `ApplicationDbContext`, query all Platform entities, return list with `Type`, `IsConnected`, `LastSyncAt`, `GrantedScopes` fields.

2. **GET /{type}/auth-url** -- Inject `IOAuthManager`, parse type, call `GenerateAuthUrlAsync`, return `{ url, state }` on success.

3. **POST /{type}/callback** -- Inject `IOAuthManager`, accept body as `OAuthCallbackRequest` record (`string Code, string? CodeVerifier, string State`). Call `ExchangeCodeAsync` with all fields including `state`. If state validation fails in the manager, it returns a failure Result which maps to 400 via `ToHttpResult()`.

4. **DELETE /{type}/disconnect** -- Inject `IOAuthManager`, call `RevokeTokenAsync`, return 204 on success.

5. **GET /{type}/status** -- Inject `ApplicationDbContext` and `IRateLimiter`. Load Platform entity, call `GetStatusAsync` on rate limiter. Return composite object with `isConnected`, `tokenExpiresAt`, `lastSyncAt`, `grantedScopes`, and `rateLimit` fields.

6. **POST /{type}/test-post** -- Inject `IPublishingPipeline` or resolve `ISocialPlatform` from `IEnumerable<ISocialPlatform>`. Create a simple test `PlatformContent` and publish. Return the `PublishResult` on success.

7. **GET /{type}/engagement/{postId}** -- Resolve correct `ISocialPlatform` from `IEnumerable<ISocialPlatform>` by matching `Type`, call `GetEngagementAsync`, return stats.

**Request/Response records** (defined in the same file or a companion file):

```csharp
public record OAuthCallbackRequest(string Code, string? CodeVerifier, string State);
```

**PlatformType parsing helper:** A private static method that tries `Enum.TryParse<PlatformType>` (case-insensitive) and returns `Results.BadRequest` with a descriptive message on failure. Each handler calls this before proceeding.

### `src/PersonalBrandAssistant.Api/Endpoints/MediaEndpoints.cs`

Static class for media file serving with HMAC validation.

- `MapMediaEndpoints` extension method
- **GET /{fileId}** -- Accepts `token` and `expires` query parameters. Validates:
  1. `expires` timestamp has not passed (return 403 if expired)
  2. Recomputes HMAC-SHA256 over `{fileId}:{expires}` using the signing key from `MediaStorageOptions.SigningKey`
  3. Compares computed HMAC with provided `token` using constant-time comparison (`CryptographicOperations.FixedTimeEquals`)
  4. If valid, calls `IMediaStorage.GetStreamAsync(fileId)`, determines content type from file extension, returns `Results.File(stream, contentType)`
  5. Returns 403 for invalid/expired tokens, 404 for missing files

- This endpoint does NOT go through `ApiKeyMiddleware` -- it uses its own HMAC auth. If the middleware applies globally, consider using `.AllowAnonymous()` or excluding the `/api/media` path in the middleware.

### `src/PersonalBrandAssistant.Api/Program.cs` (modify)

Add two lines after existing endpoint mappings:
```csharp
app.MapPlatformEndpoints();
app.MapMediaEndpoints();
```

## Tests

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/PlatformEndpointsTests.cs`

Integration tests using `CustomWebApplicationFactory` with Testcontainers PostgreSQL, following the exact pattern from `ContentEndpointsTests`. The factory will need to be updated (in section 12) to remove the new background services (`TokenRefreshProcessor`, `PlatformHealthMonitor`, `PublishCompletionPoller`), but for now test setup can handle this locally.

```csharp
// File: tests/PersonalBrandAssistant.Infrastructure.Tests/Api/PlatformEndpointsTests.cs

/// Test: GET /api/platforms returns all platforms with connection status
/// Arrange: Seed Platform entities in DB via ApplicationDbContext
/// Act: GET /api/platforms with authenticated client
/// Assert: 200 OK, response contains platform list with Type and IsConnected fields

/// Test: GET /api/platforms/{type}/auth-url returns URL and state
/// Arrange: Configure OAuth settings (ClientId etc.) via test configuration
/// Act: GET /api/platforms/TwitterX/auth-url with authenticated client
/// Assert: 200 OK, response body has non-empty "url" and "state" properties

/// Test: POST /api/platforms/{type}/callback exchanges code for tokens with state validation
/// Arrange: Call auth-url endpoint first to generate valid state, seed OAuthState in DB
/// Act: POST /api/platforms/TwitterX/callback with { code, state } body
/// Assert: 200 OK (or mock the HTTP call to platform and verify token storage)
/// Note: This test will likely need to mock the external HTTP call to the platform's token endpoint

/// Test: POST /api/platforms/{type}/callback rejects invalid state (returns 400)
/// Arrange: No OAuthState seeded for the given state value
/// Act: POST /api/platforms/TwitterX/callback with { code: "test", state: "invalid-state" }
/// Assert: 400 Bad Request

/// Test: DELETE /api/platforms/{type}/disconnect revokes tokens and sets IsConnected=false
/// Arrange: Seed a connected Platform entity
/// Act: DELETE /api/platforms/TwitterX/disconnect with authenticated client
/// Assert: 204 No Content, verify Platform.IsConnected == false in DB

/// Test: GET /api/platforms/{type}/status returns token validity, rate limits, scopes
/// Arrange: Seed connected Platform entity with token expiry and granted scopes
/// Act: GET /api/platforms/TwitterX/status with authenticated client
/// Assert: 200 OK, response contains isConnected, tokenExpiresAt, grantedScopes fields

/// Test: POST /api/platforms/{type}/test-post publishes test post
/// Arrange: Seed connected Platform with valid tokens, mock external API via HttpMessageHandler
/// Act: POST /api/platforms/TwitterX/test-post with authenticated client
/// Assert: 200 OK with publish result containing platformPostId and postUrl

/// Test: GET /api/platforms/{type}/engagement/{postId} returns engagement stats
/// Arrange: Seed connected Platform, mock external API response for engagement
/// Act: GET /api/platforms/TwitterX/engagement/12345 with authenticated client
/// Assert: 200 OK with engagement stats (likes, shares, etc.)

/// Test: All endpoints require API key auth (return 401 without key)
/// Arrange: Use _factory.CreateClient() (not CreateAuthenticatedClient())
/// Act: GET /api/platforms
/// Assert: 401 Unauthorized
```

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/MediaEndpointsTests.cs`

```csharp
// File: tests/PersonalBrandAssistant.Infrastructure.Tests/Api/MediaEndpointsTests.cs

/// Test: GET /api/media/{fileId} serves file with valid HMAC token
/// Arrange: Save a test file via IMediaStorage, generate signed URL with valid token and future expiry
/// Act: GET /api/media/{fileId}?token={hmac}&expires={futureTimestamp}
/// Assert: 200 OK, response content type matches, body contains file bytes

/// Test: GET /api/media/{fileId} returns 403 with invalid HMAC token
/// Arrange: Use a valid fileId but tampered/incorrect HMAC token
/// Act: GET /api/media/{fileId}?token=invalid&expires={futureTimestamp}
/// Assert: 403 Forbidden

/// Test: GET /api/media/{fileId} returns 403 when URL has expired
/// Arrange: Use valid fileId, valid HMAC but with expires timestamp in the past
/// Act: GET /api/media/{fileId}?token={hmac}&expires={pastTimestamp}
/// Assert: 403 Forbidden

/// Test: GET /api/media/{fileId} returns 404 for non-existent fileId
/// Arrange: Generate valid HMAC for a fileId that does not exist in storage
/// Act: GET /api/media/{nonExistentFileId}?token={hmac}&expires={futureTimestamp}
/// Assert: 404 Not Found
```

## Deviations from Plan

- **Tests:** Unit tests verifying contracts/logic instead of full `WebApplicationFactory` integration tests. Integration tests deferred to E2E phase.
- **Test-post endpoint:** Added `TestPostRequest` record with `Confirm` flag and optional `Message` — reviewer flagged no safeguards as HIGH risk.
- **GetStatus response:** Returns `TokenExpiresAt`, `LastSyncAt`, `GrantedScopes` from Platform entity directly. Does not inject `IRateLimiter` — rate limit status deferred.
- **ListPlatforms:** Added `LastSyncAt` to projection per review.
- **GetEngagement:** Added `postId` input validation (non-empty, max 256 chars).
- **Immutability:** Test post uses `ImmutableDictionary<string, string>.Empty` instead of `new Dictionary<string, string>()`.
- **CancellationToken:** Added to `ListPlatforms` handler (was missing).
- **MediaEndpoints:** Already existed from prior session (section-04). Not modified in this section.
- **MediaEndpointsTests:** Already existed from prior session. Not in this section's scope.
- **11 tests** (vs plan's integration test approach): 2 record tests, 3 OAuth contract tests, 2 adapter/publish tests, 1 engagement test, 1 enum parsing, 1 adapter resolution, 1 TestPostRequest test.

## Files Created/Modified

- `src/PersonalBrandAssistant.Api/Endpoints/PlatformEndpoints.cs` — 7 Minimal API endpoints + `OAuthCallbackRequest` + `TestPostRequest` records
- `src/PersonalBrandAssistant.Api/Program.cs` — Added `app.MapPlatformEndpoints()`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/PlatformEndpointsTests.cs` — 11 unit tests

## Implementation Notes

**PlatformType route parameter parsing:** The `{type}` parameter in routes like `/api/platforms/{type}/auth-url` arrives as a string. Use `Enum.TryParse<PlatformType>(type, ignoreCase: true, out var platformType)` to convert. If parsing fails, return `Results.BadRequest($"Invalid platform type: {type}. Valid values: {string.Join(", ", Enum.GetNames<PlatformType>())}")`. Extract this into a helper to avoid repetition across all handlers.

**Resolving the correct ISocialPlatform adapter:** Multiple `ISocialPlatform` implementations are registered in DI. Inject `IEnumerable<ISocialPlatform>` and use `.FirstOrDefault(p => p.Type == platformType)` to resolve the correct adapter. If not found, return 404.

**Test-post content:** For the test-post endpoint, create a minimal `PlatformContent` with text like `"Test post from Personal Brand Assistant - {DateTime.UtcNow:O}"` and no media. This verifies the connection is working without requiring the full publishing pipeline.

**Media endpoint auth bypass:** The media endpoint serves files to external platforms (Instagram containers need public URLs). It must not require API key auth. If `ApiKeyMiddleware` runs globally, either:
- Check the path in the middleware and skip `/api/media` routes, or
- Use endpoint metadata (`.AllowAnonymous()` or a custom attribute) that the middleware respects

**HMAC computation for media validation:** The signing key comes from `MediaStorageOptions.SigningKey` (injected via `IOptions<MediaStorageOptions>`). The HMAC is computed as `HMACSHA256(SigningKey, "{fileId}:{expiresUnixTimestamp}")` and Base64URL-encoded. This must match the computation in `LocalMediaStorage.GetSignedUrlAsync` from section 04.

**Content-type detection for media serving:** Use a lookup from file extension to MIME type. The `Microsoft.AspNetCore.StaticFiles` package provides `FileExtensionContentTypeProvider` which can be used: `new FileExtensionContentTypeProvider().TryGetContentType(path, out var contentType)`. Default to `application/octet-stream` if unknown.