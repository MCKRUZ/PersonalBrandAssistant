# Section 02: SSE Broadcaster

## Overview

A real-time Server-Sent Events (SSE) endpoint at `GET /api/events/pipeline` that streams content pipeline status changes to connected clients. The Jarvis HUD connects to this endpoint (via a BFF proxy) to display a live Kanban board of content flowing through pipeline stages.

The implementation uses a broadcast hub pattern built on `System.Threading.Channels`. A singleton `PipelineEventBroadcaster` service maintains a list of subscriber channels -- one per SSE connection. The content pipeline service writes events to the broadcaster; the broadcaster fans out to all subscriber channels. Each SSE connection gets its own dedicated `ChannelReader<PipelineEvent>`.

On connect, the SSE endpoint sends an initial `pipeline:snapshot` event containing the current state of all active pipeline items (from the database). After the snapshot, it streams live deltas from the broadcaster. This ensures clients that connect mid-flight see the full picture.

**Single-instance constraint (v1):** The broadcaster is in-process memory. This works for the single-instance deployment on the Synology NAS. If PBA ever scales to multiple instances, the broadcaster would need to move to Redis pub/sub or Postgres LISTEN/NOTIFY.

## Dependencies

- **section-03-scoped-api-keys**: The SSE endpoint accepts the readonly API key scope. Until scoped keys are implemented, the existing single API key middleware provides authentication.

## Event Types

Five event types are emitted through the SSE stream:

| Event | Trigger | Data Shape |
|-------|---------|------------|
| `pipeline:snapshot` | Client connects | Array of all active pipeline items |
| `pipeline:stage-change` | Content moves between stages | `{ contentId, previousStage, newStage, timestamp }` |
| `pipeline:created` | New content enters pipeline | `{ contentId, title, platform, contentType, timestamp }` |
| `pipeline:published` | Content is published | `{ contentId, platform, publishedAt }` |
| `pipeline:failed` | A pipeline step fails | `{ contentId, stage, error, timestamp }` |
| `pipeline:approval-needed` | Content needs manual approval | `{ contentId, title, platform, timestamp }` |

SSE wire format example:

```
event: pipeline:stage-change
data: {"contentId":"...","previousStage":"Draft","newStage":"Review","timestamp":"2026-03-23T14:00:00Z"}

```

Each event is a single `event:` line followed by a `data:` line with a JSON payload, terminated by a blank line.

## Tests (Write First)

### PipelineEventBroadcaster Tests

Test file: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/PipelineEventBroadcasterTests.cs`

```csharp
// Test: subscriber receives events written after subscription
//   Subscribe, broadcast an event, assert subscriber's channel reader yields it

// Test: unsubscribed client does not receive events
//   Subscribe, unsubscribe, broadcast event
//   Assert the channel reader does not yield any items

// Test: multiple subscribers each receive every event
//   Subscribe two clients, broadcast one event
//   Assert both channel readers yield the event

// Test: subscriber channel is cleaned up on disconnect
//   Subscribe, unsubscribe
//   Assert internal subscriber count decremented

// Test: broadcast does not block on slow subscribers
//   Subscribe with a bounded channel (capacity 1)
//   Broadcast 5 events rapidly
//   Assert broadcast completes without blocking (measure elapsed time)
//   Slow subscriber may drop events (bounded channel with BoundedChannelFullMode.DropOldest)
```

### SSE Endpoint Integration Tests

Test file: `tests/PersonalBrandAssistant.Application.Tests/Endpoints/SseEndpointTests.cs`

Use `WebApplicationFactory<Program>` with in-memory database and a test `HttpClient`.

```csharp
// Test: returns text/event-stream content type
//   GET /api/events/pipeline with API key
//   Assert response Content-Type is "text/event-stream"

// Test: sends pipeline:snapshot event on connect with current state
//   Seed 3 active content items in DB
//   Connect to SSE endpoint, read first event
//   Assert event type is "pipeline:snapshot" and data contains 3 items

// Test: streams pipeline:stage-change events when content moves stages
//   Connect to SSE, consume snapshot
//   Trigger a stage change via the content pipeline service
//   Assert next event is "pipeline:stage-change" with correct contentId and stages

// Test: streams pipeline:created events for new content
//   Connect to SSE, consume snapshot
//   Create content via pipeline service
//   Assert next event is "pipeline:created"

// Test: streams pipeline:published events
//   Connect to SSE, consume snapshot
//   Publish content
//   Assert next event is "pipeline:published"

// Test: streams pipeline:failed events
//   Connect to SSE, consume snapshot
//   Trigger a pipeline failure
//   Assert next event is "pipeline:failed"

// Test: multiple concurrent connections each receive all events (broadcast)
//   Open two SSE connections, consume snapshots
//   Trigger one event
//   Assert both connections receive the event

// Test: requires API key authentication
//   GET /api/events/pipeline without header
//   Assert 401

// Test: readonly key scope is sufficient
//   (Depends on section-03)
//   GET with readonly key, assert 200 and stream starts
```

## File Paths

### New Files

- `src/PersonalBrandAssistant.Application/Common/Interfaces/IPipelineEventBroadcaster.cs` -- Interface for the broadcaster.
- `src/PersonalBrandAssistant.Application/Common/Models/PipelineEvent.cs` -- Event record with type discriminator and JSON payload.
- `src/PersonalBrandAssistant.Infrastructure/Services/IntegrationServices/PipelineEventBroadcaster.cs` -- Singleton broadcaster implementation.
- `src/PersonalBrandAssistant.Api/Endpoints/EventEndpoints.cs` -- SSE endpoint mapper.
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/PipelineEventBroadcasterTests.cs` -- Broadcaster unit tests.
- `tests/PersonalBrandAssistant.Application.Tests/Endpoints/SseEndpointTests.cs` -- Integration tests.

### Modified Files

- `src/PersonalBrandAssistant.Api/Program.cs` -- Add `app.MapEventEndpoints();` call.
- `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` -- Register `IPipelineEventBroadcaster` as singleton.
- `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/ContentPipeline.cs` -- Inject `IPipelineEventBroadcaster` and emit events on stage transitions, creation, publishing, and failures.

## PipelineEvent Record

```csharp
public record PipelineEvent(
    string EventType,   // "pipeline:stage-change", "pipeline:created", etc.
    string Data);       // Serialized JSON payload
```

The `Data` field contains pre-serialized JSON so the broadcaster does not need to know the shape of each event type. Each producer serializes its payload before calling `Broadcast()`.

## IPipelineEventBroadcaster Interface

```csharp
public interface IPipelineEventBroadcaster
{
    /// Subscribes a client and returns a ChannelReader for consuming events.
    ChannelReader<PipelineEvent> Subscribe();

    /// Removes a subscriber's channel reader from the broadcast list.
    void Unsubscribe(ChannelReader<PipelineEvent> reader);

    /// Broadcasts an event to all active subscribers.
    ValueTask BroadcastAsync(PipelineEvent pipelineEvent);
}
```

## Broadcaster Implementation

The `PipelineEventBroadcaster` is registered as a singleton. It maintains a `ConcurrentDictionary<ChannelReader<PipelineEvent>, Channel<PipelineEvent>>` mapping readers to their channels.

`Subscribe()` creates a new bounded `Channel<PipelineEvent>` with capacity 100 and `BoundedChannelFullMode.DropOldest`. It adds the channel to the dictionary and returns the reader. Bounded channels with DropOldest prevent slow clients from blocking the broadcaster -- if a client falls behind, older events are dropped.

`Unsubscribe(reader)` removes the entry from the dictionary and completes the channel writer.

`BroadcastAsync(event)` iterates all channels and writes the event. If a channel's `TryWrite` fails (full or completed), it's removed from the dictionary. This provides passive cleanup of dead connections without explicit health checking.

## SSE Endpoint Implementation

The `EventEndpoints` mapper registers `GET /api/events/pipeline`. The handler:

1. Sets `Content-Type: text/event-stream`, `Cache-Control: no-cache`, `Connection: keep-alive`.
2. Queries the database for all active (non-terminal) pipeline items and sends a `pipeline:snapshot` event with the full list serialized as JSON.
3. Calls `broadcaster.Subscribe()` to get a `ChannelReader<PipelineEvent>`.
4. Enters an `await foreach` loop on the channel reader, writing each event in SSE format (`event: {type}\ndata: {json}\n\n`).
5. When the `CancellationToken` fires (client disconnect), calls `broadcaster.Unsubscribe(reader)` in a `finally` block.

The endpoint uses `IAsyncEnumerable<T>` semantics via the channel reader. The response is never buffered -- it streams directly to the client.

## Pipeline Service Integration

The existing `ContentPipeline` service (`src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/ContentPipeline.cs`) is modified to inject `IPipelineEventBroadcaster` via constructor injection. After each state-changing operation, the service broadcasts the appropriate event:

- `CreateFromTopicAsync` success: broadcast `pipeline:created`
- `TransitionTo` calls that change status: broadcast `pipeline:stage-change`
- Successful publish: broadcast `pipeline:published`
- Pipeline step failure: broadcast `pipeline:failed`
- Status change to Review when autonomy requires approval: broadcast `pipeline:approval-needed`

Each event is constructed with a serialized JSON payload using `System.Text.Json.JsonSerializer`. The broadcast call is fire-and-forget (`_ = broadcaster.BroadcastAsync(...)`) -- pipeline operations do not wait for SSE delivery.

## Implementation Notes

- The broadcaster is in-process only. No persistence of events -- if PBA restarts, connected clients reconnect and get a fresh snapshot.
- The SSE endpoint should set `HttpContext.Response.Headers["X-Accel-Buffering"] = "no"` to prevent Nginx/reverse proxy buffering.
- Client-side reconnection: SSE has built-in auto-reconnect in browsers. The Jarvis HUD BFF proxy should also reconnect with exponential backoff (1s, 2s, 4s, 8s, max 30s).
- The snapshot query uses `AsNoTracking()` for read performance.
- The `PipelineEvent.Data` field is already serialized JSON, so the SSE writer outputs it directly without double-serialization.
