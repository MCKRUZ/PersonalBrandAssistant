namespace PBA.Application.Common.Interfaces;

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        string fileName,
        string arguments,
        string? stdinContent = null,
        int timeoutMs = 60000,
        CancellationToken ct = default);

    IStreamingProcessHandle StartStreaming(
        string fileName,
        string arguments,
        string? stdinContent = null);
}

public interface IStreamingProcessHandle : IAsyncDisposable
{
    IAsyncEnumerable<string> ReadLinesAsync(CancellationToken ct = default);
    void Kill();
    int? ExitCode { get; }
}

public record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);
