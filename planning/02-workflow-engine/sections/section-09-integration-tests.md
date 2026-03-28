# Section 09 -- Integration Tests

## Overview

This is the final section in Phase 02. It provides end-to-end integration tests that verify the full workflow engine works correctly when all layers are wired together. These tests use Testcontainers for a real PostgreSQL database, `CustomWebApplicationFactory` for hosting the API in-process, and `TimeProvider` for controlling time in background processor tests.

**Dependencies:** All prior sections (01 through 08) must be implemented before these tests can pass. Sections 01-08 provide the domain entities, workflow engine, approval service, content scheduler, notification system, background processors, and API endpoints that these tests exercise.

---

## Test Files to Create

All integration test files live under `tests/PersonalBrandAssistant.Infrastructure.Tests/`:

```
tests/PersonalBrandAssistant.Infrastructure.Tests/
  Integration/
    FullWorkflowIntegrationTests.cs
    BackgroundJobIntegrationTests.cs
    ApiEndpointIntegrationTests.cs
    SignalRIntegrationTests.cs
```

---

## Tests FIRST

### 1. Full Workflow Integration Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Integration/FullWorkflowIntegrationTests.cs`

These tests use `IClassFixture<PostgresFixture>` and directly instantiate services against a real database (not via HTTP). They verify the complete workflow transitions, audit trail persistence, and autonomy-level behavior.

**Test stubs:**

```csharp
/// <summary>
/// Integration tests verifying the full content lifecycle through the workflow engine,
/// approval service, and content scheduler — all backed by a real PostgreSQL database.
/// </summary>
public class FullWorkflowIntegrationTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    /// Creates content, transitions Draft -> Review -> Approved -> Scheduled -> Publishing,
    /// then verifies WorkflowTransitionLog contains all five entries with correct
    /// FromStatus/ToStatus pairs, ordered by Timestamp DESC.
    [Fact]
    public async Task FullLifecycle_CreateThroughPublishing_AuditTrailComplete() { }

    /// Creates content with CapturedAutonomyLevel = Autonomous, calls TransitionAsync
    /// to Review, and verifies the engine auto-advances through Review -> Approved
    /// without explicit approval. Verifies audit log entries reflect "auto-approved by
    /// autonomy rule" reason and ActorType.System.
    [Fact]
    public async Task AutonomousLevel_AutoAdvancesThroughApproval() { }

    /// Creates content, approves it, schedules it, then cancels. Verifies:
    /// 1. Content returns to Approved status (not Draft).
    /// 2. ScheduledAt is cleared.
    /// 3. Audit trail shows Scheduled -> Approved transition.
    [Fact]
    public async Task ApproveScheduleCancel_ReturnsToApproved() { }

    /// Rejects content from Review status with feedback string. Verifies:
    /// 1. Content returns to Draft status.
    /// 2. WorkflowTransitionLog entry has Reason = the feedback string.
    /// 3. A Notification entity is persisted with Type = ContentRejected.
    [Fact]
    public async Task RejectContent_CreatesNotificationAndAuditEntry() { }

    /// Creates content with CapturedAutonomyLevel = SemiAuto and a valid
    /// ParentContentId (parent is Published). Verifies auto-approval triggers.
    /// Then creates another with SemiAuto but ParentContentId = null and verifies
    /// it does NOT auto-approve (stays in Review).
    [Fact]
    public async Task SemiAutoLevel_AutoApprovesOnlyWithPublishedParent() { }
}
```

**Setup/Teardown pattern:** Follow the exact pattern from `ContentEndpointsTests` -- use `PostgresFixture.GetUniqueConnectionString()`, create the database in `InitializeAsync`, and drop it in `DisposeAsync`. Create an `ApplicationDbContext` via `PostgresFixture.CreateDbContext()`. Instantiate `WorkflowEngine`, `ApprovalService`, `ContentScheduler`, and `NotificationService` directly (not through DI) with the real `DbContext` and mocked `IHubContext<NotificationHub>` (using Moq).

---

### 2. Background Job Integration Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Integration/BackgroundJobIntegrationTests.cs`

These tests verify background processors against a real database, using `TimeProvider` (the built-in .NET 8+ abstract class) to control time deterministically. Processor logic should be extracted into scoped service methods (as noted in section 07) so tests do not need to run the full `BackgroundService` loop.

**Test stubs:**

```csharp
/// <summary>
/// Integration tests for background processors using TimeProvider to control time
/// and a real PostgreSQL database via Testcontainers.
/// </summary>
public class BackgroundJobIntegrationTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    /// Seeds content in Scheduled status with ScheduledAt = now - 1 minute.
    /// Invokes the processor's claim-and-publish logic. Verifies:
    /// 1. Content transitions to Publishing status.
    /// 2. PublishingStartedAt is set.
    /// 3. IPublishingPipeline.PublishAsync was called.
    /// Since the stub returns failure, content should end up in Failed status
    /// with RetryCount = 1.
    [Fact]
    public async Task ScheduledPublishProcessor_PicksUpDueContent() { }

    /// Seeds content in Scheduled status with ScheduledAt = now + 10 minutes.
    /// Invokes the processor's claim logic. Verifies content is NOT picked up
    /// (still in Scheduled status, PublishingStartedAt remains null).
    [Fact]
    public async Task ScheduledPublishProcessor_IgnoresFutureContent() { }

    /// Seeds content in Failed status with RetryCount = 1 and NextRetryAt = now - 1 minute.
    /// Advances TimeProvider past the backoff. Invokes retry processor logic.
    /// Verifies content is retried (PublishingPipeline called again).
    [Fact]
    public async Task RetryFailedProcessor_RetriesAfterBackoffExpires() { }

    /// Seeds content in Failed status with RetryCount = 3. Invokes retry processor.
    /// Verifies: content is NOT retried, a Notification with Type = ContentFailed
    /// is persisted in the database.
    [Fact]
    public async Task RetryFailedProcessor_AfterMaxRetries_SendsNotification() { }

    /// Seeds content in Failed status with RetryCount = 1 and NextRetryAt = now + 5 minutes.
    /// Invokes retry processor. Verifies content is NOT picked up.
    [Fact]
    public async Task RetryFailedProcessor_RespectsBackoffTiming() { }

    /// Seeds content in Publishing status with PublishingStartedAt = now - 6 minutes.
    /// Invokes rehydrator logic. Verifies content is reset to Scheduled status
    /// and PublishingStartedAt is cleared.
    [Fact]
    public async Task WorkflowRehydrator_ResetsStuckPublishing() { }

    /// Seeds content in Publishing status with PublishingStartedAt = now - 2 minutes.
    /// Invokes rehydrator logic. Verifies content remains in Publishing status
    /// (under the 5-minute threshold).
    [Fact]
    public async Task WorkflowRehydrator_IgnoresRecentPublishing() { }
}
```

**TimeProvider usage:** Create a `FakeTimeProvider` (from `Microsoft.Extensions.TimeProvider.Testing` NuGet, or a simple manual implementation) that lets tests set and advance the current time. Pass it to both the `ApplicationDbContext` (via `PostgresFixture.CreateDbContext(dateTimeProvider)` if needed) and directly to the processor logic. The processors should accept `TimeProvider` in their constructors (section 07 defines this).

**NuGet addition required:** Add `Microsoft.Extensions.TimeProvider.Testing` to the Infrastructure.Tests project if not already present, for `FakeTimeProvider`. Alternatively, implement a minimal `FakeTimeProvider : TimeProvider` with a settable `GetUtcNow()`.

---

### 3. API Endpoint Integration Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Integration/ApiEndpointIntegrationTests.cs`

These tests exercise the HTTP API endpoints end-to-end, from request through MediatR to database and back. They use `CustomWebApplicationFactory` with a real PostgreSQL database.

**Test stubs:**

```csharp
/// <summary>
/// HTTP integration tests for workflow, approval, scheduling, and notification
/// endpoints. Uses CustomWebApplicationFactory with Testcontainers PostgreSQL.
/// </summary>
public class ApiEndpointIntegrationTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    // --- Workflow Endpoints ---

    /// PUT /api/workflow/autonomy with valid body -> returns 200.
    /// GET /api/workflow/autonomy -> returns the updated configuration.
    [Fact]
    public async Task AutonomyConfiguration_PutThenGet_RoundTrips() { }

    /// GET /api/workflow/autonomy/resolve?contentType=BlogPost&platform=TwitterX
    /// returns the resolved autonomy level based on current configuration.
    [Fact]
    public async Task ResolveAutonomyLevel_ReturnsCorrectLevel() { }

    /// POST /api/workflow/{id}/transition with valid target status -> returns 200.
    /// GET /api/workflow/{id}/transitions -> returns remaining allowed transitions.
    [Fact]
    public async Task TransitionContent_ValidTransition_Returns200() { }

    /// POST /api/workflow/{id}/transition with invalid target -> returns 400.
    [Fact]
    public async Task TransitionContent_InvalidTransition_Returns400() { }

    /// GET /api/workflow/audit?contentId={id} -> returns audit entries for content.
    [Fact]
    public async Task GetAuditTrail_ReturnsFilteredEntries() { }

    // --- Approval Endpoints ---

    /// Create content, transition to Review, then POST /api/approval/{id}/approve
    /// -> returns 200, content status becomes Approved.
    [Fact]
    public async Task ApproveContent_InReview_Returns200() { }

    /// POST /api/approval/{id}/reject with feedback -> returns 200,
    /// content returns to Draft, GET /api/notifications shows rejection notification.
    [Fact]
    public async Task RejectContent_WithFeedback_Returns200AndNotifies() { }

    /// GET /api/approval/pending -> returns only content in Review status.
    [Fact]
    public async Task ListPending_ReturnsOnlyReviewContent() { }

    /// POST /api/approval/batch-approve with mixed valid/invalid IDs ->
    /// returns success count (partial success).
    [Fact]
    public async Task BatchApprove_PartialSuccess_ReturnsCount() { }

    // --- Scheduling Endpoints ---

    /// POST /api/scheduling/{id}/schedule with future date -> returns 200.
    /// Content status becomes Scheduled.
    [Fact]
    public async Task ScheduleContent_FutureDate_Returns200() { }

    /// PUT /api/scheduling/{id}/reschedule with new date -> returns 200.
    [Fact]
    public async Task RescheduleContent_UpdatesTiming() { }

    /// DELETE /api/scheduling/{id} -> returns 200 (or 204),
    /// content returns to Approved.
    [Fact]
    public async Task CancelSchedule_ReturnsToApproved() { }

    // --- Notification Endpoints ---

    /// GET /api/notifications -> returns paginated list.
    [Fact]
    public async Task ListNotifications_ReturnsPaginatedResult() { }

    /// GET /api/notifications?isRead=false -> returns only unread.
    [Fact]
    public async Task ListNotifications_FilterByUnread() { }

    /// POST /api/notifications/{id}/read -> marks notification as read.
    [Fact]
    public async Task MarkNotificationRead_SetsIsReadTrue() { }

    /// POST /api/notifications/read-all -> marks all as read.
    [Fact]
    public async Task MarkAllNotificationsRead_SetsAllIsReadTrue() { }

    // --- Auth ---

    /// Any endpoint without X-Api-Key header -> returns 401.
    [Fact]
    public async Task AllEndpoints_WithoutApiKey_Return401() { }
}
```

**Setup pattern:** Identical to the existing `ContentEndpointsTests` -- `IClassFixture<PostgresFixture>`, `IAsyncLifetime`, create unique DB per test class, use `CustomWebApplicationFactory` and `CreateAuthenticatedClient()`. The `CustomWebApplicationFactory` needs to be updated (in section 08) to also remove the new background processors from hosted services during testing, to prevent them from interfering with test assertions.

**Important:** The factory's `ConfigureTestServices` must remove all `BackgroundService` processors (`ScheduledPublishProcessor`, `RetryFailedProcessor`, `WorkflowRehydrator`) to prevent them from running during API tests. This is handled by section 08's DI registration -- this section should verify that removal works by confirming processors do not alter content status during API tests.

---

### 4. SignalR Integration Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Integration/SignalRIntegrationTests.cs`

These tests verify the SignalR hub accepts/rejects connections based on API key auth, and that server-pushed events are received by connected clients. They use `Microsoft.AspNetCore.SignalR.Client` to create an in-process SignalR client connected through the test server.

**Test stubs:**

```csharp
/// <summary>
/// Integration tests for SignalR NotificationHub authentication and message delivery.
/// Uses CustomWebApplicationFactory with a real SignalR client connection.
/// </summary>
public class SignalRIntegrationTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    /// Connects to /hubs/notifications?apiKey={validKey} -> connection succeeds.
    [Fact]
    public async Task HubConnection_WithValidApiKey_Succeeds() { }

    /// Connects to /hubs/notifications without apiKey query string ->
    /// connection is rejected (HubException or connection fails).
    [Fact]
    public async Task HubConnection_WithoutApiKey_IsRejected() { }

    /// Connects to /hubs/notifications?apiKey=invalid-key ->
    /// connection is rejected.
    [Fact]
    public async Task HubConnection_WithInvalidApiKey_IsRejected() { }

    /// Connects a client, then triggers a notification send (via INotificationService
    /// or by rejecting content). Verifies the client receives a "NotificationReceived"
    /// event with the correct notification data.
    [Fact]
    public async Task NotificationReceived_EventPushedToConnectedClient() { }

    /// Connects a client, then triggers a workflow transition. Verifies the client
    /// receives a "WorkflowUpdated" event with the correct contentId and newStatus.
    [Fact]
    public async Task WorkflowUpdated_EventPushedOnTransition() { }
}
```

**NuGet addition required:** Add `Microsoft.AspNetCore.SignalR.Client` to the Infrastructure.Tests project for the `HubConnectionBuilder`.

**SignalR client pattern:** Build a `HubConnection` using the test server's base address:

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl(
        $"{_factory.Server.BaseAddress}hubs/notifications?apiKey={CustomWebApplicationFactory.TestApiKey}",
        options => options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
    .Build();
await connection.StartAsync();
```

This connects through the in-process test server without needing a real network socket.

---

## Implementation Details

### Project File Changes

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/PersonalBrandAssistant.Infrastructure.Tests.csproj`

Add two new NuGet packages:

- `Microsoft.AspNetCore.SignalR.Client` -- for SignalR integration test client connections
- `Microsoft.Extensions.TimeProvider.Testing` -- for `FakeTimeProvider` in background job tests (optional; a manual implementation is also acceptable)

### CustomWebApplicationFactory Updates

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/CustomWebApplicationFactory.cs`

The factory must be updated to remove all new background processors from test runs. Add these removals in `ConfigureTestServices`:

- `ScheduledPublishProcessor`
- `RetryFailedProcessor`
- `WorkflowRehydrator`
- `NotificationDispatcher` (the Channel-based background service for notifications)

Use the existing `RemoveService<T>` helper method. This prevents background processors from claiming scheduled content or dispatching notifications during API tests, which would cause non-deterministic test behavior.

### TestEntityFactory Extensions

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Utilities/TestEntityFactory.cs`

Add helper methods to create content in specific workflow states for integration tests:

- `CreateContentInReview()` -- creates content and transitions to Review
- `CreateContentInApproved()` -- creates content and transitions through to Approved
- `CreateContentInScheduled(DateTimeOffset scheduledAt)` -- full path to Scheduled with a ScheduledAt value
- `CreateContentInFailed(int retryCount, DateTimeOffset? nextRetryAt)` -- content in Failed state with retry metadata
- `CreateContentInPublishing(DateTimeOffset publishingStartedAt)` -- content stuck in Publishing for rehydrator tests

These follow the existing pattern of `CreateArchivedContent()` which chains `TransitionTo()` calls. The new `CapturedAutonomyLevel` field should be settable via an optional parameter (default: `AutonomyLevel.Manual`).

### Test Data Setup Helpers

For the full workflow integration tests, create a helper method that seeds the database with a `User` entity and default `AutonomyConfiguration` -- both are required foreign key references for notifications and workflow behavior. This avoids duplicating seed logic across every test:

```csharp
/// Seeds the minimum required reference data for integration tests:
/// a User entity and default AutonomyConfiguration (GlobalLevel = Manual).
private async Task SeedRequiredDataAsync(ApplicationDbContext context) { }
```

### Assertions Pattern

Integration tests should assert at the database level (query the `DbContext` after operations) rather than relying solely on HTTP response bodies. This verifies side effects like:

- `WorkflowTransitionLog` entries created with correct fields
- `Notification` entities persisted with correct `NotificationType`
- `Content.Status` updated to the expected value
- `Content.PublishingStartedAt`, `RetryCount`, `NextRetryAt` set correctly

For API tests, also assert HTTP status codes and response shapes to verify the API contract.

### Test Isolation

Each test class gets its own PostgreSQL database (via `GetUniqueConnectionString()`). Within a class, if tests share state, use `IAsyncLifetime` to reset between tests. The existing pattern creates and drops the database per test class, which is sufficient for the test counts in this section.

### Running the Tests

All tests run with the standard command:

```
dotnet test tests/PersonalBrandAssistant.Infrastructure.Tests/
```

Integration tests require Docker to be running (for Testcontainers). Tests that depend on Testcontainers use either `[Collection("Postgres")]` (shared fixture) or `IClassFixture<PostgresFixture>` (per-class fixture). The existing project already uses both patterns.