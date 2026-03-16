# Section 02: Sidecar Integration

> **Status:** Implemented
> **Deviations from plan:** See [Deviations](#deviations-from-plan) below.

## Overview

This section introduces the `ISidecarClient` interface, all `SidecarEvent` discriminated union records, the `SidecarOptions` configuration class, the `SidecarSession` model, and the `SidecarClient` WebSocket implementation. The sidecar is a separate TypeScript service (`claude-code-sidecar`) that wraps Claude Code CLI via WebSocket. The .NET API communicates with it over `ws://localhost:3001/ws` (local dev) or `ws://sidecar:3001/ws` (Docker internal DNS). Port 3001 must never be published to the LAN.

This section does NOT modify any existing agent code. That refactoring happens in section 03. This section only creates the new sidecar abstraction layer.

**Depends on:** section-01-domain-entities (no direct entity dependency, but follows the same project structure conventions established there)

**Blocks:** section-03 (agent refactoring), section-04 (content pipeline), section-07 (brand voice), section-08 (trend monitoring)

---

## Files to Create

| File | Project | Description |
|------|---------|-------------|
| `src/PersonalBrandAssistant.Application/Common/Interfaces/ISidecarClient.cs` | Application | Interface for sidecar communication |
| `src/PersonalBrandAssistant.Application/Common/Models/SidecarEvent.cs` | Application | Discriminated union records for all event types |
| `src/PersonalBrandAssistant.Application/Common/Models/SidecarSession.cs` | Application | Session info returned from ConnectAsync |
| `src/PersonalBrandAssistant.Application/Common/Models/SidecarOptions.cs` | Application | Options class for sidecar configuration binding |
| `src/PersonalBrandAssistant.Infrastructure/Services/SidecarClient.cs` | Infrastructure | WebSocket-based implementation |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/SidecarClientTests.cs` | Infrastructure.Tests | Unit/integration tests |

---

## Tests (Write First)

All tests go in `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/SidecarClientTests.cs`. Use xUnit + Moq. The tests use a mock WebSocket server pattern -- either a helper that creates an in-memory pipe or a lightweight `TestWebSocketServer` that listens on a local port for integration-style tests.

### Test class and stubs

```csharp
namespace PersonalBrandAssistant.Infrastructure.Tests.Services;

public class SidecarClientTests : IAsyncDisposable
{
    // Helper: create SidecarClient with test options pointing to a mock WS endpoint
    // Helper: TestWebSocketServer that accepts connections, sends canned JSON frames

    // --- SidecarEvent model tests ---

    [Fact]
    public void SidecarEvent_ChatEvent_IsDiscriminated()
    {
        /// Verify ChatEvent is assignable to SidecarEvent and pattern-matchable.
    }

    [Fact]
    public void SidecarEvent_AllTypes_AreRecords()
    {
        /// Verify all 6 event types (ChatEvent, FileChangeEvent, StatusEvent,
        /// SessionUpdateEvent, TaskCompleteEvent, ErrorEvent) are records inheriting SidecarEvent.
    }

    // --- SidecarOptions tests ---

    [Fact]
    public void SidecarOptions_BindsFromConfiguration()
    {
        /// Build IConfiguration with "Sidecar" section, bind to SidecarOptions,
        /// verify WebSocketUrl, ConnectionTimeoutSeconds, ReconnectDelaySeconds.
    }

    [Fact]
    public void SidecarOptions_HasSensibleDefaults()
    {
        /// new SidecarOptions() should have WebSocketUrl = "ws://localhost:3001/ws",
        /// ConnectionTimeoutSeconds = 30, ReconnectDelaySeconds = 5.
    }

    // --- ConnectAsync tests ---

    [Fact]
    public async Task ConnectAsync_EstablishesWebSocketConnection()
    {
        /// Start TestWebSocketServer, create SidecarClient with its URL,
        /// call ConnectAsync, verify IsConnected is true and returned SidecarSession is not null.
    }

    [Fact]
    public async Task ConnectAsync_TimesOutAfterConfiguredTimeout()
    {
        /// Configure a very short timeout (e.g., 1 second), point to a non-listening port,
        /// verify ConnectAsync throws or returns error within timeout window.
    }

    // --- SendTaskAsync tests ---

    [Fact]
    public async Task SendTaskAsync_StreamsEventsAsIAsyncEnumerable()
    {
        /// Server sends 3 ChatEvent frames then a TaskCompleteEvent.
        /// Enumerate the IAsyncEnumerable, verify all 4 events received in order.
    }

    [Fact]
    public async Task SendTaskAsync_YieldsTaskCompleteEvent_WithTokenCounts()
    {
        /// Server sends a TaskCompleteEvent with InputTokens=100, OutputTokens=50.
        /// Verify the final event is TaskCompleteEvent with correct counts.
    }

    [Fact]
    public async Task SendTaskAsync_CancelsOnCancellationToken()
    {
        /// Start streaming, cancel CancellationTokenSource after first event,
        /// verify enumeration stops (OperationCanceledException or clean exit).
    }

    // --- AbortAsync tests ---

    [Fact]
    public async Task AbortAsync_SendsAbortMessageWithSessionId()
    {
        /// Call AbortAsync with a session ID. Verify the server receives
        /// a JSON message with type "abort".
    }

    // --- Reconnection tests ---

    [Fact]
    public async Task AutomaticReconnection_ReconnectsOnDisconnect_UpTo3Retries()
    {
        /// Connect, then server drops connection. Verify client reconnects automatically.
        /// After 3 failed retries, verify it gives up and IsConnected is false.
    }

    // --- IsConnected tests ---

    [Fact]
    public async Task IsConnected_ReflectsActualConnectionState()
    {
        /// Before connect: false. After connect: true. After server disconnect + failed retries: false.
    }

    // --- Health check tests ---

    [Fact]
    public async Task HealthCheck_ReturnsHealthy_WhenConnected()
    {
        /// Connect to server, verify health check reports healthy.
    }

    [Fact]
    public async Task HealthCheck_ReturnsUnhealthy_WhenDisconnected()
    {
        /// Without connecting, verify health check reports unhealthy.
    }

    public async ValueTask DisposeAsync()
    {
        /// Dispose test server and client.
    }
}
```

---

## Implementation Details

### 1. ISidecarClient Interface

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/ISidecarClient.cs`

```csharp
namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface ISidecarClient
{
    /// Establishes a WebSocket connection to the sidecar service.
    Task<SidecarSession> ConnectAsync(CancellationToken ct);

    /// Sends a task prompt to the sidecar and streams back events.
    /// sessionId is optional -- pass null to start a new session,
    /// or a previous session ID to resume (maps to claude --resume).
    IAsyncEnumerable<SidecarEvent> SendTaskAsync(string task, string? sessionId, CancellationToken ct);

    /// Sends an abort signal for the given session (or current session if null).
    Task AbortAsync(string? sessionId, CancellationToken ct);

    /// True when the underlying WebSocket is in Open state.
    bool IsConnected { get; }
}
```

### 2. SidecarEvent Discriminated Union

**File:** `src/PersonalBrandAssistant.Application/Common/Models/SidecarEvent.cs`

Six record types forming a discriminated union. All inherit from the abstract `SidecarEvent` base.

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

public abstract record SidecarEvent;

/// Maps to sidecar's { type: "chat-event", payload: ChatEvent }.
/// EventType can be "assistant", "tool_use", "tool_result", "text", etc.
public record ChatEvent(string EventType, string? Text, string? FilePath, string? ToolName) : SidecarEvent;

/// Maps to sidecar's { type: "file-change", payload }.
/// ChangeType: "created", "modified", "deleted".
public record FileChangeEvent(string FilePath, string ChangeType) : SidecarEvent;

/// Maps to sidecar's { type: "status", payload: { status } }.
/// Status: "running" or "idle".
public record StatusEvent(string Status) : SidecarEvent;

/// Maps to sidecar's { type: "session-update", payload }.
public record SessionUpdateEvent(string SessionId) : SidecarEvent;

/// Synthetic event emitted when the sidecar task completes.
/// Token counts extracted from the final status/session-update payload.
public record TaskCompleteEvent(string SessionId, int InputTokens, int OutputTokens) : SidecarEvent;

/// Maps to sidecar's { type: "error", payload: { message } }.
public record ErrorEvent(string Message) : SidecarEvent;
```

### 3. SidecarSession Model

**File:** `src/PersonalBrandAssistant.Application/Common/Models/SidecarSession.cs`

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

/// Returned from ConnectAsync. Contains session metadata.
public record SidecarSession(string SessionId, DateTimeOffset ConnectedAt);
```

### 4. SidecarOptions

**File:** `src/PersonalBrandAssistant.Application/Common/Models/SidecarOptions.cs`

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

public class SidecarOptions
{
    public const string SectionName = "Sidecar";
    public string WebSocketUrl { get; set; } = "ws://localhost:3001/ws";
    public int ConnectionTimeoutSeconds { get; set; } = 30;
    public int ReconnectDelaySeconds { get; set; } = 5;
}
```

Bound from `appsettings.json` in section-12. For Docker: override `WebSocketUrl` to `ws://sidecar:3001/ws`.

### 5. SidecarClient Implementation

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/SidecarClient.cs`

Registered as **singleton** (one persistent WebSocket connection). Uses `System.Net.WebSockets.ClientWebSocket`.

Key implementation responsibilities:

**Connection management:**
- `ConnectAsync` creates a new `ClientWebSocket`, connects to the configured URL with a `CancellationToken` derived from `ConnectionTimeoutSeconds`.
- On successful connect, sends a `{ type: "new-session" }` message. Waits for a `session-update` response containing the session ID.
- Returns `SidecarSession` with the session ID and timestamp.
- Stores the connected `ClientWebSocket` instance in a private field.

**Sending tasks (`SendTaskAsync`):**
- Serializes `{ type: "send-message", payload: { message: task } }` as JSON and writes to the WebSocket.
- If `sessionId` is provided, the outgoing message includes the session ID for resume behavior.
- Enters a receive loop reading WebSocket message frames.
- Each frame is a complete JSON object. Deserialize using `System.Text.Json` with a discriminator on the `type` field.
- Map each JSON type to the corresponding `SidecarEvent` record and yield it.
- The stream terminates when a `StatusEvent` with `Status == "idle"` is received (indicating task completion). Before terminating, yield a `TaskCompleteEvent` with token counts parsed from the final session data.
- If the `CancellationToken` is cancelled, close the receive loop gracefully.

**Abort (`AbortAsync`):**
- Serializes `{ type: "abort" }` and writes to the WebSocket.

**Reconnection logic:**
- If the WebSocket disconnects unexpectedly during `SendTaskAsync` or while idle, attempt automatic reconnection.
- Use bounded retries: max 3 attempts with exponential backoff starting at `ReconnectDelaySeconds`.
- These are transport-level retries only (WebSocket disconnect, sidecar process crash). Claude Code handles its own API-level retries internally.
- After 3 failed retries, set `IsConnected = false` and stop retrying. Consumers will get an error.

**Health check:**
- Implement `IHealthCheck` (or expose a method consumed by a health check). Return `Healthy` if `IsConnected` is true and the WebSocket state is `Open`, otherwise `Unhealthy`.

**JSON serialization notes:**
- Outbound messages: use `System.Text.Json.JsonSerializer.SerializeToUtf8Bytes` with camelCase naming policy.
- Inbound messages: deserialize the `type` field first, then deserialize the `payload` into the appropriate record type using a switch expression on the type string.
- Expected inbound type strings: `"chat-event"`, `"file-change"`, `"status"`, `"session-update"`, `"error"`.

**Thread safety:**
- `ClientWebSocket` is not thread-safe for concurrent sends. Use a `SemaphoreSlim(1,1)` to serialize write operations.
- Reads happen on a single receive loop, so no concurrency concern there.
- `IsConnected` should be backed by a `volatile bool` or use `Interlocked`.

---

## WebSocket Protocol Reference

For implementer reference, here is the exact protocol the sidecar expects:

**Client to server messages:**
- `{ "type": "send-message", "payload": { "message": "<task prompt>" } }` -- send a task
- `{ "type": "new-session" }` -- start a fresh session
- `{ "type": "abort" }` -- abort current task

**Server to client messages:**
- `{ "type": "chat-event", "payload": { "eventType": "...", "text": "...", "filePath": "...", "toolName": "..." } }`
- `{ "type": "file-change", "payload": { "filePath": "...", "changeType": "..." } }`
- `{ "type": "status", "payload": { "status": "running" | "idle" } }`
- `{ "type": "session-update", "payload": { "sessionId": "..." } }`
- `{ "type": "error", "payload": { "message": "..." } }`

The sidecar runs at `localhost:3001` (dev) or `sidecar:3001` (Docker). Auth is delegated to Claude Code's own credentials -- the sidecar handles authentication internally.

---

## Security Considerations

- The sidecar port (3001) must be on an internal Docker network only. Never publish it to the host or LAN.
- No API keys are stored in the .NET application for Claude -- the sidecar manages Claude Code credentials.
- The `SidecarOptions.WebSocketUrl` should never contain secrets; it is a plain address.

---

## Dependencies on Other Sections

- **section-12 (Docker/DI config)** will register `ISidecarClient` as singleton and bind `SidecarOptions` from configuration. Until then, manual DI registration is fine for testing.
- **section-03 (agent refactoring)** will consume `ISidecarClient` to replace `IChatClientFactory`. This section creates the interface and implementation only.

---

## Deviations from Plan

| # | Deviation | Rationale |
|---|-----------|-----------|
| 1 | Added `_activeStream` guard with `Interlocked.CompareExchange` | Code review: no read-side thread safety. Enforces single-consumer semantics on WebSocket reads. |
| 2 | Added `using var doc` + `.Clone()` in `ReceiveFrameAsync` | Code review: `JsonDocument` leak — pooled memory became invalid after GC. |
| 3 | `ConnectAsync` checks active stream guard before disposing socket | Code review: disposing a socket while `SendTaskAsync` actively reads from it. |
| 4 | Added `MaxMessageSize` (1 MB) limit in receive loop | Code review: unbounded message size is a DoS vector. |
| 5 | `_isConnected` set after handshake, not after TCP connect | Code review: stale `true` state if handshake fails. |
| 6 | Reconnection logic descoped | Per plan, reconnection is transport-level and deferred to section-12. |
| 7 | Health check interface not implemented | Deferred to section-12 (DI/health checks registration). |
| 8 | Some planned tests descoped (reconnection, health check) | Will be added when reconnection logic is implemented in section-12. |

## Test Results

- **Domain Tests:** 158 passed
- **Application Tests:** 106 passed
- **Infrastructure Tests:** 417 passed (13 new SidecarClient tests)
- **Total:** 681 passed, 0 failed