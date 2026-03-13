# Section 03 — Workflow Engine

## Overview

This section implements the core workflow engine: the `IWorkflowEngine` interface and its Stateless-powered implementation (`WorkflowEngine`). The workflow engine is the **single source of truth** for all content state transitions. It wraps the Stateless NuGet library to configure a state machine per content item, enforces autonomy-based guards, writes `WorkflowTransitionLog` entries for audit, and dispatches domain events post-commit.

Every other service that needs to change content status (approval, scheduling, background processors) delegates to `IWorkflowEngine`. No external caller should invoke `Content.TransitionTo()` directly.

## Dependencies on Other Sections

- **Section 01 (Domain Entities):** Provides `WorkflowTransitionLog` entity, `ActorType` enum, new domain events (`ContentApprovedEvent`, `ContentRejectedEvent`, `ContentScheduledEvent`, `ContentPublishedEvent`), and Content entity modifications (`CapturedAutonomyLevel`, `RetryCount`, `NextRetryAt`, `PublishingStartedAt`). Section 01 must also add the `Scheduled -> Approved` transition to `Content.AllowedTransitions` (needed by CancelAsync in section 05, but the parity test in this section will validate it).
- **Section 02 (Autonomy Configuration):** Provides `AutonomyConfiguration` entity with its `ResolveLevel` method. The workflow engine reads the content's `CapturedAutonomyLevel` (snapshotted at creation time) to decide guard behavior.

## Existing Codebase Context

The `Content` entity at `src/PersonalBrandAssistant.Domain/Entities/Content.cs` already has:
- A `Status` property (private set, defaults to `Draft`)
- A static `AllowedTransitions` dictionary defining valid state transitions
- A `TransitionTo(ContentStatus newStatus)` method that validates the transition and raises `ContentStateChangedEvent`
- `ParentContentId` (nullable Guid) for repurposed content tracking

Key enums: `ContentStatus { Draft, Review, Approved, Scheduled, Publishing, Published, Failed, Archived }`, `AutonomyLevel { Manual, Assisted, SemiAuto, Autonomous }`.

The `Result<T>` pattern is at `src/PersonalBrandAssistant.Application/Common/Models/Result.cs` with `Success(T)`, `Failure(ErrorCode, params string[])`, `NotFound(string)`, `Conflict(string)`. `ErrorCode` has: `None, ValidationFailed, NotFound, Conflict, Unauthorized, InternalError`.

`IApplicationDbContext` at `src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs` exposes `DbSet<Content> Contents` and `SaveChangesAsync`. Section 01 will add `DbSet<WorkflowTransitionLog> WorkflowTransitionLogs`.

`EntityBase` uses `Guid.CreateVersion7()` for IDs, has `DomainEvents` list with `AddDomainEvent`/`ClearDomainEvents`.

## Tests First

All tests for this section go in the following files. Tests are listed as stubs with descriptive names; the implementer writes the full test body.

### File: `tests/PersonalBrandAssistant.Application.Tests/Features/Workflow/WorkflowEngineTests.cs`

This file contains unit tests for the `WorkflowEngine` implementation using mocked dependencies. The `WorkflowEngine` is an infrastructure service but is tested through its `IWorkflowEngine` contract. Since unit tests mock the database, they live in Application.Tests.

```csharp
namespace PersonalBrandAssistant.Application.Tests.Features.Workflow;

/// <summary>
/// Unit tests for WorkflowEngine. Dependencies (IApplicationDbContext) are mocked.
/// Content entities are created via TestEntityFactory and transitioned to desired states
/// using the existing TransitionToState helper pattern from ContentTests.
/// </summary>
public class WorkflowEngineTests
{
    // -- Valid transition tests --

    [Fact]
    /// Draft -> Review succeeds for all autonomy levels.
    public async Task TransitionAsync_DraftToReview_Succeeds() { }

    [Fact]
    /// Review -> Approved succeeds when called explicitly (Manual/Assisted levels).
    public async Task TransitionAsync_ReviewToApproved_SucceedsForManual() { }

    [Fact]
    /// Invalid transition (Draft -> Published) returns failure Result.
    public async Task TransitionAsync_InvalidTransition_ReturnsFailure() { }

    [Fact]
    /// Nonexistent contentId returns NotFound result.
    public async Task TransitionAsync_NonexistentContent_ReturnsNotFound() { }

    // -- Audit log tests --

    [Fact]
    /// Every successful transition creates a WorkflowTransitionLog with correct fields.
    public async Task TransitionAsync_CreatesWorkflowTransitionLog() { }

    [Fact]
    /// The reason parameter is captured in the transition log.
    public async Task TransitionAsync_WithReason_RecordsReasonInLog() { }

    [Fact]
    /// ActorType is correctly recorded (User, System, Agent).
    public async Task TransitionAsync_RecordsActorType() { }

    // -- Autonomy guard tests --

    [Fact]
    /// Autonomous level: Draft -> Review auto-chains to Approved.
    public async Task TransitionAsync_Autonomous_AutoApprovesFromDraftToReview() { }

    [Fact]
    /// SemiAuto with ParentContentId set and parent Published: auto-approves.
    public async Task TransitionAsync_SemiAuto_WithPublishedParent_AutoApproves() { }

    [Fact]
    /// SemiAuto without ParentContentId: does NOT auto-approve (stays in Review).
    public async Task TransitionAsync_SemiAuto_WithoutParent_DoesNotAutoApprove() { }

    [Fact]
    /// Manual level: content stays in Review, no auto-approval.
    public async Task TransitionAsync_Manual_StaysInReview() { }

    [Fact]
    /// Assisted level: same as Manual for approval (must be explicit).
    public async Task TransitionAsync_Assisted_RequiresExplicitApproval() { }

    [Fact]
    /// CapturedAutonomyLevel governs behavior, not the current global AutonomyConfiguration.
    public async Task TransitionAsync_UsesCapturedLevel_NotCurrentGlobalLevel() { }

    // -- Domain event tests --

    [Fact]
    /// Domain events are dispatched after SaveChanges (post-commit), not before.
    public async Task TransitionAsync_DispatchesDomainEventsPostCommit() { }

    // -- Concurrency tests --

    [Fact]
    /// xmin concurrency conflict (DbUpdateConcurrencyException) returns Conflict result.
    public async Task TransitionAsync_ConcurrencyConflict_ReturnsConflict() { }

    // -- GetAllowedTransitions tests --

    [Fact]
    /// Returns correct set of allowed transitions for content in Draft status.
    public async Task GetAllowedTransitionsAsync_Draft_ReturnsReviewAndArchived() { }

    [Fact]
    /// Returns correct transitions for Review status.
    public async Task GetAllowedTransitionsAsync_Review_ReturnsDraftApprovedArchived() { }

    // -- ShouldAutoApprove tests --

    [Fact]
    /// Returns true for Autonomous content.
    public async Task ShouldAutoApproveAsync_Autonomous_ReturnsTrue() { }

    [Fact]
    /// Returns false for Manual content.
    public async Task ShouldAutoApproveAsync_Manual_ReturnsFalse() { }
}
```

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/WorkflowEngineStateMachineParityTests.cs`

This is a critical structural test that ensures the Stateless state machine configuration in `WorkflowEngine` agrees with `Content.AllowedTransitions` on all valid transitions. If either side changes independently, this test catches the divergence.

```csharp
namespace PersonalBrandAssistant.Infrastructure.Tests.Services;

/// <summary>
/// Parity test: ensures the Stateless state machine in WorkflowEngine and
/// Content.AllowedTransitions agree on every valid transition.
/// 
/// Approach: iterate all ContentStatus values, ask both sources for allowed
/// transitions from that status, and assert they produce identical sets.
/// 
/// Note: Content.AllowedTransitions is private static readonly. To access it
/// for testing, either:
/// (a) Add a public static method GetAllowedTransitions(ContentStatus) on Content, or
/// (b) Use reflection in the test.
/// Option (a) is preferred for clarity.
/// </summary>
public class WorkflowEngineStateMachineParityTests
{
    [Theory]
    [MemberData(nameof(AllContentStatuses))]
    /// For each ContentStatus, the WorkflowEngine Stateless config and
    /// Content.AllowedTransitions return the same set of valid target statuses.
    public void StateMachine_And_DomainAllowedTransitions_AreInSync(ContentStatus status) { }

    public static IEnumerable<object[]> AllContentStatuses() =>
        Enum.GetValues<ContentStatus>().Select(s => new object[] { s });
}
```

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/WorkflowEngineStatelessIntegrationTests.cs`

These tests verify the Stateless library integration specifically -- that the state machine is configured correctly and triggers fire as expected. These can be pure in-memory tests (no database), instantiating the Stateless `StateMachine` directly.

```csharp
namespace PersonalBrandAssistant.Infrastructure.Tests.Services;

/// <summary>
/// Integration tests for the Stateless state machine configuration.
/// These tests instantiate the state machine directly (no DB, no mocks)
/// to verify trigger/guard configuration is correct.
/// </summary>
public class WorkflowEngineStatelessIntegrationTests
{
    [Fact]
    /// All expected triggers are configured on the state machine.
    public void StateMachine_ConfiguresAllExpectedTriggers() { }

    [Fact]
    /// Guard clauses correctly evaluate for Autonomous level.
    public void StateMachine_GuardClauses_AutonomousLevel() { }

    [Fact]
    /// Guard clauses correctly evaluate for Manual level.
    public void StateMachine_GuardClauses_ManualLevel() { }

    [Fact]
    /// External state storage reads/writes Content.Status correctly.
    public void StateMachine_ExternalStateStorage_ReadsAndWritesStatus() { }
}
```

## Implementation Details

### 1. Define the IWorkflowEngine Interface

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IWorkflowEngine.cs`

```csharp
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IWorkflowEngine
{
    /// <summary>Transition content to a new status with full audit and guard logic.</summary>
    Task<Result<Unit>> TransitionAsync(
        Guid contentId,
        ContentStatus targetStatus,
        string? reason = null,
        ActorType actor = ActorType.User,
        CancellationToken ct = default);

    /// <summary>Get all valid target statuses for the given content.</summary>
    Task<Result<ContentStatus[]>> GetAllowedTransitionsAsync(
        Guid contentId,
        CancellationToken ct = default);

    /// <summary>Check if content should be auto-approved based on CapturedAutonomyLevel.</summary>
    Task<bool> ShouldAutoApproveAsync(
        Guid contentId,
        CancellationToken ct = default);
}
```

`Unit` is a standard void-equivalent type. If one does not already exist in the codebase, define it as `public readonly record struct Unit;` in `src/PersonalBrandAssistant.Application/Common/Models/Unit.cs`.

### 2. Define ContentTrigger Enum

**File:** `src/PersonalBrandAssistant.Domain/Enums/ContentTrigger.cs`

This enum maps to Stateless triggers. It is NOT the same as `ContentStatus` -- triggers represent actions/verbs, statuses represent states/nouns.

```csharp
namespace PersonalBrandAssistant.Domain.Enums;

/// <summary>Triggers for the Stateless state machine. Each trigger maps to a state transition action.</summary>
public enum ContentTrigger
{
    Submit,        // Draft -> Review
    Approve,       // Review -> Approved
    Reject,        // Review -> Draft
    Schedule,      // Approved -> Scheduled
    Unschedule,    // Scheduled -> Approved
    Publish,       // Scheduled -> Publishing
    Complete,      // Publishing -> Published
    Fail,          // Publishing -> Failed
    ReturnToDraft, // Failed -> Draft, Approved -> Draft
    Archive,       // Any eligible -> Archived
    Unarchive,     // Archived -> Draft
}
```

### 3. Implement WorkflowEngine

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/WorkflowEngine.cs`

The implementation uses the Stateless NuGet package (`Stateless` v5.x). Key design decisions:

**Constructor dependencies:**
- `IApplicationDbContext` -- for loading Content and saving WorkflowTransitionLog
- `IDateTimeProvider` (or `TimeProvider`) -- for timestamps
- `ILogger<WorkflowEngine>` -- structured logging

**TransitionAsync flow:**

1. Load content by ID from `dbContext.Contents`. Return `NotFound` if missing.
2. Map the `targetStatus` to a `ContentTrigger` (helper method). If no trigger maps, return failure.
3. Build a Stateless `StateMachine<ContentStatus, ContentTrigger>` with external state storage:
   - State accessor: `() => content.Status`
   - State mutator: `s => { /* calls content.TransitionTo(s) internally */ }`
4. Configure all transitions. The configuration mirrors `Content.AllowedTransitions` exactly:
   - `Draft` permits `Submit` (-> Review), `Archive` (-> Archived)
   - `Review` permits `Approve` (-> Approved), `Reject` (-> Draft), `Archive` (-> Archived)
   - `Approved` permits `Schedule` (-> Scheduled), `ReturnToDraft` (-> Draft), `Archive` (-> Archived)
   - `Scheduled` permits `Publish` (-> Publishing), `Unschedule` (-> Approved), `ReturnToDraft` (-> Draft), `Archive` (-> Archived)
   - `Publishing` permits `Complete` (-> Published), `Fail` (-> Failed)
   - `Published` permits `Archive` (-> Archived)
   - `Failed` permits `ReturnToDraft` (-> Draft), `Archive` (-> Archived)
   - `Archived` permits `Unarchive` (-> Draft)
5. Add guards for auto-approval logic on the `Approve` trigger from `Review`:
   - If `content.CapturedAutonomyLevel == Autonomous`, approval is always allowed
   - If `content.CapturedAutonomyLevel == SemiAuto` and `content.ParentContentId != null`, load the parent content and check if parent is in Published or Approved status
6. Fire the mapped trigger. Catch `InvalidOperationException` from Stateless if the transition is not permitted and return a failure result.
7. Handle side effects based on the target status:
   - If transitioning to `Publishing`, set `content.PublishingStartedAt` to current time
   - If transitioning away from `Publishing`, clear `content.PublishingStartedAt`
8. Create a `WorkflowTransitionLog` entity:
   - `ContentId = content.Id`
   - `FromStatus = previousStatus` (captured before firing)
   - `ToStatus = content.Status`
   - `Reason = reason`
   - `ActorType = actor`
   - `ActorId` derived from actor type (e.g., "User" for User, caller can pass in specific IDs later)
   - `Timestamp = dateTimeProvider.UtcNow`
9. Call `dbContext.SaveChangesAsync()`. Wrap in try/catch for `DbUpdateConcurrencyException` and return `Conflict` on failure.
10. **Post-commit event dispatch:** After SaveChanges succeeds, raise appropriate domain events based on the transition. This ensures events are only dispatched for persisted state changes. The domain events to raise:
    - Review -> Approved: raise `ContentApprovedEvent`
    - Review -> Draft (rejection): raise `ContentRejectedEvent` with the reason
    - Approved -> Scheduled: raise `ContentScheduledEvent`
    - Publishing -> Published: raise `ContentPublishedEvent`

**Auto-approval chaining:** When `TransitionAsync` is called with `targetStatus = Review` (i.e., submitting for review), after the transition succeeds, check `ShouldAutoApproveAsync`. If true, recursively call `TransitionAsync` for `Approved` in the same unit of work. This means for Autonomous content, a single call to submit content results in `Draft -> Review -> Approved` with two `WorkflowTransitionLog` entries.

**GetAllowedTransitionsAsync flow:**
1. Load content by ID
2. Build the Stateless state machine (same configuration)
3. Call `stateMachine.GetPermittedTriggers()`
4. Map triggers back to target statuses
5. Return as `ContentStatus[]`

**ShouldAutoApproveAsync flow:**
1. Load content by ID
2. Check `content.CapturedAutonomyLevel`:
   - `Autonomous` -> return `true`
   - `SemiAuto` -> return `true` only if `ParentContentId` is not null AND parent content exists in `Published` or `Approved` status
   - `Assisted` or `Manual` -> return `false`

**Trigger-to-status mapping helper:** A private method that maps `ContentStatus targetStatus` plus current status to the appropriate `ContentTrigger`. For example, if current is `Review` and target is `Draft`, the trigger is `Reject`. If current is `Failed` and target is `Draft`, the trigger is `ReturnToDraft`. This is a simple dictionary/switch lookup.

### 4. DI Registration

**File to modify:** `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs`

Add the following registration inside the `AddInfrastructure` method:

```csharp
services.AddScoped<IWorkflowEngine, WorkflowEngine>();
```

The Stateless NuGet package does not require DI registration -- `StateMachine<TState, TTrigger>` is instantiated inline per call inside `WorkflowEngine`.

### 5. NuGet Package

The `Stateless` package must be added to the Infrastructure project:

```
dotnet add src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj package Stateless
```

This should have been added in Section 01 as part of project setup. If not already present, add it here.

### 6. Exposing AllowedTransitions for Parity Testing

To support the parity test without reflection, add a public static method to the `Content` entity:

**File to modify:** `src/PersonalBrandAssistant.Domain/Entities/Content.cs`

```csharp
/// <summary>
/// Exposes valid transitions for a given status. Used by parity tests to verify
/// WorkflowEngine's Stateless configuration matches domain rules.
/// </summary>
public static ContentStatus[] GetAllowedTransitions(ContentStatus status) =>
    AllowedTransitions.TryGetValue(status, out var transitions) ? transitions : [];
```

This is a read-only accessor that does not compromise encapsulation -- it returns a copy of the allowed transitions array for a given status.

## Key Design Decisions

1. **Stateless is reconstructed per call.** The `StateMachine` is not cached or stored. It is created fresh for each `TransitionAsync` or `GetAllowedTransitionsAsync` call. Stateless is designed for this -- configuration is code, state is data.

2. **WorkflowEngine calls Content.TransitionTo() internally.** The state mutator in the Stateless configuration calls `Content.TransitionTo()`, which performs its own domain validation. This means two layers validate transitions. The parity test ensures they agree. If Stateless allows a transition that `Content.TransitionTo()` rejects, the test catches it.

3. **Post-commit domain events.** Events are dispatched only after `SaveChangesAsync` succeeds. This prevents handlers from acting on state that might be rolled back. The existing EF Core interceptor pattern for domain event dispatch should be leveraged if available; otherwise, dispatch manually after SaveChanges.

4. **CapturedAutonomyLevel is immutable.** The workflow engine reads `content.CapturedAutonomyLevel` (set at creation time by Section 01's Content modifications). It never reads the current `AutonomyConfiguration` for transition decisions. This ensures changing the global autonomy dial does not retroactively affect in-flight content.

5. **Scoped lifetime.** `WorkflowEngine` is registered as scoped because it depends on `IApplicationDbContext` which is scoped (tied to the EF Core DbContext lifetime per request).

## File Summary

| File | Action |
|------|--------|
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IWorkflowEngine.cs` | Create |
| `src/PersonalBrandAssistant.Application/Common/Models/Unit.cs` | Create (if not existing) |
| `src/PersonalBrandAssistant.Domain/Enums/ContentTrigger.cs` | Create |
| `src/PersonalBrandAssistant.Infrastructure/Services/WorkflowEngine.cs` | Create |
| `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` | Modify (add registration) |
| `src/PersonalBrandAssistant.Domain/Entities/Content.cs` | Modify (add `GetAllowedTransitions` static method) |
| `tests/PersonalBrandAssistant.Application.Tests/Features/Workflow/WorkflowEngineTests.cs` | Create |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/WorkflowEngineStateMachineParityTests.cs` | Create |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/WorkflowEngineStatelessIntegrationTests.cs` | Create |