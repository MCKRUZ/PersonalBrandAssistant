# Code Review: Section 02 - Sidecar Integration

## Verdict: Block

There are critical thread-safety and resource-leak issues that must be addressed before merge.

---

## Critical Issues

### 1. JsonDocument leak in ReceiveFrameAsync

`JsonDocument.Parse` returns an `IDisposable` that owns pooled memory. The `doc` variable is never disposed, and the returned `JsonElement payload` becomes invalid once the document is collected/finalized. This is undefined behavior -- the payload `JsonElement` may silently return corrupted data.

**Fix:** Add `using` and clone the elements before disposing the document. Change `var doc` to `using var doc` and `p` to `p.Clone()`.

### 2. No read-side thread safety -- concurrent SendTaskAsync calls corrupt state

`ReceiveFrameAsync` has no synchronization. If two callers invoke `SendTaskAsync` concurrently (or one `SendTaskAsync` and one `ConnectAsync`), both will race on `ReceiveAsync` from the same `ClientWebSocket`, which is not thread-safe for concurrent reads. The `_writeLock` SemaphoreSlim only protects writes.

**Fix:** Enforce single-consumer semantics by adding an `int _activeStream` field checked with `Interlocked.CompareExchange` at the top of `SendTaskAsync`. Throw `InvalidOperationException` if a stream is already active. Reset in a `finally` block.

### 3. ConnectAsync disposes WebSocket that may be in active use

If `SendTaskAsync` is mid-stream iterating on `_ws`, calling `ConnectAsync` disposes the socket out from under it with `_ws?.Dispose(); _ws = new ClientWebSocket()`. There is no guard, lock, or state check. This will throw unpredictable exceptions in the streaming consumer.

**Fix:** Throw if `_activeStream != 0` (if using the guard from issue 2), or use a lock that coordinates with the read path. At minimum, set `_isConnected = false` before disposal.

---

## Warnings

### 4. ReceiveFrameAsync has no upper bound on message size

The `MemoryStream` grows unboundedly as frames arrive until `EndOfMessage`. A malicious or buggy sidecar can send arbitrarily large frames, causing `OutOfMemoryException`.

**Fix:** Add a `private const int MaxMessageSize = 1024 * 1024` and check `ms.Length` in the receive loop.

### 5. SidecarOptions is a mutable class with no validation

`ConnectionTimeoutSeconds` could be 0 or negative, and `WebSocketUrl` could be null/empty after binding. Given the project uses FluentValidation, add a `SidecarOptionsValidator` or use `IValidateOptions<SidecarOptions>` for fail-fast at startup.

### 6. Test server uses random port with collision risk

`Random.Shared.Next(10000, 60000)` can collide with ports already in use, causing flaky tests.

**Fix:** Bind to port 0 and read the assigned port from `app.Urls.Select(u => new Uri(u).Port).First()` after `StartAsync`.

### 7. ConnectAsync sets _isConnected = true before the session handshake completes

`_isConnected` is set to `true` immediately after `ConnectAsync` on the raw WebSocket, before the `new-session` / `session-update` handshake. If the handshake fails (wrong response, timeout), `_isConnected` remains `true` even though the client is not in a valid state.

**Fix:** Move `_isConnected = true` to after the successful handshake, just before the return statement.

### 8. Dispose does not perform graceful WebSocket close

`Dispose()` calls `_ws?.Dispose()` which does not send a Close frame. The server sees an abrupt disconnect.

**Fix:** Implement `IAsyncDisposable` with a best-effort `CloseAsync(WebSocketCloseStatus.NormalClosure, ...)` before disposal.

---

## Suggestions

### 9. SidecarOptions should use init-only setters

The project coding style mandates immutability. `SidecarOptions` uses mutable `{ get; set; }` properties. While options binding requires setters, `{ get; init; }` works with `ConfigurationBinder` in .NET 8+ when using `BindConfiguration`.

### 10. _currentSessionId mutation inside SendTaskAsync is a hidden side effect

The `session-update` case inside `SendTaskAsync` mutates `_currentSessionId`. This is a hidden side effect in a method that appears to be a pure event stream. Consider returning the session update event and letting the caller manage session state, or at minimum document this behavior.

### 11. Missing test: reconnection after disconnect

There is no test for calling `ConnectAsync` a second time after a disconnect. This would exercise the reconnection path and is where issue 3 would surface.

### 12. Missing test: SendTaskAsync called without prior ConnectAsync

The guard `_ws is null || _ws.State != WebSocketState.Open` is correct but untested.

### 13. Test helper ReceiveJson does not handle multi-frame messages

The test `ReceiveJson` reads a single frame into a 4KB buffer. If a test message ever exceeds 4KB, it will silently truncate and fail with a confusing JSON parse error. Consider asserting `result.EndOfMessage` at minimum.

### 14. Consider IAsyncDisposable for the client

`SidecarClient` holds async resources (WebSocket). Implementing `IAsyncDisposable` is more idiomatic for async resource cleanup in .NET and avoids sync-over-async patterns.

---

## Summary

| Priority | Count |
|----------|-------|
| Critical | 3 |
| Warning  | 5 |
| Suggestion | 6 |

The JsonDocument leak (1) will cause real bugs in production. The thread-safety gaps (2, 3) create race conditions under concurrent use. These three issues must be resolved before merge. The warnings around message size bounds (4) and premature `_isConnected` (7) should also be addressed in this pass.
