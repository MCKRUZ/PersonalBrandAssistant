# Section 10: Hangfire Scheduling

## Overview

This section implements scheduled content publishing using Hangfire. It adds Hangfire infrastructure (NuGet packages, PostgreSQL storage, server configuration), creates `IContentPublisher`/`ContentPublisher` that executes at scheduled times, builds a `ScheduledPublishReconciler` background service for startup catch-up, and wires `ScheduleContent`/`UnscheduleContent` command handlers to Hangfire job management.

## Dependencies

- **Section 05 (Core Commands):** `ScheduleContent` and `UnscheduleContent` command handlers
- **Section 09 (Blog Connector):** `IBlogConnector` for Blog platform publishing
- **Section 02 (State Machine):** `ContentStateMachine` and `ContentTrigger`
- **Section 01 (Schema Updates):** `Content.HangfireJobId` property

**Blocks:** Section 11 (API Endpoints for schedule/unschedule)

---

## Tests

### ContentPublisherTests

**File:** `tests/PBA.Infrastructure.Tests/Publishing/ContentPublisherTests.cs`

| Test | Description |
|------|-------------|
| `PublishAsync_PublishesContent_WhenStatusIsScheduled` | Status=Scheduled -> Published, PublishedAt set, ContentPlatformPublish record created |
| `PublishAsync_SkipsPublishing_WhenStatusIsNoLongerScheduled` | Status=Approved -> no change, no exception |
| `PublishAsync_InvokesBlogConnector_ForBlogPlatform` | PrimaryPlatform=Blog -> IBlogConnector.PublishAsync called |
| `PublishAsync_DoesNotInvokeBlogConnector_ForNonBlogPlatform` | PrimaryPlatform=Twitter -> IBlogConnector never called |
| `PublishAsync_CreatesContentPlatformPublishRecord` | Verify DB record with PublishStatus.Published |

### ScheduledPublishReconcilerTests

**File:** `tests/PBA.Infrastructure.Tests/Publishing/ScheduledPublishReconcilerTests.cs`

| Test | Description |
|------|-------------|
| `QueryOverdueContentAsync_FindsOverdueScheduledContent` | Two overdue items -> returns both IDs |
| `ReconcileAsync_PublishesEachOverdueItem` | Two overdue IDs -> PublishAsync called twice via scoped DI |
| `QueryOverdueContentAsync_IgnoresAlreadyPublishedContent` | Status=Published -> empty list |
| `QueryOverdueContentAsync_IgnoresNonScheduledContent` | Status=Approved -> empty list |
| `QueryOverdueContentAsync_IgnoresFutureScheduledContent` | ScheduledAt in future -> empty list |
| `ReconcileAsync_DoesNotPublish_WhenListEmpty` | Empty list -> PublishAsync never called |

### ScheduleContentHandlerTests

**File:** `tests/PBA.Application.Tests/Features/Content/Commands/ScheduleContentHandlerTests.cs`

| Test | Description |
|------|-------------|
| `Handle_FromApproved_SchedulesAndTransitions` | Sets ScheduledAt, fires Schedule trigger, stores HangfireJobId |
| `Handle_FromIdea_ReturnsFailure` | Invalid state -> Fail result, scheduler never called |
| `Handle_NonexistentContent_ReturnsNotFound` | Missing content -> NotFound result |

### UnscheduleContentHandlerTests

**File:** `tests/PBA.Application.Tests/Features/Content/Commands/UnscheduleContentHandlerTests.cs`

| Test | Description |
|------|-------------|
| `Handle_FromScheduled_UnschedulesAndTransitions` | Fires Unschedule trigger, cancels job, clears fields |
| `Handle_WithNullHangfireJobId_DoesNotCallCancel` | Null job ID -> CancelScheduledPublish never called |
| `Handle_FromDraft_ReturnsFailure` | Invalid state -> Fail result, scheduler never called |
| `Handle_NonexistentContent_ReturnsNotFound` | Missing content -> NotFound result |

---

## Implementation Details

### NuGet Packages

- `PBA.Infrastructure.csproj`: `Hangfire.Core` 1.8.17, `Hangfire.PostgreSql` 1.20.10
- `PBA.Api.csproj`: `Hangfire.AspNetCore` 1.8.17

### Deviation: IContentScheduler abstraction instead of Hangfire.Core in Application layer

The spec called for adding `Hangfire.Core` to `PBA.Application.csproj` so command handlers could call `BackgroundJob.Schedule`/`Delete` directly. Instead, we created:

- `IContentScheduler` interface in Application layer
- `HangfireContentScheduler` implementation in Infrastructure layer

This keeps the Application layer free of infrastructure dependencies (Hangfire.Core). The `Stateless` package reference was also removed from Application since it's not needed — `ContentStateMachine` lives in Application but Stateless types don't leak into command handler signatures.

### IContentPublisher / IContentScheduler Interfaces

**File:** `src/PBA.Application/Common/Interfaces/IContentPublisher.cs`
**File:** `src/PBA.Application/Common/Interfaces/IContentScheduler.cs`

```csharp
public interface IContentPublisher { Task PublishAsync(Guid contentId); }
public interface IContentScheduler
{
    string SchedulePublish(Guid contentId, DateTimeOffset publishAt);
    void CancelScheduledPublish(string jobId);
}
```

### ContentPublisher

**File:** `src/PBA.Infrastructure/Publishing/ContentPublisher.cs`

Dependencies: `IAppDbContext`, `IBlogConnector`, `ILogger<ContentPublisher>`

**Flow (after code review fix):**
1. Load Content by ID
2. Guard: if Status != Scheduled, log warning and return
3. If PrimaryPlatform == Blog, call `IBlogConnector.PublishAsync` for URL
4. Fire `ContentTrigger.Publish` via state machine (after connector call to prevent dirty tracking on failure)
5. Create `ContentPlatformPublish` record
6. Save changes

### HangfireContentScheduler

**File:** `src/PBA.Infrastructure/Publishing/HangfireContentScheduler.cs`

Thin wrapper around `IBackgroundJobClient`: `Schedule<IContentPublisher>` and `Delete`.

### ScheduledPublishReconciler

**File:** `src/PBA.Infrastructure/Publishing/ScheduledPublishReconciler.cs`

BackgroundService that runs once on startup:
1. Create service scope, query overdue content via `QueryOverdueContentAsync` (static method)
2. Call `ReconcileAsync` with the overdue IDs
3. For each overdue item, create a new DI scope and call `IContentPublisher.PublishAsync` (scope-per-item prevents change tracking leaks)

### UnscheduleContent ordering fix

**File:** `src/PBA.Application/Features/Content/Commands/UnscheduleContent.cs`

Code review caught a bug: the original code canceled the Hangfire job before validating the state machine transition. If the transition failed, the job was gone but content stayed Scheduled. Fixed: validate state first, cancel job after.

### TestWebApplicationFactory

**File:** `tests/PBA.Api.Tests/TestWebApplicationFactory.cs`

Added filtering for Hangfire service descriptors (including factory-based `IHostedService` registrations via `ImplementationFactory.Method.DeclaringType`) and mock `IContentScheduler`.

### Hangfire Configuration in Program.cs

```csharp
builder.Services.AddHangfire(config =>
    config.UsePostgreSqlStorage(o =>
        o.UseNpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection"))));
builder.Services.AddHangfireServer();

if (app.Environment.IsDevelopment())
    app.UseHangfireDashboard("/hangfire");
```

### DI Registration (Infrastructure)

```csharp
services.AddScoped<IContentPublisher, ContentPublisher>();
services.AddScoped<IContentScheduler, HangfireContentScheduler>();
services.AddHostedService<ScheduledPublishReconciler>();
```

---

## File Summary

| File | Action | Description |
|------|--------|-------------|
| `src/PBA.Application/Common/Interfaces/IContentPublisher.cs` | Create | Publish interface |
| `src/PBA.Application/Common/Interfaces/IContentScheduler.cs` | Create | Schedule/cancel abstraction |
| `src/PBA.Application/Features/Content/Commands/ScheduleContent.cs` | Create | Schedule command handler |
| `src/PBA.Application/Features/Content/Commands/UnscheduleContent.cs` | Create | Unschedule command handler |
| `src/PBA.Infrastructure/Publishing/ContentPublisher.cs` | Create | Hangfire job target |
| `src/PBA.Infrastructure/Publishing/HangfireContentScheduler.cs` | Create | IContentScheduler impl |
| `src/PBA.Infrastructure/Publishing/ScheduledPublishReconciler.cs` | Create | Startup catch-up service |
| `src/PBA.Infrastructure/PBA.Infrastructure.csproj` | Modify | Add Hangfire.Core, Hangfire.PostgreSql |
| `src/PBA.Api/PBA.Api.csproj` | Modify | Add Hangfire.AspNetCore |
| `src/PBA.Infrastructure/DependencyInjection.cs` | Modify | Register publisher, scheduler, reconciler |
| `src/PBA.Api/Program.cs` | Modify | Configure Hangfire server + dashboard |
| `tests/PBA.Infrastructure.Tests/Publishing/ContentPublisherTests.cs` | Create | 5 tests |
| `tests/PBA.Infrastructure.Tests/Publishing/ScheduledPublishReconcilerTests.cs` | Create | 6 tests |
| `tests/PBA.Application.Tests/Features/Content/Commands/ScheduleContentHandlerTests.cs` | Create | 3 tests |
| `tests/PBA.Application.Tests/Features/Content/Commands/UnscheduleContentHandlerTests.cs` | Create | 4 tests |
| `tests/PBA.Api.Tests/TestWebApplicationFactory.cs` | Modify | Remove Hangfire services, mock IContentScheduler |

---

## Key Design Decisions

1. **Same PostgreSQL database** for Hangfire storage -- simplifies single-user Docker setup
2. **ContentPublisher is idempotent** -- silently returns if content no longer Scheduled
3. **Reconciler is one-shot** -- runs once on startup, not polling (acceptable for v1)
4. **IContentScheduler abstraction** -- keeps Application layer free of Hangfire.Core dependency (deviation from spec)
5. **Scoped ContentPublisher** -- matches scoped IAppDbContext in Hangfire job scopes
6. **Scope-per-item in reconciler** -- prevents EF change tracking leaks between failed/successful publishes (code review fix)
7. **State transition after connector call** -- prevents dirty tracked entities if blog connector throws (code review fix)
8. **Validate before cancel in UnscheduleContent** -- prevents orphaned state if transition is invalid (code review fix)

## Test Results

All 324 tests pass: 9 migration + 76 infrastructure + 212 application + 27 API.
