using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services;
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
}
