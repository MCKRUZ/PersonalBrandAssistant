namespace PBA.Application.Common.Interfaces;

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        string fileName,
        string arguments,
        string? stdinContent = null,
        int timeoutMs = 60000,
        CancellationToken ct = default);
}

public record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);
