# Section 4: Feed Commands

## Overview

This section implements all feed mutation operations: marking items read, performing type-specific actions, batch operations, and internal feed item creation with SignalR push. These are MediatR command handlers following the established CQRS pattern (`static class > record Command > internal sealed class Handler`).

**Depends on:** section-02 (DTOs, validators, mappings must exist)
**Blocks:** section-05 (API endpoints wire to these commands), section-15 (backend tests)
**Parallelizable with:** section-03 (feed queries)

## File Structure

```
src/PBA.Application/Features/Feed/
  Commands/
    MarkFeedItemRead.cs
    ActOnFeedItem.cs
    BatchMarkRead.cs
    BatchDismiss.cs
    BatchAct.cs
    CreateFeedItem.cs
```

All commands live under `PBA.Application.Features.Feed.Commands` namespace.

## Tests First

Write these tests before implementing. Backend uses xUnit, in-memory EF Core (unique Guid DB name per test), Moq for `ISender` and `IHubContext`. Arrange-Act-Assert pattern.

Test file locations are in section-15 but the stubs are defined here so the implementer knows the contract.

### MarkFeedItemRead Tests

```csharp
// File: tests/PBA.Application.Tests/Features/Feed/Commands/MarkFeedItemReadHandlerTests.cs

// Test: marks item as read (IsRead = true)
// Test: idempotent -- already read item returns success
// Test: returns NotFound for nonexistent ID
```

### ActOnFeedItem Tests

```csharp
// File: tests/PBA.Application.Tests/Features/Feed/Commands/ActOnFeedItemHandlerTests.cs

// Test: AgentDraft + "approve" dispatches ApproveContent command and marks acted-on
// Test: AgentDraft + "dismiss" marks IsRead + IsActedOn = true
// Test: TrendAlert + "view" marks IsRead = true
// Test: TrendAlert + "dismiss" marks IsRead + IsActedOn = true
// Test: IdeaSuggestion + "create-content" deserializes Data, dispatches CreateContentFromIdea with correct params
// Test: IdeaSuggestion + "dismiss" marks IsRead + IsActedOn = true
// Test: AnalyticsHighlight + "view" marks IsRead = true
// Test: ApprovalRequest + "approve" dispatches ApproveContent and marks acted-on
// Test: SystemNotification + "view" marks IsRead = true
// Test: unknown action returns validation failure
// Test: nonexistent feed item returns NotFound
// Test: returns NavigationTarget for actions that navigate (approve, create-content)
```

### BatchMarkRead Tests

```csharp
// File: tests/PBA.Application.Tests/Features/Feed/Commands/BatchMarkReadHandlerTests.cs

// Test: marks all unread items as read, returns count
// Test: filters by type when specified
// Test: skips expired items
// Test: returns 0 when no matching items
```

### BatchDismiss Tests

```csharp
// File: tests/PBA.Application.Tests/Features/Feed/Commands/BatchDismissHandlerTests.cs

// Test: dismisses all items of specified type (IsRead + IsActedOn = true)
// Test: returns count of dismissed items
// Test: skips expired items
// Test: does not affect other types
```

### BatchAct Tests

```csharp
// File: tests/PBA.Application.Tests/Features/Feed/Commands/BatchActHandlerTests.cs

// Test: processes multiple items and returns success count
// Test: handles partial failures (some IDs not found) gracefully
// Test: returns failure details for failed items
```

### CreateFeedItem Tests

```csharp
// File: tests/PBA.Application.Tests/Features/Feed/Commands/CreateFeedItemHandlerTests.cs

// Test: creates feed item with all fields and saves
// Test: pushes new item via SignalR hub context after save
// Test: continues successfully even if SignalR push fails (fire-and-forget)
// Test: logs error when SignalR push fails
```

## Implementation Details

### MarkFeedItemRead

**File:** `src/PBA.Application/Features/Feed/Commands/MarkFeedItemRead.cs`

Static class following the project pattern (see `ApproveContent.cs` for reference).

**Command:** `record Command(Guid Id) : IRequest<Result>`

**Handler dependencies:** `IAppDbContext db`

**Behavior:**
1. Look up `FeedItem` by ID via `db.FeedItems.FindAsync`
2. If null, return `Result.NotFound($"Feed item {request.Id} not found")`
3. Set `IsRead = true` (idempotent -- no error if already true)
4. `await db.SaveChangesAsync(cancellationToken)`
5. Return `Result.Success()`

### ActOnFeedItem

**File:** `src/PBA.Application/Features/Feed/Commands/ActOnFeedItem.cs`

This is the most complex command. It dispatches type-specific behavior based on the FeedItem's type and the requested action string.

**Command:** `record Command(Guid Id, string Action) : IRequest<Result<ActOnFeedItemResponse>>`

**Response DTO:** Define a response record inside the static class (or in the DTOs folder if reused elsewhere):
```csharp
record ActOnFeedItemResponse(bool Success, string? NavigationTarget = null, Guid? TargetId = null);
```

**Handler dependencies:** `IAppDbContext db`, `ISender sender` (MediatR for dispatching sub-commands)

**Behavior:**
1. Look up `FeedItem` by ID. If null, return `Result<ActOnFeedItemResponse>.NotFound(...)`
2. Switch on `(item.Type, request.Action)` using a pattern match:

| Type + Action | Implementation |
|---|---|
| `(AgentDraft, "approve")` | Send `ApproveContent.Command(item.ActionTargetId!.Value)` via `sender.Send()`. Set `IsRead = true`, `IsActedOn = true`. Return response with `NavigationTarget = "/content"`, `TargetId = item.ActionTargetId`. |
| `(AgentDraft, "dismiss")` | Set `IsRead = true`, `IsActedOn = true`. Return success response. |
| `(TrendAlert, "view")` | Set `IsRead = true`. Return success response. |
| `(TrendAlert, "dismiss")` | Set `IsRead = true`, `IsActedOn = true`. Return success response. |
| `(IdeaSuggestion, "create-content")` | Deserialize `item.Data` JSON to extract `contentType` and `primaryPlatform` fields. Parse them to `ContentType` and `Platform` enums. Send `CreateContentFromIdea.Command(item.ActionTargetId!.Value, contentType, platform)` via `sender.Send()`. Set `IsRead = true`, `IsActedOn = true`. If the sub-command returns a new content ID, return it as `TargetId` with `NavigationTarget = "/content/{id}"`. |
| `(IdeaSuggestion, "dismiss")` | Set `IsRead = true`, `IsActedOn = true`. Return success response. |
| `(AnalyticsHighlight, "view")` | Set `IsRead = true`. Return success response. |
| `(AnalyticsHighlight, "dismiss")` | Set `IsRead = true`, `IsActedOn = true`. Return success response. |
| `(ApprovalRequest, "approve")` | Send `ApproveContent.Command(item.ActionTargetId!.Value)`. Set `IsRead = true`, `IsActedOn = true`. Return response with `NavigationTarget = "/content"`, `TargetId = item.ActionTargetId`. |
| `(ApprovalRequest, "dismiss")` | Set `IsRead = true`, `IsActedOn = true`. Return success response. |
| `(SystemNotification, "view")` | Set `IsRead = true`. Return success response. |
| `(SystemNotification, "dismiss")` | Set `IsRead = true`, `IsActedOn = true`. Return success response. |
| `(_, _)` (any other) | Return `Result<ActOnFeedItemResponse>.ValidationFailure([$"Unknown action '{request.Action}' for feed item type '{item.Type}'"])` |

3. After the switch, `await db.SaveChangesAsync(cancellationToken)`
4. Return the response

**JSON deserialization for IdeaSuggestion:** Use `System.Text.Json.JsonDocument` to parse `item.Data`. Extract `contentType` and `primaryPlatform` as strings, then parse to enums via `Enum.TryParse`. If parsing fails, return a validation failure -- don't throw.

**Important:** The `sender.Send()` calls for `ApproveContent` and `CreateContentFromIdea` can themselves fail. Check the sub-command result. If it fails, propagate the failure rather than marking the feed item as acted-on. Only mark acted-on on success of the sub-command.

### BatchMarkRead

**File:** `src/PBA.Application/Features/Feed/Commands/BatchMarkRead.cs`

**Command:** `record Command(FeedItemType? Type, bool? IsRead) : IRequest<Result<int>>`

The `IsRead` filter parameter allows the endpoint to limit the scope (e.g., "mark all unread items as read" vs "mark all items of this type as read").

**Handler dependencies:** `IAppDbContext db`

**Behavior:**
1. Start with `db.FeedItems.Where(x => !x.IsRead)` (only update unread items)
2. Exclude expired: `.Where(x => x.ExpiresAt == null || x.ExpiresAt > DateTimeOffset.UtcNow)`
3. If `Type` is specified, filter by type
4. If `IsRead` filter is specified, apply it (this allows future flexibility)
5. Load matching items to memory (scale is 50-200/day, safe to materialize)
6. Set `IsRead = true` on each
7. `await db.SaveChangesAsync(cancellationToken)`
8. Return `Result<int>.Success(count)`

### BatchDismiss

**File:** `src/PBA.Application/Features/Feed/Commands/BatchDismiss.cs`

**Command:** `record Command(FeedItemType Type) : IRequest<Result<int>>`

**Handler dependencies:** `IAppDbContext db`

**Behavior:**
1. Query `db.FeedItems.Where(x => x.Type == request.Type)`
2. Exclude expired: `.Where(x => x.ExpiresAt == null || x.ExpiresAt > DateTimeOffset.UtcNow)`
3. Exclude already dismissed: `.Where(x => !x.IsActedOn)`
4. Load to memory
5. Set `IsRead = true` and `IsActedOn = true` on each
6. Save, return count

### BatchAct

**File:** `src/PBA.Application/Features/Feed/Commands/BatchAct.cs`

**Command:** `record Command(IReadOnlyList<Guid> Ids, string Action) : IRequest<Result<BatchActResponse>>`

**Response:**
```csharp
record BatchActResponse(int SuccessCount, IReadOnlyList<BatchActFailure> Failures);
record BatchActFailure(Guid Id, string Reason);
```

**Handler dependencies:** `IAppDbContext db`, `ISender sender`

**Behavior:**
1. Initialize `successCount = 0` and `failures = new List<BatchActFailure>()`
2. Loop through each ID sequentially (not parallel -- single DbContext):
   a. Send `ActOnFeedItem.Command(id, request.Action)` via `sender.Send()`
   b. If success, increment `successCount`
   c. If failure, add to `failures` with the error reason
3. Return `Result<BatchActResponse>.Success(new(successCount, failures))`

**Why sequential:** EF Core's `DbContext` is not thread-safe. Using a loop with `sender.Send()` reuses the same `DbContext` safely. Each `ActOnFeedItem` call internally re-queries the item, so the change tracker stays consistent. If performance becomes an issue at scale, replace with raw SQL batch update.

### CreateFeedItem

**File:** `src/PBA.Application/Features/Feed/Commands/CreateFeedItem.cs`

This command is **internal only** -- not exposed via API endpoints. Background processes (content drafting agent, trend analyzer, etc.) call it to push new items into the feed.

**Command:**
```csharp
record Command(
    FeedItemType Type,
    string Title,
    string Summary,
    string? Data,
    string? ActionType,
    Guid? ActionTargetId,
    FeedItemPriority Priority = FeedItemPriority.Normal,
    DateTimeOffset? ExpiresAt = null) : IRequest<Result<Guid>>;
```

**Handler dependencies:** `IAppDbContext db`, `IFeedNotifier feedNotifier`, `ILogger<Handler> logger`

**Architecture deviation from spec:** Instead of directly referencing `IHubContext<FeedHub, IFeedHubClient>` (which would require Application → Api reference, violating Clean Architecture), we introduced `IFeedNotifier` interface in `PBA.Application.Common.Interfaces`. Section-06 implements this with the actual SignalR hub context. Minimal `FeedHub` and `IFeedHubClient` stubs were created in `PBA.Api.Hubs/` for section-06 to flesh out.

**Behavior:**
1. Create new `FeedItem` entity from command fields
2. `db.FeedItems.Add(item)`
3. `await db.SaveChangesAsync(cancellationToken)`
4. **After successful save**, push via SignalR in a try/catch:
   ```csharp
   try
   {
       await hubContext.Clients.All.ReceiveFeedItem(item.ToDto());
   }
   catch (Exception ex)
   {
       logger.LogError(ex, "Failed to push feed item {FeedItemId} via SignalR", item.Id);
   }
   ```
5. Return `Result<Guid>.Success(item.Id)`

**Critical:** The SignalR push is fire-and-forget with error logging. Never roll back the database save on SignalR failure. The item exists in the database regardless; clients will pick it up on next poll/page load. The `ToDto()` extension method comes from section-02 (FeedMappings).

## Key Design Decisions

1. **ActOnFeedItem uses pattern matching on (Type, Action)** rather than a strategy/chain-of-responsibility pattern. At 12 cases this is clear and maintainable. If the action matrix grows significantly, extract to a dictionary of delegates.

2. **BatchAct delegates to ActOnFeedItem** via MediatR `ISender`. This reuses all the type-specific logic without duplication. The cost is N+1 database round-trips, but at expected batch sizes (5-20 items) this is negligible.

3. **CreateFeedItem is not exposed via API.** It's an internal command for background processes. External feed creation would need auth, rate limiting, and validation that doesn't apply to internal callers.

4. **JSON deserialization in ActOnFeedItem** uses `System.Text.Json.JsonDocument` for lightweight parsing of the `Data` field. No need for strongly-typed DTOs since we only extract 2-3 fields and the schema varies by type.

5. **Expired items are excluded from batch operations.** Expired items are considered dead -- batch mark-read and batch dismiss skip them. Individual operations (MarkFeedItemRead, ActOnFeedItem) do not check expiry because a user may still be looking at an expired card in their UI.

## Code Review Changes

- Removed "edit" and "schedule" from `KnownActions` validator — no handler cases exist for these yet
- Added null guards on `ActionTargetId` before `.Value` access in 3 handler cases (AgentDraft approve, IdeaSuggestion create-content, ApprovalRequest approve) — returns ValidationFailure instead of NullReferenceException
- Strengthened SubCommandFails test to verify both IsRead and IsActedOn remain false
- Dropped `IsRead` parameter from `BatchMarkRead.Command` (YAGNI — handler always filters unread)
