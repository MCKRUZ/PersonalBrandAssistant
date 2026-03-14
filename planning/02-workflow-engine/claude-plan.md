# Phase 02 — Workflow & Approval Engine: Implementation Plan

## 1. Context and Purpose

The Personal Brand Assistant is an AI-powered tool for managing personal branding across Twitter/X, LinkedIn, Instagram, and YouTube. Phase 01 established the foundation: .NET 10 Clean Architecture (Domain → Application → Infrastructure → API), PostgreSQL 17 with EF Core 10, MediatR CQRS, and an Angular 19 shell.

Phase 02 builds the **Workflow & Approval Engine** — the central nervous system through which every piece of content flows. It adds configurable autonomy (from fully manual to fully autonomous), approval workflows, scheduled publishing, background job processing, real-time notifications, and an extended audit trail.

The system is self-hosted on a Synology NAS via Docker, serving a single user. This shapes key technology choices: in-process queuing over external message brokers, BackgroundService over Hangfire, and SignalR without a backplane.

## 2. Existing Codebase

### Domain Layer (What Exists)

The `Content` entity already implements a state machine with `AllowedTransitions` dictionary and `TransitionTo(ContentStatus newStatus)`. States: Draft, Review, Approved, Scheduled, Publishing, Published, Failed, Archived. Transitions raise `ContentStateChangedEvent`.

`AutonomyLevel` enum exists: Manual, Assisted, SemiAuto, Autonomous. `UserSettings` value object has `DefaultAutonomyLevel` (defaults to Manual).

`AuditLogEntry` captures entity changes via an EF Core interceptor (`AuditLogInterceptor`). `ContentCalendarSlot` handles scheduling with date, time, timezone, recurrence.

### Application Layer (What Exists)

MediatR CQRS with `Result<T>` pattern, FluentValidation pipeline, and cursor-based `PagedResult<T>`. `IApplicationDbContext` exposes DbSets for all entities. `IDateTimeProvider` abstracts time. Content CRUD commands/queries are implemented.

### Infrastructure Layer (What Exists)

EF Core 10 + Npgsql with JSONB columns, xmin optimistic concurrency. `AuditableInterceptor` sets timestamps. `AuditLogCleanupService` (BackgroundService) runs every 24h to prune old entries.

### Test Infrastructure (What Exists)

xUnit + Moq + Testcontainers.PostgreSql. `CustomWebApplicationFactory` with configurable environment and test API key. `PostgresFixture` provides unique connection strings per test. `TestEntityFactory` creates test entities including full state machine traversal.

## 3. Architecture Overview

### New Project Structure

No new projects needed — Phase 02 code lives within the existing four layers:

```
src/PersonalBrandAssistant.Domain/
  Entities/
    AutonomyConfiguration.cs        # NEW: override rules entity
    Notification.cs                  # NEW: notification entity
    WorkflowTransitionLog.cs         # NEW: workflow-specific audit
  Enums/
    NotificationType.cs              # NEW
    ActorType.cs                     # NEW: User, System, Agent
  Events/
    ContentApprovedEvent.cs          # NEW
    ContentRejectedEvent.cs          # NEW
    ContentScheduledEvent.cs         # NEW
    ContentPublishedEvent.cs         # NEW

src/PersonalBrandAssistant.Application/
  Common/
    Interfaces/
      IWorkflowEngine.cs            # NEW
      IApprovalService.cs           # NEW
      IContentScheduler.cs          # NEW
      INotificationService.cs       # NEW
      IPublishingPipeline.cs        # NEW: stub for Phase 04
  Features/
    Workflow/
      Commands/
        TransitionContent/           # NEW
        ConfigureAutonomy/           # NEW
      Queries/
        GetAutonomyConfiguration/    # NEW
        ResolveAutonomyLevel/        # NEW
    Approval/
      Commands/
        ApproveContent/              # NEW
        RejectContent/               # NEW
        BatchApproveContent/         # NEW
      Queries/
        ListPendingContent/          # NEW
    Scheduling/
      Commands/
        ScheduleContent/             # NEW
        RescheduleContent/           # NEW
        CancelSchedule/              # NEW
    Notifications/
      Commands/
        SendNotification/            # NEW
        MarkNotificationRead/        # NEW
        MarkAllNotificationsRead/    # NEW
      Queries/
        ListNotifications/           # NEW

src/PersonalBrandAssistant.Infrastructure/
  Services/
    WorkflowEngine.cs               # NEW: Stateless-powered
    ApprovalService.cs              # NEW
    ContentScheduler.cs             # NEW
    NotificationService.cs          # NEW
    PublishingPipelineStub.cs       # NEW: no-op for Phase 04
    WorkflowChannelService.cs      # NEW: Channel<T> management
  BackgroundJobs/
    ScheduledPublishProcessor.cs    # NEW
    RetryFailedProcessor.cs         # NEW
    WorkflowRehydrator.cs           # NEW: startup stuck-publishing detection
  Hubs/
    NotificationHub.cs              # NEW: SignalR hub
  Data/
    Configurations/
      AutonomyConfigurationConfiguration.cs  # NEW
      NotificationConfiguration.cs           # NEW
      WorkflowTransitionLogConfiguration.cs  # NEW

src/PersonalBrandAssistant.Api/
  Endpoints/
    WorkflowEndpoints.cs            # NEW
    ApprovalEndpoints.cs            # NEW
    SchedulingEndpoints.cs          # NEW
    NotificationEndpoints.cs        # NEW
```

## 4. Domain Layer Changes

### AutonomyConfiguration Entity

A single entity (one row in the database — singleton pattern) storing the complete autonomy configuration. Extends `AuditableEntityBase`.

Uses a well-known fixed primary key (`Guid.Empty`) to enforce singleton pattern — only one row can exist.

Fields:
- `AutonomyLevel GlobalLevel` — the base level
- `List<ContentTypeOverride> ContentTypeOverrides` — stored as JSONB array of `{ contentType, level }` objects
- `List<PlatformOverride> PlatformOverrides` — stored as JSONB array of `{ platformType, level }` objects
- `List<ContentTypePlatformOverride> ContentTypePlatformOverrides` — stored as JSONB array of `{ contentType, platformType, level }` objects

Value object types for the override lists avoid brittle string keys.

Resolution method: `ResolveLevel(ContentType type, PlatformType? platform)` — checks ContentType+Platform first, then Platform, then ContentType, then Global. Returns the most specific match.

### Notification Entity

Extends `EntityBase` (not auditable — notifications are their own audit).

Fields:
- `Guid UserId` (FK to User)
- `NotificationType Type` — enum: ContentReadyForReview, ContentApproved, ContentRejected, ContentPublished, ContentFailed
- `string Title`
- `string Message`
- `Guid? ContentId` (FK to Content, nullable — some notifications may not relate to content)
- `bool IsRead`
- `DateTimeOffset CreatedAt`

### WorkflowTransitionLog Entity

Extends `EntityBase`. Purpose-built for workflow audit trail (separate from the generic AuditLogEntry).

Fields:
- `Guid ContentId` (FK to Content)
- `ContentStatus FromStatus`
- `ContentStatus ToStatus`
- `string? Reason` — user feedback on rejection, "auto-approved by autonomy rule", etc.
- `ActorType ActorType` — User, System, Agent
- `string? ActorId` — user ID, "ScheduledPublishProcessor", agent name
- `DateTimeOffset Timestamp`

### New Domain Events

- `ContentApprovedEvent(Guid ContentId)`
- `ContentRejectedEvent(Guid ContentId, string Feedback)`
- `ContentScheduledEvent(Guid ContentId, DateTimeOffset ScheduledAt)`
- `ContentPublishedEvent(Guid ContentId, PlatformType[] Platforms)`

### Content Entity Modifications

Add a field to snapshot the autonomy level at content creation time:
- `AutonomyLevel CapturedAutonomyLevel` — set once during creation, never updated. This ensures changing the dial mid-pipeline doesn't retroactively change behavior.

Add retry tracking:
- `int RetryCount` (default 0)
- `DateTimeOffset? NextRetryAt`

Add publishing tracking for stuck detection:
- `DateTimeOffset? PublishingStartedAt` — set when transitioning to Publishing. The rehydrator treats content in Publishing for more than 5 minutes as stuck and requeues it.

## 5. Application Layer

### IWorkflowEngine Interface

```csharp
Task<Result<Unit>> TransitionAsync(Guid contentId, ContentStatus targetStatus, string? reason = null, ActorType actor = ActorType.User, CancellationToken ct = default);
Task<Result<ContentStatus[]>> GetAllowedTransitionsAsync(Guid contentId, CancellationToken ct = default);
Task<bool> ShouldAutoApproveAsync(Guid contentId, CancellationToken ct = default);
```

The `TransitionAsync` method:
1. Loads the content entity
2. Configures a Stateless state machine with guards based on the content's `CapturedAutonomyLevel`
3. Fires the transition
4. Writes a `WorkflowTransitionLog` entry
5. Raises appropriate domain events
6. Saves changes

Guard logic for auto-approval:
- If `CapturedAutonomyLevel == Autonomous` → auto-approve all transitions from Draft
- If `CapturedAutonomyLevel == SemiAuto` and content has `ParentContentId` (repurposed content where parent exists in Published/Approved status) → auto-approve
- If `CapturedAutonomyLevel == Assisted` → AI creates drafts in Draft status, auto-transitions to Review, notifies user. Same as Manual for approval (user must approve). The difference from Manual is that Assisted doesn't require manual AI generation trigger — the AI auto-creates.
- If `CapturedAutonomyLevel == Manual` → require explicit manual approval (content stays in Review)

### IApprovalService Interface

```csharp
Task<Result<Unit>> ApproveAsync(Guid contentId, CancellationToken ct = default);
Task<Result<Unit>> RejectAsync(Guid contentId, string feedback, CancellationToken ct = default);
Task<Result<Unit>> EditAndApproveAsync(Guid contentId, UpdateContentCommand changes, CancellationToken ct = default);
Task<Result<int>> BatchApproveAsync(Guid[] contentIds, CancellationToken ct = default);
```

Approve: calls `IWorkflowEngine.TransitionAsync(id, Approved)`. If content has `ScheduledAt`, chains to `Scheduled`.
Reject: calls `TransitionAsync(id, Draft)` with reason = feedback. Sends `ContentRejected` notification.
BatchApprove: wraps individual approvals in a transaction. Returns count of successfully approved items.

### IContentScheduler Interface

```csharp
Task<Result<Unit>> ScheduleAsync(Guid contentId, DateTimeOffset scheduledAt, CancellationToken ct = default);
Task<Result<Unit>> RescheduleAsync(Guid contentId, DateTimeOffset newScheduledAt, CancellationToken ct = default);
Task<Result<Unit>> CancelAsync(Guid contentId, CancellationToken ct = default);
```

Schedule: validates content is in Approved status, sets `ScheduledAt`, transitions to Scheduled.
Cancel: transitions Scheduled → Approved (content was already approved, just unscheduled), clears `ScheduledAt`.

### INotificationService Interface

```csharp
Task SendAsync(NotificationType type, string title, string message, Guid? contentId = null, CancellationToken ct = default);
Task<PagedResult<Notification>> GetUnreadAsync(int pageSize = 20, string? cursor = null, CancellationToken ct = default);
Task MarkReadAsync(Guid notificationId, CancellationToken ct = default);
Task MarkAllReadAsync(CancellationToken ct = default);
```

SendAsync: persists to database first, then pushes to SignalR hub in a try/catch (best-effort). If the DB write fails, no push is attempted. If the SignalR push fails (client offline), the notification is still persisted — client gets it from DB on next load.

### IPublishingPipeline Interface (Stub)

```csharp
Task<Result<Unit>> PublishAsync(Guid contentId, CancellationToken ct = default);
```

Phase 02 provides a stub implementation that returns `Result<Unit>.Failure(ErrorCode.InternalError, "Publishing pipeline not implemented")`. This prevents content from being falsely marked as Published before Phase 04 integrates real platform APIs. Content stays in Publishing/Failed state. Phase 04 replaces the stub with real platform integrations.

### MediatR Commands and Queries

All follow the existing pattern: Command/Query record → Handler → FluentValidation validator.

**Workflow feature:**
- `TransitionContentCommand(Guid ContentId, ContentStatus TargetStatus, string? Reason)` → calls IWorkflowEngine
- `ConfigureAutonomyCommand(AutonomyLevel GlobalLevel, Dictionary<ContentType, AutonomyLevel>? ContentTypeOverrides, ...)` → updates AutonomyConfiguration
- `GetAutonomyConfigurationQuery` → returns current config
- `ResolveAutonomyLevelQuery(ContentType Type, PlatformType? Platform)` → returns resolved level

**Approval feature:**
- `ApproveContentCommand(Guid ContentId)` → calls IApprovalService
- `RejectContentCommand(Guid ContentId, string Feedback)` → calls IApprovalService
- `BatchApproveContentCommand(Guid[] ContentIds)` → calls IApprovalService
- `ListPendingContentQuery(ContentType? Type, PlatformType? Platform, int PageSize, string? Cursor)` → queries content in Review status

**Scheduling feature:**
- `ScheduleContentCommand(Guid ContentId, DateTimeOffset ScheduledAt)`
- `RescheduleContentCommand(Guid ContentId, DateTimeOffset NewScheduledAt)`
- `CancelScheduleCommand(Guid ContentId)`

**Notification feature:**
- `SendNotificationCommand(NotificationType Type, string Title, string Message, Guid? ContentId)`
- `MarkNotificationReadCommand(Guid NotificationId)`
- `MarkAllNotificationsReadCommand`
- `ListNotificationsQuery(bool? IsRead, int PageSize, string? Cursor)`

## 6. Infrastructure Layer

### WorkflowEngine Implementation

Uses **Stateless NuGet** (v5.20.1). For each transition request:

1. Load the Content entity from database
2. Create a Stateless `StateMachine<ContentStatus, ContentTrigger>` with external state storage (`() => content.Status, s => content.Status = s`)
3. Define triggers: `Approve`, `Reject`, `Schedule`, `Publish`, `Fail`, `Archive`, `ReturnToDraft`
4. Configure transitions with guards based on `content.CapturedAutonomyLevel`
5. Fire the trigger (which internally calls `Content.TransitionTo()` — WorkflowEngine is the single source of truth, and it calls the domain validation)
6. Create `WorkflowTransitionLog` entry
7. SaveChanges (xmin concurrency protects against race conditions)
8. After SaveChanges succeeds, dispatch domain events (post-commit). Notification handlers respond to these events — never called inline during the transition

The state machine is reconstructed each time (Stateless is stateless by design — configuration is code, state is data).

### Channel<T> and WorkflowChannelService

A singleton service managing bounded channels:

- `Channel<NotificationCommand>` — for notification dispatch (event-driven, immediate push)

Scheduled publishing and retry processors use **polling-only** design (simpler, fewer moving parts). No Channel<T> needed for processors — they query the database directly on their timer intervals.

On startup, `WorkflowRehydrator` (IHostedService) queries the database for:
- Content in `Publishing` status with `PublishingStartedAt` older than 5 minutes → marks as stuck and requeues (resets to Scheduled for re-processing)

### Background Job Processors

**ScheduledPublishProcessor** (BackgroundService):
- Uses `PeriodicTimer` with 30-second interval
- Uses atomic claim query: `UPDATE contents SET status='Publishing', publishing_started_at=now WHERE id=@id AND status='Scheduled' AND scheduled_at<=now RETURNING *` — prevents double-publish even in edge cases
- For each claimed item: call IPublishingPipeline, transition to Published (or Failed)
- On failure: increment RetryCount, set NextRetryAt

**RetryFailedProcessor** (BackgroundService):
- Uses `PeriodicTimer` with 60-second interval
- Queries content where `Status == Failed AND RetryCount < 3 AND NextRetryAt <= now`
- Retry backoff: attempt 1 → 1min, attempt 2 → 5min, attempt 3 → 15min
- After 3 failures: send notification, leave in Failed status

ContentExpiryProcessor is **removed from v1 scope** — requirements are too vague. Will be added in a later phase when clear expiry rules are defined.

### SignalR NotificationHub

A simple hub mapped to `/hubs/notifications`. No client-callable methods in v1 — server-only push.

Server events:
- `NotificationReceived(NotificationDto)` — new notification
- `WorkflowUpdated(Guid contentId, ContentStatus newStatus)` — content status change

The hub is injected via `IHubContext<NotificationHub>` into `NotificationService` and `WorkflowEngine`.

SignalR auth: do NOT exempt `/hubs/` from the API key middleware. Instead, require the API key as a query string parameter (`?apiKey=xxx`) during SignalR negotiation. Validate in `OnConnectedAsync` — reject connections without a valid key. This keeps the hub secured with the same auth mechanism as the REST API.

### EF Core Configurations

**AutonomyConfiguration:** Singleton pattern enforced via unique constraint. JSONB columns for override dictionaries. Seeded with default (Global = Manual, no overrides).

**Notification:** Index on (UserId, IsRead, CreatedAt DESC) for efficient unread queries. FK to User and optional FK to Content.

**WorkflowTransitionLog:** Index on (ContentId, Timestamp DESC) for per-content audit queries. Index on Timestamp for date range queries.

**Content polling indexes:** Add composite indexes for background processor queries:
- `(Status, ScheduledAt)` — for ScheduledPublishProcessor to efficiently find due content
- `(Status, NextRetryAt)` — for RetryFailedProcessor to find retryable content
- Use `AsNoTracking()` for all read-only processor queries

### Retention Cleanup

Extend `AuditLogCleanupService` (or add a companion service) to also clean:
- `WorkflowTransitionLog` entries older than configurable threshold (default: 180 days)
- `Notification` entries older than configurable threshold (default: 90 days)

These thresholds should be configurable via `appsettings.json`.

### DependencyInjection Changes

Register in `AddInfrastructure()`:
- `IWorkflowEngine` → `WorkflowEngine` (scoped)
- `IApprovalService` → `ApprovalService` (scoped)
- `IContentScheduler` → `ContentScheduler` (scoped)
- `INotificationService` → `NotificationService` (scoped)
- `IPublishingPipeline` → `PublishingPipelineStub` (scoped)
- `WorkflowChannelService` (singleton)
- All BackgroundService processors as hosted services
- SignalR services

## 7. API Layer

### New Endpoint Groups

**WorkflowEndpoints** (`/api/workflow`):
- `PUT /api/workflow/autonomy` — update autonomy configuration
- `GET /api/workflow/autonomy` — get current configuration
- `GET /api/workflow/autonomy/resolve` — resolve level for content type + platform
- `POST /api/workflow/{id}/transition` — manual state transition
- `GET /api/workflow/{id}/transitions` — allowed transitions for content
- `GET /api/workflow/audit` — query workflow audit trail (filter by contentId, dateFrom, dateTo)

**ApprovalEndpoints** (`/api/approval`):
- `GET /api/approval/pending` — list content awaiting review
- `POST /api/approval/{id}/approve`
- `POST /api/approval/{id}/reject` — body: `{ feedback: string }`
- `POST /api/approval/batch-approve` — body: `{ contentIds: guid[] }`

**SchedulingEndpoints** (`/api/scheduling`):
- `POST /api/scheduling/{id}/schedule` — body: `{ scheduledAt: datetime }`
- `PUT /api/scheduling/{id}/reschedule` — body: `{ scheduledAt: datetime }`
- `DELETE /api/scheduling/{id}` — cancel schedule

**NotificationEndpoints** (`/api/notifications`):
- `GET /api/notifications` — list notifications (query: isRead, pageSize, cursor)
- `POST /api/notifications/{id}/read`
- `POST /api/notifications/read-all`

### SignalR Hub Mapping

Add `app.MapHub<NotificationHub>("/hubs/notifications")` after the existing middleware chain. Do NOT exempt `/hubs/` from API key middleware — SignalR uses query string API key auth validated in `OnConnectedAsync`.

### Program.cs Changes

- Add `builder.Services.AddSignalR()`
- Add Stateless NuGet package (no DI registration needed — it's instantiated per-use)
- Register new endpoint groups

## 8. Testing Strategy

### Unit Tests (Domain + Application)

**AutonomyConfiguration resolution tests:**
- Global-only returns global level
- ContentType override wins over global
- Platform override wins over ContentType
- ContentType+Platform override wins over all
- Missing override falls back correctly

**WorkflowEngine transition tests:**
- Valid transitions succeed under each autonomy level
- Invalid transitions return error
- Auto-approval triggers for Autonomous and SemiAuto (repurposed)
- Manual/Assisted require explicit approval
- CapturedAutonomyLevel is immutable after creation

**ApprovalService tests:**
- Approve transitions Review → Approved (→ Scheduled if ScheduledAt set)
- Reject transitions Review → Draft with feedback
- EditAndApprove applies changes then approves
- BatchApprove handles partial failures

**ContentScheduler tests:**
- Schedule sets ScheduledAt and transitions
- Reschedule updates timing
- Cancel returns to Approved (not Draft — content was already approved)

**Notification tests:**
- SendAsync creates entity and pushes to SignalR
- GetUnread returns only unread, ordered by newest

### Integration Tests (Infrastructure)

**Database integration (Testcontainers):**
- AutonomyConfiguration CRUD and singleton enforcement
- Notification persistence and queries
- WorkflowTransitionLog writes and queries
- Full workflow: create content → transition through states → verify audit trail

**Background job integration:**
- ScheduledPublishProcessor picks up due content (use TimeProvider to control time)
- RetryFailedProcessor respects backoff timing
- Rehydrator loads unprocessed items on startup

**API integration:**
- Full approval flow via HTTP endpoints
- Autonomy configuration CRUD
- Notification endpoints
- SignalR connection and message receipt (use SignalR test client)

**State machine parity test:**
- Add a test asserting that the Stateless state machine configuration in WorkflowEngine and the `Content.AllowedTransitions` dictionary agree on all valid transitions. This catches divergence between the two layers.

### Test Patterns

- Use `TimeProvider` (built into .NET 8+) to replace `IDateTimeProvider` for time-dependent tests
- Extract BackgroundService logic into scoped services for easier testing
- Use `TestEntityFactory` extensions for creating content in specific states
- Follow existing `CustomWebApplicationFactory` patterns

## 9. Migration and Backwards Compatibility

### Database Changes

New tables: `AutonomyConfigurations`, `Notifications`, `WorkflowTransitionLogs`

New columns on `Contents`: `CapturedAutonomyLevel` (int, default 0 = Manual), `RetryCount` (int, default 0), `NextRetryAt` (timestamptz, nullable)

Since the project uses `EnsureCreatedAsync()` (no migrations yet), these changes are applied automatically on next startup. If migrations are introduced later, create an explicit migration.

### Seed Data

DataSeeder should seed a default `AutonomyConfiguration` with GlobalLevel = Manual and no overrides.

### Content Entity Impact

The existing `Content.AllowedTransitions` dictionary and `TransitionTo()` method remain as domain-level validation. `WorkflowEngine` is the **single source of truth** — it calls `Content.TransitionTo()` internally as part of its transition flow. Both layers must agree on valid transitions (enforced by parity test). External callers should always go through `IWorkflowEngine`, never call `TransitionTo()` directly.
