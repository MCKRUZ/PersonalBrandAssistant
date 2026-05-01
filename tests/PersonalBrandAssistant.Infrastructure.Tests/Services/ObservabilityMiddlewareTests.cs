using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Infrastructure.Agents;
using PersonalBrandAssistant.Infrastructure.Services;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services;

[Collection("ObservabilityTests")]
public class ObservabilityMiddlewareTests : IDisposable
{
    private readonly Mock<ISidecarClient> _inner = new();
    private readonly ObservabilityMiddleware _middleware;
    private readonly ConcurrentBag<Activity> _stoppedActivities = [];
    private readonly ActivityListener _listener;

    public ObservabilityMiddlewareTests()
    {
        _middleware = new ObservabilityMiddleware(_inner.Object);

        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AgentTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => _stoppedActivities.Add(a)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    // --- Delegation ---

    [Fact]
    public async Task SendTaskAsync_DelegatesToInnerClient()
    {
        SetupInner([new TaskCompleteEvent("s", 1, 1)]);

        await foreach (var _ in _middleware.SendTaskAsync("task", null, null, null, CancellationToken.None)) { }

        _inner.Verify(c => c.SendTaskAsync("task", null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConnectAsync_DelegatesToInnerClient()
    {
        var session = new SidecarSession("s1", DateTimeOffset.UtcNow);
        _inner.Setup(c => c.ConnectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var result = await _middleware.ConnectAsync(CancellationToken.None);

        Assert.Equal(session, result);
        _inner.Verify(c => c.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void IsConnected_DelegatesToInnerClient()
    {
        _inner.SetupGet(c => c.IsConnected).Returns(true);

        Assert.True(_middleware.IsConnected);
    }

    // --- Span creation ---

    [Fact]
    public async Task SendTaskAsync_StartsActivity_WithCorrectSpanName()
    {
        SetupInner([new TaskCompleteEvent("s", 1, 1)]);

        await foreach (var _ in _middleware.SendTaskAsync("task", "sys", null, null, CancellationToken.None)) { }

        Assert.Contains(_stoppedActivities, a => a.OperationName == "sidecar.send_task");
    }

    [Fact]
    public async Task SendTaskAsync_SetsTokenAttributes_FromTaskCompleteEvent()
    {
        SetupInner([new TaskCompleteEvent("s", InputTokens: 100, OutputTokens: 50, CacheReadTokens: 25, Cost: 0.005m)]);

        await foreach (var _ in _middleware.SendTaskAsync("task", null, null, null, CancellationToken.None)) { }

        var activity = _stoppedActivities.Single(a => a.OperationName == "sidecar.send_task");
        var tags = activity.TagObjects.ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.Equal(100, tags["gen_ai.usage.input_tokens"]);
        Assert.Equal(50, tags["gen_ai.usage.output_tokens"]);
        Assert.Equal(25, tags["gen_ai.usage.cache_read_tokens"]);
        Assert.Equal(0.005m, tags["cost_usd"]);
    }

    [Theory]
    [InlineData("system prompt text", true)]
    [InlineData(null, false)]
    public async Task SendTaskAsync_SetsHasSystemPromptAttribute(string? prompt, bool expected)
    {
        SetupInner([new TaskCompleteEvent("s", 1, 1)]);

        await foreach (var _ in _middleware.SendTaskAsync("task", prompt, null, null, CancellationToken.None)) { }

        var activity = _stoppedActivities.Single(a => a.OperationName == "sidecar.send_task");
        var tag = activity.TagObjects.Single(kv => kv.Key == "has_system_prompt").Value;
        Assert.Equal(expected, tag);
    }

    // --- Span lifecycle ---

    [Fact]
    public async Task SendTaskAsync_ConsumerBreaksEarly_SpanIsClosed()
    {
        SetupInner([new ChatEvent("assistant", "hello", null, null), new TaskCompleteEvent("s", 1, 1)]);

        await foreach (var _ in _middleware.SendTaskAsync("task", null, null, null, CancellationToken.None))
        {
            break; // consume only first event then break
        }

        await Task.Yield(); // allow state machine finalization
        Assert.Contains(_stoppedActivities, a => a.OperationName == "sidecar.send_task");
    }

    [Fact]
    public async Task SendTaskAsync_CancellationToken_SpanIsClosedWithCancellation()
    {
        using var cts = new CancellationTokenSource();
        _inner.Setup(c => c.SendTaskAsync(It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(BlockingUntilCancelledEnumerable(cts.Token));

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in _middleware.SendTaskAsync("task", null, null, null, cts.Token)) { }
        });

        Assert.Contains(_stoppedActivities, a => a.OperationName == "sidecar.send_task");
    }

    [Fact]
    public async Task SendTaskAsync_InnerThrows_SpanIsClosedWithError()
    {
        _inner.Setup(c => c.SendTaskAsync(It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowingEnumerable());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in _middleware.SendTaskAsync("task", null, null, null, CancellationToken.None)) { }
        });

        var activity = _stoppedActivities.Single(a => a.OperationName == "sidecar.send_task");
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("sidecar error", activity.StatusDescription);
    }

    // --- Privacy ---

    [Fact]
    public async Task SendTaskAsync_NoPromptContentInSpanAttributes()
    {
        const string secretPrompt = "CONFIDENTIAL_SYSTEM_PROMPT_CONTENT";
        SetupInner([new TaskCompleteEvent("s", 1, 1)]);

        await foreach (var _ in _middleware.SendTaskAsync("task", secretPrompt, null, null, CancellationToken.None)) { }

        var activity = _stoppedActivities.Single(a => a.OperationName == "sidecar.send_task");
        var allTagValues = activity.TagObjects
            .Select(kv => kv.Value?.ToString() ?? "")
            .Concat(activity.Tags.Select(kv => kv.Value ?? ""))
            .ToList();

        Assert.DoesNotContain(allTagValues, v => v.Contains(secretPrompt));
    }

    [Fact]
    public async Task SendTaskAsync_ErrorStatus_UsesGenericMessage()
    {
        const string promptFragment = "SECRET_PROMPT_IN_EXCEPTION";
        _inner.Setup(c => c.SendTaskAsync(It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowingEnumerable($"Error processing: {promptFragment}"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in _middleware.SendTaskAsync("task", null, null, null, CancellationToken.None)) { }
        });

        var activity = _stoppedActivities.Single(a => a.OperationName == "sidecar.send_task");
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.DoesNotContain(promptFragment, activity.StatusDescription ?? "");
        Assert.Equal("sidecar error", activity.StatusDescription);
    }

    // --- Helpers ---

    private void SetupInner(IEnumerable<SidecarEvent> events) =>
        _inner.Setup(c => c.SendTaskAsync(It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(StaticEnumerable(events));

    private static async IAsyncEnumerable<SidecarEvent> StaticEnumerable(
        IEnumerable<SidecarEvent> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var evt in events)
        {
            ct.ThrowIfCancellationRequested();
            yield return evt;
        }
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<SidecarEvent> BlockingUntilCancelledEnumerable(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Delay(Timeout.Infinite, ct);
        yield break;
    }

    private static async IAsyncEnumerable<SidecarEvent> ThrowingEnumerable(
        string message = "inner error",
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        throw new InvalidOperationException(message);
#pragma warning disable CS0162 // Unreachable code — required for compiler to recognize this as an iterator method
        yield break;
#pragma warning restore CS0162
    }
}
