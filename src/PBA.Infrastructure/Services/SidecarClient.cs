using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services;

public class SidecarClient : ISidecarClient, IDisposable
{
    private readonly IProcessRunner _processRunner;
    private readonly SidecarOptions _options;
    private readonly ILogger<SidecarClient> _logger;
    private readonly SemaphoreSlim _globalSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _semaphores = new();

    public SidecarClient(
        IProcessRunner processRunner,
        IOptions<SidecarOptions> options,
        ILogger<SidecarClient> logger)
    {
        _processRunner = processRunner;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> SendPromptAsync(
        string systemPrompt,
        string userPrompt,
        string? model = null,
        CancellationToken ct = default)
    {
        await _globalSemaphore.WaitAsync(ct);
        try
        {
            var stdinContent = $"System: {systemPrompt}\n\nUser: {userPrompt}";

            var result = await _processRunner.RunAsync(
                _options.CliPath,
                "--print",
                stdinContent,
                _options.TimeoutMs,
                ct);

            if (result.ExitCode != 0)
            {
                _logger.LogError("Sidecar CLI exited with code {ExitCode}: {StdErr}",
                    result.ExitCode, result.StandardError);
                throw new InvalidOperationException(
                    $"Sidecar CLI failed with exit code {result.ExitCode}: {result.StandardError}");
            }

            return result.StandardOutput.Trim();
        }
        finally
        {
            _globalSemaphore.Release();
        }
    }

    public async IAsyncEnumerable<string> StreamPromptAsync(
        Guid contentId,
        string systemPrompt,
        string userPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var semaphore = GetSemaphore(contentId);
        await semaphore.WaitAsync(ct);
        try
        {
            var stdinContent = $"System: {systemPrompt}\n\nUser: {userPrompt}";
            await using var handle = _processRunner.StartStreaming(
                _options.CliPath, "--print", stdinContent);

            await foreach (var line in handle.ReadLinesAsync(ct))
            {
                yield return line;
            }

            if (handle.ExitCode is > 0)
            {
                _logger.LogWarning("Sidecar streaming exited with code {ExitCode} for content {ContentId}",
                    handle.ExitCode, contentId);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private SemaphoreSlim GetSemaphore(Guid key) =>
        _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

    public void Dispose()
    {
        _globalSemaphore.Dispose();
        foreach (var semaphore in _semaphores.Values)
            semaphore.Dispose();
        _semaphores.Clear();
        GC.SuppressFinalize(this);
    }
}
