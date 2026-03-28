# Section 07 -- Background Processors

## Overview

This section implements four background processing services that operate on content in the workflow pipeline. All are `BackgroundService` implementations that use polling-based designs querying the database directly on timer intervals. No `Channel<T>` is needed for these processors.

**Files to create:**

- `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/ScheduledPublishProcessor.cs`
- `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/RetryFailedProcessor.cs`
- `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/WorkflowRehydrator.cs`
- `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/RetentionCleanupService.cs`
- `src/PersonalBrandAssistant.Application/Common/Interfaces/IPublishingPipeline.cs`
- `src/PersonalBrandAssistant.Infrastructure/Services/PublishingPipelineStub.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/ScheduledPublishProcessorTests.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/RetryFailedProcessorTests.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/WorkflowRehydratorTests.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/RetentionCleanupServiceTests.cs`
- `tests/PersonalBrandAssistant.Application.Tests/Services/PublishingPipelineStubTests.cs`

**Files to modify:**

- `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` -- register new hosted services and `IPublishingPipeline`

## Dependencies

This section depends on:

- **Section 01 (Domain Entities):** Content entity modifications (`RetryCount`, `NextRetryAt`, `PublishingStartedAt`), `WorkflowTransitionLog`, `Notification` entities
- **Section 03 (Workflow Engine):** `IWorkflowEngine` interface and implementation for state transitions
- **Section 05 (Content Scheduler):** Content in `Scheduled` status with `ScheduledAt` set, which processors consume
- **Section 06 (Notification System):** `INotificationService` for sending failure notifications

## Background Context

The existing codebase already has a `BackgroundService` pattern in `AuditLogCleanupService` at `src/PersonalBrandAssistant.Infrastructure/Services/AuditLogCleanupService.cs`. It uses `IServiceScopeFactory` to create scopes, `IDateTimeProvider` for time abstraction, and `IConfiguration` for configurable settings. All new processors should follow this same pattern.

The project uses PostgreSQL via EF Core with Npgsql. The `IDateTimeProvider` interface (in `src/PersonalBrandAssistant.Application/Common/Interfaces/IDateTimeProvider.cs`) exposes a single `DateTimeOffset UtcNow` property. The `Result<T>` pattern uses `ErrorCode` enum values and supports `Success`, `Failure`, `NotFound`, etc.

The `Content` entity has `Status` (enum), `ScheduledAt` (nullable DateTimeOffset), `PublishedAt` (nullable DateTimeOffset), and after Section 01 modifications will also have `RetryCount` (int, default 0), `NextRetryAt` (nullable DateTimeOffset), and `PublishingStartedAt` (nullable DateTimeOffset).

DI registration happens in `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` via the `AddInfrastructure` extension method. New hosted services are added with `services.AddHostedService<T>()`.

---

## Tests First

### PublishingPipelineStub Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Services/PublishingPipelineStubTests.cs`

A single test class verifying the stub behavior:

- **Test: PublishAsync returns Failure with ErrorCode.InternalError and message "Publishing pipeline not implemented"** -- Call `PublishAsync` with any `Guid` and assert the result is a failure with the expected error code and message. This stub exists so that Phase 02 does not falsely publish content; Phase 04 replaces it with real platform integrations.

### ScheduledPublishProcessor Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/ScheduledPublishProcessorTests.cs`

These tests use Testcontainers PostgreSQL (follow the `[Collection("Postgres")]` and `PostgresFixture` pattern from existing tests). The processor logic should be extracted into a scoped service method so it can be called directly in tests without running the full `BackgroundService` loop.

- **Test: Picks up content where Status=Scheduled AND ScheduledAt<=now** -- Create content in Scheduled status with `ScheduledAt` set to a past time. Run the processor logic. Assert the content transitions to Publishing.
- **Test: Uses atomic claim query (content transitions atomically to Publishing)** -- Verify the content is claimed atomically via a raw SQL `UPDATE ... WHERE status='Scheduled' AND scheduled_at<=now RETURNING *` query. Assert that `PublishingStartedAt` is set when claiming.
- **Test: Sets PublishingStartedAt when claiming content** -- After claiming, verify `PublishingStartedAt` is set to approximately the current time.
- **Test: Calls IPublishingPipeline for claimed content** -- Mock `IPublishingPipeline` and verify `PublishAsync` is called for each claimed item.
- **Test: On failure, increments RetryCount and sets NextRetryAt** -- When `IPublishingPipeline.PublishAsync` returns failure, assert `RetryCount` is incremented by 1 and `NextRetryAt` is set according to the backoff schedule.
- **Test: Does not pick up content where ScheduledAt is in the future** -- Create content scheduled for a future time. Run the processor. Assert the content remains in Scheduled status.

### RetryFailedProcessor Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/RetryFailedProcessorTests.cs`

Same test infrastructure pattern as above.

- **Test: Picks up content where Status=Failed AND RetryCount<3 AND NextRetryAt<=now** -- Create failed content with `RetryCount=1`, `NextRetryAt` in the past. Run processor logic. Assert it attempts to re-publish.
- **Test: Respects backoff timing (1min, 5min, 15min)** -- After a failure at retry count 0, assert `NextRetryAt` is set to now + 1 minute. After retry count 1, assert now + 5 minutes. After retry count 2, assert now + 15 minutes.
- **Test: After 3 failures, sends notification and leaves in Failed status** -- Create content with `RetryCount=3`. Run processor logic. Assert content is NOT retried, a notification is sent via `INotificationService`, and content remains in Failed status.
- **Test: Does not pick up content where NextRetryAt is in the future** -- Create failed content with `NextRetryAt` set to a future time. Assert processor skips it.
- **Test: Does not pick up content where RetryCount >= 3** -- Create failed content with `RetryCount=3` and `NextRetryAt` in the past. Assert processor skips it.

### WorkflowRehydrator Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/WorkflowRehydratorTests.cs`

The rehydrator runs once on startup (as an `IHostedService`), not on a loop.

- **Test: On startup, detects content stuck in Publishing for >5 minutes** -- Create content in Publishing status with `PublishingStartedAt` set to 6 minutes ago. Run rehydrator. Assert it resets the content back to Scheduled.
- **Test: Resets stuck content back to Scheduled for reprocessing** -- After reset, verify `PublishingStartedAt` is cleared and status is Scheduled so the `ScheduledPublishProcessor` can pick it up again.
- **Test: Does not touch content in Publishing for <5 minutes** -- Create content in Publishing status with `PublishingStartedAt` set to 3 minutes ago. Run rehydrator. Assert content remains in Publishing status.

### RetentionCleanupService Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/RetentionCleanupServiceTests.cs`

Follow the existing `AuditLogCleanupServiceTests` pattern using `[Collection("Postgres")]` and `PostgresFixture`.

- **Test: Cleans WorkflowTransitionLog entries older than threshold** -- Insert entries older than 180 days and entries within threshold. Run cleanup. Assert only old entries are deleted.
- **Test: Cleans Notification entries older than threshold** -- Insert notifications older than 90 days and recent ones. Run cleanup. Assert only old entries are deleted.
- **Test: Does not clean entries within threshold** -- Insert entries that are newer than the threshold. Run cleanup. Assert zero deletions.
- **Test: Thresholds are configurable** -- Pass different configuration values for retention days and verify they are respected.

---

## Implementation Details

### IPublishingPipeline Interface

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IPublishingPipeline.cs`

```csharp
/// <summary>
/// Abstraction for the content publishing pipeline. Phase 02 provides a stub
/// that always returns failure. Phase 04 replaces with real platform integrations.
/// </summary>
public interface IPublishingPipeline
{
    Task<Result<Unit>> PublishAsync(Guid contentId, CancellationToken ct = default);
}
```

### PublishingPipelineStub

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/PublishingPipelineStub.cs`

Implements `IPublishingPipeline`. The single method `PublishAsync` returns `Result<Unit>.Failure(ErrorCode.InternalError, "Publishing pipeline not implemented")`. This prevents content from being falsely marked as Published before Phase 04 integrates real platform APIs. Content will transition to Failed status when this stub is called, which is the expected behavior.

### ScheduledPublishProcessor

**File:** `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/ScheduledPublishProcessor.cs`

A `BackgroundService` that polls every 30 seconds using `PeriodicTimer`.

**Constructor dependencies:**
- `IServiceScopeFactory` -- to create scoped services each iteration
- `IDateTimeProvider` -- for testable time
- `ILogger<ScheduledPublishProcessor>` -- structured logging

**Core logic (per iteration):**

1. Create a new `IServiceScope`
2. Resolve `ApplicationDbContext` and `IPublishingPipeline` from the scope
3. Execute an atomic claim query using raw SQL: `UPDATE "Contents" SET "Status"=4, "PublishingStartedAt"=@now WHERE "Status"=3 AND "ScheduledAt"<=@now RETURNING *` (Status 4 = Publishing, Status 3 = Scheduled). This atomic UPDATE prevents double-publish race conditions.
4. For each claimed content item, call `IPublishingPipeline.PublishAsync(contentId, ct)`
5. On success: use `IWorkflowEngine.TransitionAsync(contentId, ContentStatus.Published, actor: ActorType.System)` to transition to Published
6. On failure: increment `RetryCount`, calculate `NextRetryAt` based on backoff schedule, use `IWorkflowEngine.TransitionAsync(contentId, ContentStatus.Failed, actor: ActorType.System)` to transition to Failed
7. Save changes

**Backoff schedule calculation:** A static method that maps retry count to delay:
- Attempt 0 (first failure) -> 1 minute
- Attempt 1 -> 5 minutes
- Attempt 2 -> 15 minutes

**Error handling:** Wrap the entire iteration in try/catch. On exception, log the error and continue to the next timer tick. Follow the same pattern as `AuditLogCleanupService`: catch `OperationCanceledException` when cancellation is requested to break cleanly, catch general `Exception` to log and delay before retrying.

### RetryFailedProcessor

**File:** `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/RetryFailedProcessor.cs`

A `BackgroundService` that polls every 60 seconds using `PeriodicTimer`.

**Constructor dependencies:** Same as `ScheduledPublishProcessor`, plus `INotificationService` (resolved from scope).

**Core logic (per iteration):**

1. Create a new `IServiceScope`
2. Query content where `Status == Failed AND RetryCount < 3 AND NextRetryAt <= now` using `AsNoTracking()` for the initial query, then load tracked entities for modification
3. For each eligible content item:
   - Transition to Publishing via `IWorkflowEngine`
   - Set `PublishingStartedAt`
   - Call `IPublishingPipeline.PublishAsync`
   - On success: transition to Published
   - On failure: increment `RetryCount`, calculate new `NextRetryAt`, transition back to Failed
4. After processing, query content where `Status == Failed AND RetryCount >= 3` that has not yet been notified (check if a notification of type `ContentFailed` already exists for this content to avoid duplicate notifications). Send a `ContentFailed` notification via `INotificationService` for each.

The max-retry notification should include the content title and a message indicating the content has exhausted all retry attempts.

### WorkflowRehydrator

**File:** `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/WorkflowRehydrator.cs`

An `IHostedService` (not `BackgroundService`) that runs once on startup and then stops.

**Constructor dependencies:**
- `IServiceScopeFactory`
- `IDateTimeProvider`
- `ILogger<WorkflowRehydrator>`

**Core logic (in `StartAsync`):**

1. Create a scope
2. Query content where `Status == Publishing AND PublishingStartedAt < (now - 5 minutes)`
3. For each stuck item: reset `Status` to Scheduled, clear `PublishingStartedAt`, log a warning
4. Save changes
5. Log summary: "Rehydrated {count} stuck content items"

This handles the case where the application crashed or restarted while content was in the Publishing state. By resetting to Scheduled, the `ScheduledPublishProcessor` will pick them up on its next polling cycle.

**Important:** The rehydrator should use `IWorkflowEngine.TransitionAsync` if available, but since Publishing -> Scheduled is not in the standard `AllowedTransitions` dictionary, it may need to directly update the status. This is an exceptional recovery path. One approach: add a `Scheduled` entry to the `Publishing` allowed transitions in the `Content.AllowedTransitions` dictionary (Section 01 would need this). Alternatively, the rehydrator can use a dedicated recovery method that bypasses the state machine for this specific scenario. The recommended approach is to add `ContentStatus.Scheduled` to the allowed transitions from `Publishing` (alongside Published and Failed) -- this is a legitimate "requeue" operation.

### RetentionCleanupService

**File:** `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/RetentionCleanupService.cs`

A `BackgroundService` that runs every 24 hours, complementing the existing `AuditLogCleanupService`. It handles cleanup for the two new entity types.

**Constructor dependencies:**
- `IServiceScopeFactory`
- `IDateTimeProvider`
- `IConfiguration` -- reads `Retention:WorkflowTransitionLogDays` (default: 180) and `Retention:NotificationDays` (default: 90)
- `ILogger<RetentionCleanupService>`

**Core logic (per iteration):**

1. Create a scope, resolve `ApplicationDbContext`
2. Delete `WorkflowTransitionLog` entries where `Timestamp < (now - workflowRetentionDays)`
3. Delete `Notification` entries where `CreatedAt < (now - notificationRetentionDays)`
4. Log deletion counts
5. Delay 24 hours

Follow the exact same error handling pattern as `AuditLogCleanupService`: catch `OperationCanceledException` when stopping, catch general exceptions with a 5-minute retry delay.

**appsettings.json additions:**

```json
{
  "Retention": {
    "WorkflowTransitionLogDays": 180,
    "NotificationDays": 90
  }
}
```

### DI Registration

**File to modify:** `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs`

Add the following registrations inside the `AddInfrastructure` method:

```csharp
services.AddScoped<IPublishingPipeline, PublishingPipelineStub>();
services.AddHostedService<ScheduledPublishProcessor>();
services.AddHostedService<RetryFailedProcessor>();
services.AddHostedService<WorkflowRehydrator>();
services.AddHostedService<RetentionCleanupService>();
```

The `WorkflowRehydrator` is registered as a hosted service -- it implements `IHostedService` and runs its logic in `StartAsync`, completing before the application starts accepting requests. This ensures stuck content is recovered before the scheduled processor begins polling.

### Content Composite Indexes

While the EF Core configurations are primarily Section 01's responsibility, the background processors depend on these indexes for performance. Ensure the following composite indexes exist on the `Contents` table (configured in `ContentConfiguration.cs`):

- `(Status, ScheduledAt)` -- used by `ScheduledPublishProcessor` to find due content
- `(Status, NextRetryAt)` -- used by `RetryFailedProcessor` to find retryable content

All processor read queries should use `AsNoTracking()` for the initial lookup to avoid unnecessary change tracking overhead.

---

## Implementation Checklist

1. Create `IPublishingPipeline` interface in Application layer
2. Create `PublishingPipelineStub` in Infrastructure Services
3. Create `ScheduledPublishProcessor` with atomic claim query and 30s polling
4. Create `RetryFailedProcessor` with backoff logic and 60s polling
5. Create `WorkflowRehydrator` as one-shot startup service with 5-minute threshold
6. Create `RetentionCleanupService` with configurable thresholds for WorkflowTransitionLog and Notification cleanup
7. Register all services in `DependencyInjection.cs`
8. Add retention configuration to `appsettings.json`
9. Write all tests following the existing `[Collection("Postgres")]` + `PostgresFixture` pattern
10. Verify `Content.AllowedTransitions` includes `Scheduled` as a valid transition from `Publishing` (coordinate with Section 01 if needed)