# Section 6: SignalR Hub -- FeedHub, IFeedHubClient, and Hub Registration

## Overview

This section creates a push-only SignalR hub for real-time feed updates. The hub broadcasts new feed items and summary updates to all connected clients. It has no server-callable methods -- all communication flows from server to client.

## Dependencies

- **section-02-dtos-validators-mappings** must be complete (provides `FeedItemDto` and `FeedSummaryDto` used in the hub client interface)

## File Structure

```
src/PBA.Api/Hubs/
  FeedHub.cs
  IFeedHubClient.cs
```

Modification:
```
src/PBA.Api/Program.cs  (add hub registration)
```

## Tests First

These tests live in section-15-backend-tests but are listed here for TDD reference. The hub is push-only with no server-callable methods, so testing is minimal.

```csharp
// File: tests/PBA.Application.Tests/Features/Feed/Hubs/FeedHubTests.cs

// Test: FeedHub can be instantiated (constructor injection works)
//   Arrange: create a FeedHub instance
//   Act: verify it's a Hub<IFeedHubClient>
//   Assert: instance is not null, derives from Hub<IFeedHubClient>

// Test: IFeedHubClient interface defines ReceiveFeedItem and FeedSummaryUpdated methods
//   Arrange: reflect on IFeedHubClient interface
//   Act: get method info for both methods
//   Assert: both methods exist, return Task, have correct parameter types (FeedItemDto, FeedSummaryDto)
```

Additionally, the `CreateFeedItem` command (section-04) tests verify SignalR integration:

```csharp
// Test: CreateFeedItem pushes new item via SignalR hub context after save
// Test: CreateFeedItem continues successfully even if SignalR push fails (fire-and-forget)
// Test: CreateFeedItem logs error when SignalR push fails
```

These tests belong to section-04/section-15 but are relevant context -- they prove the hub integration works end-to-end.

## Implementation Details

### IFeedHubClient Interface

Create a typed hub client interface following the established pattern from `IContentHubClient`. The interface defines two server-to-client methods:

```csharp
// File: src/PBA.Api/Hubs/IFeedHubClient.cs
// Namespace: PBA.Api.Hubs

// Interface: IFeedHubClient
//   Task ReceiveFeedItem(FeedItemDto item)
//     - Called when a new feed item is created by a background process
//     - Pushes the full DTO so the client can display it without a round-trip
//
//   Task FeedSummaryUpdated(FeedSummaryDto summary)
//     - Called after feed mutations that affect summary stats
//     - Client uses this to update KPI cards in real-time
```

Both methods accept the corresponding DTO from `PBA.Application.Features.Feed.Dtos`. The interface must import that namespace.

### FeedHub Class

Create a minimal hub class. Unlike `ContentHub` which has server-callable methods (`SendChatMessage`), `FeedHub` is push-only -- the class body is empty.

```csharp
// File: src/PBA.Api/Hubs/FeedHub.cs
// Namespace: PBA.Api.Hubs

// Class: FeedHub : Hub<IFeedHubClient>
//   - No constructor parameters needed (no DI dependencies)
//   - No server-callable methods
//   - All communication is server->client via IHubContext<FeedHub, IFeedHubClient> injected elsewhere
```

The hub is intentionally empty. Server-side code (specifically `CreateFeedItem.Handler` in section-04) injects `IHubContext<FeedHub, IFeedHubClient>` to push messages to all connected clients. This is the standard ASP.NET Core pattern for pushing from outside a hub.

### How the Hub Gets Used (Context for Integration)

In `CreateFeedItem.Handler` (implemented in section-04), after saving the new feed item:

1. Inject `IHubContext<FeedHub, IFeedHubClient>` via constructor
2. Call `hubContext.Clients.All.ReceiveFeedItem(newItem.ToDto())`
3. Optionally recalculate summary and call `hubContext.Clients.All.FeedSummaryUpdated(summary)`
4. Wrap in try/catch -- SignalR push is fire-and-forget. Log errors but never roll back the database save on SignalR failure.

This section does NOT implement the `CreateFeedItem` handler -- only the hub and interface that the handler will consume.

### Hub Registration in Program.cs

Add the hub mapping alongside the existing `ContentHub` registration.

In `src/PBA.Api/Program.cs`, after the existing line:
```csharp
app.MapHub<ContentHub>("/hubs/content");
```

Add:
```csharp
app.MapHub<FeedHub>("/hubs/feed");
```

The `builder.Services.AddSignalR()` call already exists in Program.cs (line 25) and covers all hubs -- no additional service registration is needed.

### Debouncing

Not needed for the initial build. At 50-200 items/day, pushing immediately on each `CreateFeedItem` is fine. If future testing reveals jitter from burst creation (e.g., a batch import creating 50 items at once), debouncing can be added by buffering pushes over a 2-5 second window. That optimization is out of scope for this section.

## Established Patterns to Follow

The existing `ContentHub` + `IContentHubClient` in `src/PBA.Api/Hubs/` is the reference pattern:

- Interface file is separate from hub class (one file per type)
- Namespace is `PBA.Api.Hubs`
- Hub uses typed client via `Hub<TClient>` generic
- Hub registration is a single `app.MapHub<T>("/hubs/path")` line in Program.cs

The key difference: `ContentHub` has server-callable methods and constructor-injected dependencies. `FeedHub` has neither -- it's a pure push channel.

## Checklist

1. Create `IFeedHubClient.cs` with `ReceiveFeedItem` and `FeedSummaryUpdated` method signatures
2. Create `FeedHub.cs` as an empty `Hub<IFeedHubClient>` class
3. Add `app.MapHub<FeedHub>("/hubs/feed")` to Program.cs after the existing ContentHub mapping
4. Verify the project compiles (`dotnet build`) -- the DTOs from section-02 must exist first
5. Confirm `AddSignalR()` is already registered (it is, line 25 of Program.cs)

## Implementation Notes

### Files Modified
- `src/PBA.Api/Hubs/IFeedHubClient.cs` -- added `FeedSummaryUpdated(FeedSummaryDto)` method (file already existed with `ReceiveFeedItem` from earlier work)
- `src/PBA.Api/Program.cs` (line 49) -- added `app.MapHub<FeedHub>("/hubs/feed")`
- `src/PBA.Api/Hubs/FeedHub.cs` -- already existed as empty `Hub<IFeedHubClient>`, no changes needed

### Deviations from Plan
- FeedHub.cs and IFeedHubClient.cs already existed from prior work (likely created during section-01 or section-04). Only needed to add the missing `FeedSummaryUpdated` method and register the hub in Program.cs.

### Test Results
All 404 tests passing.
