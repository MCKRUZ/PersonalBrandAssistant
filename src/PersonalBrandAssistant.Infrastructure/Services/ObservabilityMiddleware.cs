using System.Diagnostics;
using System.Runtime.CompilerServices;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Infrastructure.Agents;

namespace PersonalBrandAssistant.Infrastructure.Services;

/// <summary>
/// ISidecarClient decorator that wraps every SendTaskAsync call in a sidecar.send_task OTel span.
/// Sets token/cost attributes from TaskCompleteEvent. Never emits prompt or response text in span attributes.
/// Disposes Activity in try/finally to handle early-break, cancellation, and exceptions.
/// </summary>
public sealed class ObservabilityMiddleware : ISidecarClient
{
    private readonly ISidecarClient _inner;

    public ObservabilityMiddleware(ISidecarClient inner)
    {
        _inner = inner;
    }

    public bool IsConnected => _inner.IsConnected;

    public Task<SidecarSession> ConnectAsync(CancellationToken ct) => _inner.ConnectAsync(ct);

    public Task<SidecarSession> NewSessionAsync(CancellationToken ct) => _inner.NewSessionAsync(ct);

    public Task AbortAsync(string? sessionId, CancellationToken ct) => _inner.AbortAsync(sessionId, ct);

    public async IAsyncEnumerable<SidecarEvent> SendTaskAsync(
        string task,
        string? systemPrompt,
        string? sessionId,
        string? modelId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var activity = AgentTelemetry.Source.StartActivity("sidecar.send_task");
        activity?.SetTag("has_system_prompt", systemPrompt != null);

        // Manual enumerator pattern: MoveNextAsync (can throw) is inside try/catch,
        // yield return is in the outer try/finally (no catch), which is legal in C#.
        var enumerator = _inner.SendTaskAsync(task, systemPrompt, sessionId, modelId, ct)
            .GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
                    throw;
                }
                catch (Exception)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "sidecar error");
                    throw;
                }

                if (!hasNext)
                    break;

                var evt = enumerator.Current;
                if (evt is TaskCompleteEvent complete)
                {
                    activity?.SetTag("gen_ai.usage.input_tokens", complete.InputTokens);
                    activity?.SetTag("gen_ai.usage.output_tokens", complete.OutputTokens);
                    activity?.SetTag("gen_ai.usage.cache_read_tokens", complete.CacheReadTokens);
                    activity?.SetTag("cost_usd", complete.Cost);
                }

                yield return evt; // outside any try/catch — legal
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
            activity?.Dispose();
        }
    }
}
