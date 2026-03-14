using System.Collections.Concurrent;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services;

public sealed class ChatClientFactory : IChatClientFactory, IDisposable
{
    private static readonly IReadOnlyDictionary<ModelTier, string> DefaultModels =
        new Dictionary<ModelTier, string>
        {
            [ModelTier.Fast] = "claude-haiku-4-5",
            [ModelTier.Standard] = "claude-sonnet-4-5-20250929",
            [ModelTier.Advanced] = "claude-opus-4-6"
        };

    private readonly AnthropicClient _anthropicClient;
    private readonly IReadOnlyDictionary<ModelTier, string> _modelMappings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChatClientFactory> _logger;
    private readonly ConcurrentDictionary<ModelTier, IChatClient> _clientCache = new();
    private volatile bool _disposed;

    public ChatClientFactory(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<ChatClientFactory> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var apiKey = configuration["AgentOrchestration:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "AgentOrchestration:ApiKey is not configured. Set it via User Secrets (dev) or Azure Key Vault (prod).");

        _anthropicClient = new AnthropicClient { ApiKey = apiKey };

        var modelsSection = configuration.GetSection("AgentOrchestration:Models");
        var mappings = new Dictionary<ModelTier, string>();

        foreach (var tier in Enum.GetValues<ModelTier>())
        {
            var configuredModel = modelsSection[tier.ToString()];
            if (!string.IsNullOrWhiteSpace(configuredModel))
            {
                mappings[tier] = configuredModel;
            }
            else if (DefaultModels.TryGetValue(tier, out var defaultModel))
            {
                mappings[tier] = defaultModel;
                _logger.LogWarning(
                    "No model configured for tier {Tier}, using default: {ModelId}",
                    tier, defaultModel);
            }
        }

        _modelMappings = mappings;
    }

    public IChatClient CreateClient(ModelTier tier)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _clientCache.GetOrAdd(tier, t =>
        {
            var modelId = GetModelId(t);
            var innerClient = _anthropicClient.AsIChatClient(modelId);
            var wrappedClient = new TokenTrackingDecorator(innerClient, _scopeFactory, modelId);

            _logger.LogInformation("Created chat client for tier {Tier} with model {ModelId}", t, modelId);
            return wrappedClient;
        });
    }

    public IChatClient CreateStreamingClient(ModelTier tier)
    {
        // IChatClient supports both streaming and non-streaming via the same interface
        return CreateClient(tier);
    }

    internal string GetModelId(ModelTier tier)
    {
        if (_modelMappings.TryGetValue(tier, out var modelId))
            return modelId;

        throw new InvalidOperationException($"No model configured for tier: {tier}");
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var client in _clientCache.Values)
        {
            if (client is IDisposable disposable)
                disposable.Dispose();
        }
        _clientCache.Clear();
    }
}
