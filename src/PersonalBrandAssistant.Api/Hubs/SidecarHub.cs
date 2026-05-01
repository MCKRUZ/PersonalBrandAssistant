using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Api.Hubs;

public sealed class SidecarHub : Hub<ISidecarHubClient>
{
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> ActiveStreams = new();
    private static readonly ConcurrentDictionary<string, string> StreamRouteContexts = new();

    private readonly ISidecarClient _sidecarClient;
    private readonly ILogger<SidecarHub> _logger;

    public SidecarHub(ISidecarClient sidecarClient, ILogger<SidecarHub> logger)
    {
        _sidecarClient = sidecarClient;
        _logger = logger;
    }

    public async Task SendMessage(string routeContext, string message)
    {
        var connectionId = Context.ConnectionId;

        if (HasActiveStream(connectionId))
        {
            await Clients.Caller.StreamError("", "A stream is already active for this connection");
            return;
        }

        var streamId = Guid.NewGuid().ToString("N");
        var cts = new CancellationTokenSource();
        var key = BuildKey(connectionId, streamId);
        ActiveStreams[key] = cts;
        StreamRouteContexts[key] = routeContext;

        await Clients.Caller.TypingStarted(streamId);

        _ = Task.Run(() => StreamTokensAsync(connectionId, streamId, routeContext, message, cts), CancellationToken.None);
    }

    public async Task CancelStream(string streamId)
    {
        var key = BuildKey(Context.ConnectionId, streamId);
        if (ActiveStreams.TryRemove(key, out var cts))
        {
            await cts.CancelAsync();
            cts.Dispose();
            StreamRouteContexts.TryRemove(key, out _);
            await Clients.Caller.StreamError(streamId, "Stream cancelled by client");
        }
    }

    public Task ApplyDraft(string streamId)
    {
        var key = BuildKey(Context.ConnectionId, streamId);
        if (StreamRouteContexts.TryGetValue(key, out var routeContext))
        {
            _logger.LogInformation("Draft applied: streamId={StreamId}, routeContext={RouteContext}", streamId, routeContext);
        }
        return Task.CompletedTask;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var prefix = Context.ConnectionId + ":";
        foreach (var kvp in ActiveStreams)
        {
            if (kvp.Key.StartsWith(prefix, StringComparison.Ordinal) && ActiveStreams.TryRemove(kvp.Key, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                StreamRouteContexts.TryRemove(kvp.Key, out _);
            }
        }
        return base.OnDisconnectedAsync(exception);
    }

    private async Task StreamTokensAsync(string connectionId, string streamId, string routeContext, string message, CancellationTokenSource cts)
    {
        var buffer = new List<string>();
        var fullText = new System.Text.StringBuilder();
        var sw = Stopwatch.StartNew();
        var key = BuildKey(connectionId, streamId);

        try
        {
            await foreach (var evt in _sidecarClient.SendTaskAsync(message, null, null, null, cts.Token))
            {
                if (cts.Token.IsCancellationRequested) break;

                switch (evt)
                {
                    case ChatEvent { Text: not null } chat:
                        buffer.Add(chat.Text);
                        fullText.Append(chat.Text);
                        if (sw.ElapsedMilliseconds >= 100)
                        {
                            await Clients.Client(connectionId).ReceiveTokenBatch(streamId, buffer.ToList());
                            buffer.Clear();
                            sw.Restart();
                        }
                        break;

                    case TaskCompleteEvent complete:
                        if (buffer.Count > 0)
                        {
                            await Clients.Client(connectionId).ReceiveTokenBatch(streamId, buffer.ToList());
                            buffer.Clear();
                        }
                        await Clients.Client(connectionId).StreamComplete(streamId, true, fullText.ToString());
                        await Clients.Client(connectionId).TokenUsage(streamId, complete.InputTokens, complete.OutputTokens, complete.Cost);
                        return;

                    case ErrorEvent error:
                        if (buffer.Count > 0)
                        {
                            await Clients.Client(connectionId).ReceiveTokenBatch(streamId, buffer.ToList());
                            buffer.Clear();
                        }
                        await Clients.Client(connectionId).StreamError(streamId, error.Message);
                        return;
                }
            }

            if (buffer.Count > 0)
                await Clients.Client(connectionId).ReceiveTokenBatch(streamId, buffer.ToList());

            await Clients.Client(connectionId).StreamComplete(streamId, true, fullText.ToString());
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Stream {StreamId} cancelled", streamId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stream {StreamId} failed", streamId);
            try { await Clients.Client(connectionId).StreamError(streamId, "An internal error occurred"); } catch { /* connection may be gone */ }
        }
        finally
        {
            if (ActiveStreams.TryRemove(key, out var removed))
                removed.Dispose();
        }
    }

    private bool HasActiveStream(string connectionId)
    {
        var prefix = connectionId + ":";
        return ActiveStreams.Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static string BuildKey(string connectionId, string streamId) => $"{connectionId}:{streamId}";
}
