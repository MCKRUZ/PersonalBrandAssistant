# Section 11: Retry Handler

## Overview

This section implements `IPublishRetryHandler`, the service responsible for retrying failed secondary-platform publishes. When `ContentPublisher` (section-05) publishes content to secondary platforms and one fails, it creates a `ContentPlatformPublish` record with `Status = Failed` and `RetryCount = 0`, then schedules a delayed retry via Hangfire's `BackgroundJob.Schedule`. The retry handler picks up that job, attempts to republish, and either marks success or schedules the next retry with exponential backoff.

The pattern mirrors how `HangfireContentScheduler` works today -- no polling, just delayed job execution via `BackgroundJob.Schedule`.

## Dependencies

- **Section 02 (domain-model-changes):** Provides `ContentPlatformPublish.RetryCount` and `NextRetryAt` fields, `PlatformCredential` entity.
- **Section 05 (publisher-refactor):** The refactored `ContentPublisher` creates `Failed` records and calls `IPublishRetryHandler` to schedule retries. Section 05 defines the flow; this section implements the retry side of it.

Also relies on types from:
- **Section 01 (interfaces-and-types):** `IPlatformConnector`, `PlatformPublishRequest`, `PlatformPublishResult`, `PublishMode` records.
- **Section 03 (content-transformation):** `IContentTransformer` for re-transforming content before retry.

## Background

### Retry Strategy

Failed secondary publishes use exponential backoff with three attempts:

| Attempt | Delay | Description |
|---------|-------|-------------|
| 1st retry | 5 minutes | Quick retry for transient failures (network blip, temporary rate limit) |
| 2nd retry | 30 minutes | Medium delay for extended outages |
| 3rd retry | 2 hours | Final attempt before permanent failure |

After 3 failed retries (`RetryCount >= 3`), the record is marked as permanently failed and surfaced in the UI for manual retry.

### How It Fits Together

```
ContentPublisher.PublishAsync(contentId)
  -> secondary platform fails
  -> creates ContentPlatformPublish { Status = Failed, RetryCount = 0 }
  -> calls BackgroundJob.Schedule<IPublishRetryHandler>(
       x => x.RetryAsync(publishRecordId), TimeSpan.FromMinutes(5))

--- 5 minutes later ---

Hangfire invokes IPublishRetryHandler.RetryAsync(publishRecordId)
  -> loads ContentPlatformPublish record
  -> idempotency: already Published? skip
  -> resolves connector, transforms content, attempts publish
  -> success: update to Published with URL
  -> failure: increment RetryCount
     -> RetryCount < 3: schedule next retry at next backoff
     -> RetryCount >= 3: mark permanently failed
```

### Domain Model Context

`ContentPlatformPublish` (from section-02) has these retry-related fields:

```csharp
public int RetryCount { get; set; }           // defaults to 0
public DateTimeOffset? NextRetryAt { get; set; } // set when retry is scheduled
```

`PublishStatus` enum:

```csharp
public enum PublishStatus
{
    Pending,
    Published,
    Failed
}
```

The `ContentPlatformPublish` entity also has `ContentId`, `Platform`, `Status`, `PublishedUrl`, `PlatformPostId`, `PublishedAt`, and `ErrorMessage`.

---

## Tests (Write First)

Test file: `tests/PBA.Infrastructure.Tests/Publishing/PublishRetryHandlerTests.cs`

This is a new file. Follow the established test patterns in `ContentPublisherTests.cs` -- EF Core InMemory provider, Moq for external dependencies, `IDisposable` for cleanup.

### Test Setup

The `PublishRetryHandler` constructor will need:
- `IAppDbContext db` -- EF context for loading/saving `ContentPlatformPublish` records and the associated `Content`
- `IServiceProvider serviceProvider` -- for keyed DI resolution of `IPlatformConnector` by `Platform`
- `IContentTransformer transformer` -- to re-transform content for the target platform
- `IBackgroundJobClient jobClient` -- Hangfire client for scheduling follow-up retries
- `ILogger<PublishRetryHandler> logger`

The test fixture needs:
- `ApplicationDbContext` via InMemory provider (same pattern as `ContentPublisherTests`)
- Mock `IPlatformConnector` registered as a keyed service in a `ServiceCollection`
- Mock `IContentTransformer`
- Mock `IBackgroundJobClient`
- Mock `ILogger<PublishRetryHandler>`

### Test Stubs

```csharp
namespace PBA.Infrastructure.Tests.Publishing;

public class PublishRetryHandlerTests : IDisposable
{
    // Setup: ApplicationDbContext (InMemory), ServiceCollection with keyed mock connectors,
    // Mock<IContentTransformer>, Mock<IBackgroundJobClient>, Mock<ILogger<PublishRetryHandler>>

    // RetryAsync_PublishedRecord_SkipsWithoutPublishing
    //   Arrange: ContentPlatformPublish record with Status = Published
    //   Act: RetryAsync(recordId)
    //   Assert: No connector calls, record unchanged

    // RetryAsync_Success_UpdatesRecordToPublished
    //   Arrange: Failed record (RetryCount=0), Content exists, connector returns success
    //   Act: RetryAsync(recordId)
    //   Assert: Status = Published, PublishedUrl set, PlatformPostId set, PublishedAt set

    // RetryAsync_Failure_IncrementsRetryCount
    //   Arrange: Failed record (RetryCount=0), connector returns failure
    //   Act: RetryAsync(recordId)
    //   Assert: RetryCount = 1, Status still Failed, ErrorMessage updated

    // RetryAsync_UnderMaxRetries_SchedulesNextRetry
    //   Arrange: Failed record (RetryCount=0), connector returns failure
    //   Act: RetryAsync(recordId)
    //   Assert: IBackgroundJobClient.Schedule called with next backoff delay,
    //           NextRetryAt is set on the record

    // RetryAsync_AtMaxRetries_MarksAsPermanentlyFailed
    //   Arrange: Failed record (RetryCount=2, meaning this is the 3rd attempt),
    //            connector returns failure
    //   Act: RetryAsync(recordId)
    //   Assert: RetryCount = 3, Status = Failed, NextRetryAt = null,
    //           IBackgroundJobClient.Schedule NOT called

    // RetryAsync_BackoffIncreases_5min_30min_2hours
    //   Arrange: Three sequential retry failures
    //   Assert: First schedule delay = 5 min, second = 30 min, third = 2 hours
    //   This can be tested by examining the TimeSpan passed to BackgroundJob.Schedule
    //   for RetryCount 0, 1, and 2 respectively.
}
```

### Key Test Behaviors

**Idempotency test:** The record might have been manually retried and succeeded between the time the job was scheduled and when it runs. The handler must check status before attempting.

**Backoff verification:** The test should verify the specific `TimeSpan` values passed to `IBackgroundJobClient.Schedule`:
- `RetryCount == 0` (first failure) -> schedule next at `BackoffDelays[1]` = 30 min
- `RetryCount == 1` (second failure) -> schedule next at `BackoffDelays[2]` = 2 hours
- `RetryCount == 2` (third failure) -> no more retries (permanent failure)

**Permanent failure:** When `RetryCount` reaches 3, the handler must NOT call `BackgroundJob.Schedule`. It should set `NextRetryAt = null` to indicate no more retries are coming. The status stays `Failed` -- a separate "permanent failure" status is not needed because `RetryCount >= 3 && NextRetryAt == null` is the permanent failure signal.

---

## Implementation Details

### 1. Define `IPublishRetryHandler` Interface

**File:** `src/PBA.Application/Common/Interfaces/IPublishRetryHandler.cs`

```csharp
namespace PBA.Application.Common.Interfaces;

public interface IPublishRetryHandler
{
    Task RetryAsync(Guid publishRecordId);
}
```

The method signature takes only the `Guid` of the `ContentPlatformPublish` record. This is intentional -- Hangfire serializes job arguments, so the parameter must be a simple type. All context (content, platform, credentials) is loaded from the database inside the method.

### 2. Implement `PublishRetryHandler`

**File:** `src/PBA.Infrastructure/Publishing/PublishRetryHandler.cs`

Constructor dependencies:
```csharp
public sealed class PublishRetryHandler(
    IAppDbContext db,
    IServiceProvider serviceProvider,
    IContentTransformer transformer,
    IBackgroundJobClient jobClient,
    ILogger<PublishRetryHandler> logger) : IPublishRetryHandler
```

**Backoff schedule as a static readonly array:**
```csharp
private static readonly TimeSpan[] BackoffDelays =
[
    TimeSpan.FromMinutes(5),
    TimeSpan.FromMinutes(30),
    TimeSpan.FromHours(2)
];

private const int MaxRetries = 3;
```

**`RetryAsync` method flow:**

1. **Load the record:** Query `ContentPlatformPublishes` by ID. Include the related `Content` entity (needed for transformation). If record not found, log warning and return.

2. **Idempotency check:** If `record.Status == PublishStatus.Published`, log that the record was already published and return immediately. This handles the case where a manual retry or concurrent job succeeded between scheduling and execution.

3. **Load content:** Access `record.Content` (or load via `record.ContentId` if Include wasn't used). If content is null, log error and return.

4. **Resolve connector:** Use `serviceProvider.GetKeyedService<IPlatformConnector>(record.Platform)`. If null, log error, update `ErrorMessage` to "No connector registered for {platform}", and return (don't retry -- this is a configuration issue, not transient).

5. **Transform content:** Call `transformer.TransformAsync(content, record.Platform, CancellationToken.None)`.

6. **Build request:** Create a `PlatformPublishRequest` with the content, transformed output, tags, canonical URL (from the primary platform's `ContentPlatformPublish` record if available), and `PublishMode.Publish`.

7. **Attempt publish:** Call `connector.PublishAsync(request, CancellationToken.None)`.

8. **On success:**
   - Update record: `Status = Published`, `PublishedUrl = result.PublishedUrl`, `PlatformPostId = result.PlatformPostId`, `PublishedAt = DateTimeOffset.UtcNow`, `NextRetryAt = null`
   - Log success
   - Call `db.SaveChangesAsync()`

9. **On failure:**
   - Increment `record.RetryCount`
   - Update `record.ErrorMessage` with the latest error
   - If `RetryCount < MaxRetries`:
     - Calculate delay: `BackoffDelays[record.RetryCount]` (after incrementing)
     - Set `record.NextRetryAt = DateTimeOffset.UtcNow + delay`
     - Call `jobClient.Schedule<IPublishRetryHandler>(x => x.RetryAsync(record.Id), delay)`
     - Log the scheduled retry with delay and attempt number
   - If `RetryCount >= MaxRetries`:
     - Set `record.NextRetryAt = null` (signals permanent failure)
     - Log that max retries reached, manual intervention needed
   - Call `db.SaveChangesAsync()`

**Backoff index mapping (after incrementing RetryCount):**
- `RetryCount == 1` -> schedule next at `BackoffDelays[1]` = 30 min
- `RetryCount == 2` -> schedule next at `BackoffDelays[2]` = 2 hours
- `RetryCount == 3` -> no more retries (permanent failure)

Note: `BackoffDelays[0]` (5 minutes) is used by `ContentPublisher` when scheduling the initial retry, not by the retry handler itself. The handler uses indices 1 and 2 for follow-up retries.

### 3. Canonical URL Resolution for Retries

When retrying a secondary platform publish, the canonical URL should point to the primary platform's published URL. To get this:

```csharp
var primaryPublish = await db.ContentPlatformPublishes
    .Where(p => p.ContentId == content.Id
             && p.Platform == content.PrimaryPlatform
             && p.Status == PublishStatus.Published)
    .Select(p => p.PublishedUrl)
    .FirstOrDefaultAsync();
```

Pass this as the `CanonicalUrl` in the `PlatformPublishRequest`.

### 4. Error Handling

The handler should catch exceptions from both the transformer and the connector:

```csharp
try
{
    var transformed = await transformer.TransformAsync(content, record.Platform, CancellationToken.None);
    var request = new PlatformPublishRequest(content, transformed, content.Tags, canonicalUrl, PublishMode.Publish);
    var result = await connector.PublishAsync(request, CancellationToken.None);
    // handle result...
}
catch (Exception ex)
{
    logger.LogError(ex, "Retry failed for {Platform} publish {RecordId}", record.Platform, record.Id);
    HandleFailure(record, ex.Message);
}
```

Exceptions are treated identically to `result.Success == false` -- increment retry count, schedule next retry or mark permanent failure.

---

## Files Created

| File | Layer | Purpose |
|------|-------|---------|
| `src/PBA.Application/Common/Interfaces/IPublishRetryHandler.cs` | Application | Interface: `Task RetryAsync(Guid publishRecordId, CancellationToken ct = default)` |
| `src/PBA.Infrastructure/Publishing/PublishRetryHandler.cs` | Infrastructure | Implementation with exponential backoff and Hangfire scheduling (~130 lines) |
| `tests/PBA.Infrastructure.Tests/Publishing/PublishRetryHandlerTests.cs` | Tests | 8 test cases covering all retry scenarios |

## Deviations from Plan

1. **CancellationToken added:** Spec said "Hangfire job methods don't receive cancellation tokens" but Hangfire 1.7+ supports CancellationToken injection for graceful shutdown. Added `CancellationToken ct = default` to interface and implementation (code review fix HIGH-2).

2. **8 tests instead of 6:** Added `RetryAsync_ConnectorThrows_IncrementsRetryAndStoresMessage` and `RetryAsync_NonexistentRecord_ReturnsWithoutError` from code review (MEDIUM-5, MEDIUM-6).

3. **Explicit Failed status in HandleFailure:** Added `record.Status = PublishStatus.Failed` as defensive measure against future callers passing non-Failed records (code review fix MEDIUM-2).

## DI Registration

Handled in section-15 (di-registration):
```csharp
services.AddScoped<IPublishRetryHandler, PublishRetryHandler>();
```

## Verification

- `dotnet build` passes
- All 8 tests pass
- IPublishRetryHandler in Application layer, PublishRetryHandler in Infrastructure layer
- IBackgroundJobClient used for scheduling (not static BackgroundJob class)
