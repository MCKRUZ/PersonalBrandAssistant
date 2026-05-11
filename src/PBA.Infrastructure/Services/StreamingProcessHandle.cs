using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using PBA.Application.Common.Interfaces;

namespace PBA.Infrastructure.Services;

public sealed class StreamingProcessHandle : IStreamingProcessHandle
{
    private readonly Process _process;
    private readonly Task<string> _stderrTask;
    private int _disposed;
    private int? _cachedExitCode;

    internal StreamingProcessHandle(Process process, Task<string> stderrTask)
    {
        _process = process;
        _stderrTask = stderrTask;
    }

    public int? ExitCode
    {
        get
        {
            if (_cachedExitCode.HasValue) return _cachedExitCode;
            try
            {
                if (_process.HasExited)
                {
                    _cachedExitCode = _process.ExitCode;
                    return _cachedExitCode;
                }
            }
            catch (InvalidOperationException) { }
            return null;
        }
    }

    public async IAsyncEnumerable<string> ReadLinesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string? line;
        while (true)
        {
            try
            {
                line = await _process.StandardOutput.ReadLineAsync(ct);
            }
            catch (OperationCanceledException)
            {
                Kill();
                yield break;
            }

            if (line is null)
                break;

            yield return line;
        }
    }

    public void Kill()
    {
        try { _process.Kill(entireProcessTree: true); } catch { /* best effort */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;

        Kill();

        try { await _stderrTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { /* best effort */ }

        // Cache exit code before disposing the process
        _ = ExitCode;

        _process.Dispose();
    }
}
