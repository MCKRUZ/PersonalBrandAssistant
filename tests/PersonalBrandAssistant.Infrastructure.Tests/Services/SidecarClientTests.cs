using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Infrastructure.Services;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services;

public class SidecarClientTests : IAsyncDisposable
{
    private WebApplication? _server;
    private int _serverPort;
    private readonly List<IDisposable> _disposables = [];

    private async Task<(WebApplication server, int port)> StartTestServer(
        Func<WebSocket, CancellationToken, Task> handler)
    {
        var port = Random.Shared.Next(10000, 60000);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://localhost:{port}");
        builder.Services.AddLogging(l => l.AddFilter(_ => false));
        var app = builder.Build();
        app.UseWebSockets();
        app.Map("/ws", async context =>
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                using var ws = await context.WebSockets.AcceptWebSocketAsync();
                await handler(ws, context.RequestAborted);
            }
            else
            {
                context.Response.StatusCode = 400;
            }
        });
        await app.StartAsync();
        return (app, port);
    }

    private SidecarClient CreateClient(int port, int timeoutSeconds = 30, int reconnectDelaySeconds = 1)
    {
        var options = Options.Create(new SidecarOptions
        {
            WebSocketUrl = $"ws://localhost:{port}/ws",
            ConnectionTimeoutSeconds = timeoutSeconds,
            ReconnectDelaySeconds = reconnectDelaySeconds,
        });
        return new SidecarClient(options, NullLogger<SidecarClient>.Instance);
    }

    private static async Task SendJson(WebSocket ws, object message, CancellationToken ct = default)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        await ws.SendAsync(json, WebSocketMessageType.Text, true, ct);
    }

    private static async Task<JsonElement> ReceiveJson(WebSocket ws, CancellationToken ct = default)
    {
        var buffer = new byte[4096];
        var result = await ws.ReceiveAsync(buffer, ct);
        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        return JsonDocument.Parse(json).RootElement;
    }

    // --- SidecarEvent model tests ---

    [Fact]
    public void SidecarEvent_ChatEvent_IsDiscriminated()
    {
        SidecarEvent evt = new ChatEvent("assistant", "Hello", null, null);
        Assert.IsType<ChatEvent>(evt);

        var matched = evt switch
        {
            ChatEvent c => c.Text,
            _ => null,
        };
        Assert.Equal("Hello", matched);
    }

    [Fact]
    public void SidecarEvent_AllTypes_AreRecords()
    {
        SidecarEvent[] events =
        [
            new ChatEvent("assistant", "text", null, null),
            new FileChangeEvent("/file.cs", "created"),
            new StatusEvent("running"),
            new SessionUpdateEvent("sess-1"),
            new TaskCompleteEvent("sess-1", 100, 50),
            new ErrorEvent("fail"),
        ];

        Assert.Equal(6, events.Length);
        foreach (var evt in events)
        {
            Assert.True(evt.GetType().IsAssignableTo(typeof(SidecarEvent)));
        }
    }

    // --- SidecarOptions tests ---

    [Fact]
    public void SidecarOptions_BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sidecar:WebSocketUrl"] = "ws://custom:9999/ws",
                ["Sidecar:ConnectionTimeoutSeconds"] = "60",
                ["Sidecar:ReconnectDelaySeconds"] = "10",
            })
            .Build();

        var options = new SidecarOptions();
        config.GetSection(SidecarOptions.SectionName).Bind(options);

        Assert.Equal("ws://custom:9999/ws", options.WebSocketUrl);
        Assert.Equal(60, options.ConnectionTimeoutSeconds);
        Assert.Equal(10, options.ReconnectDelaySeconds);
    }

    [Fact]
    public void SidecarOptions_HasSensibleDefaults()
    {
        var options = new SidecarOptions();
        Assert.Equal("ws://localhost:3001/ws", options.WebSocketUrl);
        Assert.Equal(30, options.ConnectionTimeoutSeconds);
        Assert.Equal(5, options.ReconnectDelaySeconds);
    }

    // --- ConnectAsync tests ---

    [Fact]
    public async Task ConnectAsync_EstablishesWebSocketConnection()
    {
        (_server, _serverPort) = await StartTestServer(async (ws, ct) =>
        {
            var msg = await ReceiveJson(ws, ct);
            Assert.Equal("new-session", msg.GetProperty("type").GetString());
            await SendJson(ws, new { type = "session-update", payload = new { sessionId = "test-session-1" } }, ct);

            // Keep connection alive until closed
            try
            {
                var buf = new byte[1024];
                await ws.ReceiveAsync(buf, ct);
            }
            catch (WebSocketException) { }
        });

        using var client = CreateClient(_serverPort);
        var session = await client.ConnectAsync(CancellationToken.None);

        Assert.NotNull(session);
        Assert.Equal("test-session-1", session.SessionId);
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_TimesOutAfterConfiguredTimeout()
    {
        // Point to a port nobody is listening on
        var port = 19999;
        using var client = CreateClient(port, timeoutSeconds: 1);

        await Assert.ThrowsAnyAsync<Exception>(
            () => client.ConnectAsync(CancellationToken.None));
    }

    // --- SendTaskAsync tests ---

    [Fact]
    public async Task SendTaskAsync_StreamsEventsAsIAsyncEnumerable()
    {
        (_server, _serverPort) = await StartTestServer(async (ws, ct) =>
        {
            // Handle connect
            await ReceiveJson(ws, ct);
            await SendJson(ws, new { type = "session-update", payload = new { sessionId = "s1" } }, ct);

            // Receive the send-message
            await ReceiveJson(ws, ct);

            // Send 3 chat events then idle status
            await SendJson(ws, new { type = "chat-event", payload = new { eventType = "assistant", text = "msg1" } }, ct);
            await SendJson(ws, new { type = "chat-event", payload = new { eventType = "assistant", text = "msg2" } }, ct);
            await SendJson(ws, new { type = "chat-event", payload = new { eventType = "assistant", text = "msg3" } }, ct);
            await SendJson(ws, new { type = "status", payload = new { status = "idle", inputTokens = 100, outputTokens = 50 } }, ct);

            try
            {
                var buf = new byte[1024];
                await ws.ReceiveAsync(buf, ct);
            }
            catch (WebSocketException) { }
        });

        using var client = CreateClient(_serverPort);
        await client.ConnectAsync(CancellationToken.None);

        var events = new List<SidecarEvent>();
        await foreach (var evt in client.SendTaskAsync("do something", null, null, CancellationToken.None))
        {
            events.Add(evt);
        }

        Assert.Equal(4, events.Count);
        Assert.IsType<ChatEvent>(events[0]);
        Assert.IsType<ChatEvent>(events[1]);
        Assert.IsType<ChatEvent>(events[2]);
        Assert.IsType<TaskCompleteEvent>(events[3]);
    }

    [Fact]
    public async Task SendTaskAsync_YieldsTaskCompleteEvent_WithTokenCounts()
    {
        (_server, _serverPort) = await StartTestServer(async (ws, ct) =>
        {
            await ReceiveJson(ws, ct);
            await SendJson(ws, new { type = "session-update", payload = new { sessionId = "s2" } }, ct);

            await ReceiveJson(ws, ct);
            await SendJson(ws, new { type = "status", payload = new { status = "idle", inputTokens = 200, outputTokens = 75 } }, ct);

            try
            {
                var buf = new byte[1024];
                await ws.ReceiveAsync(buf, ct);
            }
            catch (WebSocketException) { }
        });

        using var client = CreateClient(_serverPort);
        await client.ConnectAsync(CancellationToken.None);

        SidecarEvent? lastEvent = null;
        await foreach (var evt in client.SendTaskAsync("test", null, null, CancellationToken.None))
        {
            lastEvent = evt;
        }

        var complete = Assert.IsType<TaskCompleteEvent>(lastEvent);
        Assert.Equal("s2", complete.SessionId);
        Assert.Equal(200, complete.InputTokens);
        Assert.Equal(75, complete.OutputTokens);
    }

    [Fact]
    public async Task SendTaskAsync_CancelsOnCancellationToken()
    {
        var sendGate = new SemaphoreSlim(0);

        (_server, _serverPort) = await StartTestServer(async (ws, ct) =>
        {
            await ReceiveJson(ws, ct);
            await SendJson(ws, new { type = "session-update", payload = new { sessionId = "s3" } }, ct);

            await ReceiveJson(ws, ct);

            // Send 2 events, then wait for gate (client will cancel before we release)
            await SendJson(ws, new { type = "chat-event", payload = new { eventType = "assistant", text = "msg0" } }, ct);
            await SendJson(ws, new { type = "chat-event", payload = new { eventType = "assistant", text = "msg1" } }, ct);

            // Block the server — next receive on client side will await with token cancelled
            try
            {
                await sendGate.WaitAsync(ct);
            }
            catch { }
        });

        using var client = CreateClient(_serverPort);
        await client.ConnectAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var events = new List<SidecarEvent>();
        var gotCancellation = false;

        try
        {
            await foreach (var evt in client.SendTaskAsync("test", null, null, cts.Token))
            {
                events.Add(evt);
                if (events.Count >= 2)
                    cts.Cancel();
            }
        }
        catch (OperationCanceledException)
        {
            gotCancellation = true;
        }
        catch (WebSocketException)
        {
            gotCancellation = true;
        }

        Assert.Equal(2, events.Count);
        Assert.True(gotCancellation, "Expected cancellation to terminate the stream");
    }

    // --- AbortAsync tests ---

    [Fact]
    public async Task AbortAsync_SendsAbortMessageWithSessionId()
    {
        var abortReceived = new TaskCompletionSource<JsonElement>();

        (_server, _serverPort) = await StartTestServer(async (ws, ct) =>
        {
            await ReceiveJson(ws, ct);
            await SendJson(ws, new { type = "session-update", payload = new { sessionId = "s-abort" } }, ct);

            // Receive the abort message
            var msg = await ReceiveJson(ws, ct);
            abortReceived.SetResult(msg);

            try
            {
                var buf = new byte[1024];
                await ws.ReceiveAsync(buf, ct);
            }
            catch (WebSocketException) { }
        });

        using var client = CreateClient(_serverPort);
        await client.ConnectAsync(CancellationToken.None);
        await client.AbortAsync("s-abort", CancellationToken.None);

        var received = await abortReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("abort", received.GetProperty("type").GetString());
    }

    // --- IsConnected tests ---

    [Fact]
    public async Task IsConnected_ReflectsActualConnectionState()
    {
        (_server, _serverPort) = await StartTestServer(async (ws, ct) =>
        {
            await ReceiveJson(ws, ct);
            await SendJson(ws, new { type = "session-update", payload = new { sessionId = "s-conn" } }, ct);

            try
            {
                var buf = new byte[1024];
                await ws.ReceiveAsync(buf, ct);
            }
            catch (WebSocketException) { }
        });

        using var client = CreateClient(_serverPort, reconnectDelaySeconds: 1);
        Assert.False(client.IsConnected);

        await client.ConnectAsync(CancellationToken.None);
        Assert.True(client.IsConnected);
    }

    // --- FileChangeEvent streaming ---

    [Fact]
    public async Task SendTaskAsync_HandlesFileChangeEvents()
    {
        (_server, _serverPort) = await StartTestServer(async (ws, ct) =>
        {
            await ReceiveJson(ws, ct);
            await SendJson(ws, new { type = "session-update", payload = new { sessionId = "s-fc" } }, ct);

            await ReceiveJson(ws, ct);
            await SendJson(ws, new { type = "file-change", payload = new { filePath = "/src/test.cs", changeType = "modified" } }, ct);
            await SendJson(ws, new { type = "status", payload = new { status = "idle", inputTokens = 10, outputTokens = 5 } }, ct);

            try
            {
                var buf = new byte[1024];
                await ws.ReceiveAsync(buf, ct);
            }
            catch (WebSocketException) { }
        });

        using var client = CreateClient(_serverPort);
        await client.ConnectAsync(CancellationToken.None);

        var events = new List<SidecarEvent>();
        await foreach (var evt in client.SendTaskAsync("test", null, null, CancellationToken.None))
        {
            events.Add(evt);
        }

        Assert.Equal(2, events.Count);
        var fileChange = Assert.IsType<FileChangeEvent>(events[0]);
        Assert.Equal("/src/test.cs", fileChange.FilePath);
        Assert.Equal("modified", fileChange.ChangeType);
    }

    // --- ErrorEvent handling ---

    [Fact]
    public async Task SendTaskAsync_YieldsErrorEvent()
    {
        (_server, _serverPort) = await StartTestServer(async (ws, ct) =>
        {
            await ReceiveJson(ws, ct);
            await SendJson(ws, new { type = "session-update", payload = new { sessionId = "s-err" } }, ct);

            await ReceiveJson(ws, ct);
            await SendJson(ws, new { type = "error", payload = new { message = "something went wrong" } }, ct);
            await SendJson(ws, new { type = "status", payload = new { status = "idle", inputTokens = 0, outputTokens = 0 } }, ct);

            try
            {
                var buf = new byte[1024];
                await ws.ReceiveAsync(buf, ct);
            }
            catch (WebSocketException) { }
        });

        using var client = CreateClient(_serverPort);
        await client.ConnectAsync(CancellationToken.None);

        var events = new List<SidecarEvent>();
        await foreach (var evt in client.SendTaskAsync("test", null, null, CancellationToken.None))
        {
            events.Add(evt);
        }

        Assert.Equal(2, events.Count);
        var error = Assert.IsType<ErrorEvent>(events[0]);
        Assert.Equal("something went wrong", error.Message);
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.StopAsync();
            await _server.DisposeAsync();
        }
        foreach (var d in _disposables)
            d.Dispose();
    }
}
