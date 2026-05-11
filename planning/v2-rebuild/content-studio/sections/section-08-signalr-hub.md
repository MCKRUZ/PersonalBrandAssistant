# Section 08: SignalR Hub

## Overview

This section creates the `ContentHub` SignalR hub in the API project and wires up the real-time infrastructure needed for the sidecar chat panel. When a user sends a chat message, the hub loads the relevant content and brand profile, calls `ISidecarClient.StreamPromptAsync` (built in section-07), and pushes tokens back to the caller in real time via `ReceiveToken`, followed by `GenerationComplete` or `GenerationError`.

This section also registers SignalR in `Program.cs` and updates the CORS policy to support SignalR's `.AllowCredentials()` requirement.

## Dependencies

- **section-07-sidecar-streaming** must be complete. This section consumes `ISidecarClient.StreamPromptAsync(Guid contentId, string systemPrompt, string userPrompt, CancellationToken ct)` which returns `IAsyncEnumerable<string>`.
- **section-01-schema-updates** should be complete (for `IAppDbContext` to expose `BrandProfiles` DbSet).

## Files Created

| File | Description |
|------|-------------|
| `src/PBA.Api/Hubs/ContentHub.cs` | SignalR hub for content chat streaming. Primary constructor. Input validation, generic error messages (no info disclosure), CancellationToken passthrough. |
| `src/PBA.Api/Hubs/IContentHubClient.cs` | Strongly-typed client interface: ReceiveToken, GenerationComplete, GenerationError |
| `tests/PBA.Api.Tests/Hubs/ContentHubTests.cs` | 5 unit tests with EF Core async query provider helpers and Hub reflection wiring |

## Files Modified

| File | Change |
|------|--------|
| `src/PBA.Api/Program.cs` | Added `AddSignalR()`, `.AllowCredentials()` on CORS, `MapHub<ContentHub>("/hubs/content")` |

## Tests

All tests go in `tests/PBA.Api.Tests/Hubs/ContentHubTests.cs`.

### Test: SendChatMessage_LoadsContentAndBrandProfile

- Arrange: Mock `IAppDbContext` with seeded Content and BrandProfile. Mock `ISidecarClient.StreamPromptAsync` to return empty async enumerable.
- Act: Call `hub.SendChatMessage(contentId, "test message")`
- Assert: Verify Content and BrandProfile were queried. Sidecar received system prompt containing brand profile fields.

### Test: SendChatMessage_CallsStreamPromptAsync

- Arrange: Seed Content/BrandProfile. Mock `StreamPromptAsync` to yield `["token1", "token2"]`.
- Act: Call `hub.SendChatMessage(contentId, "test message")`
- Assert: `StreamPromptAsync` was called with content ID, system prompt, and user prompt containing the message and content body.

### Test: SendChatMessage_ForwardsTokensToCallerViaReceiveToken

- Arrange: Mock `StreamPromptAsync` to yield `["Hello", " world", "!"]`. Set up `Clients.Caller` mock.
- Act: Call `hub.SendChatMessage(contentId, "test message")`
- Assert: `Clients.Caller.ReceiveToken` called 3 times with "Hello", " world", "!" in order.

### Test: SendChatMessage_CallsGenerationCompleteWithFullText

- Arrange: Mock `StreamPromptAsync` to yield `["Hello", " world", "!"]`.
- Assert: `Clients.Caller.GenerationComplete` called once with `"Hello world!"`.

### Test: SendChatMessage_CallsGenerationErrorOnSidecarFailure

- Arrange: Mock `StreamPromptAsync` to throw `InvalidOperationException("Sidecar CLI failed")`.
- Assert: `Clients.Caller.GenerationError` called with message. `GenerationComplete` NOT called.

## Implementation Details

### IContentHubClient Interface

**File:** `src/PBA.Api/Hubs/IContentHubClient.cs`

```csharp
public interface IContentHubClient
{
    Task ReceiveToken(string token);
    Task GenerationComplete(string fullResponse);
    Task GenerationError(string error);
}
```

### ContentHub

**File:** `src/PBA.Api/Hubs/ContentHub.cs`

Inherits from `Hub<IContentHubClient>`. Constructor dependencies: `IAppDbContext`, `ISidecarClient`, `ILogger<ContentHub>`.

**`SendChatMessage(Guid contentId, string message)` method:**

1. Load Content from `IAppDbContext.Contents.FindAsync(contentId)`. If null, call `Clients.Caller.GenerationError("Content not found")` and return.
2. Load first BrandProfile from `IAppDbContext.BrandProfiles.FirstOrDefaultAsync()`. Use empty defaults if null.
3. Construct system prompt with brand profile (personality, tone, vocabulary, avoid words) + platform constraints + humanizer rules.
4. Construct user prompt with current content body + user message.
5. Wrap in try/catch:
   - `await foreach` over `ISidecarClient.StreamPromptAsync(contentId, systemPrompt, userPrompt, Context.ConnectionAborted)`
   - Call `await Clients.Caller.ReceiveToken(token)` for each
   - Accumulate in `StringBuilder`
   - After loop: `await Clients.Caller.GenerationComplete(fullText)`
6. Catch: log exception, `await Clients.Caller.GenerationError(ex.Message)`

### Program.cs Changes

1. Add `builder.Services.AddSignalR()` after CORS config
2. Update CORS to include `.AllowCredentials()`
3. Map hub: `app.MapHub<ContentHub>("/hubs/content")`

---

## Key Design Decisions

1. **Strongly-typed hub** (`Hub<IContentHubClient>`) for compile-time safety and easier mocking
2. **No MediatR in hub** -- streaming pattern doesn't map to request/response model. Hub directly uses IAppDbContext and ISidecarClient.
3. **`Context.ConnectionAborted` as cancellation token** -- auto-cancels on disconnect, which kills the sidecar process
4. **Token accumulation** via StringBuilder during streaming -- full text sent via GenerationComplete for "Apply to Editor" and "Copy" actions
5. **Single BrandProfile** loaded via `FirstOrDefaultAsync()` -- correct for single-user app
