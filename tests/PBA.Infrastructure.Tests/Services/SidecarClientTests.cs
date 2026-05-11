using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services;
using PBA.Infrastructure.Tests.Services.Fakes;
using Xunit;

namespace PBA.Infrastructure.Tests.Services;

public class SidecarClientTests
{
    private readonly Mock<IProcessRunner> _processRunnerMock = new();
    private readonly SidecarClient _sut;

    public SidecarClientTests()
    {
        var options = Options.Create(new SidecarOptions
        {
            CliPath = "/usr/local/bin/claude",
            TimeoutMs = 60000,
        });
        _sut = new SidecarClient(
            _processRunnerMock.Object,
            options,
            NullLogger<SidecarClient>.Instance);
    }

    [Fact]
    public async Task SendPromptAsync_PassesContentViaStdin()
    {
        _processRunnerMock.Setup(p => p.RunAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(0, "response", ""));

        await _sut.SendPromptAsync("system prompt", "user prompt");

        _processRunnerMock.Verify(p => p.RunAsync(
            "/usr/local/bin/claude",
            It.IsAny<string>(),
            It.Is<string?>(s => s != null && s.Contains("system prompt") && s.Contains("user prompt")),
            60000,
            It.IsAny<CancellationToken>()));

        _processRunnerMock.Verify(p => p.RunAsync(
            It.IsAny<string>(),
            It.Is<string>(args => !args.Contains("system prompt") && !args.Contains("user prompt")),
            It.IsAny<string?>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task SendPromptAsync_ReturnsStdoutOnSuccess()
    {
        _processRunnerMock.Setup(p => p.RunAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(0, "  AI response text  \n", ""));

        var result = await _sut.SendPromptAsync("sys", "usr");

        Assert.Equal("AI response text", result);
    }

    [Fact]
    public async Task SendPromptAsync_ThrowsOnNonZeroExitCode()
    {
        _processRunnerMock.Setup(p => p.RunAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(1, "", "something went wrong"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SendPromptAsync("sys", "usr"));

        Assert.Contains("something went wrong", ex.Message);
    }

    [Fact]
    public async Task SendPromptAsync_PropagatesTimeoutException()
    {
        _processRunnerMock.Setup(p => p.RunAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Process timed out"));

        await Assert.ThrowsAsync<TimeoutException>(
            () => _sut.SendPromptAsync("sys", "usr"));
    }

    [Fact]
    public async Task SendPromptAsync_SerializesConcurrentCalls()
    {
        var callTimes = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        var callLock = new object();

        _processRunnerMock.Setup(p => p.RunAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string _, string _, string? _, int _, CancellationToken _) =>
            {
                var start = DateTimeOffset.UtcNow;
                await Task.Delay(50);
                var end = DateTimeOffset.UtcNow;
                lock (callLock)
                {
                    callTimes.Add((start, end));
                }
                return new ProcessRunResult(0, "ok", "");
            });

        var tasks = Enumerable.Range(0, 3)
            .Select(_ => _sut.SendPromptAsync("sys", "usr"))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(3, callTimes.Count);

        var sorted = callTimes.OrderBy(t => t.Start).ToList();
        for (var i = 1; i < sorted.Count; i++)
        {
            Assert.True(sorted[i].Start >= sorted[i - 1].End,
                $"Call {i} started at {sorted[i].Start:O} before call {i - 1} ended at {sorted[i - 1].End:O}");
        }
    }

    [Fact]
    public async Task SendPromptAsync_ReleasesSemaphoreOnFailure()
    {
        _processRunnerMock.SetupSequence(p => p.RunAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessRunResult(1, "", "first call fails"))
            .ReturnsAsync(new ProcessRunResult(0, "second succeeds", ""));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SendPromptAsync("sys", "usr"));

        var result = await _sut.SendPromptAsync("sys", "usr");
        Assert.Equal("second succeeds", result);
    }

    [Fact]
    public async Task StreamPromptAsync_YieldsTokensFromProcessStdout()
    {
        var handle = new FakeStreamingProcessHandle(["Hello", " world", "!"]);
        _processRunnerMock.Setup(p => p.StartStreaming(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(handle);

        var tokens = new List<string>();
        await foreach (var token in _sut.StreamPromptAsync(Guid.NewGuid(), "sys", "usr"))
            tokens.Add(token);

        Assert.Equal(["Hello", " world", "!"], tokens);
    }

    [Fact]
    public async Task StreamPromptAsync_CancellationToken_StopsIteration()
    {
        using var cts = new CancellationTokenSource();
        var lines = new[] { "first", "second", "third" };
        var handle = new FakeStreamingProcessHandle(lines, TimeSpan.FromMilliseconds(50));
        _processRunnerMock.Setup(p => p.StartStreaming(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(handle);

        var tokens = new List<string>();
        try
        {
            await foreach (var token in _sut.StreamPromptAsync(Guid.NewGuid(), "sys", "usr", cts.Token))
            {
                tokens.Add(token);
                if (tokens.Count == 1) cts.Cancel();
            }
        }
        catch (OperationCanceledException) { }

        Assert.Single(tokens);
        Assert.Equal("first", tokens[0]);
    }

    [Fact]
    public async Task StreamPromptAsync_KeyedSemaphore_AllowsConcurrentDifferentContentIds()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        _processRunnerMock.Setup(p => p.StartStreaming(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(() => new FakeStreamingProcessHandle(["tok"], TimeSpan.FromMilliseconds(100)));

        var sw = Stopwatch.StartNew();
        var t1 = Task.Run(async () =>
        {
            await foreach (var _ in _sut.StreamPromptAsync(id1, "sys", "usr")) { }
        });
        var t2 = Task.Run(async () =>
        {
            await foreach (var _ in _sut.StreamPromptAsync(id2, "sys", "usr")) { }
        });

        await Task.WhenAll(t1, t2);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 180, $"Expected concurrent execution but took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task StreamPromptAsync_KeyedSemaphore_SerializesSameContentId()
    {
        var contentId = Guid.NewGuid();
        _processRunnerMock.Setup(p => p.StartStreaming(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(() => new FakeStreamingProcessHandle(["tok"], TimeSpan.FromMilliseconds(100)));

        var sw = Stopwatch.StartNew();
        var t1 = Task.Run(async () =>
        {
            await foreach (var _ in _sut.StreamPromptAsync(contentId, "sys", "usr")) { }
        });
        var t2 = Task.Run(async () =>
        {
            await foreach (var _ in _sut.StreamPromptAsync(contentId, "sys", "usr")) { }
        });

        await Task.WhenAll(t1, t2);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= 180, $"Expected serialized execution but took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task StreamPromptAsync_ReleasesSemaphoreOnError()
    {
        var contentId = Guid.NewGuid();
        var callCount = 0;

        _processRunnerMock.Setup(p => p.StartStreaming(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                    throw new InvalidOperationException("process failed");
                return new FakeStreamingProcessHandle(["ok"]);
            });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in _sut.StreamPromptAsync(contentId, "sys", "usr")) { }
        });

        var tokens = new List<string>();
        await foreach (var token in _sut.StreamPromptAsync(contentId, "sys", "usr"))
            tokens.Add(token);

        Assert.Equal(["ok"], tokens);
    }
}
