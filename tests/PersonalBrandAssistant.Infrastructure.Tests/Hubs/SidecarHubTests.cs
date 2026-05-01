using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using PersonalBrandAssistant.Api.Hubs;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Infrastructure.Tests.Hubs;

public class SidecarHubTests
{
    private readonly Mock<ISidecarClient> _sidecarClient = new();
    private readonly Mock<ILogger<SidecarHub>> _logger = new();
    private readonly Mock<IHubCallerClients<ISidecarHubClient>> _clients = new();
    private readonly Mock<ISidecarHubClient> _callerClient = new();
    private readonly Mock<HubCallerContext> _hubContext = new();
    private readonly string _connectionId = Guid.NewGuid().ToString("N");

    private SidecarHub CreateHub()
    {
        _hubContext.Setup(c => c.ConnectionId).Returns(_connectionId);
        _clients.Setup(c => c.Caller).Returns(_callerClient.Object);
        _clients.Setup(c => c.Client(_connectionId)).Returns(_callerClient.Object);

        var hub = new SidecarHub(_sidecarClient.Object, _logger.Object)
        {
            Context = _hubContext.Object,
            Clients = _clients.Object
        };
        return hub;
    }

    private void SetupStreamWith(params SidecarEvent[] events)
    {
        _sidecarClient.Setup(s => s.SendTaskAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(events));
    }

    private static async IAsyncEnumerable<SidecarEvent> ToAsyncEnumerable(
        SidecarEvent[] events, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var evt in events)
        {
            ct.ThrowIfCancellationRequested();
            yield return evt;
            await Task.Yield();
        }
    }

    [Fact]
    public async Task SendMessage_InvokesTypingStarted()
    {
        SetupStreamWith(new TaskCompleteEvent("s1", 10, 20, Cost: 0.01m));
        var hub = CreateHub();

        await hub.SendMessage("editor:/content/123", "Hello");
        await Task.Delay(200);

        _callerClient.Verify(c => c.TypingStarted(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task SendMessage_StreamsTokensAndCompletes()
    {
        SetupStreamWith(
            new ChatEvent("text", "Hello ", null, null),
            new ChatEvent("text", "world", null, null),
            new TaskCompleteEvent("s1", 10, 20, Cost: 0.01m));

        var hub = CreateHub();
        await hub.SendMessage("editor", "test");
        await Task.Delay(500);

        _callerClient.Verify(c => c.StreamComplete(It.IsAny<string>(), true, "Hello world"), Times.Once);
        _callerClient.Verify(c => c.TokenUsage(It.IsAny<string>(), 10, 20, 0.01m), Times.Once);
    }

    [Fact]
    public async Task SendMessage_RejectsWhenStreamAlreadyActive()
    {
        var tcs = new TaskCompletionSource();
        _sidecarClient.Setup(s => s.SendTaskAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(BlockingStream(tcs.Task));

        var hub = CreateHub();
        await hub.SendMessage("ctx", "first");
        await Task.Delay(100);

        await hub.SendMessage("ctx", "second");

        _callerClient.Verify(c => c.StreamError("", "A stream is already active for this connection"), Times.Once);
        tcs.SetResult();
    }

    [Fact]
    public async Task SendMessage_HandlesErrorEvent()
    {
        SetupStreamWith(
            new ChatEvent("text", "partial", null, null),
            new ErrorEvent("Something went wrong"));

        var hub = CreateHub();
        await hub.SendMessage("ctx", "test");
        await Task.Delay(300);

        _callerClient.Verify(c => c.StreamError(It.IsAny<string>(), "Something went wrong"), Times.Once);
    }

    [Fact]
    public async Task CancelStream_CancelsAndNotifiesClient()
    {
        var tcs = new TaskCompletionSource();
        _sidecarClient.Setup(s => s.SendTaskAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(BlockingStream(tcs.Task));

        var hub = CreateHub();
        await hub.SendMessage("ctx", "msg");
        await Task.Delay(100);

        _callerClient.Verify(c => c.TypingStarted(It.IsAny<string>()), Times.Once);
        var streamId = CaptureStreamId();

        await hub.CancelStream(streamId);

        _callerClient.Verify(c => c.StreamError(streamId, "Stream cancelled by client"), Times.Once);
        tcs.SetResult();
    }

    [Fact]
    public async Task OnDisconnectedAsync_CleansUpActiveStreams()
    {
        var tcs = new TaskCompletionSource();
        _sidecarClient.Setup(s => s.SendTaskAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(BlockingStream(tcs.Task));

        var hub = CreateHub();
        await hub.SendMessage("ctx", "msg");
        await Task.Delay(100);

        await hub.OnDisconnectedAsync(null);
        tcs.SetResult();
        await Task.Delay(100);

        await hub.SendMessage("ctx", "new msg after disconnect");
        _callerClient.Verify(c => c.TypingStarted(It.IsAny<string>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ApplyDraft_LogsWithRouteContext()
    {
        SetupStreamWith(new TaskCompleteEvent("s1", 5, 10, Cost: 0m));
        var hub = CreateHub();
        await hub.SendMessage("editor:/content/abc", "test");
        await Task.Delay(200);

        var streamId = CaptureStreamId();
        await hub.ApplyDraft(streamId);

        _logger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Draft applied")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private string CaptureStreamId()
    {
        var streamId = string.Empty;
        _callerClient.Verify(c => c.TypingStarted(It.IsAny<string>()));
        _callerClient.Invocations
            .Where(i => i.Method.Name == nameof(ISidecarHubClient.TypingStarted))
            .ToList()
            .ForEach(i => streamId = (string)i.Arguments[0]);
        return streamId;
    }

    private static async IAsyncEnumerable<SidecarEvent> BlockingStream(
        Task blockUntil, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new ChatEvent("text", "waiting...", null, null);
        try { await blockUntil.WaitAsync(ct); } catch (OperationCanceledException) { yield break; }
    }
}
