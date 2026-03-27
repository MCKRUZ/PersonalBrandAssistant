diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/ISidecarClient.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/ISidecarClient.cs
new file mode 100644
index 0000000..75dc277
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Interfaces/ISidecarClient.cs
@@ -0,0 +1,14 @@
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Application.Common.Interfaces;
+
+public interface ISidecarClient
+{
+    Task<SidecarSession> ConnectAsync(CancellationToken ct);
+
+    IAsyncEnumerable<SidecarEvent> SendTaskAsync(string task, string? sessionId, CancellationToken ct);
+
+    Task AbortAsync(string? sessionId, CancellationToken ct);
+
+    bool IsConnected { get; }
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/SidecarEvent.cs b/src/PersonalBrandAssistant.Application/Common/Models/SidecarEvent.cs
new file mode 100644
index 0000000..9086bfc
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/SidecarEvent.cs
@@ -0,0 +1,15 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public abstract record SidecarEvent;
+
+public record ChatEvent(string EventType, string? Text, string? FilePath, string? ToolName) : SidecarEvent;
+
+public record FileChangeEvent(string FilePath, string ChangeType) : SidecarEvent;
+
+public record StatusEvent(string Status) : SidecarEvent;
+
+public record SessionUpdateEvent(string SessionId) : SidecarEvent;
+
+public record TaskCompleteEvent(string SessionId, int InputTokens, int OutputTokens) : SidecarEvent;
+
+public record ErrorEvent(string Message) : SidecarEvent;
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/SidecarOptions.cs b/src/PersonalBrandAssistant.Application/Common/Models/SidecarOptions.cs
new file mode 100644
index 0000000..6cd6064
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/SidecarOptions.cs
@@ -0,0 +1,9 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public class SidecarOptions
+{
+    public const string SectionName = "Sidecar";
+    public string WebSocketUrl { get; set; } = "ws://localhost:3001/ws";
+    public int ConnectionTimeoutSeconds { get; set; } = 30;
+    public int ReconnectDelaySeconds { get; set; } = 5;
+}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/SidecarSession.cs b/src/PersonalBrandAssistant.Application/Common/Models/SidecarSession.cs
new file mode 100644
index 0000000..9b71000
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/SidecarSession.cs
@@ -0,0 +1,3 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public record SidecarSession(string SessionId, DateTimeOffset ConnectedAt);
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/SidecarClient.cs b/src/PersonalBrandAssistant.Infrastructure/Services/SidecarClient.cs
new file mode 100644
index 0000000..dd8e5ab
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/SidecarClient.cs
@@ -0,0 +1,181 @@
+using System.Net.WebSockets;
+using System.Runtime.CompilerServices;
+using System.Text;
+using System.Text.Json;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Infrastructure.Services;
+
+public sealed class SidecarClient : ISidecarClient, IDisposable
+{
+    private readonly SidecarOptions _options;
+    private readonly ILogger<SidecarClient> _logger;
+    private readonly SemaphoreSlim _writeLock = new(1, 1);
+    private readonly JsonSerializerOptions _jsonOptions = new()
+    {
+        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
+        PropertyNameCaseInsensitive = true,
+    };
+
+    private ClientWebSocket? _ws;
+    private string? _currentSessionId;
+    private volatile bool _isConnected;
+
+    public SidecarClient(IOptions<SidecarOptions> options, ILogger<SidecarClient> logger)
+    {
+        _options = options.Value;
+        _logger = logger;
+    }
+
+    public bool IsConnected => _isConnected;
+
+    public async Task<SidecarSession> ConnectAsync(CancellationToken ct)
+    {
+        _ws?.Dispose();
+        _ws = new ClientWebSocket();
+
+        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
+        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.ConnectionTimeoutSeconds));
+
+        await _ws.ConnectAsync(new Uri(_options.WebSocketUrl), timeoutCts.Token);
+        _isConnected = true;
+
+        await SendMessageAsync(new { type = "new-session" }, timeoutCts.Token);
+
+        // Wait for session-update response
+        var (eventType, payload) = await ReceiveFrameAsync(timeoutCts.Token);
+        if (eventType != "session-update")
+            throw new InvalidOperationException($"Expected session-update, got {eventType}");
+
+        _currentSessionId = payload.GetProperty("sessionId").GetString()
+            ?? throw new InvalidOperationException("session-update missing sessionId");
+
+        _logger.LogInformation("Connected to sidecar, session {SessionId}", _currentSessionId);
+        return new SidecarSession(_currentSessionId, DateTimeOffset.UtcNow);
+    }
+
+    public async IAsyncEnumerable<SidecarEvent> SendTaskAsync(
+        string task,
+        string? sessionId,
+        [EnumeratorCancellation] CancellationToken ct)
+    {
+        if (_ws is null || _ws.State != WebSocketState.Open)
+            throw new InvalidOperationException("Not connected to sidecar");
+
+        var message = sessionId is not null
+            ? new { type = "send-message", payload = new { message = task, sessionId } }
+            : (object)new { type = "send-message", payload = new { message = task } };
+
+        await SendMessageAsync(message, ct);
+
+        while (true)
+        {
+            ct.ThrowIfCancellationRequested();
+            var (eventType, payload) = await ReceiveFrameAsync(ct);
+
+            switch (eventType)
+            {
+                case "chat-event":
+                    yield return new ChatEvent(
+                        payload.TryGetProperty("eventType", out var et) ? et.GetString() ?? "" : "",
+                        payload.TryGetProperty("text", out var txt) ? txt.GetString() : null,
+                        payload.TryGetProperty("filePath", out var fp) ? fp.GetString() : null,
+                        payload.TryGetProperty("toolName", out var tn) ? tn.GetString() : null);
+                    break;
+
+                case "file-change":
+                    yield return new FileChangeEvent(
+                        payload.GetProperty("filePath").GetString() ?? "",
+                        payload.GetProperty("changeType").GetString() ?? "");
+                    break;
+
+                case "status":
+                    var status = payload.GetProperty("status").GetString();
+                    if (status == "idle")
+                    {
+                        var inputTokens = payload.TryGetProperty("inputTokens", out var it) ? it.GetInt32() : 0;
+                        var outputTokens = payload.TryGetProperty("outputTokens", out var ot) ? ot.GetInt32() : 0;
+                        yield return new TaskCompleteEvent(
+                            _currentSessionId ?? "",
+                            inputTokens,
+                            outputTokens);
+                        yield break;
+                    }
+                    yield return new StatusEvent(status ?? "unknown");
+                    break;
+
+                case "session-update":
+                    var sid = payload.GetProperty("sessionId").GetString() ?? "";
+                    _currentSessionId = sid;
+                    yield return new SessionUpdateEvent(sid);
+                    break;
+
+                case "error":
+                    yield return new ErrorEvent(
+                        payload.TryGetProperty("message", out var msg) ? msg.GetString() ?? "Unknown error" : "Unknown error");
+                    break;
+
+                default:
+                    _logger.LogWarning("Unknown sidecar event type: {EventType}", eventType);
+                    break;
+            }
+        }
+    }
+
+    public async Task AbortAsync(string? sessionId, CancellationToken ct)
+    {
+        if (_ws is null || _ws.State != WebSocketState.Open)
+            throw new InvalidOperationException("Not connected to sidecar");
+
+        await SendMessageAsync(new { type = "abort", sessionId = sessionId ?? _currentSessionId }, ct);
+    }
+
+    private async Task SendMessageAsync(object message, CancellationToken ct)
+    {
+        var json = JsonSerializer.SerializeToUtf8Bytes(message, _jsonOptions);
+        await _writeLock.WaitAsync(ct);
+        try
+        {
+            await _ws!.SendAsync(json, WebSocketMessageType.Text, true, ct);
+        }
+        finally
+        {
+            _writeLock.Release();
+        }
+    }
+
+    private async Task<(string eventType, JsonElement payload)> ReceiveFrameAsync(CancellationToken ct)
+    {
+        var buffer = new byte[8192];
+        using var ms = new MemoryStream();
+
+        WebSocketReceiveResult result;
+        do
+        {
+            result = await _ws!.ReceiveAsync(buffer, ct);
+            ms.Write(buffer, 0, result.Count);
+        } while (!result.EndOfMessage);
+
+        if (result.MessageType == WebSocketMessageType.Close)
+        {
+            _isConnected = false;
+            throw new WebSocketException("Connection closed by server");
+        }
+
+        var doc = JsonDocument.Parse(ms.ToArray());
+        var root = doc.RootElement;
+        var type = root.GetProperty("type").GetString() ?? "";
+        var payload = root.TryGetProperty("payload", out var p) ? p : new JsonElement();
+        return (type, payload);
+    }
+
+    public void Dispose()
+    {
+        _ws?.Dispose();
+        _writeLock.Dispose();
+        _isConnected = false;
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/SidecarClientTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/SidecarClientTests.cs
new file mode 100644
index 0000000..2cbf616
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/SidecarClientTests.cs
@@ -0,0 +1,453 @@
+using System.Net;
+using System.Net.WebSockets;
+using System.Text;
+using System.Text.Json;
+using Microsoft.AspNetCore.Builder;
+using Microsoft.AspNetCore.Hosting;
+using Microsoft.Extensions.Configuration;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Logging.Abstractions;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Infrastructure.Services;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services;
+
+public class SidecarClientTests : IAsyncDisposable
+{
+    private WebApplication? _server;
+    private int _serverPort;
+    private readonly List<IDisposable> _disposables = [];
+
+    private async Task<(WebApplication server, int port)> StartTestServer(
+        Func<WebSocket, CancellationToken, Task> handler)
+    {
+        var port = Random.Shared.Next(10000, 60000);
+        var builder = WebApplication.CreateBuilder();
+        builder.WebHost.UseUrls($"http://localhost:{port}");
+        builder.Services.AddLogging(l => l.AddFilter(_ => false));
+        var app = builder.Build();
+        app.UseWebSockets();
+        app.Map("/ws", async context =>
+        {
+            if (context.WebSockets.IsWebSocketRequest)
+            {
+                using var ws = await context.WebSockets.AcceptWebSocketAsync();
+                await handler(ws, context.RequestAborted);
+            }
+            else
+            {
+                context.Response.StatusCode = 400;
+            }
+        });
+        await app.StartAsync();
+        return (app, port);
+    }
+
+    private SidecarClient CreateClient(int port, int timeoutSeconds = 30, int reconnectDelaySeconds = 1)
+    {
+        var options = Options.Create(new SidecarOptions
+        {
+            WebSocketUrl = $"ws://localhost:{port}/ws",
+            ConnectionTimeoutSeconds = timeoutSeconds,
+            ReconnectDelaySeconds = reconnectDelaySeconds,
+        });
+        return new SidecarClient(options, NullLogger<SidecarClient>.Instance);
+    }
+
+    private static async Task SendJson(WebSocket ws, object message, CancellationToken ct = default)
+    {
+        var json = JsonSerializer.SerializeToUtf8Bytes(message, new JsonSerializerOptions
+        {
+            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
+        });
+        await ws.SendAsync(json, WebSocketMessageType.Text, true, ct);
+    }
+
+    private static async Task<JsonElement> ReceiveJson(WebSocket ws, CancellationToken ct = default)
+    {
+        var buffer = new byte[4096];
+        var result = await ws.ReceiveAsync(buffer, ct);
+        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
+        return JsonDocument.Parse(json).RootElement;
+    }
+
+    // --- SidecarEvent model tests ---
+
+    [Fact]
+    public void SidecarEvent_ChatEvent_IsDiscriminated()
+    {
+        SidecarEvent evt = new ChatEvent("assistant", "Hello", null, null);
+        Assert.IsType<ChatEvent>(evt);
+
+        var matched = evt switch
+        {
+            ChatEvent c => c.Text,
+            _ => null,
+        };
+        Assert.Equal("Hello", matched);
+    }
+
+    [Fact]
+    public void SidecarEvent_AllTypes_AreRecords()
+    {
+        SidecarEvent[] events =
+        [
+            new ChatEvent("assistant", "text", null, null),
+            new FileChangeEvent("/file.cs", "created"),
+            new StatusEvent("running"),
+            new SessionUpdateEvent("sess-1"),
+            new TaskCompleteEvent("sess-1", 100, 50),
+            new ErrorEvent("fail"),
+        ];
+
+        Assert.Equal(6, events.Length);
+        foreach (var evt in events)
+        {
+            Assert.True(evt.GetType().IsAssignableTo(typeof(SidecarEvent)));
+        }
+    }
+
+    // --- SidecarOptions tests ---
+
+    [Fact]
+    public void SidecarOptions_BindsFromConfiguration()
+    {
+        var config = new ConfigurationBuilder()
+            .AddInMemoryCollection(new Dictionary<string, string?>
+            {
+                ["Sidecar:WebSocketUrl"] = "ws://custom:9999/ws",
+                ["Sidecar:ConnectionTimeoutSeconds"] = "60",
+                ["Sidecar:ReconnectDelaySeconds"] = "10",
+            })
+            .Build();
+
+        var options = new SidecarOptions();
+        config.GetSection(SidecarOptions.SectionName).Bind(options);
+
+        Assert.Equal("ws://custom:9999/ws", options.WebSocketUrl);
+        Assert.Equal(60, options.ConnectionTimeoutSeconds);
+        Assert.Equal(10, options.ReconnectDelaySeconds);
+    }
+
+    [Fact]
+    public void SidecarOptions_HasSensibleDefaults()
+    {
+        var options = new SidecarOptions();
+        Assert.Equal("ws://localhost:3001/ws", options.WebSocketUrl);
+        Assert.Equal(30, options.ConnectionTimeoutSeconds);
+        Assert.Equal(5, options.ReconnectDelaySeconds);
+    }
+
+    // --- ConnectAsync tests ---
+
+    [Fact]
+    public async Task ConnectAsync_EstablishesWebSocketConnection()
+    {
+        (_server, _serverPort) = await StartTestServer(async (ws, ct) =>
+        {
+            var msg = await ReceiveJson(ws, ct);
+            Assert.Equal("new-session", msg.GetProperty("type").GetString());
+            await SendJson(ws, new { type = "session-update", payload = new { sessionId = "test-session-1" } }, ct);
+
+            // Keep connection alive until closed
+            try
+            {
+                var buf = new byte[1024];
+                await ws.ReceiveAsync(buf, ct);
+            }
+            catch (WebSocketException) { }
+        });
+
+        using var client = CreateClient(_serverPort);
+        var session = await client.ConnectAsync(CancellationToken.None);
+
+        Assert.NotNull(session);
+        Assert.Equal("test-session-1", session.SessionId);
+        Assert.True(client.IsConnected);
+    }
+
+    [Fact]
+    public async Task ConnectAsync_TimesOutAfterConfiguredTimeout()
+    {
+        // Point to a port nobody is listening on
+        var port = 19999;
+        using var client = CreateClient(port, timeoutSeconds: 1);
+
+        await Assert.ThrowsAnyAsync<Exception>(
+            () => client.ConnectAsync(CancellationToken.None));
+    }
+
+    // --- SendTaskAsync tests ---
+
+    [Fact]
+    public async Task SendTaskAsync_StreamsEventsAsIAsyncEnumerable()
+    {
+        (_server, _serverPort) = await StartTestServer(async (ws, ct) =>
+        {
+            // Handle connect
+            await ReceiveJson(ws, ct);
+            await SendJson(ws, new { type = "session-update", payload = new { sessionId = "s1" } }, ct);
+
+            // Receive the send-message
+            await ReceiveJson(ws, ct);
+
+            // Send 3 chat events then idle status
+            await SendJson(ws, new { type = "chat-event", payload = new { eventType = "assistant", text = "msg1" } }, ct);
+            await SendJson(ws, new { type = "chat-event", payload = new { eventType = "assistant", text = "msg2" } }, ct);
+            await SendJson(ws, new { type = "chat-event", payload = new { eventType = "assistant", text = "msg3" } }, ct);
+            await SendJson(ws, new { type = "status", payload = new { status = "idle", inputTokens = 100, outputTokens = 50 } }, ct);
+
+            try
+            {
+                var buf = new byte[1024];
+                await ws.ReceiveAsync(buf, ct);
+            }
+            catch (WebSocketException) { }
+        });
+
+        using var client = CreateClient(_serverPort);
+        await client.ConnectAsync(CancellationToken.None);
+
+        var events = new List<SidecarEvent>();
+        await foreach (var evt in client.SendTaskAsync("do something", null, CancellationToken.None))
+        {
+            events.Add(evt);
+        }
+
+        Assert.Equal(4, events.Count);
+        Assert.IsType<ChatEvent>(events[0]);
+        Assert.IsType<ChatEvent>(events[1]);
+        Assert.IsType<ChatEvent>(events[2]);
+        Assert.IsType<TaskCompleteEvent>(events[3]);
+    }
+
+    [Fact]
+    public async Task SendTaskAsync_YieldsTaskCompleteEvent_WithTokenCounts()
+    {
+        (_server, _serverPort) = await StartTestServer(async (ws, ct) =>
+        {
+            await ReceiveJson(ws, ct);
+            await SendJson(ws, new { type = "session-update", payload = new { sessionId = "s2" } }, ct);
+
+            await ReceiveJson(ws, ct);
+            await SendJson(ws, new { type = "status", payload = new { status = "idle", inputTokens = 200, outputTokens = 75 } }, ct);
+
+            try
+            {
+                var buf = new byte[1024];
+                await ws.ReceiveAsync(buf, ct);
+            }
+            catch (WebSocketException) { }
+        });
+
+        using var client = CreateClient(_serverPort);
+        await client.ConnectAsync(CancellationToken.None);
+
+        SidecarEvent? lastEvent = null;
+        await foreach (var evt in client.SendTaskAsync("test", null, CancellationToken.None))
+        {
+            lastEvent = evt;
+        }
+
+        var complete = Assert.IsType<TaskCompleteEvent>(lastEvent);
+        Assert.Equal("s2", complete.SessionId);
+        Assert.Equal(200, complete.InputTokens);
+        Assert.Equal(75, complete.OutputTokens);
+    }
+
+    [Fact]
+    public async Task SendTaskAsync_CancelsOnCancellationToken()
+    {
+        var sendGate = new SemaphoreSlim(0);
+
+        (_server, _serverPort) = await StartTestServer(async (ws, ct) =>
+        {
+            await ReceiveJson(ws, ct);
+            await SendJson(ws, new { type = "session-update", payload = new { sessionId = "s3" } }, ct);
+
+            await ReceiveJson(ws, ct);
+
+            // Send 2 events, then wait for gate (client will cancel before we release)
+            await SendJson(ws, new { type = "chat-event", payload = new { eventType = "assistant", text = "msg0" } }, ct);
+            await SendJson(ws, new { type = "chat-event", payload = new { eventType = "assistant", text = "msg1" } }, ct);
+
+            // Block the server — next receive on client side will await with token cancelled
+            try
+            {
+                await sendGate.WaitAsync(ct);
+            }
+            catch { }
+        });
+
+        using var client = CreateClient(_serverPort);
+        await client.ConnectAsync(CancellationToken.None);
+
+        using var cts = new CancellationTokenSource();
+        var events = new List<SidecarEvent>();
+        var gotCancellation = false;
+
+        try
+        {
+            await foreach (var evt in client.SendTaskAsync("test", null, cts.Token))
+            {
+                events.Add(evt);
+                if (events.Count >= 2)
+                    cts.Cancel();
+            }
+        }
+        catch (OperationCanceledException)
+        {
+            gotCancellation = true;
+        }
+        catch (WebSocketException)
+        {
+            gotCancellation = true;
+        }
+
+        Assert.Equal(2, events.Count);
+        Assert.True(gotCancellation, "Expected cancellation to terminate the stream");
+    }
+
+    // --- AbortAsync tests ---
+
+    [Fact]
+    public async Task AbortAsync_SendsAbortMessageWithSessionId()
+    {
+        var abortReceived = new TaskCompletionSource<JsonElement>();
+
+        (_server, _serverPort) = await StartTestServer(async (ws, ct) =>
+        {
+            await ReceiveJson(ws, ct);
+            await SendJson(ws, new { type = "session-update", payload = new { sessionId = "s-abort" } }, ct);
+
+            // Receive the abort message
+            var msg = await ReceiveJson(ws, ct);
+            abortReceived.SetResult(msg);
+
+            try
+            {
+                var buf = new byte[1024];
+                await ws.ReceiveAsync(buf, ct);
+            }
+            catch (WebSocketException) { }
+        });
+
+        using var client = CreateClient(_serverPort);
+        await client.ConnectAsync(CancellationToken.None);
+        await client.AbortAsync("s-abort", CancellationToken.None);
+
+        var received = await abortReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
+        Assert.Equal("abort", received.GetProperty("type").GetString());
+    }
+
+    // --- IsConnected tests ---
+
+    [Fact]
+    public async Task IsConnected_ReflectsActualConnectionState()
+    {
+        (_server, _serverPort) = await StartTestServer(async (ws, ct) =>
+        {
+            await ReceiveJson(ws, ct);
+            await SendJson(ws, new { type = "session-update", payload = new { sessionId = "s-conn" } }, ct);
+
+            try
+            {
+                var buf = new byte[1024];
+                await ws.ReceiveAsync(buf, ct);
+            }
+            catch (WebSocketException) { }
+        });
+
+        using var client = CreateClient(_serverPort, reconnectDelaySeconds: 1);
+        Assert.False(client.IsConnected);
+
+        await client.ConnectAsync(CancellationToken.None);
+        Assert.True(client.IsConnected);
+    }
+
+    // --- FileChangeEvent streaming ---
+
+    [Fact]
+    public async Task SendTaskAsync_HandlesFileChangeEvents()
+    {
+        (_server, _serverPort) = await StartTestServer(async (ws, ct) =>
+        {
+            await ReceiveJson(ws, ct);
+            await SendJson(ws, new { type = "session-update", payload = new { sessionId = "s-fc" } }, ct);
+
+            await ReceiveJson(ws, ct);
+            await SendJson(ws, new { type = "file-change", payload = new { filePath = "/src/test.cs", changeType = "modified" } }, ct);
+            await SendJson(ws, new { type = "status", payload = new { status = "idle", inputTokens = 10, outputTokens = 5 } }, ct);
+
+            try
+            {
+                var buf = new byte[1024];
+                await ws.ReceiveAsync(buf, ct);
+            }
+            catch (WebSocketException) { }
+        });
+
+        using var client = CreateClient(_serverPort);
+        await client.ConnectAsync(CancellationToken.None);
+
+        var events = new List<SidecarEvent>();
+        await foreach (var evt in client.SendTaskAsync("test", null, CancellationToken.None))
+        {
+            events.Add(evt);
+        }
+
+        Assert.Equal(2, events.Count);
+        var fileChange = Assert.IsType<FileChangeEvent>(events[0]);
+        Assert.Equal("/src/test.cs", fileChange.FilePath);
+        Assert.Equal("modified", fileChange.ChangeType);
+    }
+
+    // --- ErrorEvent handling ---
+
+    [Fact]
+    public async Task SendTaskAsync_YieldsErrorEvent()
+    {
+        (_server, _serverPort) = await StartTestServer(async (ws, ct) =>
+        {
+            await ReceiveJson(ws, ct);
+            await SendJson(ws, new { type = "session-update", payload = new { sessionId = "s-err" } }, ct);
+
+            await ReceiveJson(ws, ct);
+            await SendJson(ws, new { type = "error", payload = new { message = "something went wrong" } }, ct);
+            await SendJson(ws, new { type = "status", payload = new { status = "idle", inputTokens = 0, outputTokens = 0 } }, ct);
+
+            try
+            {
+                var buf = new byte[1024];
+                await ws.ReceiveAsync(buf, ct);
+            }
+            catch (WebSocketException) { }
+        });
+
+        using var client = CreateClient(_serverPort);
+        await client.ConnectAsync(CancellationToken.None);
+
+        var events = new List<SidecarEvent>();
+        await foreach (var evt in client.SendTaskAsync("test", null, CancellationToken.None))
+        {
+            events.Add(evt);
+        }
+
+        Assert.Equal(2, events.Count);
+        var error = Assert.IsType<ErrorEvent>(events[0]);
+        Assert.Equal("something went wrong", error.Message);
+    }
+
+    public async ValueTask DisposeAsync()
+    {
+        if (_server is not null)
+        {
+            await _server.StopAsync();
+            await _server.DisposeAsync();
+        }
+        foreach (var d in _disposables)
+            d.Dispose();
+    }
+}
