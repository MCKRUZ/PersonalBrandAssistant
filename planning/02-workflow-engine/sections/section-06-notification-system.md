# Section 06 -- Notification System

## Overview

This section implements the notification system for the Workflow and Approval Engine. The notification system follows a **persist-first** pattern: every notification is written to the database before any attempt to push it via SignalR. This ensures notifications are never lost, even if the real-time push fails (e.g., client is offline). A `Channel<T>` decouples the notification creation from the dispatch, and a `NotificationDispatcher` BackgroundService reads from the channel to push events through SignalR.

The system is self-hosted on a Synology NAS via Docker, serving a single user. This means SignalR runs without a backplane (no Redis, no Azure SignalR), and in-process `Channel<T>` is sufficient for queuing.

## Dependencies

- **Section 01 (Domain Entities):** The `Notification` entity, `NotificationType` enum, and the `EntityBase` base class must exist before implementing this section.
- **No other section dependencies.** This section is parallelizable with sections 04 and 05.
- **Blocked by this section:** Sections 07 (background processors) and 08 (API endpoints) depend on the notification system.

## Existing Codebase Context

**Base classes:** `EntityBase` (in `Domain/Common/EntityBase.cs`) provides `Id` (Guid v7), domain events list, and `ClearDomainEvents()`. The `Notification` entity extends `EntityBase` (not `AuditableEntityBase` -- notifications are their own audit).

**Result pattern:** `Result<T>` in `Application/Common/Models/Result.cs` with `Success(T)`, `Failure(ErrorCode, params string[])`, `NotFound(string)`, etc.

**Pagination:** `PagedResult<T>` in `Application/Common/Models/PagedResult.cs` with cursor-based pagination using `EncodeCursor`/`DecodeCursor` based on `(CreatedAt, Id)` tuples.

**DbContext interface:** `IApplicationDbContext` in `Application/Common/Interfaces/IApplicationDbContext.cs` exposes DbSets. Must be extended to include `DbSet<Notification> Notifications`.

**API key middleware:** `ApiKeyMiddleware` in `Api/Middleware/ApiKeyMiddleware.cs` checks `X-Api-Key` header with SHA256 hash comparison. SignalR cannot send custom headers during WebSocket negotiation, so the hub must validate the API key from the query string instead.

**DI registration:** `DependencyInjection.cs` in the Infrastructure project registers services. New notification services will be registered here.

## Tests (Write First)

All tests follow xUnit + Moq conventions. Write these tests before implementing the production code.

### Domain Tests

File: `tests/PersonalBrandAssistant.Domain.Tests/Entities/NotificationTests.cs`

```csharp
/// Test: Constructor sets IsRead to false by default
/// Test: MarkAsRead sets IsRead to true
```

These are covered by Section 01 (domain entities). Only listed here for reference -- do not duplicate implementation.

### Application Layer Tests -- INotificationService

File: `tests/PersonalBrandAssistant.Application.Tests/Features/Notifications/NotificationServiceTests.cs`

```csharp
/// Test: SendAsync persists notification to database
///   Arrange: Mock IApplicationDbContext, mock IHubContext<NotificationHub>
///   Act: Call SendAsync with NotificationType.ContentApproved, title, message, contentId
///   Assert: Verify DbSet.Add called with correct Notification entity, SaveChangesAsync called

/// Test: SendAsync pushes to SignalR after DB write succeeds
///   Arrange: Mock IApplicationDbContext (SaveChangesAsync returns 1), mock IHubContext
///   Act: Call SendAsync
///   Assert: Verify IHubContext.Clients.All.SendAsync("NotificationReceived", ...) called

/// Test: SendAsync does NOT push to SignalR if DB write fails
///   Arrange: Mock IApplicationDbContext (SaveChangesAsync throws)
///   Act/Assert: Call SendAsync, verify IHubContext never called

/// Test: SendAsync handles SignalR push failure gracefully (notification still persisted)
///   Arrange: Mock IHubContext to throw on SendAsync
///   Act: Call SendAsync
///   Assert: No exception thrown, DB write still succeeded

/// Test: GetUnreadAsync returns only unread notifications, newest first
///   Arrange: Seed mix of read/unread notifications with varying CreatedAt
///   Act: Call GetUnreadAsync
///   Assert: Only IsRead=false returned, ordered by CreatedAt DESC

/// Test: MarkReadAsync sets IsRead to true
///   Arrange: Seed unread notification
///   Act: Call MarkReadAsync with notification ID
///   Assert: Notification.IsRead == true, SaveChangesAsync called

/// Test: MarkAllReadAsync marks all user's notifications as read
///   Arrange: Seed multiple unread notifications
///   Act: Call MarkAllReadAsync
///   Assert: All notifications have IsRead == true
```

### Application Layer Tests -- MediatR Handlers

File: `tests/PersonalBrandAssistant.Application.Tests/Features/Notifications/Commands/SendNotificationCommandTests.cs`

```csharp
/// Test: SendNotificationCommand handler calls INotificationService.SendAsync with correct parameters
```

File: `tests/PersonalBrandAssistant.Application.Tests/Features/Notifications/Commands/MarkNotificationReadCommandTests.cs`

```csharp
/// Test: MarkNotificationReadCommand handler calls INotificationService.MarkReadAsync
```

File: `tests/PersonalBrandAssistant.Application.Tests/Features/Notifications/Commands/MarkAllNotificationsReadCommandTests.cs`

```csharp
/// Test: MarkAllNotificationsReadCommand handler calls INotificationService.MarkAllReadAsync
```

File: `tests/PersonalBrandAssistant.Application.Tests/Features/Notifications/Queries/ListNotificationsQueryTests.cs`

```csharp
/// Test: ListNotificationsQuery returns paginated results with optional IsRead filter
///   Arrange: Mock IApplicationDbContext with notification data
///   Act: Call handler with IsRead=false filter
///   Assert: Returns PagedResult with only unread notifications
```

### Infrastructure Tests -- SignalR NotificationHub

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Hubs/NotificationHubTests.cs`

```csharp
/// Test: Hub connection requires API key in query string
///   Arrange: Create test SignalR connection with ?apiKey=valid-key
///   Assert: Connection established successfully

/// Test: Hub rejects connection without valid API key
///   Arrange: Create test SignalR connection without apiKey param
///   Assert: Connection rejected (HubException or disconnect)

/// Test: Server can push NotificationReceived event
///   Arrange: Connected client
///   Act: Server sends NotificationReceived via IHubContext
///   Assert: Client receives the event with correct DTO

/// Test: Server can push WorkflowUpdated event
///   Arrange: Connected client
///   Act: Server sends WorkflowUpdated via IHubContext
///   Assert: Client receives contentId and newStatus
```

### Infrastructure Tests -- Channel and Dispatcher

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/WorkflowChannelServiceTests.cs`

```csharp
/// Test: NotificationCommand channel is bounded (capacity 100)
/// Test: Write and read from notification channel works
```

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/NotificationDispatcherTests.cs`

```csharp
/// Test: Dispatcher reads from channel and pushes via IHubContext
/// Test: Dispatcher handles IHubContext failure gracefully (logs, continues)
/// Test: Dispatcher stops when cancellation is requested
```

### Infrastructure Tests -- EF Core Configuration

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Data/NotificationConfigurationTests.cs`

```csharp
/// Test: Notification indexes exist (UserId, IsRead, CreatedAt DESC)
///   Use Testcontainers to verify the index is created on the actual database schema

/// Test: Notification FK to User exists
/// Test: Notification optional FK to Content exists
```

### Infrastructure Tests -- Retention Cleanup

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/NotificationCleanupTests.cs`

```csharp
/// Test: Cleans Notification entries older than threshold (default 90 days)
/// Test: Does not clean entries within threshold
/// Test: Threshold is configurable via appsettings
```

## Implementation Details

### 1. INotificationService Interface

File: `src/PersonalBrandAssistant.Application/Common/Interfaces/INotificationService.cs`

```csharp
public interface INotificationService
{
    Task SendAsync(NotificationType type, string title, string message, Guid? contentId = null, CancellationToken ct = default);
    Task<PagedResult<Notification>> GetUnreadAsync(int pageSize = 20, string? cursor = null, CancellationToken ct = default);
    Task MarkReadAsync(Guid notificationId, CancellationToken ct = default);
    Task MarkAllReadAsync(CancellationToken ct = default);
}
```

This is the primary interface consumed by other services (ApprovalService sends rejection notifications, RetryFailedProcessor sends failure notifications, etc.).

### 2. NotificationService Implementation

File: `src/PersonalBrandAssistant.Infrastructure/Services/NotificationService.cs`

Implements `INotificationService`. Injected dependencies:
- `IApplicationDbContext` -- for database persistence
- `IHubContext<NotificationHub>` -- for SignalR push
- `ILogger<NotificationService>` -- for error logging

**SendAsync logic (persist-first pattern):**
1. Create a `Notification` entity with all fields populated
2. Add to `DbContext.Notifications`
3. Call `SaveChangesAsync` -- if this fails, exception propagates (no SignalR push attempted)
4. After successful DB write, push to SignalR in a try/catch block
5. On SignalR failure: log a warning, do NOT rethrow. The notification is already persisted; the client will pick it up on next load or reconnect.

**GetUnreadAsync logic:**
- Query `Notifications` where `IsRead == false`
- Order by `CreatedAt` descending
- Apply cursor-based pagination using the existing `PagedResult<T>.DecodeCursor`/`EncodeCursor` pattern
- Use `AsNoTracking()` for read-only queries

**MarkReadAsync logic:**
- Load the notification by ID, set `IsRead = true`, save

**MarkAllReadAsync logic:**
- Use `ExecuteUpdateAsync` to bulk-update all unread notifications for efficiency: `context.Notifications.Where(n => !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true))`

### 3. MediatR Commands and Queries

All follow the existing pattern: record command/query, handler class, FluentValidation validator.

**SendNotificationCommand:**

File: `src/PersonalBrandAssistant.Application/Features/Notifications/Commands/SendNotification/SendNotificationCommand.cs`

```csharp
public record SendNotificationCommand(NotificationType Type, string Title, string Message, Guid? ContentId) : IRequest<Result<Unit>>;
```

Handler delegates to `INotificationService.SendAsync`.

**MarkNotificationReadCommand:**

File: `src/PersonalBrandAssistant.Application/Features/Notifications/Commands/MarkNotificationRead/MarkNotificationReadCommand.cs`

```csharp
public record MarkNotificationReadCommand(Guid NotificationId) : IRequest<Result<Unit>>;
```

**MarkAllNotificationsReadCommand:**

File: `src/PersonalBrandAssistant.Application/Features/Notifications/Commands/MarkAllNotificationsRead/MarkAllNotificationsReadCommand.cs`

```csharp
public record MarkAllNotificationsReadCommand : IRequest<Result<Unit>>;
```

**ListNotificationsQuery:**

File: `src/PersonalBrandAssistant.Application/Features/Notifications/Queries/ListNotifications/ListNotificationsQuery.cs`

```csharp
public record ListNotificationsQuery(bool? IsRead, int PageSize = 20, string? Cursor = null) : IRequest<Result<PagedResult<Notification>>>;
```

Handler queries `IApplicationDbContext.Notifications` directly with optional `IsRead` filter, cursor-based pagination, ordered by `CreatedAt` descending.

### 4. SignalR NotificationHub

File: `src/PersonalBrandAssistant.Infrastructure/Hubs/NotificationHub.cs`

A minimal SignalR hub with no client-callable methods in v1 (server-push only).

**Key design decisions:**
- The hub path is `/hubs/notifications`
- Authentication uses the API key via query string (`?apiKey=xxx`) validated in `OnConnectedAsync`
- If the API key is missing or invalid, `OnConnectedAsync` calls `Context.Abort()` to reject the connection
- The API key middleware already runs on `/hubs/` paths (do NOT exempt them), but since SignalR WebSocket connections cannot send custom headers after the initial HTTP handshake, the hub must also accept the key from the query string

**Server events pushed from the hub:**
- `NotificationReceived(NotificationDto)` -- a new notification was created
- `WorkflowUpdated(Guid contentId, ContentStatus newStatus)` -- content status changed

The hub is never called directly. Instead, `IHubContext<NotificationHub>` is injected into `NotificationService` and `WorkflowEngine` (from Section 03) to push events.

**OnConnectedAsync implementation sketch:**

```csharp
public override async Task OnConnectedAsync()
{
    var apiKey = Context.GetHttpContext()?.Request.Query["apiKey"].ToString();
    if (string.IsNullOrEmpty(apiKey) || !IsValidApiKey(apiKey))
    {
        Context.Abort();
        return;
    }
    await base.OnConnectedAsync();
}
```

The `IsValidApiKey` method should use the same SHA256 hash comparison as `ApiKeyMiddleware`. To avoid duplication, extract the key validation into a shared static helper (e.g., `ApiKeyValidator.Validate(string providedKey, byte[] expectedHash)`) that both the middleware and the hub can use.

### 5. WorkflowChannelService

File: `src/PersonalBrandAssistant.Infrastructure/Services/WorkflowChannelService.cs`

A singleton service managing a bounded `Channel<NotificationCommand>` for async notification dispatch.

```csharp
/// <summary>
/// Manages bounded channels for async notification dispatch.
/// Registered as singleton. Channel capacity: 100.
/// </summary>
public class WorkflowChannelService
{
    // Channel<NotificationCommand> NotificationChannel { get; }
    // Task WriteNotificationAsync(NotificationCommand command, CancellationToken ct)
    // IAsyncEnumerable<NotificationCommand> ReadAllNotificationsAsync(CancellationToken ct)
}
```

The `NotificationCommand` is a simple record containing the notification type, title, message, and optional content ID. The channel is bounded with `BoundedChannelFullMode.Wait` (capacity 100 is generous for a single-user system).

### 6. NotificationDispatcher BackgroundService

File: `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/NotificationDispatcher.cs`

A `BackgroundService` that reads from the `WorkflowChannelService` notification channel and pushes events via `IHubContext<NotificationHub>`.

**ExecuteAsync logic:**
1. Read from the notification channel using `ReadAllNotificationsAsync`
2. For each command, push the event to all connected SignalR clients
3. Wrap each push in a try/catch -- log errors but continue processing
4. Exit cleanly when cancellation is requested

This service is the consumer side of the channel. Producers (like `NotificationService.SendAsync`) write to the channel after persisting the notification to the database.

**Note:** The `NotificationService` can also push directly via `IHubContext` (as described in the persist-first pattern). The channel/dispatcher pattern provides an alternative path for fire-and-forget notifications from other services that do not need the full persist-first flow. Both paths are valid and can coexist.

### 7. EF Core Configuration

File: `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/NotificationConfiguration.cs`

```csharp
/// <summary>
/// EF Core configuration for Notification entity.
/// - FK to User (required)
/// - FK to Content (optional)
/// - Composite index on (UserId, IsRead, CreatedAt DESC) for efficient unread queries
/// </summary>
```

Key configuration points:
- `HasOne<User>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade)`
- `HasOne<Content>().WithMany().HasForeignKey(n => n.ContentId).IsRequired(false).OnDelete(DeleteBehavior.SetNull)`
- `HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt }).IsDescending(false, false, true)` -- the descending on `CreatedAt` optimizes "newest first" queries

### 8. IApplicationDbContext Extension

File: `src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs`

Add to the existing interface:

```csharp
DbSet<Notification> Notifications { get; }
```

Also add the corresponding `DbSet<Notification>` property to `ApplicationDbContext`.

### 9. Retention Cleanup

Either extend the existing `AuditLogCleanupService` or create a companion `NotificationCleanupService` BackgroundService.

File: `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/NotificationCleanupService.cs` (if separate)

**Behavior:**
- Runs on a periodic timer (every 24 hours, same as `AuditLogCleanupService`)
- Deletes `Notification` entries older than a configurable threshold (default: 90 days)
- Threshold read from `appsettings.json` under a key like `Retention:NotificationDays`
- Uses `ExecuteDeleteAsync` for efficient bulk deletion

### 10. DI Registration

File: `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs`

Add to the `AddInfrastructure` method:

```csharp
services.AddScoped<INotificationService, NotificationService>();
services.AddSingleton<WorkflowChannelService>();
services.AddHostedService<NotificationDispatcher>();
services.AddHostedService<NotificationCleanupService>();
```

SignalR registration happens in the API layer's `Program.cs` (covered in Section 08):

```csharp
builder.Services.AddSignalR();
// ...
app.MapHub<NotificationHub>("/hubs/notifications");
```

### 11. API Key Validation Extraction

File: `src/PersonalBrandAssistant.Api/Auth/ApiKeyValidator.cs` (new shared helper)

Extract the SHA256 hash comparison logic from `ApiKeyMiddleware` into a static helper so both the middleware and `NotificationHub.OnConnectedAsync` can use it without code duplication.

```csharp
/// <summary>
/// Shared API key validation using constant-time SHA256 comparison.
/// Used by both ApiKeyMiddleware and NotificationHub.
/// </summary>
public static class ApiKeyValidator
{
    public static byte[] ComputeHash(string apiKey);
    public static bool Validate(string providedKey, byte[] expectedHash);
}
```

Refactor `ApiKeyMiddleware` to use `ApiKeyValidator` internally. Inject the computed hash (from configuration) into `NotificationHub` via constructor or `IOptions<>`.

## File Summary

Files to **create**:
- `src/PersonalBrandAssistant.Application/Common/Interfaces/INotificationService.cs`
- `src/PersonalBrandAssistant.Application/Features/Notifications/Commands/SendNotification/SendNotificationCommand.cs`
- `src/PersonalBrandAssistant.Application/Features/Notifications/Commands/SendNotification/SendNotificationCommandHandler.cs`
- `src/PersonalBrandAssistant.Application/Features/Notifications/Commands/MarkNotificationRead/MarkNotificationReadCommand.cs`
- `src/PersonalBrandAssistant.Application/Features/Notifications/Commands/MarkNotificationRead/MarkNotificationReadCommandHandler.cs`
- `src/PersonalBrandAssistant.Application/Features/Notifications/Commands/MarkAllNotificationsRead/MarkAllNotificationsReadCommand.cs`
- `src/PersonalBrandAssistant.Application/Features/Notifications/Commands/MarkAllNotificationsRead/MarkAllNotificationsReadCommandHandler.cs`
- `src/PersonalBrandAssistant.Application/Features/Notifications/Queries/ListNotifications/ListNotificationsQuery.cs`
- `src/PersonalBrandAssistant.Application/Features/Notifications/Queries/ListNotifications/ListNotificationsQueryHandler.cs`
- `src/PersonalBrandAssistant.Infrastructure/Services/NotificationService.cs`
- `src/PersonalBrandAssistant.Infrastructure/Services/WorkflowChannelService.cs`
- `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/NotificationDispatcher.cs`
- `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/NotificationCleanupService.cs`
- `src/PersonalBrandAssistant.Infrastructure/Hubs/NotificationHub.cs`
- `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/NotificationConfiguration.cs`
- `src/PersonalBrandAssistant.Api/Auth/ApiKeyValidator.cs`
- `tests/PersonalBrandAssistant.Application.Tests/Features/Notifications/NotificationServiceTests.cs`
- `tests/PersonalBrandAssistant.Application.Tests/Features/Notifications/Commands/SendNotificationCommandTests.cs`
- `tests/PersonalBrandAssistant.Application.Tests/Features/Notifications/Commands/MarkNotificationReadCommandTests.cs`
- `tests/PersonalBrandAssistant.Application.Tests/Features/Notifications/Commands/MarkAllNotificationsReadCommandTests.cs`
- `tests/PersonalBrandAssistant.Application.Tests/Features/Notifications/Queries/ListNotificationsQueryTests.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Hubs/NotificationHubTests.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/WorkflowChannelServiceTests.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/NotificationDispatcherTests.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Data/NotificationConfigurationTests.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/NotificationCleanupTests.cs`

Files to **modify**:
- `src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs` -- add `DbSet<Notification> Notifications`
- `src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs` -- add `DbSet<Notification> Notifications`
- `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` -- register notification services
- `src/PersonalBrandAssistant.Api/Middleware/ApiKeyMiddleware.cs` -- refactor to use shared `ApiKeyValidator`

## NuGet Packages

- `Microsoft.AspNetCore.SignalR` -- already included in the ASP.NET Core framework; no additional package needed for server-side
- `Microsoft.AspNetCore.SignalR.Client` -- needed in the **test project** only, for integration tests that connect to the hub as a client

## Implementation Order

1. Create `INotificationService` interface
2. Create MediatR commands/queries (records + validators only)
3. Create `NotificationConfiguration` (EF Core)
4. Update `IApplicationDbContext` and `ApplicationDbContext`
5. Create `ApiKeyValidator` shared helper and refactor `ApiKeyMiddleware`
6. Create `NotificationHub` with API key validation in `OnConnectedAsync`
7. Create `WorkflowChannelService`
8. Create `NotificationService` implementation (persist-first pattern)
9. Create `NotificationDispatcher` BackgroundService
10. Create `NotificationCleanupService`
11. Create MediatR command/query handlers
12. Register all services in `DependencyInjection.cs`
13. Write and verify all tests pass