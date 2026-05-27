# Section 12: API Endpoints

## Overview

This section adds new API endpoints and modifies existing ones to support multi-platform publishing. It covers four categories of endpoints:

1. **OAuth endpoints** for initiating and handling OAuth flows (LinkedIn, Twitter)
2. **Platform management endpoints** for listing platforms and storing manual credentials (Medium token)
3. **Updated content endpoints** to accept `targetPlatforms` on publish/schedule and add retry/status routes
4. **Updated content DTOs** for create/update to include `TargetPlatforms`

## Dependencies

- **Section 05 (Publisher Refactor):** The updated `PublishContent.Command` and `IContentPublisher` with multi-platform support must exist before these endpoints can route to them
- **Section 06 (Encryption and OAuth):** `IOAuthService`, `ITokenEncryptor`, and `PlatformCredential` entity must be available for the OAuth and credential-storage endpoints
- **Section 11 (Retry Handler):** `IPublishRetryHandler` must be registered for the retry endpoint

## Actual Implementation

### Files Created

| File | Purpose |
|------|---------|
| `src/PBA.Api/Endpoints/OAuthEndpoints.cs` | OAuth authorize, callback, status, disconnect |
| `src/PBA.Api/Endpoints/PlatformEndpoints.cs` | List platforms, store credentials |
| `src/PBA.Application/Features/Content/Dtos/PublishContentRequest.cs` | Request DTO with `TargetPlatforms` |
| `src/PBA.Application/Features/Content/Dtos/PublishStatusDto.cs` | Per-platform publish status response DTO |
| `src/PBA.Application/Features/Content/Dtos/PlatformStatusDto.cs` | Platform connection status response DTO |
| `src/PBA.Application/Features/Content/Dtos/StoreCredentialsRequest.cs` | Request DTO for manual credential storage |
| `src/PBA.Application/Features/Content/Dtos/PlatformPublishDto.cs` | Per-platform publish record DTO |
| `src/PBA.Application/Features/Content/Queries/GetPublishStatus.cs` | MediatR query for per-platform publish status |
| `src/PBA.Application/Features/Content/Commands/RetryPlatformPublish.cs` | MediatR command for manual retry |
| `tests/PBA.Api.Tests/Endpoints/OAuthEndpointsTests.cs` | 7 integration tests for OAuth endpoints |
| `tests/PBA.Api.Tests/Endpoints/PlatformEndpointsTests.cs` | 6 integration tests for platform management |

### Files Modified

| File | Change |
|------|--------|
| `src/PBA.Api/Endpoints/ContentEndpoints.cs` | Added `publish-status`, `retry/{platform}` routes; updated `publish` to accept optional body with `targetPlatforms`; updated `schedule` to pass `TargetPlatforms` |
| `src/PBA.Api/Program.cs` | Added `app.MapOAuthEndpoints()` and `app.MapPlatformEndpoints()` |
| `src/PBA.Application/Features/Content/Commands/ScheduleContent.cs` | Added `TargetPlatforms` parameter to `Command` record |
| `src/PBA.Application/Features/Content/Dtos/CreateContentRequest.cs` | Added `TargetPlatforms` property |
| `src/PBA.Application/Features/Content/Dtos/UpdateContentRequest.cs` | Added `TargetPlatforms` property |
| `src/PBA.Application/Features/Content/Dtos/ScheduleContentRequest.cs` | Added `TargetPlatforms` property |
| `tests/PBA.Api.Tests/TestWebApplicationFactory.cs` | Registered mocks for `IOAuthService`, `ITokenEncryptor`, `IPublishRetryHandler` |
| `tests/PBA.Api.Tests/Endpoints/ContentEndpointsTests.cs` | Added 4 tests: publish with platforms, publish-status 200/404, retry invalid platform |

### Deviations from Plan

1. **RetryPublishRequest.cs deleted** -- Plan called for a request DTO for retry, but the route parses platform from URL path (`/retry/{platform}`), making a request body unnecessary. Created during implementation but removed in code review.

2. **Substack credential storage returns 400** -- Plan called for Substack email/password credential storage with cookie extraction. Implementation returns `400 "Substack credential storage via API is not yet supported. Use browser login."` because the headless login flow is complex and out of scope for this section.

3. **Blog credential endpoint returns explicit error** -- Plan's default case gave a misleading "uses OAuth" error for Blog. Implementation adds explicit `case Platform.Blog: return BadRequest("Blog does not require credentials.")`.

4. **OAuth callback has logging** -- Plan had a bare `catch` block. Code review added `ILoggerFactory` injection and `logger.LogError(ex, ...)` for non-SecurityException failures.

5. **PlatformPublishDto enriched** -- Plan's DTO only had Id, Platform, PublishStatus, PublishedUrl, PublishedAt. Code review added `ErrorMessage`, `RetryCount`, `NextRetryAt` for frontend failure details.

6. **PlatformEndpoints switch is fully explicit** -- Each platform case (Blog, Medium, Substack, LinkedIn, Twitter) has its own branch with a specific error message rather than a generic default fallback.

## Test Results

- 65 API integration tests pass (including 7 OAuth + 6 Platform + 4 new Content tests)
- 212 Infrastructure tests pass
- 305 Application tests pass
- 9 Migration tests pass
- **591 total tests passing**

## Key Design Decisions

**Backward-compatible publish endpoint:** The existing `POST /api/content/{id}/publish` previously accepted no body. The updated version accepts an optional `PublishContentRequest?` body. When null, `TargetPlatforms = null` tells the publisher to use defaults. Existing clients continue to work.

**Platform string parsing in routes:** OAuth and retry endpoints use `{platform}` as a string route parameter with `Enum.TryParse` rather than binding to `Platform` enum directly. This returns clean 400 responses for invalid values.

**OAuth callback redirects to frontend:** After successful token exchange, the callback redirects to `/settings/platforms?connected={platform}` since the user's browser is making this request.

**Credential storage is platform-specific with explicit cases:** Each platform has its own validation and error messaging in the switch statement, avoiding misleading generic errors.

**Status endpoint checks expiry:** The `/api/auth/{platform}/status` endpoint checks not just credential existence but whether it is active and not expired.
