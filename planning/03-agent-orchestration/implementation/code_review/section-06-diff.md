diff --git a/planning/03-agent-orchestration/implementation/deep_implement_config.json b/planning/03-agent-orchestration/implementation/deep_implement_config.json
index ab7cb25..69d3a66 100644
--- a/planning/03-agent-orchestration/implementation/deep_implement_config.json
+++ b/planning/03-agent-orchestration/implementation/deep_implement_config.json
@@ -35,6 +35,10 @@
     "section-04-ef-core-config": {
       "status": "complete",
       "commit_hash": "1080151"
+    },
+    "section-05-prompt-system": {
+      "status": "complete",
+      "commit_hash": "1da6786"
     }
   },
   "pre_commit": {
diff --git a/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj b/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
index 7d54c78..4675c46 100644
--- a/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
+++ b/src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
@@ -10,6 +10,7 @@
   </ItemGroup>
 
   <ItemGroup>
+    <PackageReference Include="Anthropic" Version="12.8.0" />
     <PackageReference Include="Fluid.Core" Version="2.31.0" />
     <PackageReference Include="Microsoft.AspNetCore.DataProtection" Version="10.0.5" />
     <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.5" />
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/AgentExecutionContext.cs b/src/PersonalBrandAssistant.Infrastructure/Services/AgentExecutionContext.cs
new file mode 100644
index 0000000..89453be
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/AgentExecutionContext.cs
@@ -0,0 +1,12 @@
+namespace PersonalBrandAssistant.Infrastructure.Services;
+
+public static class AgentExecutionContext
+{
+    private static readonly AsyncLocal<Guid?> _currentExecutionId = new();
+
+    public static Guid? CurrentExecutionId
+    {
+        get => _currentExecutionId.Value;
+        set => _currentExecutionId.Value = value;
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/ChatClientFactory.cs b/src/PersonalBrandAssistant.Infrastructure/Services/ChatClientFactory.cs
new file mode 100644
index 0000000..c0441b9
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/ChatClientFactory.cs
@@ -0,0 +1,101 @@
+using System.Collections.Concurrent;
+using Anthropic;
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.Configuration;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services;
+
+public sealed class ChatClientFactory : IChatClientFactory, IDisposable
+{
+    private static readonly IReadOnlyDictionary<ModelTier, string> DefaultModels =
+        new Dictionary<ModelTier, string>
+        {
+            [ModelTier.Fast] = "claude-haiku-4-5",
+            [ModelTier.Standard] = "claude-sonnet-4-5-20250929",
+            [ModelTier.Advanced] = "claude-opus-4-6"
+        };
+
+    private readonly AnthropicClient _anthropicClient;
+    private readonly IReadOnlyDictionary<ModelTier, string> _modelMappings;
+    private readonly IServiceScopeFactory _scopeFactory;
+    private readonly ILogger<ChatClientFactory> _logger;
+    private readonly ConcurrentDictionary<ModelTier, IChatClient> _clientCache = new();
+
+    public ChatClientFactory(
+        IConfiguration configuration,
+        IServiceScopeFactory scopeFactory,
+        ILogger<ChatClientFactory> logger)
+    {
+        _scopeFactory = scopeFactory;
+        _logger = logger;
+
+        var apiKey = configuration["AgentOrchestration:ApiKey"];
+        if (string.IsNullOrWhiteSpace(apiKey))
+            throw new InvalidOperationException(
+                "AgentOrchestration:ApiKey is not configured. Set it via User Secrets (dev) or Azure Key Vault (prod).");
+
+        _anthropicClient = new AnthropicClient { ApiKey = apiKey };
+
+        var modelsSection = configuration.GetSection("AgentOrchestration:Models");
+        var mappings = new Dictionary<ModelTier, string>();
+
+        foreach (var tier in Enum.GetValues<ModelTier>())
+        {
+            var configuredModel = modelsSection[tier.ToString()];
+            if (!string.IsNullOrWhiteSpace(configuredModel))
+            {
+                mappings[tier] = configuredModel;
+            }
+            else if (DefaultModels.TryGetValue(tier, out var defaultModel))
+            {
+                mappings[tier] = defaultModel;
+                _logger.LogWarning(
+                    "No model configured for tier {Tier}, using default: {ModelId}",
+                    tier, defaultModel);
+            }
+        }
+
+        _modelMappings = mappings;
+    }
+
+    public IChatClient CreateClient(ModelTier tier)
+    {
+        return _clientCache.GetOrAdd(tier, t =>
+        {
+            var modelId = GetModelId(t);
+            var innerClient = _anthropicClient.AsIChatClient(modelId);
+            var wrappedClient = new TokenTrackingDecorator(innerClient, _scopeFactory, modelId);
+
+            _logger.LogInformation("Created chat client for tier {Tier} with model {ModelId}", t, modelId);
+            return wrappedClient;
+        });
+    }
+
+    public IChatClient CreateStreamingClient(ModelTier tier)
+    {
+        // IChatClient supports both streaming and non-streaming via the same interface
+        return CreateClient(tier);
+    }
+
+    internal string GetModelId(ModelTier tier)
+    {
+        if (_modelMappings.TryGetValue(tier, out var modelId))
+            return modelId;
+
+        throw new InvalidOperationException($"No model configured for tier: {tier}");
+    }
+
+    public void Dispose()
+    {
+        foreach (var client in _clientCache.Values)
+        {
+            if (client is IDisposable disposable)
+                disposable.Dispose();
+        }
+        _clientCache.Clear();
+    }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/TokenTrackingDecorator.cs b/src/PersonalBrandAssistant.Infrastructure/Services/TokenTrackingDecorator.cs
new file mode 100644
index 0000000..e408eca
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/TokenTrackingDecorator.cs
@@ -0,0 +1,95 @@
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.DependencyInjection;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+
+namespace PersonalBrandAssistant.Infrastructure.Services;
+
+public sealed class TokenTrackingDecorator : DelegatingChatClient
+{
+    private readonly IServiceScopeFactory _scopeFactory;
+    private readonly string _modelId;
+
+    public TokenTrackingDecorator(
+        IChatClient innerClient,
+        IServiceScopeFactory scopeFactory,
+        string modelId)
+        : base(innerClient)
+    {
+        _scopeFactory = scopeFactory;
+        _modelId = modelId;
+    }
+
+    public override async Task<ChatResponse> GetResponseAsync(
+        IEnumerable<ChatMessage> messages,
+        ChatOptions? options = null,
+        CancellationToken cancellationToken = default)
+    {
+        var response = await base.GetResponseAsync(messages, options, cancellationToken);
+        await RecordUsageFromResponseAsync(response.Usage, cancellationToken);
+        return response;
+    }
+
+    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
+        IEnumerable<ChatMessage> messages,
+        ChatOptions? options = null,
+        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
+    {
+        // Accumulate usage across streaming chunks, then record after stream completes
+        var accumulatedUsage = new UsageDetails();
+
+        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
+        {
+            // Accumulate usage from Content properties if available
+            if (update.Contents is not null)
+            {
+                foreach (var content in update.Contents)
+                {
+                    if (content is UsageContent usageContent)
+                    {
+                        accumulatedUsage = usageContent.Details;
+                    }
+                }
+            }
+
+            yield return update;
+        }
+
+        if (accumulatedUsage.InputTokenCount > 0 || accumulatedUsage.OutputTokenCount > 0)
+            await RecordUsageFromResponseAsync(accumulatedUsage, cancellationToken);
+    }
+
+    private async Task RecordUsageFromResponseAsync(UsageDetails? usage, CancellationToken ct)
+    {
+        if (usage is null)
+            return;
+
+        var executionId = AgentExecutionContext.CurrentExecutionId;
+        if (executionId is null)
+            return;
+
+        var inputTokens = (int)(usage.InputTokenCount ?? 0);
+        var outputTokens = (int)(usage.OutputTokenCount ?? 0);
+
+        // Extract cache tokens from AdditionalCounts if available
+        var cacheReadTokens = 0;
+        var cacheCreationTokens = 0;
+        if (usage.AdditionalCounts is not null)
+        {
+            if (usage.AdditionalCounts.TryGetValue("CacheReadInputTokens", out var cacheRead))
+                cacheReadTokens = (int)cacheRead;
+            if (usage.AdditionalCounts.TryGetValue("CacheCreationInputTokens", out var cacheCreation))
+                cacheCreationTokens = (int)cacheCreation;
+        }
+
+        await using var scope = _scopeFactory.CreateAsyncScope();
+        var tracker = scope.ServiceProvider.GetRequiredService<ITokenTracker>();
+        await tracker.RecordUsageAsync(
+            executionId.Value,
+            _modelId,
+            inputTokens,
+            outputTokens,
+            cacheReadTokens,
+            cacheCreationTokens,
+            ct);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ChatClientFactoryTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ChatClientFactoryTests.cs
new file mode 100644
index 0000000..2b30487
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ChatClientFactoryTests.cs
@@ -0,0 +1,183 @@
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.Configuration;
+using Microsoft.Extensions.DependencyInjection;
+using Microsoft.Extensions.Logging;
+using Moq;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services;
+
+public class ChatClientFactoryTests
+{
+    private readonly Mock<IServiceScopeFactory> _scopeFactory;
+    private readonly Mock<ILogger<ChatClientFactory>> _logger;
+
+    public ChatClientFactoryTests()
+    {
+        _scopeFactory = new Mock<IServiceScopeFactory>();
+        _logger = new Mock<ILogger<ChatClientFactory>>();
+    }
+
+    private IConfiguration BuildConfig(
+        string apiKey = "test-api-key",
+        Dictionary<string, string?>? models = null)
+    {
+        var configData = new Dictionary<string, string?>
+        {
+            ["AgentOrchestration:ApiKey"] = apiKey
+        };
+
+        if (models is not null)
+        {
+            foreach (var (tier, modelId) in models)
+            {
+                configData[$"AgentOrchestration:Models:{tier}"] = modelId;
+            }
+        }
+
+        return new ConfigurationBuilder()
+            .AddInMemoryCollection(configData)
+            .Build();
+    }
+
+    private ChatClientFactory CreateFactory(
+        string apiKey = "test-api-key",
+        Dictionary<string, string?>? models = null)
+    {
+        return new ChatClientFactory(
+            BuildConfig(apiKey, models),
+            _scopeFactory.Object,
+            _logger.Object);
+    }
+
+    [Theory]
+    [InlineData(ModelTier.Fast, "claude-haiku-4-5")]
+    [InlineData(ModelTier.Standard, "claude-sonnet-4-5-20250929")]
+    [InlineData(ModelTier.Advanced, "claude-opus-4-6")]
+    public void CreateClient_MapsDefaultTierToCorrectModelId(ModelTier tier, string expectedModelId)
+    {
+        var factory = CreateFactory();
+
+        var modelId = factory.GetModelId(tier);
+
+        Assert.Equal(expectedModelId, modelId);
+    }
+
+    [Fact]
+    public void CreateClient_ReturnsWrappedChatClient()
+    {
+        var factory = CreateFactory();
+
+        var client = factory.CreateClient(ModelTier.Standard);
+
+        Assert.NotNull(client);
+        Assert.IsType<TokenTrackingDecorator>(client);
+    }
+
+    [Fact]
+    public void CreateClient_UsesConfiguredModelIds()
+    {
+        var models = new Dictionary<string, string?>
+        {
+            ["Fast"] = "custom-fast-model",
+            ["Standard"] = "custom-standard-model",
+            ["Advanced"] = "custom-advanced-model"
+        };
+        var factory = CreateFactory(models: models);
+
+        Assert.Equal("custom-fast-model", factory.GetModelId(ModelTier.Fast));
+        Assert.Equal("custom-standard-model", factory.GetModelId(ModelTier.Standard));
+        Assert.Equal("custom-advanced-model", factory.GetModelId(ModelTier.Advanced));
+    }
+
+    [Fact]
+    public void Constructor_ThrowsWhenApiKeyIsMissing()
+    {
+        Assert.Throws<InvalidOperationException>(() =>
+            CreateFactory(apiKey: ""));
+    }
+
+    [Fact]
+    public void CreateStreamingClient_ReturnsSameClientAsCreateClient()
+    {
+        var factory = CreateFactory();
+
+        var client = factory.CreateClient(ModelTier.Fast);
+        var streamingClient = factory.CreateStreamingClient(ModelTier.Fast);
+
+        Assert.Same(client, streamingClient);
+    }
+
+    [Fact]
+    public void CreateClient_CachesClientsPerTier()
+    {
+        var factory = CreateFactory();
+
+        var first = factory.CreateClient(ModelTier.Standard);
+        var second = factory.CreateClient(ModelTier.Standard);
+
+        Assert.Same(first, second);
+    }
+
+    [Fact]
+    public void CreateClient_DifferentTiersReturnDifferentClients()
+    {
+        var factory = CreateFactory();
+
+        var fast = factory.CreateClient(ModelTier.Fast);
+        var standard = factory.CreateClient(ModelTier.Standard);
+
+        Assert.NotSame(fast, standard);
+    }
+}
+
+public class AgentExecutionContextTests
+{
+    [Fact]
+    public void CurrentExecutionId_DefaultsToNull()
+    {
+        Assert.Null(AgentExecutionContext.CurrentExecutionId);
+    }
+
+    [Fact]
+    public void CurrentExecutionId_CanBeSetAndRead()
+    {
+        var id = Guid.NewGuid();
+        AgentExecutionContext.CurrentExecutionId = id;
+
+        Assert.Equal(id, AgentExecutionContext.CurrentExecutionId);
+
+        // Clean up
+        AgentExecutionContext.CurrentExecutionId = null;
+    }
+
+    [Fact]
+    public async Task CurrentExecutionId_IsIsolatedPerAsyncFlow()
+    {
+        var id1 = Guid.NewGuid();
+        var id2 = Guid.NewGuid();
+
+        Guid? capturedInTask1 = null;
+        Guid? capturedInTask2 = null;
+
+        var task1 = Task.Run(() =>
+        {
+            AgentExecutionContext.CurrentExecutionId = id1;
+            Thread.Sleep(50);
+            capturedInTask1 = AgentExecutionContext.CurrentExecutionId;
+        });
+
+        var task2 = Task.Run(() =>
+        {
+            AgentExecutionContext.CurrentExecutionId = id2;
+            Thread.Sleep(50);
+            capturedInTask2 = AgentExecutionContext.CurrentExecutionId;
+        });
+
+        await Task.WhenAll(task1, task2);
+
+        Assert.Equal(id1, capturedInTask1);
+        Assert.Equal(id2, capturedInTask2);
+    }
+}
