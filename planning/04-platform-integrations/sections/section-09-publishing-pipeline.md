# Section 09: Publishing Pipeline

## Overview

This section replaces the existing `PublishingPipelineStub` with a real `PublishingPipeline` that orchestrates multi-platform content publishing. The pipeline handles idempotency (via `IdempotencyKey`), optimistic concurrency leasing (via xmin), per-platform formatting and rate limit checks, media upload, publish execution, status tracking through `ContentPlatformStatus`, and user notification on partial failures.

## Dependencies

This section depends on prior sections being completed:

- **Section 01 (Domain Entities):** `ContentPlatformStatus` entity, `PlatformPublishStatus` enum
- **Section 02 (Interfaces & Models):** `ISocialPlatform`, `IPlatformContentFormatter`, `IRateLimiter`, `IMediaStorage`, `PlatformContent`, `PublishResult`, `RateLimitDecision`, `ContentValidation`
- **Section 03 (EF Core Config):** `ContentPlatformStatus` persistence with composite index on `(ContentId, Platform)`, unique index on `IdempotencyKey`, xmin concurrency token
- **Section 04 (Media Storage):** `IMediaStorage` for uploading media during publish
- **Section 05 (Rate Limiter):** `IRateLimiter.CanMakeRequestAsync` returning `RateLimitDecision` with `RetryAt`
- **Section 07 (Content Formatters):** `IPlatformContentFormatter.FormatAndValidate` returning `Result<PlatformContent>`
- **Section 08 (Platform Adapters):** Concrete `ISocialPlatform` implementations resolved via `IEnumerable<ISocialPlatform>`

## Existing Code Context

The codebase already has:

- `IPublishingPipeline` interface at `src/PersonalBrandAssistant.Application/Common/Interfaces/IPublishingPipeline.cs` with signature `Task<Result<MediatR.Unit>> PublishAsync(Guid contentId, CancellationToken ct = default)`
- `PublishingPipelineStub` at `src/PersonalBrandAssistant.Infrastructure/Services/PublishingPipelineStub.cs` that always returns failure
- `Content` entity with `TargetPlatforms` (PlatformType[]), `Status` (ContentStatus), `Version` (xmin), `Title`, `Body`, `Metadata`
- `Platform` entity with `IsConnected`, `EncryptedAccessToken`, `RateLimitState`
- `INotificationService.SendAsync(NotificationType, title, message, contentId?, ct)` for user notifications
- `Result<T>` pattern with `Success`, `Failure`, `NotFound`, `ValidationFailure`, `Conflict`
- `ErrorCode` enum: `None`, `ValidationFailed`, `NotFound`, `Conflict`, `Unauthorized`, `InternalError`
- `IApplicationDbContext` exposing `DbSet<Content>`, `DbSet<Platform>` (will need `DbSet<ContentPlatformStatus>` from Section 03)
- `ContentStatus` enum includes `Publishing`, `Published`, `Failed`

**Deviations from plan:**
- File path: `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/PublishingPipeline.cs` (PlatformServices convention)
- Removed `IMediaStorage` dependency (adapters handle media URLs internally)
- Concurrency conflicts counted as `succeeded` (another instance is handling the platform)
- Rate limiter failures are fail-open with warning log (proceeds with publish)
- Async/Processing platform handling deferred to section-11 (always sets Published for now)
- No transaction scope — eventual consistency by design
- 12 tests (vs 14 in plan — async/Processing and concurrency retry tests deferred)

## File Created

`src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/PublishingPipeline.cs`

## Tests First

**Test file:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/PublishingPipelineTests.cs`

The tests use Moq to mock all dependencies (`IApplicationDbContext`, `IEnumerable<ISocialPlatform>`, `IEnumerable<IPlatformContentFormatter>`, `IRateLimiter`, `IMediaStorage`, `INotificationService`, `ILogger<PublishingPipeline>`). The `DbSet<ContentPlatformStatus>` is mocked using the project's existing `AsyncQueryableHelpers` for async EF Core DbSet mocking.

```csharp
// Test: PublishAsync loads content with target platforms
// Verify: calls db.Contents.FindAsync(contentId) or equivalent query
// Verify: returns NotFound if content does not exist

// Test: PublishAsync skips platform if ContentPlatformStatus is already Published (idempotency)
// Setup: existing ContentPlatformStatus with Status = Published for Twitter
// Verify: does NOT call ISocialPlatform.PublishAsync for Twitter
// Verify: still processes other non-Published platforms

// Test: PublishAsync skips platform if ContentPlatformStatus is Processing
// Setup: existing ContentPlatformStatus with Status = Processing for Instagram
// Verify: does NOT call ISocialPlatform.PublishAsync for Instagram

// Test: PublishAsync acquires lease via optimistic concurrency (sets Pending)
// Setup: no existing ContentPlatformStatus for target platform
// Verify: creates new ContentPlatformStatus with Status = Pending
// Verify: calls SaveChangesAsync to persist the lease before publishing

// Test: PublishAsync throws DbUpdateConcurrencyException on concurrent lease attempt
// Setup: SaveChangesAsync throws DbUpdateConcurrencyException on lease step
// Verify: exception propagates (caller handles retry)

// Test: PublishAsync sets IdempotencyKey as SHA256(ContentId:Platform:ContentVersion)
// Setup: Content with known Id and Version, target platform Twitter
// Verify: created ContentPlatformStatus has IdempotencyKey matching SHA256 of "{ContentId}:TwitterX:{Version}"

// Test: PublishAsync calls FormatAndValidate and skips platform on failure (sets Skipped)
// Setup: IPlatformContentFormatter.FormatAndValidate returns Result.Failure
// Verify: ContentPlatformStatus.Status set to Skipped
// Verify: does NOT call ISocialPlatform.PublishAsync

// Test: PublishAsync checks rate limit and defers on RateLimited (sets NextRetryAt)
// Setup: IRateLimiter.CanMakeRequestAsync returns Allowed=false, RetryAt=future
// Verify: ContentPlatformStatus.Status set to RateLimited
// Verify: ContentPlatformStatus.NextRetryAt set to the RetryAt value from RateLimitDecision

// Test: PublishAsync publishes to each platform independently
// Setup: Content with TargetPlatforms = [Twitter, LinkedIn]
// Verify: calls ISocialPlatform.PublishAsync for both platforms independently
// Verify: failure on Twitter does not prevent LinkedIn from publishing

// Test: PublishAsync records PlatformPostId and PostUrl on success
// Setup: ISocialPlatform.PublishAsync returns Success with PlatformPostId and PostUrl
// Verify: ContentPlatformStatus has matching PlatformPostId, PostUrl, PublishedAt

// Test: PublishAsync sets Processing for async uploads (IG video, YT)
// Setup: ISocialPlatform.PublishAsync returns Success with metadata indicating async processing
// Verify: ContentPlatformStatus.Status set to Processing (not Published)

// Test: PublishAsync sets overall status to Published when all succeed
// Setup: all target platforms publish successfully
// Verify: Content.Status transitions to Published

// Test: PublishAsync sets overall status to PartiallyPublished when some succeed
// Setup: Twitter succeeds, LinkedIn fails
// Verify: Content remains in Publishing state (or a PartiallyPublished state if the enum supports it)
// Note: The Content entity uses ContentStatus enum which does not have PartiallyPublished;
//       the "PartiallyPublished" concept may be tracked via the individual ContentPlatformStatus records

// Test: PublishAsync sets overall status to Failed when all fail
// Setup: all target platforms fail
// Verify: Content.Status transitions to Failed

// Test: PublishAsync notifies user on partial failure via INotificationService
// Setup: Twitter succeeds, LinkedIn fails
// Verify: INotificationService.SendAsync called with appropriate notification
```

## Implementation Details

### Class Structure

`PublishingPipeline` implements `IPublishingPipeline` and is registered as scoped in DI.

Constructor dependencies:
- `IApplicationDbContext _db`
- `IEnumerable<ISocialPlatform> _platformAdapters` (resolved from DI, one per platform type)
- `IEnumerable<IPlatformContentFormatter> _formatters` (resolved from DI, one per platform type)
- `IRateLimiter _rateLimiter`
- `IMediaStorage _mediaStorage`
- `INotificationService _notificationService`
- `ILogger<PublishingPipeline> _logger`

### PublishAsync Algorithm

The `PublishAsync(Guid contentId, CancellationToken ct)` method follows this flow:

1. **Load content:** Query `Content` by ID including `TargetPlatforms`. Return `Result.NotFound` if missing.

2. **Iterate target platforms independently.** For each `PlatformType` in `Content.TargetPlatforms`:

   a. **Idempotency check:** Query `ContentPlatformStatus` by `(ContentId, Platform)`. If status is `Published` or `Processing`, skip this platform.

   b. **Compute IdempotencyKey:** `SHA256($"{contentId}:{platform}:{content.Version}")` encoded as lowercase hex string. If an existing `ContentPlatformStatus` with a different status exists, update it; otherwise create a new one.

   c. **Acquire lease:** Set status to `Pending`, call `SaveChangesAsync`. If `DbUpdateConcurrencyException` is thrown, another process is handling this platform -- skip or rethrow.

   d. **Resolve formatter:** Find the `IPlatformContentFormatter` where `Platform == currentPlatform`. Call `FormatAndValidate(content)`. On failure, set status to `Skipped` with error message, save, continue to next platform.

   e. **Check rate limit:** Call `IRateLimiter.CanMakeRequestAsync(platform, "publish", ct)`. If `Allowed == false`, set status to `RateLimited`, set `NextRetryAt` from `RateLimitDecision.RetryAt`, save, continue to next platform.

   f. **Upload media:** If the formatted `PlatformContent` has media files, use `IMediaStorage` to resolve paths/URLs needed by the adapter.

   g. **Publish:** Resolve the `ISocialPlatform` adapter where `Type == currentPlatform`. Call `PublishAsync(formattedContent, ct)`.

   h. **Record result:** On success, set `PlatformPostId`, `PostUrl`, `PublishedAt`, and `Status = Published`. For platforms that return async processing indicators (Instagram video, YouTube), set `Status = Processing` instead. On failure, set `Status = Failed`, increment `RetryCount`, record `ErrorMessage`.

   i. **Save:** Call `SaveChangesAsync` after each platform result.

3. **Determine overall status:** After all platforms are processed:
   - All `Published` (or `Processing`) => transition `Content.Status` to `Published`
   - Mix of `Published`/`Failed`/`Skipped`/`RateLimited` => keep `Content.Status` as `Publishing` (the partially published state is represented by the individual `ContentPlatformStatus` records)
   - All `Failed` => transition `Content.Status` to `Failed`

4. **Notify on partial failure:** If some platforms succeeded and others failed, call `INotificationService.SendAsync` with `NotificationType.Warning` (or appropriate type), a descriptive title like "Partial publish failure", and a message listing which platforms failed.

5. **Return:** `Result.Success(MediatR.Unit.Value)` on any successful publish, or `Result.Failure` with `ErrorCode.InternalError` if all platforms failed.

### IdempotencyKey Computation

Use `System.Security.Cryptography.SHA256` to compute the hash:

```csharp
/// <summary>
/// Computes SHA256("{contentId}:{platform}:{version}") as lowercase hex.
/// Ensures the same content+platform+version combination produces
/// the same key, preventing duplicate publishes on retry.
/// </summary>
private static string ComputeIdempotencyKey(Guid contentId, PlatformType platform, uint version)
```

### Handling Async Platforms

Instagram video posts and YouTube uploads return immediately but require polling for completion. The pipeline sets `Status = Processing` for these. The `PublishCompletionPoller` background processor (Section 11) handles polling and transitioning to `Published` or `Failed`.

To distinguish async results from synchronous ones, the `PublishResult` returned by the adapter can include metadata (e.g., `Metadata["async"] = "true"` or a specific container/job ID). The pipeline checks for this metadata to decide between `Published` and `Processing` status.

### Error Isolation

Each platform publishes independently within a try-catch block. A failure on one platform does not prevent others from being attempted. All exceptions are caught, logged, and recorded in the `ContentPlatformStatus.ErrorMessage`. The pipeline never throws for individual platform failures -- it aggregates results and returns a single `Result`.

### Concurrency Safety

The optimistic concurrency lease via xmin ensures that if two pipeline instances try to process the same content+platform simultaneously, only one succeeds at the lease step. The other gets a `DbUpdateConcurrencyException`. The pipeline catches this at the per-platform level and skips that platform (another instance is handling it).

### Content Status Transitions

The pipeline calls `Content.TransitionTo()` to change the overall content status. The existing `Content` entity supports these transitions:
- `Publishing -> Published` (all succeeded)
- `Publishing -> Failed` (all failed)
- `Publishing -> Publishing` is a no-op for partial results (status stays as-is)

The caller (e.g., `ScheduledPublishProcessor` or API endpoint) is responsible for transitioning to `Publishing` before calling the pipeline. The pipeline transitions to the final state.