# Section 04 -- Approval Service

## Overview

This section implements the `IApprovalService` interface and its concrete `ApprovalService` class. The approval service provides approve, reject, edit-and-approve, and batch-approve operations for content in the Review state. It delegates all state transitions to `IWorkflowEngine` (section 03) and sends notifications on rejection via `INotificationService` (section 06).

This is a coordination service -- it does not perform state transitions directly. It validates preconditions, orchestrates calls to the workflow engine, and triggers side effects (notifications). The service lives in the Infrastructure layer with its interface in the Application layer.

## Dependencies

- **Section 01 (Domain Entities):** `Content` entity with `Status`, `ScheduledAt`, `ParentContentId` fields; `ContentStatus` enum; `WorkflowTransitionLog` entity; `ActorType` enum; `NotificationType` enum
- **Section 03 (Workflow Engine):** `IWorkflowEngine` interface -- all state transitions are delegated here
- **Section 06 (Notification System):** `INotificationService` interface -- used to send rejection notifications

When implementing, you can mock `IWorkflowEngine` and `INotificationService` to develop this section in isolation.

## Existing Code Context

### Result Pattern

The codebase uses `Result<T>` at `src/PersonalBrandAssistant.Application/Common/Models/Result.cs`:

```csharp
Result<T>.Success(value)
Result<T>.Failure(ErrorCode.ValidationFailed, "message")
Result<T>.NotFound("message")
Result<T>.Conflict("message")
```

`ErrorCode` enum values: `None`, `ValidationFailed`, `NotFound`, `Conflict`, `Unauthorized`, `InternalError`.

### Content Entity

At `src/PersonalBrandAssistant.Domain/Entities/Content.cs`, the `Content` entity has:
- `ContentStatus Status { get; private set; }` -- current state (Draft, Review, Approved, Scheduled, Publishing, Published, Failed, Archived)
- `DateTimeOffset? ScheduledAt { get; set; }` -- when content is scheduled for publishing
- `Guid? ParentContentId { get; set; }` -- parent content reference for repurposed content

### UpdateContentCommand

At `src/PersonalBrandAssistant.Application/Features/Content/Commands/UpdateContent/UpdateContentCommand.cs`:

```csharp
public sealed record UpdateContentCommand(
    Guid Id,
    string? Title = null,
    string? Body = null,
    PlatformType[]? TargetPlatforms = null,
    ContentMetadata? Metadata = null,
    uint Version = 0) : IRequest<Result<Unit>>;
```

### IWorkflowEngine Interface (from section 03)

```csharp
Task<Result<Unit>> TransitionAsync(Guid contentId, ContentStatus targetStatus, string? reason = null, ActorType actor = ActorType.User, CancellationToken ct = default);
```

### INotificationService Interface (from section 06)

```csharp
Task SendAsync(NotificationType type, string title, string message, Guid? contentId = null, CancellationToken ct = default);
```

### IApplicationDbContext

At `src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs`, exposes `DbSet<Content> Contents` for querying.

## Files to Create

| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IApprovalService.cs` | Interface definition |
| `src/PersonalBrandAssistant.Infrastructure/Services/ApprovalService.cs` | Implementation |
| `src/PersonalBrandAssistant.Application/Features/Approval/Commands/ApproveContent/ApproveContentCommand.cs` | MediatR command |
| `src/PersonalBrandAssistant.Application/Features/Approval/Commands/ApproveContent/ApproveContentCommandHandler.cs` | Handler |
| `src/PersonalBrandAssistant.Application/Features/Approval/Commands/ApproveContent/ApproveContentCommandValidator.cs` | Validation |
| `src/PersonalBrandAssistant.Application/Features/Approval/Commands/RejectContent/RejectContentCommand.cs` | MediatR command |
| `src/PersonalBrandAssistant.Application/Features/Approval/Commands/RejectContent/RejectContentCommandHandler.cs` | Handler |
| `src/PersonalBrandAssistant.Application/Features/Approval/Commands/RejectContent/RejectContentCommandValidator.cs` | Validation |
| `src/PersonalBrandAssistant.Application/Features/Approval/Commands/BatchApproveContent/BatchApproveContentCommand.cs` | MediatR command |
| `src/PersonalBrandAssistant.Application/Features/Approval/Commands/BatchApproveContent/BatchApproveContentCommandHandler.cs` | Handler |
| `src/PersonalBrandAssistant.Application/Features/Approval/Commands/BatchApproveContent/BatchApproveContentCommandValidator.cs` | Validation |
| `src/PersonalBrandAssistant.Application/Features/Approval/Queries/ListPendingContent/ListPendingContentQuery.cs` | MediatR query |
| `src/PersonalBrandAssistant.Application/Features/Approval/Queries/ListPendingContent/ListPendingContentQueryHandler.cs` | Handler |
| `tests/PersonalBrandAssistant.Application.Tests/Features/Approval/ApprovalServiceTests.cs` | Unit tests |
| `tests/PersonalBrandAssistant.Application.Tests/Features/Approval/Commands/ApproveContentCommandValidatorTests.cs` | Validator tests |
| `tests/PersonalBrandAssistant.Application.Tests/Features/Approval/Commands/RejectContentCommandValidatorTests.cs` | Validator tests |
| `tests/PersonalBrandAssistant.Application.Tests/Features/Approval/Commands/BatchApproveContentCommandValidatorTests.cs` | Validator tests |

## Tests (Write First)

### ApprovalService Unit Tests

File: `tests/PersonalBrandAssistant.Application.Tests/Features/Approval/ApprovalServiceTests.cs`

All tests mock `IWorkflowEngine`, `INotificationService`, and `IApplicationDbContext`. Use Moq to set up return values and verify calls.

**ApproveAsync tests:**

- `ApproveAsync_WhenContentInReview_TransitionsToApproved` -- Set up a content entity in Review status. Call `ApproveAsync`. Verify `IWorkflowEngine.TransitionAsync` was called with `(contentId, ContentStatus.Approved, null, ActorType.User, ct)`. Assert result is success.

- `ApproveAsync_WhenContentHasScheduledAt_ChainsToScheduled` -- Set up content in Review with `ScheduledAt` set to a future date. After the first transition to Approved succeeds, verify a second call to `TransitionAsync` with `ContentStatus.Scheduled`. Both transitions should succeed.

- `ApproveAsync_WhenContentNotInReview_ReturnsFailure` -- Set up content in Draft status. Call `ApproveAsync`. Verify the workflow engine returns a failure result. The approval service should propagate that failure without calling further transitions.

**RejectAsync tests:**

- `RejectAsync_TransitionsReviewToDraft_WithFeedback` -- Set up content in Review. Call `RejectAsync(contentId, "Needs more detail")`. Verify `TransitionAsync` called with `(contentId, ContentStatus.Draft, "Needs more detail", ActorType.User, ct)`.

- `RejectAsync_SendsContentRejectedNotification` -- After successful rejection, verify `INotificationService.SendAsync` was called with `NotificationType.ContentRejected`, a title containing the content identifier, the feedback message, and the content ID.

**EditAndApproveAsync tests:**

- `EditAndApproveAsync_AppliesChangesThenTransitions` -- Set up content in Review. Call `EditAndApproveAsync` with an `UpdateContentCommand` containing new body text. Verify the content update is applied (via MediatR `ISender.Send` or direct entity update) before the transition to Approved is triggered. The order matters -- edit first, then approve.

**BatchApproveAsync tests:**

- `BatchApproveAsync_ApprovesMultipleItems_ReturnsSuccessCount` -- Set up three content items in Review. Call `BatchApproveAsync` with all three IDs. Verify each gets `TransitionAsync` called. Assert result value is 3.

- `BatchApproveAsync_HandlesPartialFailures` -- Set up three content items; two in Review, one in Draft. Call `BatchApproveAsync`. The Draft item's transition fails. Assert result value is 2 (only successful ones counted). The method should NOT throw on partial failure.

### Validator Tests

File: `tests/PersonalBrandAssistant.Application.Tests/Features/Approval/Commands/ApproveContentCommandValidatorTests.cs`

- `Validate_EmptyContentId_ShouldFail` -- `ApproveContentCommand(Guid.Empty)` should produce a validation error.
- `Validate_ValidContentId_ShouldPass` -- `ApproveContentCommand(Guid.NewGuid())` should pass.

File: `tests/PersonalBrandAssistant.Application.Tests/Features/Approval/Commands/RejectContentCommandValidatorTests.cs`

- `Validate_EmptyFeedback_ShouldFail` -- Reject command with empty or whitespace feedback should fail.
- `Validate_EmptyContentId_ShouldFail`
- `Validate_ValidCommand_ShouldPass`

File: `tests/PersonalBrandAssistant.Application.Tests/Features/Approval/Commands/BatchApproveContentCommandValidatorTests.cs`

- `Validate_EmptyContentIds_ShouldFail` -- Empty array should fail.
- `Validate_ValidContentIds_ShouldPass`

### MediatR Handler Tests

These can be lightweight since handlers simply delegate to `IApprovalService`:

- `ApproveContentCommandHandler_CallsApprovalService` -- Verify the handler calls `IApprovalService.ApproveAsync` with the command's content ID.
- `RejectContentCommandHandler_CallsApprovalService` -- Verify it calls `RejectAsync` with content ID and feedback.
- `BatchApproveContentCommandHandler_CallsApprovalService` -- Verify it calls `BatchApproveAsync` with the content IDs array.

### ListPendingContentQuery Tests

- `ListPendingContentQuery_ReturnsOnlyReviewStatusContent` -- Set up contents in various statuses. Query should return only those in Review status.
- `ListPendingContentQuery_FiltersByContentType` -- When `ContentType` filter is provided, results are narrowed.
- `ListPendingContentQuery_FiltersByPlatformType` -- When `PlatformType` filter is provided, results include only content targeting that platform.
- `ListPendingContentQuery_SupportsCursorPagination` -- Uses the existing `PagedResult<T>` cursor-based pagination pattern.

## Implementation Details

### IApprovalService Interface

File: `src/PersonalBrandAssistant.Application/Common/Interfaces/IApprovalService.cs`

```csharp
public interface IApprovalService
{
    /// <summary>Approve content in Review status. Chains to Scheduled if ScheduledAt is set.</summary>
    Task<Result<Unit>> ApproveAsync(Guid contentId, CancellationToken ct = default);

    /// <summary>Reject content back to Draft with feedback. Sends rejection notification.</summary>
    Task<Result<Unit>> RejectAsync(Guid contentId, string feedback, CancellationToken ct = default);

    /// <summary>Apply edits to content then approve it in one operation.</summary>
    Task<Result<Unit>> EditAndApproveAsync(Guid contentId, UpdateContentCommand changes, CancellationToken ct = default);

    /// <summary>Approve multiple content items. Returns count of successfully approved items.</summary>
    Task<Result<int>> BatchApproveAsync(Guid[] contentIds, CancellationToken ct = default);
}
```

### ApprovalService Implementation

File: `src/PersonalBrandAssistant.Infrastructure/Services/ApprovalService.cs`

Constructor dependencies:
- `IWorkflowEngine workflowEngine`
- `INotificationService notificationService`
- `IApplicationDbContext dbContext`
- `ISender mediator` (for dispatching `UpdateContentCommand` in edit-and-approve)

**ApproveAsync logic:**
1. Load the content entity from `dbContext.Contents` by ID. Return `NotFound` if missing.
2. Call `workflowEngine.TransitionAsync(contentId, ContentStatus.Approved)`.
3. If the transition fails, return the failure result immediately.
4. If the content has `ScheduledAt` set (not null and in the future), chain a second transition: `workflowEngine.TransitionAsync(contentId, ContentStatus.Scheduled)`. If this second transition fails, still return success for the approval (the content is Approved, just not Scheduled -- the user can schedule manually).
5. Return success.

**RejectAsync logic:**
1. Call `workflowEngine.TransitionAsync(contentId, ContentStatus.Draft, reason: feedback)`.
2. If the transition fails, return the failure result.
3. Send notification: `notificationService.SendAsync(NotificationType.ContentRejected, $"Content rejected: {contentId}", feedback, contentId)`. This is best-effort -- wrap in try/catch. Notification failure should not fail the rejection.
4. Return success.

**EditAndApproveAsync logic:**
1. Dispatch the `UpdateContentCommand` via `ISender.Send(changes with { Id = contentId })`. If it fails, return that failure.
2. Call `ApproveAsync(contentId, ct)` to reuse the approve logic.
3. Return the result.

**BatchApproveAsync logic:**
1. Initialize a success counter at 0.
2. Iterate over each content ID in the array.
3. For each, call `ApproveAsync(contentId, ct)`.
4. If the individual approve succeeds, increment the counter.
5. If it fails, continue to the next item (do not throw).
6. Return `Result<int>.Success(successCount)`.

Note: The batch operation does NOT wrap everything in a single transaction. Each approval is independent. This is intentional -- partial success is preferred over all-or-nothing for batch operations where the user wants to know how many succeeded.

### MediatR Commands

**ApproveContentCommand:**
```csharp
public sealed record ApproveContentCommand(Guid ContentId) : IRequest<Result<Unit>>;
```
Handler calls `IApprovalService.ApproveAsync(command.ContentId, ct)`.
Validator: `ContentId` must not be `Guid.Empty`.

**RejectContentCommand:**
```csharp
public sealed record RejectContentCommand(Guid ContentId, string Feedback) : IRequest<Result<Unit>>;
```
Handler calls `IApprovalService.RejectAsync(command.ContentId, command.Feedback, ct)`.
Validator: `ContentId` must not be `Guid.Empty`. `Feedback` must not be empty or whitespace.

**BatchApproveContentCommand:**
```csharp
public sealed record BatchApproveContentCommand(Guid[] ContentIds) : IRequest<Result<int>>;
```
Handler calls `IApprovalService.BatchApproveAsync(command.ContentIds, ct)`.
Validator: `ContentIds` must not be null or empty.

### ListPendingContentQuery

```csharp
public sealed record ListPendingContentQuery(
    ContentType? Type = null,
    PlatformType? Platform = null,
    int PageSize = 20,
    string? Cursor = null) : IRequest<Result<PagedResult<Content>>>;
```

Handler queries `dbContext.Contents` filtered by `Status == ContentStatus.Review`. Applies optional `ContentType` filter and `PlatformType` filter (using `TargetPlatforms.Contains(platform)`). Uses cursor-based pagination following the existing `PagedResult<T>` pattern in the codebase. Uses `AsNoTracking()` since this is a read-only query.

### DI Registration

In `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs`, add:

```csharp
services.AddScoped<IApprovalService, ApprovalService>();
```

This is done as part of section 08 (API Endpoints) where all DI registrations are consolidated, but the service can be registered here during development for testing purposes.

## Implementation Checklist

1. Write all test files with stubs (RED phase)
2. Create `IApprovalService` interface in Application layer
3. Create MediatR command/query records with validators
4. Create MediatR handlers (thin delegates to `IApprovalService`)
5. Implement `ApprovalService` in Infrastructure layer
6. Implement `ListPendingContentQueryHandler`
7. Run tests and verify they pass (GREEN phase)
8. Refactor if needed -- ensure no method exceeds 50 lines, no deep nesting