using System.Diagnostics;
using System.Text;
using PBA.Application.Common.Interfaces;

namespace PBA.Infrastructure.Services;

public class ProcessRunner : IProcessRunner
{
    public IStreamingProcessHandle StartStreaming(
        string fileName,
        string arguments,
        string? stdinContent = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardInput = stdinContent != null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var process = new Process { StartInfo = psi };
        process.Start();

        if (stdinContent != null)
        {
            process.StandardInput.Write(stdinContent);
            process.StandardInput.Close();
        }

        var stderrTask = process.StandardError.ReadToEndAsync();

        return new StreamingProcessHandle(process, stderrTask);
    }

    public async Task<ProcessRunResult> RunAsync(
        string fileName,
        string arguments,
        string? stdinContent = null,
        int timeoutMs = 60000,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardInput = stdinContent != null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        if (stdinContent != null)
        {
            await process.StandardInput.WriteAsync(stdinContent);
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await process.WaitForExitAsync(cts.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return new ProcessRunResult(process.ExitCode, stdout, stderr);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException($"Process '{fileName}' timed out after {timeoutMs}ms");
        }
    }
}
