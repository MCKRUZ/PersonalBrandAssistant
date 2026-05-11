using System.Runtime.CompilerServices;
using PBA.Application.Common.Interfaces;

namespace PBA.Infrastructure.Tests.Services.Fakes;

internal sealed class FakeStreamingProcessHandle : IStreamingProcessHandle
{
    private readonly IReadOnlyList<string> _lines;
    private readonly TimeSpan _delayPerLine;
    private bool _killed;

    public FakeStreamingProcessHandle(IReadOnlyList<string> lines, TimeSpan? delayPerLine = null)
    {
        _lines = lines;
        _delayPerLine = delayPerLine ?? TimeSpan.Zero;
    }

    public int? ExitCode => _killed ? -1 : 0;
    public bool KillCalled => _killed;

    public async IAsyncEnumerable<string> ReadLinesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var line in _lines)
        {
            ct.ThrowIfCancellationRequested();
            if (_delayPerLine > TimeSpan.Zero)
                await Task.Delay(_delayPerLine, ct);
            if (_killed) yield break;
            yield return line;
        }
    }

    public void Kill() => _killed = true;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
