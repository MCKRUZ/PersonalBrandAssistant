# Section 01 -- Domain Entities

## Overview

This section introduces the new domain entities, enums, domain events, and Content entity modifications required by the Phase 02 Workflow and Approval Engine. It also adds EF Core configurations for the new entities, updates `IApplicationDbContext` with new DbSets, and extends the `TestEntityFactory`. Everything in this section is foundational -- all subsequent sections depend on it.

## Dependencies

None. This is the first section and has no dependencies on other Phase 02 sections.

## Blocked Sections

All other sections (02 through 09) depend on this section being completed first.

---

## Tests First

All tests follow the existing xUnit conventions. Test files live in `tests/PersonalBrandAssistant.Domain.Tests/`.

### New Enum Tests

**File:** `tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs` (modify existing)

Add tests verifying:

- `NotificationType` enum has five members: `ContentReadyForReview`, `ContentApproved`, `ContentRejected`, `ContentPublished`, `ContentFailed`
- `ActorType` enum has three members: `User`, `System`, `Agent`

These follow the same pattern already in the file for other enum tests.

### Notification Entity Tests

**File:** `tests/PersonalBrandAssistant.Domain.Tests/Entities/NotificationTests.cs` (new)

- Test: Constructor sets `IsRead` to `false` by default
- Test: `MarkAsRead()` sets `IsRead` to `true`
- Test: All required fields (`UserId`, `Type`, `Title`, `Message`) are populated on creation
- Test: `ContentId` is nullable and unset by default

### WorkflowTransitionLog Entity Tests

**File:** `tests/PersonalBrandAssistant.Domain.Tests/Entities/WorkflowTransitionLogTests.cs` (new)

- Test: Constructor sets `Timestamp` to a non-default value
- Test: All required fields (`ContentId`, `FromStatus`, `ToStatus`, `ActorType`, `Timestamp`) populated on creation
- Test: `Reason` and `ActorId` are optional (nullable)

### Content Entity Modification Tests

**File:** `tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentTests.cs` (modify existing)

Add tests for the new properties:

- Test: `CapturedAutonomyLevel` defaults to `AutonomyLevel.Manual` (the enum's zero value)
- Test: `RetryCount` defaults to `0`
- Test: `NextRetryAt` is nullable and unset by default
- Test: `PublishingStartedAt` is nullable and unset by default

### Domain Event Tests

**File:** `tests/PersonalBrandAssistant.Domain.Tests/Events/DomainEventTests.cs` (new)

- Test: `ContentApprovedEvent` contains correct `ContentId`
- Test: `ContentRejectedEvent` contains `ContentId` and `Feedback`
- Test: `ContentScheduledEvent` contains `ContentId` and `ScheduledAt`
- Test: `ContentPublishedEvent` contains `ContentId` and `Platforms`
- Test: All four event types implement `IDomainEvent`

These are simple record construction tests confirming the record properties are correctly assigned.

### EF Core Configuration Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs` (modify existing)

Add tests verifying:

- `Notifications` table is configured with indexes on `(UserId, IsRead, CreatedAt DESC)`
- `WorkflowTransitionLogs` table is configured with indexes on `(ContentId, Timestamp DESC)` and `(Timestamp)`
- `Contents` table has new composite indexes: `(Status, ScheduledAt)` and `(Status, NextRetryAt)`
- New columns on `Contents` have correct defaults: `CapturedAutonomyLevel` = 0, `RetryCount` = 0, `NextRetryAt` = null, `PublishingStartedAt` = null

---

## Implementation Details

### New Enums

**File:** `src/PersonalBrandAssistant.Domain/Enums/NotificationType.cs` (new)

```csharp
namespace PersonalBrandAssistant.Domain.Enums;

public enum NotificationType
{
    ContentReadyForReview,
    ContentApproved,
    ContentRejected,
    ContentPublished,
    ContentFailed
}
```

**File:** `src/PersonalBrandAssistant.Domain/Enums/ActorType.cs` (new)

```csharp
namespace PersonalBrandAssistant.Domain.Enums;

public enum ActorType { User, System, Agent }
```

### Notification Entity

**File:** `src/PersonalBrandAssistant.Domain/Entities/Notification.cs` (new)

Extends `EntityBase` (not auditable -- notifications serve as their own audit trail). Contains:

- `Guid UserId` -- FK to User
- `NotificationType Type`
- `string Title`
- `string Message`
- `Guid? ContentId` -- optional FK to Content
- `bool IsRead` -- defaults to `false`
- `DateTimeOffset CreatedAt` -- set at construction time

Provide a `MarkAsRead()` method that sets `IsRead = true`.

Use a static factory method `Create(Guid userId, NotificationType type, string title, string message, Guid? contentId = null)` that sets `CreatedAt` to `DateTimeOffset.UtcNow`. The private parameterless constructor is for EF Core.

### WorkflowTransitionLog Entity

**File:** `src/PersonalBrandAssistant.Domain/Entities/WorkflowTransitionLog.cs` (new)

Extends `EntityBase`. Purpose-built for workflow audit trail, separate from the generic `AuditLogEntry`. Contains:

- `Guid ContentId` -- FK to Content
- `ContentStatus FromStatus`
- `ContentStatus ToStatus`
- `string? Reason` -- user rejection feedback, "auto-approved by autonomy rule", etc.
- `ActorType ActorType` -- User, System, or Agent
- `string? ActorId` -- user ID, "ScheduledPublishProcessor", agent name, etc.
- `DateTimeOffset Timestamp` -- set at construction time

Use a static factory method `Create(Guid contentId, ContentStatus from, ContentStatus to, ActorType actorType, string? actorId = null, string? reason = null)` that sets `Timestamp` to `DateTimeOffset.UtcNow`.

### New Domain Events

**File:** `src/PersonalBrandAssistant.Domain/Events/ContentApprovedEvent.cs` (new)

```csharp
public sealed record ContentApprovedEvent(Guid ContentId) : IDomainEvent;
```

**File:** `src/PersonalBrandAssistant.Domain/Events/ContentRejectedEvent.cs` (new)

```csharp
public sealed record ContentRejectedEvent(Guid ContentId, string Feedback) : IDomainEvent;
```

**File:** `src/PersonalBrandAssistant.Domain/Events/ContentScheduledEvent.cs` (new)

```csharp
public sealed record ContentScheduledEvent(Guid ContentId, DateTimeOffset ScheduledAt) : IDomainEvent;
```

**File:** `src/PersonalBrandAssistant.Domain/Events/ContentPublishedEvent.cs` (new)

```csharp
public sealed record ContentPublishedEvent(Guid ContentId, PlatformType[] Platforms) : IDomainEvent;
```

All follow the same sealed record pattern as the existing `ContentStateChangedEvent`.

### Content Entity Modifications

**File:** `src/PersonalBrandAssistant.Domain/Entities/Content.cs` (modify)

Add four new properties to the `Content` class:

- `AutonomyLevel CapturedAutonomyLevel { get; private init; }` -- snapshot of the autonomy level at creation time. Uses `private init` so it is set once during creation and never changed. This ensures changing the global autonomy dial mid-pipeline does not retroactively alter behavior for in-flight content.
- `int RetryCount { get; set; }` -- defaults to `0`. Tracks how many times publishing has been retried after failure.
- `DateTimeOffset? NextRetryAt { get; set; }` -- nullable. Set by the retry processor to schedule the next retry attempt.
- `DateTimeOffset? PublishingStartedAt { get; set; }` -- nullable. Set when transitioning to `Publishing` status. The `WorkflowRehydrator` uses this to detect content stuck in Publishing for more than 5 minutes.

Update the `Content.Create` factory method to accept an optional `AutonomyLevel capturedAutonomyLevel = AutonomyLevel.Manual` parameter and assign it to `CapturedAutonomyLevel`.

The existing `AllowedTransitions` dictionary and `TransitionTo()` method remain unchanged -- they continue to serve as domain-level validation. The `WorkflowEngine` (section 03) will be the single source of truth that calls `TransitionTo()` internally.

Make the `AllowedTransitions` dictionary `internal static` (instead of `private static`) so that the state machine parity test in section 03 can access it. Alternatively, expose it via a public static read-only property `ValidTransitions` that returns an `IReadOnlyDictionary`.

### IApplicationDbContext Updates

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs` (modify)

Add two new DbSet properties:

```csharp
DbSet<Notification> Notifications { get; }
DbSet<WorkflowTransitionLog> WorkflowTransitionLogs { get; }
```

The `AutonomyConfiguration` DbSet is added in section 02.

### EF Core Configurations

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/NotificationConfiguration.cs` (new)

- Table name: `Notifications`
- Primary key on `Id`
- Required properties: `UserId`, `Type`, `Title`, `Message`, `CreatedAt`
- `IsRead` defaults to `false`
- FK to `User` via `UserId`
- Optional FK to `Content` via `ContentId` with `OnDelete(DeleteBehavior.SetNull)`
- Composite index on `(UserId, IsRead, CreatedAt DESC)` for efficient unread queries
- Ignore `DomainEvents` navigation

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/WorkflowTransitionLogConfiguration.cs` (new)

- Table name: `WorkflowTransitionLogs`
- Primary key on `Id`
- Required properties: `ContentId`, `FromStatus`, `ToStatus`, `ActorType`, `Timestamp`
- Optional: `Reason`, `ActorId`
- FK to `Content` via `ContentId` with `OnDelete(DeleteBehavior.Cascade)`
- Index on `(ContentId, Timestamp DESC)` for per-content audit queries
- Index on `(Timestamp)` for date range queries and retention cleanup
- Ignore `DomainEvents` navigation

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentConfiguration.cs` (modify)

Add configuration for the new Content properties:

- `CapturedAutonomyLevel` -- required, integer column, default value `0` (Manual)
- `RetryCount` -- required, integer column, default value `0`
- `NextRetryAt` -- optional timestamptz column
- `PublishingStartedAt` -- optional timestamptz column
- Add composite index on `(Status, ScheduledAt)` for the `ScheduledPublishProcessor`
- Add composite index on `(Status, NextRetryAt)` for the `RetryFailedProcessor`

The existing individual indexes on `Status` and `ScheduledAt` can be kept or replaced by the composites.

### ApplicationDbContext Updates

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs` (modify)

Add the two new `DbSet` properties to match `IApplicationDbContext`:

```csharp
public DbSet<Notification> Notifications => Set<Notification>();
public DbSet<WorkflowTransitionLog> WorkflowTransitionLogs => Set<WorkflowTransitionLog>();
```

### TestEntityFactory Extensions

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Utilities/TestEntityFactory.cs` (modify)

Add helper methods:

- `CreateNotification(...)` -- creates a `Notification` with sensible defaults
- `CreateWorkflowTransitionLog(...)` -- creates a `WorkflowTransitionLog` with sensible defaults
- `CreateContentInState(ContentStatus target, AutonomyLevel autonomyLevel = AutonomyLevel.Manual)` -- creates a `Content`, transitions it to the target state (using the existing `TransitionToState` path logic from `ContentTests`), clears domain events, and returns it. This replaces duplicated state-walking code across tests.

---

## File Summary

### New Files

| File | Layer |
|------|-------|
| `src/PersonalBrandAssistant.Domain/Enums/NotificationType.cs` | Domain |
| `src/PersonalBrandAssistant.Domain/Enums/ActorType.cs` | Domain |
| `src/PersonalBrandAssistant.Domain/Entities/Notification.cs` | Domain |
| `src/PersonalBrandAssistant.Domain/Entities/WorkflowTransitionLog.cs` | Domain |
| `src/PersonalBrandAssistant.Domain/Events/ContentApprovedEvent.cs` | Domain |
| `src/PersonalBrandAssistant.Domain/Events/ContentRejectedEvent.cs` | Domain |
| `src/PersonalBrandAssistant.Domain/Events/ContentScheduledEvent.cs` | Domain |
| `src/PersonalBrandAssistant.Domain/Events/ContentPublishedEvent.cs` | Domain |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/NotificationConfiguration.cs` | Infrastructure |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/WorkflowTransitionLogConfiguration.cs` | Infrastructure |
| `tests/PersonalBrandAssistant.Domain.Tests/Entities/NotificationTests.cs` | Tests |
| `tests/PersonalBrandAssistant.Domain.Tests/Entities/WorkflowTransitionLogTests.cs` | Tests |
| `tests/PersonalBrandAssistant.Domain.Tests/Events/DomainEventTests.cs` | Tests |

### Modified Files

| File | Change |
|------|--------|
| `src/PersonalBrandAssistant.Domain/Entities/Content.cs` | Add `CapturedAutonomyLevel`, `RetryCount`, `NextRetryAt`, `PublishingStartedAt`; expose `AllowedTransitions` as internal/public read-only |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs` | Add `Notifications` and `WorkflowTransitionLogs` DbSets |
| `src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs` | Add matching DbSet properties |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentConfiguration.cs` | Configure new columns, add composite indexes |
| `tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs` | Add tests for `NotificationType` and `ActorType` |
| `tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentTests.cs` | Add tests for new Content properties |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Utilities/TestEntityFactory.cs` | Add factory methods for new entities |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs` | Add configuration tests for new entities and indexes |

---

## Implementation Checklist

1. Create `NotificationType` and `ActorType` enums
2. Create `Notification` entity with factory method and `MarkAsRead()`
3. Create `WorkflowTransitionLog` entity with factory method
4. Create all four domain event records
5. Modify `Content` entity -- add four new properties, update `Create()`, expose `AllowedTransitions`
6. Update `IApplicationDbContext` with new DbSets
7. Update `ApplicationDbContext` with new DbSet properties
8. Create `NotificationConfiguration` EF Core configuration
9. Create `WorkflowTransitionLogConfiguration` EF Core configuration
10. Modify `ContentConfiguration` for new columns and composite indexes
11. Write all domain tests (enums, entities, events)
12. Extend `TestEntityFactory` with new helper methods
13. Write EF Core configuration tests
14. Run `dotnet build` and `dotnet test` to verify everything compiles and passes