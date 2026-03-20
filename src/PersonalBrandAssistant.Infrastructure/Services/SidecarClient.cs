using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Services;

public sealed class SidecarClient : ISidecarClient, IDisposable
{
    private const int MaxMessageSize = 1_048_576; // 1 MB

    private readonly SidecarOptions _options;
    private readonly ILogger<SidecarClient> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private ClientWebSocket? _ws;
    private string? _currentSessionId;
    private volatile bool _isConnected;
    private int _activeStream; // 0 = idle, 1 = streaming

    public SidecarClient(IOptions<SidecarOptions> options, ILogger<SidecarClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConnected => _isConnected;

    public async Task<SidecarSession> ConnectAsync(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _activeStream, 0, 0) == 1)
            throw new InvalidOperationException("Cannot reconnect while a stream is active");

        _isConnected = false;
        _ws?.Dispose();
        _ws = new ClientWebSocket();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.ConnectionTimeoutSeconds));

        await _ws.ConnectAsync(new Uri(_options.WebSocketUrl), timeoutCts.Token);

        await SendMessageAsync(new { type = "new-session" }, timeoutCts.Token);

        // Wait for session-update response, skipping non-session events (e.g. config)
        string eventType;
        JsonElement payload;
        do
        {
            (eventType, payload) = await ReceiveFrameAsync(timeoutCts.Token);
        } while (eventType != "session-update");

        _currentSessionId = payload.GetProperty("sessionId").GetString()
            ?? throw new InvalidOperationException("session-update missing sessionId");

        _isConnected = true;
        _logger.LogInformation("Connected to sidecar, session {SessionId}", _currentSessionId);
        return new SidecarSession(_currentSessionId, DateTimeOffset.UtcNow);
    }

    public async IAsyncEnumerable<SidecarEvent> SendTaskAsync(
        string task,
        string? systemPrompt,
        string? sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (_ws is null || _ws.State != WebSocketState.Open)
            throw new InvalidOperationException("Not connected to sidecar");

        if (Interlocked.CompareExchange(ref _activeStream, 1, 0) != 0)
            throw new InvalidOperationException("Only one concurrent stream is supported");

        try
        {
            object message = (systemPrompt, sessionId) switch
            {
                (not null, not null) => new { type = "send-message", payload = new { message = task, systemPrompt, sessionId } },
                (not null, null) => new { type = "send-message", payload = new { message = task, systemPrompt } },
                (null, not null) => new { type = "send-message", payload = new { message = task, sessionId } },
                _ => new { type = "send-message", payload = new { message = task } },
            };

            await SendMessageAsync(message, ct);

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var (eventType, payload) = await ReceiveFrameAsync(ct);

                switch (eventType)
                {
                    case "chat-event":
                        yield return new ChatEvent(
                            payload.TryGetProperty("type", out var et) ? et.GetString() ?? "" : "",
                            payload.TryGetProperty("content", out var txt) ? txt.GetString() : null,
                            payload.TryGetProperty("filePath", out var fp) ? fp.GetString() : null,
                            payload.TryGetProperty("toolName", out var tn) ? tn.GetString() : null);
                        break;

                    case "file-change":
                        yield return new FileChangeEvent(
                            payload.GetProperty("filePath").GetString() ?? "",
                            payload.GetProperty("changeType").GetString() ?? "");
                        break;

                    case "status":
                        var status = payload.GetProperty("status").GetString();
                        if (status == "idle")
                        {
                            var inputTokens = payload.TryGetProperty("inputTokens", out var it) ? it.GetInt32() : 0;
                            var outputTokens = payload.TryGetProperty("outputTokens", out var ot) ? ot.GetInt32() : 0;
                            var cacheReadTokens = payload.TryGetProperty("cacheReadTokens", out var crt) ? crt.GetInt32() : 0;
                            var cacheCreationTokens = payload.TryGetProperty("cacheCreationTokens", out var cct) ? cct.GetInt32() : 0;
                            var cost = payload.TryGetProperty("cost", out var c) ? c.GetDecimal() : 0m;
                            yield return new TaskCompleteEvent(
                                _currentSessionId ?? "",
                                inputTokens,
                                outputTokens,
                                cacheReadTokens,
                                cacheCreationTokens,
                                cost);
                            yield break;
                        }
                        yield return new StatusEvent(status ?? "unknown");
                        break;

                    case "session-update":
                        var sid = payload.GetProperty("sessionId").GetString() ?? "";
                        _currentSessionId = sid;
                        yield return new SessionUpdateEvent(sid);
                        break;

                    case "error":
                        yield return new ErrorEvent(
                            payload.TryGetProperty("message", out var msg) ? msg.GetString() ?? "Unknown error" : "Unknown error");
                        break;

                    default:
                        _logger.LogWarning("Unknown sidecar event type: {EventType}", eventType);
                        break;
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _activeStream, 0);
        }
    }

    public async Task AbortAsync(string? sessionId, CancellationToken ct)
    {
        if (_ws is null || _ws.State != WebSocketState.Open)
            throw new InvalidOperationException("Not connected to sidecar");

        await SendMessageAsync(new { type = "abort", sessionId = sessionId ?? _currentSessionId }, ct);
    }

    private async Task SendMessageAsync(object message, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, _jsonOptions);
        await _writeLock.WaitAsync(ct);
        try
        {
            await _ws!.SendAsync(json, WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<(string eventType, JsonElement payload)> ReceiveFrameAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();

        WebSocketReceiveResult result;
        do
        {
            result = await _ws!.ReceiveAsync(buffer, ct);
            ms.Write(buffer, 0, result.Count);
            if (ms.Length > MaxMessageSize)
                throw new InvalidOperationException($"WebSocket message exceeds {MaxMessageSize} byte limit");
        } while (!result.EndOfMessage);

        if (result.MessageType == WebSocketMessageType.Close)
        {
            _isConnected = false;
            throw new WebSocketException("Connection closed by server");
        }

        using var doc = JsonDocument.Parse(ms.ToArray());
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString() ?? "";
        var payload = root.TryGetProperty("payload", out var p) ? p.Clone() : default;
        return (type, payload);
    }

    public void Dispose()
    {
        _ws?.Dispose();
        _writeLock.Dispose();
        _isConnected = false;
    }
}
