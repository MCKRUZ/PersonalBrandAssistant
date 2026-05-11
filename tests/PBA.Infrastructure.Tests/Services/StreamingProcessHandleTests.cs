using PBA.Infrastructure.Services;
using Xunit;

namespace PBA.Infrastructure.Tests.Services;

[Trait("Category", "Integration")]
public class StreamingProcessHandleTests
{
    private static ProcessRunner CreateRunner() => new();

    [Fact]
    public async Task ReadLinesAsync_YieldsStdoutLines()
    {
        var runner = CreateRunner();
        await using var handle = runner.StartStreaming("dotnet", "--list-runtimes");

        var lines = new List<string>();
        await foreach (var line in handle.ReadLinesAsync())
            lines.Add(line);

        Assert.NotEmpty(lines);
        Assert.All(lines, l => Assert.False(string.IsNullOrWhiteSpace(l)));
        Assert.NotNull(handle.ExitCode);
        Assert.Equal(0, handle.ExitCode);
    }

    [Fact]
    public async Task Kill_TerminatesRunningProcess()
    {
        var runner = CreateRunner();
        var handle = runner.StartStreaming("ping", "-t localhost");

        await using (handle)
        {
            var lines = new List<string>();
            await foreach (var line in handle.ReadLinesAsync())
            {
                lines.Add(line);
                if (lines.Count >= 2)
                {
                    handle.Kill();
                    break;
                }
            }

            Assert.True(lines.Count >= 2);
        }
    }

    [Fact]
    public async Task DisposeAsync_CleansUpProcess()
    {
        var runner = CreateRunner();
        var handle = runner.StartStreaming("dotnet", "--list-runtimes");

        await handle.DisposeAsync();
        await handle.DisposeAsync();
    }
}
