I now have sufficient context to write the section content. Let me compile everything I've gathered.

# Section 02: ComfyUI Client

## Overview

This section implements `ComfyUiClient`, a thin HTTP + WebSocket wrapper around the ComfyUI API. The client handles four operations: queueing image generation prompts, waiting for completion via WebSocket (with HTTP polling fallback), downloading generated images, and health checking the server. It contains zero business logic -- just transport-level communication.

**Depends on:** section-01-foundation (provides `IComfyUiClient` interface, `ComfyUiOptions` configuration record)

**Blocks:** section-03-image-services (which orchestrates image generation through this client)

---

## File Inventory

| Action | File Path |
|--------|-----------|
| Create | `src/PersonalBrandAssistant.Infrastructure/Services/ContentAutomation/ComfyUiClient.cs` |
| Create | `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentAutomation/ComfyUiClientTests.cs` |

---

## Dependencies from Section 01

The following must exist before implementing this section. They are defined in section-01-foundation:

**`IComfyUiClient` interface** at `src/PersonalBrandAssistant.Application/Common/Interfaces/IComfyUiClient.cs`:

```csharp
interface IComfyUiClient
{
    Task<string> QueuePromptAsync(JsonObject workflow, CancellationToken ct);
    Task<ComfyUiResult> WaitForCompletionAsync(string promptId, CancellationToken ct);
    Task<byte[]> DownloadImageAsync(string filename, string subfolder, CancellationToken ct);
    Task<bool> IsAvailableAsync(CancellationToken ct);
}
```

**`ComfyUiOptions` record** at `src/PersonalBrandAssistant.Application/Common/Models/ComfyUiOptions.cs`:
- `BaseUrl` (string, default `"http://192.168.50.47:8188"`)
- `TimeoutSeconds` (int, default `120`)
- `HealthCheckTimeoutSeconds` (int, default `5`)

**`ComfyUiResult` record** -- a simple DTO carrying the output filenames and subfolder from a completed generation. Should be defined alongside the interface or in the Models folder. Suggested shape:

```csharp
record ComfyUiResult(IReadOnlyList<ComfyUiOutputImage> Images);
record ComfyUiOutputImage(string Filename, string Subfolder, string Type);
```

**DI Registration** in `DependencyInjection.cs`:
- `services.AddSingleton<IComfyUiClient, ComfyUiClient>()` -- singleton because it uses `IHttpClientFactory` internally; WebSocket connections are created per-request.

---

## Tests First

Create `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentAutomation/ComfyUiClientTests.cs`.

The project uses xUnit + Moq. For HTTP testing, follow the existing pattern in `LinkedInPlatformAdapterTests`: mock `HttpMessageHandler` via `Moq.Protected()`. For WebSocket integration tests, follow the pattern in `SidecarClientTests`: spin up a lightweight `WebApplication` with `UseWebSockets()` as a local test server.

### Unit Tests (Mock HTTP)

These tests mock the `HttpMessageHandler` to verify correct request construction and response parsing without hitting a real ComfyUI server.

**Test: QueuePromptAsync sends POST to /prompt with workflow JSON and client_id**
- Arrange: Create a `JsonObject` workflow. Set up the mock handler to capture the outgoing request and return a successful response with `{"prompt_id": "abc-123"}`.
- Assert: The request method is POST, the URL path is `/prompt`, the body contains both the `prompt` key (the workflow) and a `client_id` key (a GUID string). The `client_id` must be consistent per client instance (stored as a field).

**Test: QueuePromptAsync returns prompt_id from response**
- Arrange: Mock handler returns `{"prompt_id": "test-prompt-id"}`.
- Assert: Return value equals `"test-prompt-id"`.

**Test: QueuePromptAsync throws when ComfyUI returns error response with node_errors**
- Arrange: Mock handler returns HTTP 400 with body `{"error": "prompt validation error", "node_errors": {"3": {"errors": [{"message": "Invalid model"}]}}}`.
- Assert: Throws an exception (use a custom `ComfyUiException` or `InvalidOperationException`) whose message contains "prompt validation error" or the node error details.

**Test: IsAvailableAsync returns true when GET /system_stats succeeds**
- Arrange: Mock handler returns HTTP 200 for `/system_stats`.
- Assert: Returns `true`.

**Test: IsAvailableAsync returns false on connection refused**
- Arrange: Mock handler throws `HttpRequestException`.
- Assert: Returns `false` (does not throw).

**Test: IsAvailableAsync returns false on timeout**
- Arrange: Mock handler delays longer than `HealthCheckTimeoutSeconds`.
- Assert: Returns `false`. The health check must use its own short timeout (from `ComfyUiOptions.HealthCheckTimeoutSeconds`), not the general `TimeoutSeconds`.

**Test: DownloadImageAsync returns byte array from GET /view endpoint**
- Arrange: Mock handler returns a byte array (e.g., PNG magic bytes `[0x89, 0x50, 0x4E, 0x47, ...]`) for the `/view` endpoint.
- Assert: Returned bytes match the expected content.

**Test: DownloadImageAsync constructs correct URL with filename, subfolder, type=output params**
- Arrange: Call `DownloadImageAsync("ComfyUI_00001_.png", "output_images", ct)`.
- Assert: The captured request URL is `/view?filename=ComfyUI_00001_.png&subfolder=output_images&type=output`.

### Integration Tests (Local WebSocket Server)

These tests spin up a real WebSocket server to verify the completion detection protocol. Follow the `SidecarClientTests` pattern: `WebApplication` with `UseWebSockets()`, random port, proper async disposal.

**Test: WaitForCompletionAsync detects completion via WebSocket**
- Arrange: Start a local WS server at `/ws?clientId={id}`. After connection, send a sequence of ComfyUI-style messages:
  1. `{"type": "status", "data": {"status": {"exec_info": {"queue_remaining": 1}}}}`
  2. `{"type": "executing", "data": {"node": "3", "prompt_id": "test-id"}}`
  3. `{"type": "executing", "data": {"node": null, "prompt_id": "test-id"}}` (completion signal)
- Also set up an HTTP endpoint at `/history/test-id` that returns the history JSON with output filenames.
- Assert: Returns a `ComfyUiResult` with the correct output image filenames extracted from the history response.

**Test: WaitForCompletionAsync falls back to HTTP polling when WebSocket fails**
- Arrange: Start server that rejects WebSocket upgrades (return 400). Set up `/history/{promptId}` to initially return an empty object (no outputs) and then on subsequent calls return a completed history with outputs.
- Assert: Eventually returns a `ComfyUiResult` with correct outputs. The client should poll `/history/{promptId}` at a reasonable interval (e.g., every 2 seconds).

**Test: WaitForCompletionAsync throws on timeout**
- Arrange: Start a WS server that connects but never sends a completion message. Set `TimeoutSeconds = 2` in options.
- Assert: Throws `TimeoutException` or `OperationCanceledException` after roughly 2 seconds.

**Test: WebSocket connection is created per-request (not reused)**
- Arrange: Start a WS server that tracks connection count. Call `WaitForCompletionAsync` twice sequentially.
- Assert: Server received 2 separate WebSocket connections.

---

## Implementation Details

### Class: `ComfyUiClient`

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/ContentAutomation/ComfyUiClient.cs`

**Namespace:** `PersonalBrandAssistant.Infrastructure.Services.ContentAutomation`

**Constructor dependencies:**
- `IHttpClientFactory` -- create named client `"ComfyUI"` (register in DI with `BaseAddress` from `ComfyUiOptions.BaseUrl`)
- `IOptions<ComfyUiOptions>` -- configuration
- `ILogger<ComfyUiClient>` -- structured logging

**Instance fields:**
- `_clientId` (string) -- a stable GUID generated once at construction (`Guid.NewGuid().ToString()`). ComfyUI uses this to route WebSocket messages to the correct listener. Must be the same value passed to both `/prompt` POST body and `/ws?clientId=` query string.

### Method: `QueuePromptAsync`

1. Create HTTP client via `_httpClientFactory.CreateClient("ComfyUI")`
2. Build the request body as a `JsonObject` with two keys:
   - `"prompt"` -- the incoming workflow `JsonObject`
   - `"client_id"` -- the instance `_clientId`
3. POST to `/prompt` with `Content-Type: application/json`
4. On success: parse response JSON, extract `prompt_id` string, return it
5. On error: if response body contains `"error"` or `"node_errors"`, parse them and throw with details. Otherwise throw with HTTP status code.

### Method: `WaitForCompletionAsync`

Primary path (WebSocket):
1. Create a new `ClientWebSocket` instance (per-request, never reused)
2. Connect to `{BaseUrl.Replace("http", "ws")}/ws?clientId={_clientId}`
3. Create a `CancellationTokenSource` linked to the incoming `ct` with timeout from `ComfyUiOptions.TimeoutSeconds`
4. Read messages in a loop. Parse each as JSON. Look for messages where:
   - `type == "executing"` AND `data.node` is null AND `data.prompt_id` matches the target `promptId`
   - This signals that all nodes have finished executing
5. On completion signal: close the WebSocket gracefully, then call `GET /history/{promptId}` to get output filenames
6. Parse the history response: navigate to `{promptId}.outputs` and find nodes that have an `images` array. Extract `filename`, `subfolder`, and `type` from each image entry.
7. Return `ComfyUiResult` with the collected output images

Fallback path (HTTP polling):
1. If WebSocket connection throws (connection refused, upgrade rejected, etc.), log a warning and fall back to polling
2. Poll `GET /history/{promptId}` every 2 seconds
3. The history response for an incomplete prompt either won't contain the `promptId` key or will have empty outputs. Once outputs appear, the prompt is complete.
4. Same timeout applies via the linked `CancellationTokenSource`

**Important:** Always dispose the `ClientWebSocket` after use (wrap in `using` or `try/finally`). ComfyUI server restarts would kill persistent connections silently, so per-request WebSocket creation is intentional.

### Method: `DownloadImageAsync`

1. Create HTTP client via factory
2. GET `/view?filename={Uri.EscapeDataString(filename)}&subfolder={Uri.EscapeDataString(subfolder)}&type=output`
3. Return `await response.Content.ReadAsByteArrayAsync(ct)`
4. Throw on non-success status

### Method: `IsAvailableAsync`

1. Create HTTP client via factory
2. Create a separate `CancellationTokenSource` with timeout from `ComfyUiOptions.HealthCheckTimeoutSeconds` (default 5s), linked to the incoming `ct`
3. Try `GET /system_stats` with the short timeout
4. Return `true` on HTTP 200, `false` on any exception (`HttpRequestException`, `TaskCanceledException`, etc.)
5. Must not throw -- this is a safe health probe

### ComfyUI History Response Shape

The `/history/{promptId}` endpoint returns JSON structured as:

```json
{
  "{promptId}": {
    "prompt": [...],
    "outputs": {
      "9": {
        "images": [
          { "filename": "ComfyUI_00001_.png", "subfolder": "", "type": "output" }
        ]
      }
    },
    "status": { "status_str": "success", "completed": true }
  }
}
```

The client needs to iterate over `outputs` nodes, find any that contain an `images` array, and collect all image entries. The node ID (e.g., `"9"`) varies by workflow but is irrelevant to the client -- just find all images.

### DI Registration Addition

In `DependencyInjection.cs`, add a named HttpClient for ComfyUI:

```csharp
services.AddHttpClient("ComfyUI", (sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<ComfyUiOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
});
services.AddSingleton<IComfyUiClient, ComfyUiClient>();
```

The client is registered as singleton because it holds no per-request state (the `_clientId` is stable, `IHttpClientFactory` is safe for singleton use, and WebSocket connections are created per-request).

---

## Error Handling

| Scenario | Behavior |
|----------|----------|
| `/prompt` returns HTTP 400 with `node_errors` | Parse error details, throw with descriptive message including node IDs and error text |
| `/prompt` returns other HTTP error | Throw with status code and response body snippet |
| WebSocket connection fails | Log warning, fall back to HTTP polling automatically |
| WebSocket receives unexpected message types | Ignore them (ComfyUI sends `progress`, `status`, `execution_cached` -- only `executing` with null node matters) |
| `/history/{promptId}` returns empty/missing | In polling mode, continue polling. In WebSocket mode, this is unexpected after completion signal -- log error and retry once |
| Timeout exceeded | `OperationCanceledException` or `TimeoutException` propagates to caller |
| `/view` returns non-200 | Throw `HttpRequestException` |
| `/system_stats` unreachable | Return `false` (never throw from health check) |

---

## Configuration Reference

In `appsettings.json` under the `ContentAutomation:ImageGeneration` section (defined in section-01):

```json
{
  "ContentAutomation": {
    "ImageGeneration": {
      "ComfyUiBaseUrl": "http://192.168.50.47:8188",
      "TimeoutSeconds": 120,
      "HealthCheckTimeoutSeconds": 5
    }
  }
}
```

The `ComfyUiOptions` record maps these values. The `BaseUrl` comes from `ComfyUiBaseUrl`.

---

## Testing Strategy Notes

- **Unit tests** use `Mock<HttpMessageHandler>` with `Moq.Protected()` to intercept `SendAsync`. This is the established pattern in `LinkedInPlatformAdapterTests.cs`.
- **WebSocket integration tests** spin up a real `WebApplication` test server, following the exact pattern in `SidecarClientTests.cs`. Use `Random.Shared.Next(10000, 60000)` for port selection, `IAsyncDisposable` for cleanup.
- The test class should implement `IAsyncDisposable` to properly shut down any test servers.
- No external dependencies required -- everything runs in-process.
- For the `IHttpClientFactory` mock in unit tests: create a mock that returns an `HttpClient` wrapping the mocked handler. Alternatively, construct the `HttpClient` directly if the class accepts it (but since the class uses `IHttpClientFactory`, mock the factory's `CreateClient("ComfyUI")` method).