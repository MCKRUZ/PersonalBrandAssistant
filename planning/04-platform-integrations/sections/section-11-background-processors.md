# Section 11 -- Background Processors

## Overview

This section implements three `BackgroundService` classes that run on periodic timers to handle out-of-band platform maintenance tasks:

- **TokenRefreshProcessor** -- proactively refreshes OAuth tokens before expiry, cleans up expired `OAuthState` entries (runs every 5 minutes)
- **PlatformHealthMonitor** -- validates platform connectivity and scope integrity (runs every 15 minutes)
- **PublishCompletionPoller** -- polls async upload status for Instagram containers and YouTube video processing (runs every 30 seconds)

All three follow the same pattern established by the existing `ScheduledPublishProcessor` and `RetryFailedProcessor`: `BackgroundService` with `PeriodicTimer`, `IServiceScopeFactory` for scoped resolution, and `internal` processing methods for testability.

## Dependencies

This section depends on:

- **Section 01** -- `ContentPlatformStatus` entity, `PlatformPublishStatus` enum
- **Section 02** -- `ISocialPlatform`, `IOAuthManager`, `IRateLimiter`, `OAuthTokens` interfaces/models
- **Section 03** -- EF Core configuration for `OAuthState`, `ContentPlatformStatus`
- **Section 06** -- `OAuthManager` (for `RefreshTokenAsync`)
- **Section 08** -- Platform adapters (for `GetProfileAsync`, polling status)
- **Section 09** -- `PublishingPipeline` (for `ContentPlatformStatus` status values)

The `NotificationType` enum (at `/src/PersonalBrandAssistant.Domain/Enums/NotificationType.cs`) will need new values: `PlatformDisconnected`, `PlatformTokenExpiring`, `PlatformScopeMismatch`.

## File Paths

### Production Code

- `/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/TokenRefreshProcessor.cs`
- `/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/PlatformHealthMonitor.cs`
- `/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/PublishCompletionPoller.cs`
- `/src/PersonalBrandAssistant.Domain/Enums/NotificationType.cs` (modify -- add new enum values)

### Test Code

- `/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/TokenRefreshProcessorTests.cs`
- `/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/PlatformHealthMonitorTests.cs`
- `/tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/PublishCompletionPollerTests.cs`

## Tests First

All tests use xUnit + Moq. Each processor has an `internal` method that performs the actual work per cycle, allowing tests to invoke the logic directly without running the timer loop. This matches the pattern used in the existing `ScheduledPublishProcessorTests`.

### TokenRefreshProcessorTests

```csharp
// File: tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/TokenRefreshProcessorTests.cs

// Test: Refreshes Twitter tokens when expiry < 30min
//   Arrange: Platform with Type=Twitter, IsConnected=true, TokenExpiresAt=now+20min
//   Act: call ProcessTokenRefreshAsync
//   Assert: IOAuthManager.RefreshTokenAsync called for Twitter

// Test: Refreshes LinkedIn/Instagram tokens when expiry < 10 days
//   Arrange: Platform with Type=LinkedIn, IsConnected=true, TokenExpiresAt=now+8days
//   Act: call ProcessTokenRefreshAsync
//   Assert: IOAuthManager.RefreshTokenAsync called for LinkedIn

// Test: Skips YouTube (no scheduled refresh)
//   Arrange: Platform with Type=YouTube, IsConnected=true, TokenExpiresAt=now+20min
//   Act: call ProcessTokenRefreshAsync
//   Assert: IOAuthManager.RefreshTokenAsync NOT called for YouTube

// Test: Marks platform disconnected on refresh failure
//   Arrange: Platform with Type=Twitter, IsConnected=true, TokenExpiresAt=now+10min
//   Setup: IOAuthManager.RefreshTokenAsync returns Result.Failure
//   Act: call ProcessTokenRefreshAsync
//   Assert: Platform.IsConnected == false

// Test: Notifies user on refresh failure
//   Arrange: same as above
//   Act: call ProcessTokenRefreshAsync
//   Assert: INotificationService.SendAsync called with NotificationType.PlatformDisconnected

// Test: Instagram logs warning at 14 days, error at 3 days before expiry
//   Arrange: Platform with Type=Instagram, TokenExpiresAt=now+13days
//   Act: call ProcessTokenRefreshAsync
//   Assert: Logger.LogWarning called with message about Instagram token expiry
//   Arrange: Platform with Type=Instagram, TokenExpiresAt=now+2days
//   Act: call ProcessTokenRefreshAsync
//   Assert: Logger.LogError called with message about Instagram token expiry

// Test: Cleans up expired OAuthState entries
//   Arrange: OAuthState entries with ExpiresAt in the past (older than 1 hour)
//   Act: call ProcessTokenRefreshAsync
//   Assert: expired entries deleted from DB

// Test: Only queries platforms with tokens expiring within threshold (efficient query)
//   Arrange: Multiple platforms, only one within threshold
//   Act: call ProcessTokenRefreshAsync
//   Assert: IOAuthManager.RefreshTokenAsync called only for the platform within threshold
```

### PlatformHealthMonitorTests

```csharp
// File: tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/PlatformHealthMonitorTests.cs

// Test: Calls GetProfileAsync for each connected platform
//   Arrange: 3 connected platforms
//   Act: call CheckPlatformHealthAsync
//   Assert: ISocialPlatform.GetProfileAsync called 3 times

// Test: Updates LastSyncAt on success
//   Arrange: Connected platform, GetProfileAsync returns success
//   Act: call CheckPlatformHealthAsync
//   Assert: Platform.LastSyncAt updated to current time

// Test: Warns when granted scopes don't include required scopes
//   Arrange: Twitter platform with GrantedScopes missing "tweet.write"
//   Act: call CheckPlatformHealthAsync
//   Assert: INotificationService.SendAsync called with NotificationType.PlatformScopeMismatch

// Test: Attempts token refresh on auth failure
//   Arrange: Connected platform, GetProfileAsync returns auth error (401-like)
//   Act: call CheckPlatformHealthAsync
//   Assert: IOAuthManager.RefreshTokenAsync called

// Test: Logs API errors without disconnecting platform
//   Arrange: Connected platform, GetProfileAsync returns non-auth error (e.g., 500)
//   Act: call CheckPlatformHealthAsync
//   Assert: Logger.LogWarning called, Platform.IsConnected still true
```

### PublishCompletionPollerTests

```csharp
// File: tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/PublishCompletionPollerTests.cs

// Test: Polls Processing entries for Instagram container status
//   Arrange: ContentPlatformStatus with Status=Processing, Platform=Instagram
//   Setup: ISocialPlatform (Instagram) returns container status = "IN_PROGRESS"
//   Act: call PollProcessingEntriesAsync
//   Assert: status remains Processing

// Test: Updates to Published when IG container status is FINISHED
//   Arrange: ContentPlatformStatus with Status=Processing, Platform=Instagram
//   Setup: ISocialPlatform (Instagram) returns container status = "FINISHED"
//   Act: call PollProcessingEntriesAsync
//   Assert: status updated to Published, PlatformPostId and PostUrl set

// Test: Polls YouTube video processing status
//   Arrange: ContentPlatformStatus with Status=Processing, Platform=YouTube
//   Setup: ISocialPlatform (YouTube) returns processing complete
//   Act: call PollProcessingEntriesAsync
//   Assert: status updated to Published

// Test: Marks Failed after 30 minutes of polling
//   Arrange: ContentPlatformStatus with Status=Processing, CreatedAt=now-31min
//   Act: call PollProcessingEntriesAsync
//   Assert: status updated to Failed, ErrorMessage indicates timeout
```

## Implementation Details

### NotificationType Enum Updates

Add new values to the existing `NotificationType` enum at `/src/PersonalBrandAssistant.Domain/Enums/NotificationType.cs`:

```csharp
public enum NotificationType
{
    ContentReadyForReview,
    ContentApproved,
    ContentRejected,
    ContentPublished,
    ContentFailed,
    PlatformDisconnected,      // new
    PlatformTokenExpiring,     // new
    PlatformScopeMismatch      // new
}
```

### TokenRefreshProcessor

**File:** `/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/TokenRefreshProcessor.cs`

Follow the same structure as the existing `ScheduledPublishProcessor`:

- Inherits `BackgroundService`
- Constructor takes `IServiceScopeFactory`, `IDateTimeProvider`, `ILogger<TokenRefreshProcessor>`
- `ExecuteAsync` runs a `PeriodicTimer` with `TimeSpan.FromMinutes(5)` interval
- `internal async Task ProcessTokenRefreshAsync(CancellationToken ct)` does the actual work

Processing logic:

1. Create a scope and resolve `ApplicationDbContext`, `IOAuthManager`, `INotificationService`.
2. Query platforms needing refresh using a single efficient query. The query should filter by `IsConnected = true` and apply platform-specific expiry thresholds in the WHERE clause. For Twitter, the threshold is `TokenExpiresAt < now + 30min`. For LinkedIn and Instagram, the threshold is `TokenExpiresAt < now + 10 days`. YouTube is excluded entirely from this query.
3. For each platform needing refresh, call `IOAuthManager.RefreshTokenAsync(platform.Type, ct)`.
4. On refresh failure, set `Platform.IsConnected = false`, save, and call `INotificationService.SendAsync` with `NotificationType.PlatformDisconnected`.
5. Instagram-specific logging: if the platform is Instagram and `TokenExpiresAt - now < 14 days`, log a warning. If `< 3 days`, log an error. These are critical because Instagram long-lived tokens that expire cannot be recovered -- the user must re-authenticate.
6. After token refresh processing, clean up expired `OAuthState` entries. Delete all entries where `ExpiresAt < now - 1 hour`. This is a simple `ExecuteDeleteAsync` call.

### PlatformHealthMonitor

**File:** `/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/PlatformHealthMonitor.cs`

Same `BackgroundService` + `PeriodicTimer` pattern with `TimeSpan.FromMinutes(15)` interval.

`internal async Task CheckPlatformHealthAsync(CancellationToken ct)` logic:

1. Create a scope and resolve `ApplicationDbContext`, `IEnumerable<ISocialPlatform>`, `IOAuthManager`, `INotificationService`.
2. Query all platforms where `IsConnected = true`.
3. For each connected platform, resolve the matching `ISocialPlatform` from the enumerable (match on `Type`).
4. Call `GetProfileAsync(ct)`.
5. On success: update `Platform.LastSyncAt = _dateTimeProvider.UtcNow`, save.
6. On failure: inspect the error. If it looks like an auth failure (e.g., error message contains "unauthorized" or "401"), attempt `IOAuthManager.RefreshTokenAsync`. If refresh also fails, log a warning but do NOT disconnect -- the platform may recover. For non-auth errors (API outages, rate limits), log a warning and move on.
7. Scope validation: define required scopes per platform as a static dictionary. Compare `Platform.GrantedScopes` against required scopes. If any required scope is missing, call `INotificationService.SendAsync` with `NotificationType.PlatformScopeMismatch` and log a warning with the missing scopes.

Required scopes dictionary:

| Platform | Required Scopes |
|----------|----------------|
| Twitter | `tweet.read`, `tweet.write`, `users.read`, `offline.access` |
| LinkedIn | `w_member_social`, `r_liteprofile` |
| Instagram | `instagram_basic`, `instagram_content_publish`, `pages_show_list` |
| YouTube | `youtube`, `youtube.upload` |

### PublishCompletionPoller

**File:** `/src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/PublishCompletionPoller.cs`

Same `BackgroundService` + `PeriodicTimer` pattern with `TimeSpan.FromSeconds(30)` interval.

`internal async Task PollProcessingEntriesAsync(CancellationToken ct)` logic:

1. Create a scope and resolve `ApplicationDbContext`, `IEnumerable<ISocialPlatform>`.
2. Query `ContentPlatformStatus` entries where `Status == PlatformPublishStatus.Processing`.
3. For each entry:
   a. Check if the entry has been processing for more than 30 minutes (compare `CreatedAt` or a new `ProcessingStartedAt` field against `_dateTimeProvider.UtcNow`). If so, set `Status = Failed`, `ErrorMessage = "Processing timed out after 30 minutes"`, save, and continue.
   b. Resolve the matching `ISocialPlatform` adapter.
   c. For Instagram: the adapter needs a method to check container status. This can be done via a platform-specific approach -- the `PlatformPostId` stored during the initial "create container" step is the container ID. The adapter checks the container status via the Graph API. If `FINISHED`, call the publish endpoint, then update to `Published` with the final `PostUrl`. If `ERROR`, mark as `Failed`.
   d. For YouTube: the `PlatformPostId` is the video ID. Check the processing status via the YouTube Data API. If processing is complete (`processingStatus = "succeeded"`), update to `Published`. If `failed`, mark as `Failed`.
4. Save changes after processing all entries.

**Design note on polling platform-specific status:** The `ISocialPlatform` interface does not have a dedicated polling method. Two approaches:

- Option A: Add a `Task<Result<PublishStatus>> CheckPublishStatusAsync(string platformPostId, CancellationToken ct)` method to `ISocialPlatform`. This is the cleanest approach and avoids the poller needing to know platform-specific details.
- Option B: Cast to concrete adapter types and call platform-specific methods.

Prefer Option A. Add `CheckPublishStatusAsync` to the `ISocialPlatform` interface (in section 02's interface file). Platforms that don't support async processing (Twitter, LinkedIn) return `Published` immediately. Instagram and YouTube implement the actual polling logic.

The return type can be a simple record:

```csharp
public record PlatformPublishStatusCheck(PlatformPublishStatus Status, string? PostUrl, string? ErrorMessage);
```

### Error Handling Pattern

All three processors follow the same error handling pattern from the existing codebase:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    using var timer = new PeriodicTimer(/* interval */);
    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
        try
        {
            await InternalProcessingMethodAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            break;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during {Processor} processing", nameof(/* processor name */));
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
```

### Constructor Dependencies

All three processors share the same constructor signature pattern:

```csharp
public TokenRefreshProcessor(
    IServiceScopeFactory scopeFactory,
    IDateTimeProvider dateTimeProvider,
    ILogger<TokenRefreshProcessor> logger)
```

Scoped services (`ApplicationDbContext`, `IOAuthManager`, `ISocialPlatform`, `INotificationService`) are resolved inside the processing method via `_scopeFactory.CreateScope()`, not injected via constructor. This matches the established pattern in `ScheduledPublishProcessor`.

### DI Registration

These services are registered by section 12 (DI Configuration) via:

```csharp
services.AddHostedService<TokenRefreshProcessor>();
services.AddHostedService<PlatformHealthMonitor>();
services.AddHostedService<PublishCompletionPoller>();
```

The existing `CustomWebApplicationFactory` in tests removes hosted services, so these will not run during integration tests. Unit tests call the `internal` processing methods directly.

## Deviations from Plan

- **ISocialPlatform interface:** Added `CheckPublishStatusAsync(string platformPostId, CancellationToken ct)` returning `Result<PlatformPublishStatusCheck>`. Base class returns Published by default (sync platforms). Added `PlatformPublishStatusCheck` record to Application models.
- **OAuthState cleanup:** Uses materialized delete (ToListAsync + Remove) instead of ExecuteDeleteAsync — mock DbSet doesn't support server-side deletes. Acceptable for expected low volume.
- **PlatformHealthMonitor:** SaveChangesAsync batched after loop (not per-platform) per review fix. Refresh failure result is logged.
- **Instagram token expiry:** Sends `PlatformTokenExpiring` notifications at both < 14 days (warning) and < 3 days (critical) thresholds, in addition to logging.
- **PlatformAdapterBase:** Added virtual `CheckPublishStatusAsync` returning Published immediately for sync platforms (Twitter, LinkedIn).
- **Test count:** 16 tests total (7 TokenRefresh + 5 HealthMonitor + 4 CompletionPoller) vs plan's ~17.

## Files Created/Modified

- `src/PersonalBrandAssistant.Domain/Enums/NotificationType.cs` — Added PlatformDisconnected, PlatformTokenExpiring, PlatformScopeMismatch
- `src/PersonalBrandAssistant.Application/Common/Interfaces/ISocialPlatform.cs` — Added CheckPublishStatusAsync
- `src/PersonalBrandAssistant.Application/Common/Models/PlatformPublishStatusCheck.cs` — New record
- `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/PlatformAdapterBase.cs` — Added virtual CheckPublishStatusAsync
- `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/TokenRefreshProcessor.cs` — New
- `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/PlatformHealthMonitor.cs` — New
- `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/PublishCompletionPoller.cs` — New
- `tests/.../BackgroundJobs/TokenRefreshProcessorTests.cs` — 7 tests
- `tests/.../BackgroundJobs/PlatformHealthMonitorTests.cs` — 5 tests
- `tests/.../BackgroundJobs/PublishCompletionPollerTests.cs` — 4 tests