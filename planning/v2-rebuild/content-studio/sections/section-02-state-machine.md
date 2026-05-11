# Section 02: Content State Machine

## Overview

This section implements the content lifecycle state machine using the **Stateless** NuGet package. The state machine governs all status transitions for `Content` entities (Idea, Draft, Review, Approved, Scheduled, Published, Archived), enforces guards (e.g., body must not be empty before review), and applies entry actions (e.g., set `PublishedAt` on publish). Command handlers in later sections delegate all transition logic to this machine.

**Depends on:** section-01-schema-updates (Content entity must have `HangfireJobId`, `IsDeleted`, `Children` properties)

**Blocks:** section-05-core-commands, section-06-status-commands

---

## Key Design Decisions

- The state machine does NOT call BlogConnector, Hangfire, or any external service. It only manages status transitions and timestamps. Orchestration of side effects belongs to command handlers.
- Each command handler constructs a fresh state machine instance from a Content entity, fires a trigger, and saves changes. The machine is stateless in the library sense -- it reads/writes the entity's `Status` property directly.
- Guards are synchronous boolean checks on the Content entity (e.g., `!string.IsNullOrWhiteSpace(content.Body)`).
- Entry actions are async and mutate the Content entity's timestamp fields.
- Invalid transitions throw `InvalidOperationException` from Stateless. Command handlers catch this and return `Result<T>.Fail(...)`.

---

## Tests First

All tests go in: `tests/PBA.Application.Tests/Features/Content/ContentStateMachineTests.cs`

The test project already has xUnit, Moq, and the in-memory EF Core provider. The test file uses the same project conventions as existing tests (e.g., `CreateIdeaHandlerTests.cs`).

### Test File Structure

```csharp
// tests/PBA.Application.Tests/Features/Content/ContentStateMachineTests.cs
namespace PBA.Application.Tests.Features.Content;

public class ContentStateMachineTests
{
    private static Domain.Entities.Content CreateContent(
        ContentStatus status = ContentStatus.Idea,
        string body = "",
        DateTimeOffset? scheduledAt = null)
    {
        // Helper: creates a Content entity in the specified state with optional body/scheduledAt
    }
}
```

### Test Cases (20 tests)

**Valid transitions (14 tests):**

| Test Name | Setup | Trigger | Expected State |
|-----------|-------|---------|----------------|
| `Fire_StartDraft_FromIdea_TransitionsToDraft` | Status=Idea | StartDraft | Draft |
| `Fire_SubmitForReview_FromDraft_TransitionsToReview` | Status=Draft, Body="content" | SubmitForReview | Review |
| `Fire_Approve_FromDraft_TransitionsToApproved` | Status=Draft, Body="content" | Approve | Approved |
| `Fire_Archive_FromDraft_TransitionsToArchived` | Status=Draft | Archive | Archived |
| `Fire_Approve_FromReview_TransitionsToApproved` | Status=Review | Approve | Approved |
| `Fire_RequestChanges_FromReview_TransitionsToDraft` | Status=Review | RequestChanges | Draft |
| `Fire_Archive_FromReview_TransitionsToArchived` | Status=Review | Archive | Archived |
| `Fire_Schedule_FromApproved_TransitionsToScheduled` | Status=Approved, ScheduledAt=future | Schedule | Scheduled |
| `Fire_PublishNow_FromApproved_TransitionsToPublished` | Status=Approved | PublishNow | Published |
| `Fire_Publish_FromScheduled_TransitionsToPublished` | Status=Scheduled | Publish | Published |
| `Fire_Unschedule_FromScheduled_TransitionsToApproved` | Status=Scheduled | Unschedule | Approved |
| `Fire_Archive_FromPublished_TransitionsToArchived` | Status=Published | Archive | Archived |
| `Fire_Unpublish_FromPublished_TransitionsToDraft` | Status=Published | Unpublish | Draft |
| `Fire_Restore_FromArchived_TransitionsToDraft` | Status=Archived | Restore | Draft |

**Guard tests (4 tests):**

| Test Name | Setup | Trigger | Expected |
|-----------|-------|---------|----------|
| `Fire_SubmitForReview_FromDraft_FailsWhenBodyEmpty` | Status=Draft, Body="" | SubmitForReview | Throws InvalidOperationException |
| `Fire_Approve_FromDraft_FailsWhenBodyEmpty` | Status=Draft, Body="" | Approve | Throws InvalidOperationException |
| `Fire_Schedule_FromApproved_FailsWhenScheduledAtNull` | Status=Approved, ScheduledAt=null | Schedule | Throws InvalidOperationException |
| `Fire_InvalidTransition_IdeaToPublished_Throws` | Status=Idea | PublishNow | Throws InvalidOperationException |

**Entry action tests (2 tests):**

| Test Name | Setup | Trigger | Assertions |
|-----------|-------|---------|------------|
| `Fire_Publish_SetsPublishedAtAndUpdatedAt` | Status=Approved | PublishNow | PublishedAt != null, UpdatedAt updated |
| `Fire_Unpublish_ClearsScheduledAtAndHangfireJobId` | Status=Published, ScheduledAt=past, HangfireJobId="job1" | Unpublish | ScheduledAt == null, HangfireJobId == null, UpdatedAt updated |

### Test Pattern

Each test follows Arrange-Act-Assert:

```csharp
[Fact]
public async Task Fire_StartDraft_FromIdea_TransitionsToDraft()
{
    // Arrange: create content in Idea status
    // Act: create state machine, fire StartDraft trigger
    // Assert: content.Status == ContentStatus.Draft
}
```

For guard failures, assert that `FireAsync` throws `InvalidOperationException`:

```csharp
[Fact]
public async Task Fire_SubmitForReview_FromDraft_FailsWhenBodyEmpty()
{
    // Arrange: content in Draft with empty body
    // Act + Assert: await Assert.ThrowsAsync<InvalidOperationException>(...)
}
```

---

## Implementation

### File 1: ContentTrigger Enum

**Path:** `src/PBA.Domain/Enums/ContentTrigger.cs`

Define the trigger enum in the Domain layer alongside `ContentStatus`:

```csharp
namespace PBA.Domain.Enums;

public enum ContentTrigger
{
    StartDraft,
    SubmitForReview,
    Approve,
    RequestChanges,
    Schedule,
    Unschedule,
    PublishNow,
    Publish,
    Archive,
    Restore,
    Unpublish
}
```

11 triggers covering all possible status transitions. `Publish` is distinct from `PublishNow` -- `Publish` is fired by Hangfire when a scheduled time arrives, while `PublishNow` is the user-initiated immediate publish from Approved state.

### File 2: ContentStateMachine

**Path:** `src/PBA.Application/Features/Content/ContentStateMachine.cs`

A static helper class with a single `Create(Content content)` factory method. Returns a configured `StateMachine<ContentStatus, ContentTrigger>`.

**NuGet dependency:** Add `Stateless` package to `PBA.Application.csproj`. The Stateless library is intentionally placed in the Application layer (not Domain) because it's an infrastructure concern for orchestrating transitions, and the Application layer already has MediatR/FluentValidation dependencies.

#### Factory Method Signature

```csharp
namespace PBA.Application.Features.Content;

public static class ContentStateMachine
{
    public static StateMachine<ContentStatus, ContentTrigger> Create(Domain.Entities.Content content)
    {
        // Constructs and returns a fully configured state machine
    }
}
```

#### State Machine Constructor

The Stateless `StateMachine` constructor takes a getter and setter for the state:

```csharp
var machine = new StateMachine<ContentStatus, ContentTrigger>(
    () => content.Status,
    s => content.Status = s);
```

This means the machine reads and writes the entity's `Status` property directly. No separate state storage.

#### Transition Configuration

Configure all 14 transitions from the transition table. Use `machine.Configure(state)` fluent API:

- **Idea:** `Permit(StartDraft, Draft)`
- **Draft:** `PermitIf(SubmitForReview, Review, guard)`, `PermitIf(Approve, Approved, guard)`, `Permit(Archive, Archived)` -- where guard = body not empty
- **Review:** `Permit(Approve, Approved)`, `Permit(RequestChanges, Draft)`, `Permit(Archive, Archived)`
- **Approved:** `PermitIf(Schedule, Scheduled, guard)`, `Permit(PublishNow, Published)` -- where guard = scheduledAt set
- **Scheduled:** `Permit(Publish, Published)`, `Permit(Unschedule, Approved)`
- **Published:** `Permit(Archive, Archived)`, `Permit(Unpublish, Draft)`
- **Archived:** `Permit(Restore, Draft)`

#### Guards

Two guard functions, both checking the Content entity:

1. **Body not empty:** `() => !string.IsNullOrWhiteSpace(content.Body)` -- used on Draft->Review and Draft->Approved transitions
2. **ScheduledAt set:** `() => content.ScheduledAt.HasValue && content.ScheduledAt > DateTimeOffset.UtcNow` -- used on Approved->Scheduled transition

When a guard fails, Stateless throws `InvalidOperationException` with a message indicating the transition was not permitted. Command handlers catch this.

#### Entry Actions

Use `OnEntryFromAsync` or `OnEntryAsync` on each state configuration. These mutate the Content entity's timestamp fields:

- **Published (entry from any):** `content.PublishedAt = DateTimeOffset.UtcNow; content.UpdatedAt = DateTimeOffset.UtcNow;`
- **Scheduled (entry):** `content.UpdatedAt = DateTimeOffset.UtcNow;`
- **Archived (entry):** `content.UpdatedAt = DateTimeOffset.UtcNow;`
- **Draft (entry from any):** `content.ScheduledAt = null; content.HangfireJobId = null; content.UpdatedAt = DateTimeOffset.UtcNow;`

Use `OnEntryAsync` for each state (the Stateless library supports async entry actions, and the command handlers call `FireAsync`).

#### Usage Pattern in Command Handlers (Reference Only)

This is how downstream sections (05, 06) will use the state machine. Do NOT implement these handlers in this section -- they belong to sections 05 and 06.

```csharp
// In a command handler (section 05/06):
var content = await db.Contents.FindAsync(request.ContentId);
var machine = ContentStateMachine.Create(content);

try
{
    await machine.FireAsync(ContentTrigger.Approve);
    await db.SaveChangesAsync(ct);
    return Result<Unit>.Success(Unit.Value);
}
catch (InvalidOperationException ex)
{
    return Result<Unit>.Fail($"Invalid transition: {ex.Message}");
}
```

### Package Addition

Add to `src/PBA.Application/PBA.Application.csproj`:

```xml
<PackageReference Include="Stateless" Version="5.17.1" />
```

Verify the latest version before adding. Stateless 5.x is the current stable line targeting .NET Standard 2.0+ and is compatible with .NET 10.

Also add `Stateless` to the test project `tests/PBA.Application.Tests/PBA.Application.Tests.csproj` if direct machine construction is needed in tests (it will be, since tests construct the machine directly).

---

## Files Summary

| Action | Path | Description |
|--------|------|-------------|
| Create | `src/PBA.Domain/Enums/ContentTrigger.cs` | 11-member trigger enum |
| Create | `src/PBA.Application/Features/Content/ContentStateMachine.cs` | Static factory building Stateless machine with 14 transitions, 2 guards, 4 entry actions |
| Modify | `src/PBA.Application/PBA.Application.csproj` | Add Stateless NuGet package |
| Modify | `tests/PBA.Application.Tests/PBA.Application.Tests.csproj` | Add Stateless NuGet package (if needed for test compilation) |
| Create | `tests/PBA.Application.Tests/Features/Content/ContentStateMachineTests.cs` | 20 tests covering all transitions, guards, invalid transitions, and entry actions |

---

## Verification Checklist

1. All 21 tests pass: `dotnet test --filter "FullyQualifiedName~ContentStateMachineTests"`
2. Every valid transition from the transition table is exercised by at least one test
3. Both guards (body not empty, scheduledAt set) are tested for both pass and fail cases
4. At least one invalid transition is tested to confirm it throws
5. Entry actions for Published (sets PublishedAt) and Draft (clears ScheduledAt/HangfireJobId) are verified
6. The state machine does not reference any external services (no DI, no database, no sidecar)
7. Build succeeds: `dotnet build`

---

## Implementation Notes (Post-Build)

### Deviations from Plan
- **Namespace renamed**: `PBA.Application.Features.Content` -> `PBA.Application.Features.ContentStudio` to avoid collision with `PBA.Domain.Entities.Content` class. All downstream sections must use `ContentStudio` namespace.
- **Stateless version**: Plan specified 5.17.1, installed 5.20.1 (latest stable). API is compatible.
- **Test count**: 21 (plan: 20). Added `Fire_Schedule_FromApproved_FailsWhenScheduledAtInPast` during code review to cover the `> UtcNow` guard condition.
- **Configure consolidation**: Code review caught double `Configure` calls on Draft and Scheduled states. Merged into single blocks per state.

### Files Created
- `src/PBA.Domain/Enums/ContentTrigger.cs` — 11-member trigger enum
- `src/PBA.Application/Features/ContentStudio/ContentStateMachine.cs` — Static factory, 14 transitions, 2 guards, 4 entry actions
- `tests/PBA.Application.Tests/Features/ContentStudio/ContentStateMachineTests.cs` — 21 tests

### Files Modified
- `src/PBA.Application/PBA.Application.csproj` — Added Stateless 5.20.1
- `tests/PBA.Application.Tests/PBA.Application.Tests.csproj` — Added Stateless 5.20.1
