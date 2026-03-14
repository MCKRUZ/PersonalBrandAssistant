using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;

namespace PersonalBrandAssistant.Infrastructure.Services;

public sealed class TokenTrackingDecorator : DelegatingChatClient
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _modelId;
    private readonly ILogger<TokenTrackingDecorator>? _logger;

    public TokenTrackingDecorator(
        IChatClient innerClient,
        IServiceScopeFactory scopeFactory,
        string modelId,
        ILogger<TokenTrackingDecorator>? logger = null)
        : base(innerClient)
    {
        _scopeFactory = scopeFactory;
        _modelId = modelId;
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        await TryRecordUsageAsync(response.Usage, cancellationToken);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var accumulatedUsage = new UsageDetails();

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            if (update.Contents is not null)
            {
                foreach (var content in update.Contents)
                {
                    if (content is UsageContent usageContent)
                    {
                        accumulatedUsage = usageContent.Details;
                    }
                }
            }

            yield return update;
        }

        if (accumulatedUsage.InputTokenCount > 0 || accumulatedUsage.OutputTokenCount > 0)
            await TryRecordUsageAsync(accumulatedUsage, cancellationToken);
    }

    private async Task TryRecordUsageAsync(UsageDetails? usage, CancellationToken ct)
    {
        if (usage is null)
            return;

        var executionId = AgentExecutionContext.CurrentExecutionId;
        if (executionId is null)
            return;

        try
        {
            var inputTokens = checked((int)(usage.InputTokenCount ?? 0));
            var outputTokens = checked((int)(usage.OutputTokenCount ?? 0));

            var cacheReadTokens = 0;
            var cacheCreationTokens = 0;
            if (usage.AdditionalCounts is not null)
            {
                if (usage.AdditionalCounts.TryGetValue("CacheReadInputTokens", out var cacheRead))
                    cacheReadTokens = checked((int)cacheRead);
                if (usage.AdditionalCounts.TryGetValue("CacheCreationInputTokens", out var cacheCreation))
                    cacheCreationTokens = checked((int)cacheCreation);
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var tracker = scope.ServiceProvider.GetRequiredService<ITokenTracker>();
            await tracker.RecordUsageAsync(
                executionId.Value,
                _modelId,
                inputTokens,
                outputTokens,
                cacheReadTokens,
                cacheCreationTokens,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex,
                "Failed to record token usage for execution {ExecutionId}, model {ModelId}",
                executionId, _modelId);
        }
    }
}
