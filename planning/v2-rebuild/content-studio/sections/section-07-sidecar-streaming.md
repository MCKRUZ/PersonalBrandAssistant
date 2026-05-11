# Section 07: Sidecar Streaming

## Overview

This section adds real-time streaming capability to the sidecar CLI client, enabling token-by-token AI response delivery. It also fixes a concurrency bottleneck where the singleton `SemaphoreSlim(1,1)` blocks all sidecar calls globally -- replacing it with per-content keyed semaphores so different content items can stream concurrently while same-content calls remain serialized.

This section has **no dependencies** on other sections and is a prerequisite for **section-08-signalr-hub**, which consumes `StreamPromptAsync` to push tokens to the Angular frontend via SignalR.

## Background: Current State

The existing sidecar infrastructure consists of three pieces:

**`ISidecarClient`** (`src/PBA.Application/Common/Interfaces/ISidecarClient.cs`):
```csharp
public interface ISidecarClient
{
    Task<string> SendPromptAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}
```

**`SidecarClient`** (`src/PBA.Infrastructure/Services/SidecarClient.cs`): A singleton that wraps `IProcessRunner`, protects all calls with a single `SemaphoreSlim(1,1)`, and returns the complete stdout after the process exits.

**`IProcessRunner` / `ProcessRunner`** (`src/PBA.Application/Common/Interfaces/IProcessRunner.cs`, `src/PBA.Infrastructure/Services/ProcessRunner.cs`): Runs a CLI process, captures stdout/stderr in full, kills on timeout. Has no concept of line-by-line streaming or external kill signaling.

**DI registration** (`src/PBA.Infrastructure/DependencyInjection.cs`): Both `IProcessRunner` and `ISidecarClient` are registered as singletons.

## What Changes

### 1. Add `StreamPromptAsync` to `ISidecarClient`

**File:** `src/PBA.Application/Common/Interfaces/ISidecarClient.cs`

Add a streaming method that returns `IAsyncEnumerable<string>`:

```csharp
IAsyncEnumerable<string> StreamPromptAsync(
    Guid contentId,
    string systemPrompt,
    string userPrompt,
    CancellationToken ct = default);
```

The existing `SendPromptAsync` remains unchanged -- toolbar AI actions (draft, refine, shorten, expand, changeTone) and voice check continue to use the synchronous one-shot method. The streaming variant is exclusively for the sidecar chat panel via SignalR.

### 2. Add `Kill()` to `IProcessRunner` and `ProcessRunner`

**File:** `src/PBA.Application/Common/Interfaces/IProcessRunner.cs`

Add a new method for starting a process that can be killed externally, plus a handle type:

```csharp
IStreamingProcessHandle StartStreaming(
    string fileName,
    string arguments,
    string? stdinContent = null);
```

And a handle interface:

```csharp
public interface IStreamingProcessHandle : IAsyncDisposable
{
    IAsyncEnumerable<string> ReadLinesAsync(CancellationToken ct = default);
    void Kill();
    int? ExitCode { get; }
}
```

**File:** `src/PBA.Infrastructure/Services/ProcessRunner.cs`

Implement `StartStreaming` by creating a `Process`, starting it, writing stdin if provided, then returning a `StreamingProcessHandle` wrapper. The handle's `ReadLinesAsync` reads `process.StandardOutput.ReadLineAsync()` in a loop yielding each non-null line. `Kill()` calls `process.Kill(entireProcessTree: true)`. The handle disposes the process on `DisposeAsync`.

Key implementation considerations:
- The `CancellationToken` passed to `ReadLinesAsync` should kill the process when cancelled (call `Kill()` in the cancellation callback)
- `ReadLinesAsync` should catch `OperationCanceledException` and return cleanly after killing
- stderr should be redirected but not read line-by-line -- capture it asynchronously for error reporting if `ExitCode != 0`

### 3. Replace Global Semaphore with Keyed Semaphore in `SidecarClient`

**File:** `src/PBA.Infrastructure/Services/SidecarClient.cs`

Replace the single `SemaphoreSlim _semaphore = new(1, 1)` with a `ConcurrentDictionary<Guid, SemaphoreSlim>` that creates a per-content-ID semaphore on demand.

This means:
- Two different content items can stream concurrently (different semaphore keys)
- Two calls for the same content item are serialized (same semaphore key)
- `SendPromptAsync` keeps a global fallback semaphore since callers already operate sequentially

The recommended signature:

```csharp
public interface ISidecarClient
{
    Task<string> SendPromptAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default);

    IAsyncEnumerable<string> StreamPromptAsync(
        Guid contentId,
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default);
}
```

**Keyed semaphore implementation details:**
- `private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _semaphores = new();`
- Helper method: `private SemaphoreSlim GetSemaphore(Guid key) => _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));`
- In `StreamPromptAsync`: acquire the keyed semaphore, yield tokens, release in a `finally` block
- Cleanup: Consider removing semaphore entries after release if no other waiter is queued (check `_semaphore.CurrentCount == 1`). For a single-user app this is not critical -- the dictionary will hold a handful of entries at most.
- `Dispose()` must dispose all semaphores in the dictionary, plus the existing global one.

### 4. Implement `StreamPromptAsync` in `SidecarClient`

The streaming method:
1. Acquires the keyed semaphore for `contentId`
2. Calls `_processRunner.StartStreaming(cliPath, "--print", stdinContent)` to get a handle
3. Iterates `handle.ReadLinesAsync(ct)`, yielding each line
4. On cancellation: the handle's `ReadLinesAsync` kills the process
5. Releases the semaphore in `finally`
6. After iteration completes, checks `handle.ExitCode` -- logs a warning if non-zero but does NOT throw (partial output is still useful for streaming)

## Files Created

| File | Description |
|------|-------------|
| `src/PBA.Infrastructure/Services/StreamingProcessHandle.cs` | `IStreamingProcessHandle` implementation wrapping `System.Diagnostics.Process`. Thread-safe disposal via `Interlocked.CompareExchange`. Caches exit code before process disposal. |
| `tests/PBA.Infrastructure.Tests/Services/Fakes/FakeStreamingProcessHandle.cs` | Test double for `IStreamingProcessHandle` supporting configurable delay and kill tracking |
| `tests/PBA.Infrastructure.Tests/Services/StreamingProcessHandleTests.cs` | Integration tests (3) against real processes, marked `[Trait("Category", "Integration")]` |

## Files Modified

| File | Change |
|------|--------|
| `src/PBA.Application/Common/Interfaces/ISidecarClient.cs` | Added `StreamPromptAsync(Guid, string, string, CancellationToken)` returning `IAsyncEnumerable<string>` |
| `src/PBA.Application/Common/Interfaces/IProcessRunner.cs` | Added `IStreamingProcessHandle` interface and `StartStreaming` method. Note: `[EnumeratorCancellation]` cannot be used on interface methods — attribute is on implementations only. |
| `src/PBA.Infrastructure/Services/ProcessRunner.cs` | Added `StartStreaming` implementation creating `StreamingProcessHandle` wrapper |
| `src/PBA.Infrastructure/Services/SidecarClient.cs` | Added `StreamPromptAsync` impl with keyed semaphore (`ConcurrentDictionary<Guid, SemaphoreSlim>`). Global semaphore renamed to `_globalSemaphore` for `SendPromptAsync`. `Dispose` cleans up all semaphores. |
| `tests/PBA.Infrastructure.Tests/Services/SidecarClientTests.cs` | Added 5 streaming tests: token yielding, cancellation, concurrent different IDs, serialized same ID, semaphore release on error |

## Tests

All tests go in `tests/PBA.Infrastructure.Tests/Services/SidecarClientTests.cs` (extend existing file) and a new `tests/PBA.Infrastructure.Tests/Services/StreamingProcessHandleTests.cs`.

### Test File: `tests/PBA.Infrastructure.Tests/Services/SidecarClientTests.cs`

Add the following tests to the existing test class. The existing tests for `SendPromptAsync` remain unchanged.

**StreamPromptAsync_YieldsTokensFromProcessStdout**
- Arrange: Mock `IProcessRunner.StartStreaming` to return a fake `IStreamingProcessHandle` whose `ReadLinesAsync` yields `["Hello", " world", "!"]`
- Act: Collect all tokens from `StreamPromptAsync`
- Assert: Collected tokens equal `["Hello", " world", "!"]`

**StreamPromptAsync_CancellationToken_KillsSidecarProcess**
- Arrange: Create a `CancellationTokenSource`. Mock `StartStreaming` to return a handle whose `ReadLinesAsync` yields one token, then blocks (simulated with `Task.Delay(Timeout.Infinite, ct)` -- when cancelled, the iteration stops)
- Act: Start collecting tokens, cancel after first token
- Assert: Only one token collected. Verify `Kill()` was called on the handle (or that the handle was disposed)

**StreamPromptAsync_KeyedSemaphore_AllowsConcurrentDifferentContentIds**
- Arrange: Two different `Guid` content IDs. Mock `StartStreaming` to return handles that yield tokens with a 50ms delay each
- Act: Call `StreamPromptAsync` for both content IDs concurrently
- Assert: Both complete in approximately the same wall-clock time (not serialized). Verify overlap exists.

**StreamPromptAsync_KeyedSemaphore_SerializesSameContentId**
- Arrange: Same `Guid` content ID for two calls. Mock `StartStreaming` with a 50ms delay per handle
- Act: Call `StreamPromptAsync` for the same content ID concurrently
- Assert: Calls are serialized (second starts after first ends). Measure wall-clock time to confirm ~100ms, not ~50ms.

**StreamPromptAsync_ReleasesSemaphoreOnError**
- Arrange: Mock `StartStreaming` to throw on first call, return normally on second
- Act: Call `StreamPromptAsync` -- first throws. Call again -- second succeeds
- Assert: Second call completes without deadlock (semaphore was released despite the error)

### Test File: `tests/PBA.Infrastructure.Tests/Services/StreamingProcessHandleTests.cs`

These test the handle in isolation (without mocking -- they test the real `StreamingProcessHandle` against a real process). Use a simple cross-platform process like `dotnet --info` or `echo` for line output.

**ReadLinesAsync_YieldsStdoutLines**
- Arrange: Create a `StreamingProcessHandle` wrapping a process that outputs multiple lines
- Act: Collect all lines
- Assert: Contains expected lines

**Kill_TerminatesRunningProcess**
- Arrange: Start a long-running process (e.g., `ping -t localhost` on Windows or `sleep 999` on Linux)
- Act: Call `Kill()`
- Assert: Process is no longer running. `ReadLinesAsync` terminates.

**DisposeAsync_CleansUpProcess**
- Arrange: Start a process
- Act: Dispose the handle
- Assert: Process resources are cleaned up (no exception on double-dispose)

Note: These integration-style tests for the handle are optional if the mock-based SidecarClient tests provide sufficient coverage. They add confidence but may be flaky in CI due to process timing. Consider marking them with `[Trait("Category", "Integration")]` so they can be excluded from fast CI runs.

### Mock Helper

To support the streaming tests, create a `FakeStreamingProcessHandle` test helper:

```csharp
internal class FakeStreamingProcessHandle : IStreamingProcessHandle
{
    private readonly IReadOnlyList<string> _lines;
    private readonly TimeSpan _delayPerLine;
    private bool _killed;

    public FakeStreamingProcessHandle(IReadOnlyList<string> lines, TimeSpan? delayPerLine = null)
    {
        _lines = lines;
        _delayPerLine = delayPerLine ?? TimeSpan.Zero;
    }

    public int? ExitCode => _killed ? -1 : 0;

    public async IAsyncEnumerable<string> ReadLinesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var line in _lines)
        {
            ct.ThrowIfCancellationRequested();
            if (_delayPerLine > TimeSpan.Zero)
                await Task.Delay(_delayPerLine, ct);
            if (_killed) yield break;
            yield return line;
        }
    }

    public void Kill() => _killed = true;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

Place this in `tests/PBA.Infrastructure.Tests/Services/Fakes/FakeStreamingProcessHandle.cs` or as a nested class in the test file.

## Implementation Notes

### Why `IAsyncEnumerable<string>` Instead of Callbacks

The `IAsyncEnumerable<string>` return type for `StreamPromptAsync` is the idiomatic C# approach for streaming data. It composes naturally with:
- SignalR hub (section-08): `await foreach (var token in client.StreamPromptAsync(...))` inside the hub method
- CancellationToken: built-in support via `[EnumeratorCancellation]` attribute
- LINQ-style processing if needed later

### Semaphore Cleanup Strategy

For a single-user app, the `ConcurrentDictionary<Guid, SemaphoreSlim>` will hold at most a few dozen entries (one per content item ever streamed in the current app lifecycle). No eviction strategy is needed. If this ever became a multi-user system, a bounded cache with TTL-based eviction would be appropriate -- but YAGNI.

### DI Registration

The registration remains singleton for both `IProcessRunner` and `ISidecarClient`. The keyed semaphore strategy is thread-safe by design (`ConcurrentDictionary`), and the `SidecarClient` holds no per-request state. No registration change needed.

### Error Handling in Streaming

Unlike `SendPromptAsync` which throws on non-zero exit code, `StreamPromptAsync` should **not** throw after partial token delivery. The caller (SignalR hub) has already sent tokens to the client -- throwing would cause a confusing UX. Instead:
- Log a warning if exit code is non-zero after streaming completes
- The hub (section-08) should catch exceptions from the async enumerable and send `GenerationError` to the client
- If the process fails before any tokens are yielded, it is acceptable to throw

### Stdin Format

Both `SendPromptAsync` and `StreamPromptAsync` use the same stdin format: `"System: {systemPrompt}\n\nUser: {userPrompt}"`. The `--print` argument tells the sidecar CLI to output to stdout. For streaming, the sidecar process outputs tokens line-by-line as it generates them -- each `ReadLineAsync` call yields one chunk.
