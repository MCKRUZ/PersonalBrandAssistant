# Section 05 — Content Scheduler

## Overview

This section implements the `IContentScheduler` interface and its `ContentScheduler` infrastructure service. The content scheduler manages three operations: scheduling approved content for future publication, rescheduling already-scheduled content, and cancelling a schedule (returning content to Approved status, not Draft). It delegates state transitions to `IWorkflowEngine` and validates business rules around content status and schedule timing.

## Dependencies

- **Section 01 (Domain Entities):** The `Content` entity with its `ScheduledAt` property, `ContentStatus` enum, and the `ContentScheduledEvent` domain event.
- **Section 03 (Workflow Engine):** `IWorkflowEngine` interface and its `TransitionAsync` method, which handles state machine transitions and audit logging.

These must be implemented before this section.

## Files to Create

| File | Layer | Purpose |
|------|-------|---------|
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IContentScheduler.cs` | Application | Interface definition |
| `src/PersonalBrandAssistant.Application/Features/Scheduling/Commands/ScheduleContent/ScheduleContentCommand.cs` | Application | MediatR command + handler + validator |
| `src/PersonalBrandAssistant.Application/Features/Scheduling/Commands/RescheduleContent/RescheduleContentCommand.cs` | Application | MediatR command + handler + validator |
| `src/PersonalBrandAssistant.Application/Features/Scheduling/Commands/CancelSchedule/CancelScheduleCommand.cs` | Application | MediatR command + handler + validator |
| `src/PersonalBrandAssistant.Infrastructure/Services/ContentScheduler.cs` | Infrastructure | Implementation |
| `tests/PersonalBrandAssistant.Application.Tests/Features/Scheduling/ContentSchedulerTests.cs` | Tests | Unit tests |

## Tests (Write First)

All tests go in `tests/PersonalBrandAssistant.Application.Tests/Features/Scheduling/ContentSchedulerTests.cs`. Use xUnit and Moq. Mock `IWorkflowEngine`, `IApplicationDbContext`, and `IDateTimeProvider`.

### ContentSchedulerTests

```csharp
/// Test: ScheduleAsync sets ScheduledAt and transitions Approved -> Scheduled
/// Arrange: Content in Approved status, scheduledAt 1 hour from now
/// Act: Call ScheduleAsync
/// Assert: Content.ScheduledAt == scheduledAt, IWorkflowEngine.TransitionAsync called
///         with (contentId, ContentStatus.Scheduled), returns Success

/// Test: ScheduleAsync fails when content is not Approved
/// Arrange: Content in Draft status
/// Act: Call ScheduleAsync
/// Assert: Returns Failure with ErrorCode.ValidationFailed, message indicating
///         content must be in Approved status

/// Test: ScheduleAsync fails when scheduledAt is in the past
/// Arrange: Content in Approved status, scheduledAt 1 hour ago
/// Act: Call ScheduleAsync
/// Assert: Returns Failure with ErrorCode.ValidationFailed, message indicating
///         schedule time must be in the future

/// Test: RescheduleAsync updates ScheduledAt on Scheduled content
/// Arrange: Content in Scheduled status with existing ScheduledAt, new time 2 hours from now
/// Act: Call RescheduleAsync
/// Assert: Content.ScheduledAt == newScheduledAt, returns Success

/// Test: RescheduleAsync fails when content is not Scheduled
/// Arrange: Content in Approved status
/// Act: Call RescheduleAsync
/// Assert: Returns Failure with ErrorCode.ValidationFailed

/// Test: RescheduleAsync fails when newScheduledAt is in the past
/// Arrange: Content in Scheduled status, newScheduledAt 1 hour ago
/// Act: Call RescheduleAsync
/// Assert: Returns Failure with ErrorCode.ValidationFailed

/// Test: CancelAsync transitions Scheduled -> Approved (not Draft)
/// Arrange: Content in Scheduled status
/// Act: Call CancelAsync
/// Assert: IWorkflowEngine.TransitionAsync called with (contentId, ContentStatus.Approved),
///         returns Success

/// Test: CancelAsync clears ScheduledAt
/// Arrange: Content in Scheduled status with ScheduledAt set
/// Act: Call CancelAsync
/// Assert: Content.ScheduledAt == null

/// Test: CancelAsync fails when content is not Scheduled
/// Arrange: Content in Draft status
/// Act: Call CancelAsync
/// Assert: Returns Failure with ErrorCode.ValidationFailed
```

### MediatR Command Validator Tests

These go in `tests/PersonalBrandAssistant.Application.Tests/Features/Scheduling/` as separate test files per command.

```csharp
/// ScheduleContentCommandValidatorTests:
/// Test: ScheduleContentCommand validates ContentId is not empty (Guid.Empty fails)
/// Test: ScheduleContentCommand validates ScheduledAt is in the future

/// RescheduleContentCommandValidatorTests:
/// Test: RescheduleContentCommand validates ContentId is not empty
/// Test: RescheduleContentCommand validates NewScheduledAt is in the future

/// CancelScheduleCommandValidatorTests:
/// Test: CancelScheduleCommand validates ContentId is not empty
```

## Interface Definition

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IContentScheduler.cs`

```csharp
namespace PersonalBrandAssistant.Application.Common.Interfaces;

/// <summary>
/// Manages content scheduling operations: schedule, reschedule, and cancel.
/// Delegates state transitions to IWorkflowEngine.
/// </summary>
public interface IContentScheduler
{
    Task<Result<Unit>> ScheduleAsync(Guid contentId, DateTimeOffset scheduledAt, CancellationToken ct = default);
    Task<Result<Unit>> RescheduleAsync(Guid contentId, DateTimeOffset newScheduledAt, CancellationToken ct = default);
    Task<Result<Unit>> CancelAsync(Guid contentId, CancellationToken ct = default);
}
```

The `Result<T>` type is the existing type at `src/PersonalBrandAssistant.Application/Common/Models/Result.cs`. `Unit` is from MediatR.

## Implementation Details

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/ContentScheduler.cs`

The `ContentScheduler` class implements `IContentScheduler`. It requires three constructor dependencies:

- `IApplicationDbContext` -- for loading and saving Content entities
- `IWorkflowEngine` -- for delegating state transitions (which handles audit logging, domain events, and concurrency)
- `IDateTimeProvider` -- for "now" comparisons (enables testability via TimeProvider)

### ScheduleAsync Logic

1. Load the Content entity by ID. Return `NotFound` if missing.
2. Validate the content is in `ContentStatus.Approved`. If not, return `ValidationFailed` with message "Content must be in Approved status to schedule."
3. Validate `scheduledAt` is in the future relative to `IDateTimeProvider.UtcNow`. If not, return `ValidationFailed` with message "Scheduled time must be in the future."
4. Set `content.ScheduledAt = scheduledAt`.
5. Delegate to `IWorkflowEngine.TransitionAsync(contentId, ContentStatus.Scheduled, reason: null, ActorType.User)`.
6. Return the result from the workflow engine (which handles SaveChanges internally).

Important: The workflow engine's `TransitionAsync` handles saving changes and raising domain events (including `ContentScheduledEvent`). The scheduler sets the `ScheduledAt` property before calling the engine, so it is persisted in the same SaveChanges call.

### RescheduleAsync Logic

1. Load the Content entity by ID. Return `NotFound` if missing.
2. Validate the content is in `ContentStatus.Scheduled`. If not, return `ValidationFailed` with message "Content must be in Scheduled status to reschedule."
3. Validate `newScheduledAt` is in the future. If not, return `ValidationFailed`.
4. Set `content.ScheduledAt = newScheduledAt`.
5. Call `SaveChangesAsync` on the db context. No state transition needed -- the content stays in Scheduled status, only the timing changes.
6. Return `Result<Unit>.Success(Unit.Value)`.

Note: Rescheduling does not require a workflow engine call because the status does not change. It is a simple property update.

### CancelAsync Logic

1. Load the Content entity by ID. Return `NotFound` if missing.
2. Validate the content is in `ContentStatus.Scheduled`. If not, return `ValidationFailed` with message "Content must be in Scheduled status to cancel."
3. Clear `content.ScheduledAt = null`.
4. Delegate to `IWorkflowEngine.TransitionAsync(contentId, ContentStatus.Approved, reason: "Schedule cancelled", ActorType.User)`.
5. Return the result from the workflow engine.

Critical design decision: Cancel returns to `Approved`, not `Draft`. The content was already approved -- cancelling the schedule merely un-schedules it, preserving the approval. The existing `Content.AllowedTransitions` dictionary does not include `Scheduled -> Approved`. This transition must be added to the `AllowedTransitions` dictionary in Section 01 (domain entities). Verify that `[ContentStatus.Scheduled]` includes `ContentStatus.Approved` in the allowed array. If the current code only has `[ContentStatus.Scheduled] = [ContentStatus.Publishing, ContentStatus.Draft, ContentStatus.Archived]`, then `ContentStatus.Approved` must be appended.

Looking at the existing code: `[ContentStatus.Scheduled] = [ContentStatus.Publishing, ContentStatus.Draft, ContentStatus.Archived]` -- this does NOT include `Approved`. Section 01 must add `ContentStatus.Approved` to this array. The content scheduler depends on this change.

## MediatR Commands

### ScheduleContentCommand

**File:** `src/PersonalBrandAssistant.Application/Features/Scheduling/Commands/ScheduleContent/ScheduleContentCommand.cs`

```csharp
/// <summary>
/// Command record: ScheduleContentCommand(Guid ContentId, DateTimeOffset ScheduledAt) : IRequest<Result<Unit>>
/// Handler: injects IContentScheduler, delegates to ScheduleAsync
/// Validator: ContentId != Guid.Empty, ScheduledAt > now (inject IDateTimeProvider)
/// </summary>
```

### RescheduleContentCommand

**File:** `src/PersonalBrandAssistant.Application/Features/Scheduling/Commands/RescheduleContent/RescheduleContentCommand.cs`

```csharp
/// <summary>
/// Command record: RescheduleContentCommand(Guid ContentId, DateTimeOffset NewScheduledAt) : IRequest<Result<Unit>>
/// Handler: injects IContentScheduler, delegates to RescheduleAsync
/// Validator: ContentId != Guid.Empty, NewScheduledAt > now
/// </summary>
```

### CancelScheduleCommand

**File:** `src/PersonalBrandAssistant.Application/Features/Scheduling/Commands/CancelSchedule/CancelScheduleCommand.cs`

```csharp
/// <summary>
/// Command record: CancelScheduleCommand(Guid ContentId) : IRequest<Result<Unit>>
/// Handler: injects IContentScheduler, delegates to CancelAsync
/// Validator: ContentId != Guid.Empty
/// </summary>
```

All commands follow the existing MediatR pattern in the codebase: a record implementing `IRequest<Result<Unit>>`, a handler class implementing `IRequestHandler`, and a FluentValidation `AbstractValidator<T>`.

## DI Registration

In the `AddInfrastructure()` method (or equivalent DI setup in Infrastructure), register:

```csharp
services.AddScoped<IContentScheduler, ContentScheduler>();
```

This is part of the broader DI registration in Section 08 (API Endpoints), but the mapping itself is straightforward and can be added here for testing purposes.

## Domain Transition Prerequisite

The `Content.AllowedTransitions` dictionary currently defines:

```csharp
[ContentStatus.Scheduled] = [ContentStatus.Publishing, ContentStatus.Draft, ContentStatus.Archived]
```

For `CancelAsync` to work, `ContentStatus.Approved` must be added:

```csharp
[ContentStatus.Scheduled] = [ContentStatus.Publishing, ContentStatus.Approved, ContentStatus.Draft, ContentStatus.Archived]
```

This change belongs to Section 01 (Domain Entities). If Section 01 has not made this change, the `CancelAsync` method will fail at the workflow engine level with an invalid transition error. Ensure this dependency is met before running tests.

## Error Handling

All methods use the `Result<T>` pattern. No exceptions are thrown for business rule violations -- they return `Result<Unit>.Failure(ErrorCode.ValidationFailed, "message")` or `Result<Unit>.NotFound("message")`. The workflow engine may return its own failures (concurrency conflicts via `ErrorCode.Conflict`, invalid transitions), which are passed through to the caller.

## Key Design Decisions

1. **Cancel returns to Approved, not Draft.** This preserves the approval status. The user already approved the content; cancelling the schedule should not force re-approval.
2. **Reschedule does not go through the workflow engine.** The status does not change (stays Scheduled), so no transition log is needed. This is a simple property update.
3. **Time validation uses IDateTimeProvider.** This allows tests to control "now" via mocking, and aligns with the existing codebase pattern.
4. **ScheduleAsync sets ScheduledAt before calling TransitionAsync.** The workflow engine's SaveChanges persists both the property change and the status transition atomically.