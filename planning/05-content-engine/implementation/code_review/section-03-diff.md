diff --git a/src/PersonalBrandAssistant.Application/Common/Interfaces/IChatClientFactory.cs b/src/PersonalBrandAssistant.Application/Common/Interfaces/IChatClientFactory.cs
deleted file mode 100644
index 749a205..0000000
--- a/src/PersonalBrandAssistant.Application/Common/Interfaces/IChatClientFactory.cs
+++ /dev/null
@@ -1,10 +0,0 @@
-using Microsoft.Extensions.AI;
-using PersonalBrandAssistant.Domain.Enums;
-
-namespace PersonalBrandAssistant.Application.Common.Interfaces;
-
-public interface IChatClientFactory
-{
-    IChatClient CreateClient(ModelTier tier);
-    IChatClient CreateStreamingClient(ModelTier tier);
-}
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/AgentContext.cs b/src/PersonalBrandAssistant.Application/Common/Models/AgentContext.cs
index c2fed67..f2caa04 100644
--- a/src/PersonalBrandAssistant.Application/Common/Models/AgentContext.cs
+++ b/src/PersonalBrandAssistant.Application/Common/Models/AgentContext.cs
@@ -1,6 +1,4 @@
-using Microsoft.Extensions.AI;
 using PersonalBrandAssistant.Application.Common.Interfaces;
-using PersonalBrandAssistant.Domain.Enums;
 
 namespace PersonalBrandAssistant.Application.Common.Models;
 
@@ -10,7 +8,7 @@ public record AgentContext
     public required BrandProfilePromptModel BrandProfile { get; init; }
     public ContentPromptModel? Content { get; init; }
     public required IPromptTemplateService PromptService { get; init; }
-    public required IChatClient ChatClient { get; init; }
+    public required ISidecarClient SidecarClient { get; init; }
+    public string? SessionId { get; init; }
     public required Dictionary<string, string> Parameters { get; init; }
-    public required ModelTier ModelTier { get; init; }
 }
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs b/src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs
index 6d4a1d1..c10d8e0 100644
--- a/src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs
+++ b/src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs
@@ -6,17 +6,7 @@ public class AgentOrchestrationOptions
 
     public decimal DailyBudget { get; init; } = 10.00m;
     public decimal MonthlyBudget { get; init; } = 100.00m;
-    public string DefaultModelTier { get; init; } = "Standard";
-    public Dictionary<string, string> Models { get; init; } = new();
-    public Dictionary<string, ModelPricingOptions> Pricing { get; init; } = new();
     public string PromptsPath { get; init; } = "prompts";
-    public int MaxRetriesPerExecution { get; init; } = 3;
     public int ExecutionTimeoutSeconds { get; init; } = 180;
     public bool LogPromptContent { get; init; }
 }
-
-public record ModelPricingOptions
-{
-    public decimal InputPerMillion { get; init; }
-    public decimal OutputPerMillion { get; init; }
-}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Agents/AgentOrchestrator.cs b/src/PersonalBrandAssistant.Infrastructure/Agents/AgentOrchestrator.cs
index d50ccff..30ce618 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Agents/AgentOrchestrator.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Agents/AgentOrchestrator.cs
@@ -7,7 +7,6 @@ using PersonalBrandAssistant.Application.Common.Interfaces;
 using PersonalBrandAssistant.Application.Common.Models;
 using PersonalBrandAssistant.Domain.Entities;
 using PersonalBrandAssistant.Domain.Enums;
-using PersonalBrandAssistant.Infrastructure.Services;
 
 namespace PersonalBrandAssistant.Infrastructure.Agents;
 
@@ -15,7 +14,7 @@ public class AgentOrchestrator : IAgentOrchestrator
 {
     private readonly FrozenDictionary<AgentCapabilityType, IAgentCapability> _capabilities;
     private readonly ITokenTracker _tokenTracker;
-    private readonly IChatClientFactory _chatClientFactory;
+    private readonly ISidecarClient _sidecarClient;
     private readonly IPromptTemplateService _promptTemplateService;
     private readonly IApplicationDbContext _dbContext;
     private readonly IWorkflowEngine _workflowEngine;
@@ -26,7 +25,7 @@ public class AgentOrchestrator : IAgentOrchestrator
     public AgentOrchestrator(
         IEnumerable<IAgentCapability> capabilities,
         ITokenTracker tokenTracker,
-        IChatClientFactory chatClientFactory,
+        ISidecarClient sidecarClient,
         IPromptTemplateService promptTemplateService,
         IApplicationDbContext dbContext,
         IWorkflowEngine workflowEngine,
@@ -36,7 +35,7 @@ public class AgentOrchestrator : IAgentOrchestrator
     {
         _capabilities = capabilities.ToFrozenDictionary(c => c.Type);
         _tokenTracker = tokenTracker;
-        _chatClientFactory = chatClientFactory;
+        _sidecarClient = sidecarClient;
         _promptTemplateService = promptTemplateService;
         _dbContext = dbContext;
         _workflowEngine = workflowEngine;
@@ -66,8 +65,7 @@ public class AgentOrchestrator : IAgentOrchestrator
                     ErrorCode.ValidationFailed, $"No capability registered for {task.Type}");
             }
 
-            var modelTier = capability.DefaultModelTier;
-            var execution = AgentExecution.Create(task.Type, modelTier, task.ContentId);
+            var execution = AgentExecution.Create(task.Type, capability.DefaultModelTier, task.ContentId);
             _dbContext.AgentExecutions.Add(execution);
             await _dbContext.SaveChangesAsync(ct);
 
@@ -82,103 +80,60 @@ public class AgentOrchestrator : IAgentOrchestrator
             execution.MarkRunning();
             await _dbContext.SaveChangesAsync(ct);
 
-            var currentTier = modelTier;
-            var maxRetries = _options.MaxRetriesPerExecution;
-            Exception? lastException = null;
-
-            for (var attempt = 0; attempt <= maxRetries; attempt++)
+            try
             {
-                try
-                {
-                    if (attempt > 0 && await _tokenTracker.IsOverBudgetAsync(timeoutCts.Token))
-                    {
-                        execution.Fail("Budget exceeded during retries");
-                        await _dbContext.SaveChangesAsync(ct);
-                        return Result<AgentExecutionResult>.Failure(
-                            ErrorCode.ValidationFailed, "Budget exceeded during retries");
-                    }
-
-                    AgentExecutionContext.CurrentExecutionId = execution.Id;
-
-                    var context = BuildAgentContext(execution.Id, brandProfile, content,
-                        currentTier, task.Parameters);
-
-                    var result = await capability.ExecuteAsync(context, timeoutCts.Token);
-
-                    if (!result.IsSuccess)
-                    {
-                        execution.Fail(string.Join("; ", result.Errors));
-                        await _dbContext.SaveChangesAsync(ct);
-                        return Result<AgentExecutionResult>.Failure(result.ErrorCode, result.Errors.ToArray());
-                    }
-
-                    var output = result.Value!;
-                    await RecordUsageAsync(execution, currentTier, output, ct);
-                    execution.Complete(TruncateSummary(output.GeneratedText));
-                    await _dbContext.SaveChangesAsync(ct);
-
-                    Guid? createdContentId = null;
-                    if (output.CreatesContent)
-                    {
-                        createdContentId = await CreateContentFromOutputAsync(
-                            task.Type, output, task.ContentId, ct);
-                    }
+                var context = BuildAgentContext(execution.Id, brandProfile, content, task.Parameters);
+                var result = await capability.ExecuteAsync(context, timeoutCts.Token);
 
-                    return Result<AgentExecutionResult>.Success(new AgentExecutionResult(
-                        execution.Id, AgentExecutionStatus.Completed, output, createdContentId));
-                }
-                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
+                if (!result.IsSuccess)
                 {
-                    execution.Cancel();
+                    execution.Fail(string.Join("; ", result.Errors));
                     await _dbContext.SaveChangesAsync(ct);
-                    return Result<AgentExecutionResult>.Failure(ErrorCode.InternalError, "Execution timed out");
-                }
-                catch (Exception ex) when (IsTransientError(ex))
-                {
-                    lastException = ex;
-                    _logger.LogWarning(ex, "Transient error on attempt {Attempt} for {AgentType}",
-                        attempt + 1, task.Type);
-
-                    if (attempt < maxRetries)
-                    {
-                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
-                        await Task.Delay(delay, ct);
-
-                        if (attempt >= 1)
-                        {
-                            var downgraded = DowngradeModelTier(currentTier);
-                            if (downgraded.HasValue)
-                            {
-                                _logger.LogInformation("Downgrading model tier from {From} to {To}",
-                                    currentTier, downgraded.Value);
-                                currentTier = downgraded.Value;
-                            }
-                        }
-                        continue;
-                    }
+                    return Result<AgentExecutionResult>.Failure(result.ErrorCode, result.Errors.ToArray());
                 }
-                catch (Exception ex)
-                {
-                    lastException = ex;
-                    break;
-                }
-                finally
+
+                var output = result.Value!;
+                await RecordUsageAsync(execution, output, ct);
+                execution.Complete(TruncateSummary(output.GeneratedText));
+                await _dbContext.SaveChangesAsync(ct);
+
+                Guid? createdContentId = null;
+                if (output.CreatesContent)
                 {
-                    AgentExecutionContext.CurrentExecutionId = null;
+                    createdContentId = await CreateContentFromOutputAsync(
+                        task.Type, output, task.ContentId, ct);
                 }
+
+                return Result<AgentExecutionResult>.Success(new AgentExecutionResult(
+                    execution.Id, AgentExecutionStatus.Completed, output, createdContentId));
             }
+            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
+            {
+                execution.Cancel();
+                await _dbContext.SaveChangesAsync(ct);
 
-            var errorMessage = lastException?.Message ?? "Unknown error";
-            execution.Fail(errorMessage);
-            await _dbContext.SaveChangesAsync(ct);
+                await _notificationService.SendAsync(
+                    NotificationType.ContentFailed,
+                    "Agent Execution Timed Out",
+                    $"Agent {task.Type} timed out after {_options.ExecutionTimeoutSeconds}s.",
+                    task.ContentId, ct);
 
-            await _notificationService.SendAsync(
-                NotificationType.ContentFailed,
-                "Agent Execution Failed",
-                $"Agent {task.Type} failed permanently.",
-                task.ContentId, ct);
+                return Result<AgentExecutionResult>.Failure(ErrorCode.InternalError, "Execution timed out");
+            }
+            catch (Exception ex)
+            {
+                _logger.LogError(ex, "Agent {AgentType} failed during execution", task.Type);
+                execution.Fail(ex.Message);
+                await _dbContext.SaveChangesAsync(ct);
 
-            return Result<AgentExecutionResult>.Failure(ErrorCode.InternalError, errorMessage);
+                await _notificationService.SendAsync(
+                    NotificationType.ContentFailed,
+                    "Agent Execution Failed",
+                    $"Agent {task.Type} failed permanently.",
+                    task.ContentId, ct);
+
+                return Result<AgentExecutionResult>.Failure(ErrorCode.InternalError, ex.Message);
+            }
         }
         catch (Exception ex)
         {
@@ -210,19 +165,17 @@ public class AgentOrchestrator : IAgentOrchestrator
         Guid executionId,
         BrandProfilePromptModel brandProfile,
         ContentPromptModel? content,
-        ModelTier tier,
         Dictionary<string, string> parameters)
     {
-        var chatClient = _chatClientFactory.CreateClient(tier);
         return new AgentContext
         {
             ExecutionId = executionId,
             BrandProfile = brandProfile,
             Content = content,
             PromptService = _promptTemplateService,
-            ChatClient = chatClient,
+            SidecarClient = _sidecarClient,
+            SessionId = null,
             Parameters = parameters,
-            ModelTier = tier,
         };
     }
 
@@ -275,13 +228,13 @@ public class AgentOrchestrator : IAgentOrchestrator
     }
 
     private async Task RecordUsageAsync(
-        AgentExecution execution, ModelTier actualTier, AgentOutput output, CancellationToken ct)
+        AgentExecution execution, AgentOutput output, CancellationToken ct)
     {
         if (output.InputTokens > 0 || output.OutputTokens > 0)
         {
             await _tokenTracker.RecordUsageAsync(
                 execution.Id,
-                actualTier.ToString(),
+                "sidecar",
                 output.InputTokens,
                 output.OutputTokens,
                 output.CacheReadTokens,
@@ -329,23 +282,6 @@ public class AgentOrchestrator : IAgentOrchestrator
                 $"Capability type {capabilityType} does not map to a content type"),
         };
 
-    private static bool IsTransientError(Exception ex) =>
-        ex is HttpRequestException httpEx &&
-            httpEx.StatusCode is
-                System.Net.HttpStatusCode.TooManyRequests or
-                System.Net.HttpStatusCode.InternalServerError or
-                System.Net.HttpStatusCode.BadGateway or
-                System.Net.HttpStatusCode.ServiceUnavailable or
-                System.Net.HttpStatusCode.GatewayTimeout;
-
-    private static ModelTier? DowngradeModelTier(ModelTier current) =>
-        current switch
-        {
-            ModelTier.Advanced => ModelTier.Standard,
-            ModelTier.Standard => ModelTier.Fast,
-            _ => null,
-        };
-
     private static string? TruncateSummary(string? text) =>
         text?.Length > 500 ? text[..500] : text;
 }
diff --git a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AgentCapabilityBase.cs b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AgentCapabilityBase.cs
index 521d57e..63157b7 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AgentCapabilityBase.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AgentCapabilityBase.cs
@@ -1,4 +1,4 @@
-using Microsoft.Extensions.AI;
+using System.Text;
 using Microsoft.Extensions.Logging;
 using PersonalBrandAssistant.Application.Common.Errors;
 using PersonalBrandAssistant.Application.Common.Interfaces;
@@ -32,23 +32,50 @@ public abstract class AgentCapabilityBase : IAgentCapability
             var systemPrompt = await context.PromptService.RenderAsync(AgentName, "system", variables);
             var taskPrompt = await context.PromptService.RenderAsync(AgentName, templateName, variables);
 
-            var messages = new List<ChatMessage>
+            var combinedPrompt = $"{systemPrompt}\n\n{taskPrompt}";
+
+            var responseBuilder = new StringBuilder();
+            var inputTokens = 0;
+            var outputTokens = 0;
+            var fileChanges = new List<string>();
+
+            await foreach (var evt in context.SidecarClient.SendTaskAsync(combinedPrompt, context.SessionId, ct))
             {
-                new(ChatRole.System, systemPrompt),
-                new(ChatRole.User, taskPrompt)
-            };
+                switch (evt)
+                {
+                    case ChatEvent { Text: not null } chat:
+                        responseBuilder.Append(chat.Text);
+                        break;
+
+                    case FileChangeEvent fileChange:
+                        fileChanges.Add($"{fileChange.ChangeType}:{fileChange.FilePath}");
+                        break;
 
-            var response = await context.ChatClient.GetResponseAsync(messages, cancellationToken: ct);
-            var responseText = response.Text ?? string.Empty;
+                    case TaskCompleteEvent complete:
+                        inputTokens = complete.InputTokens;
+                        outputTokens = complete.OutputTokens;
+                        break;
+
+                    case ErrorEvent error:
+                        _logger.LogError("{Capability} received error from sidecar: {Error}", Type, error.Message);
+                        return Result<AgentOutput>.Failure(ErrorCode.InternalError, error.Message);
+                }
+            }
+
+            var responseText = responseBuilder.ToString();
 
             if (string.IsNullOrWhiteSpace(responseText))
             {
-                _logger.LogWarning("{Capability} received empty response from chat client", Type);
+                _logger.LogWarning("{Capability} received empty response from sidecar", Type);
                 return Result<AgentOutput>.Failure(ErrorCode.InternalError,
-                    $"{Type} capability received empty response from model");
+                    $"{Type} capability received empty response from sidecar");
             }
 
-            return BuildOutput(responseText, response.Usage);
+            return BuildOutput(responseText, inputTokens, outputTokens, fileChanges);
+        }
+        catch (OperationCanceledException)
+        {
+            throw;
         }
         catch (Exception ex)
         {
@@ -73,14 +100,20 @@ public abstract class AgentCapabilityBase : IAgentCapability
         return variables;
     }
 
-    protected virtual Result<AgentOutput> BuildOutput(string responseText, UsageDetails? usage)
+    protected virtual Result<AgentOutput> BuildOutput(
+        string responseText, int inputTokens, int outputTokens, List<string> fileChanges)
     {
+        var metadata = new Dictionary<string, string>();
+        if (fileChanges.Count > 0)
+            metadata["file_changes"] = string.Join(";", fileChanges);
+
         return Result<AgentOutput>.Success(new AgentOutput
         {
             GeneratedText = responseText,
             CreatesContent = CreatesContent,
-            InputTokens = (int)(usage?.InputTokenCount ?? 0),
-            OutputTokens = (int)(usage?.OutputTokenCount ?? 0)
+            InputTokens = inputTokens,
+            OutputTokens = outputTokens,
+            Metadata = metadata,
         });
     }
 }
diff --git a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/WriterAgentCapability.cs b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/WriterAgentCapability.cs
index 4edb2c6..def49a3 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/WriterAgentCapability.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/WriterAgentCapability.cs
@@ -1,5 +1,4 @@
 using System.Text.RegularExpressions;
-using Microsoft.Extensions.AI;
 using Microsoft.Extensions.Logging;
 using PersonalBrandAssistant.Application.Common.Models;
 using PersonalBrandAssistant.Domain.Enums;
@@ -16,17 +15,23 @@ public sealed partial class WriterAgentCapability : AgentCapabilityBase
     protected override string DefaultTemplate => "blog-post";
     protected override bool CreatesContent => true;
 
-    protected override Result<AgentOutput> BuildOutput(string responseText, UsageDetails? usage)
+    protected override Result<AgentOutput> BuildOutput(
+        string responseText, int inputTokens, int outputTokens, List<string> fileChanges)
     {
         var title = ExtractTitle(responseText);
 
+        var metadata = new Dictionary<string, string>();
+        if (fileChanges.Count > 0)
+            metadata["file_changes"] = string.Join(";", fileChanges);
+
         return Result<AgentOutput>.Success(new AgentOutput
         {
             GeneratedText = responseText,
             Title = title,
             CreatesContent = true,
-            InputTokens = (int)(usage?.InputTokenCount ?? 0),
-            OutputTokens = (int)(usage?.OutputTokenCount ?? 0)
+            InputTokens = inputTokens,
+            OutputTokens = outputTokens,
+            Metadata = metadata,
         });
     }
 
diff --git a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
index a6bf0bf..a0144fc 100644
--- a/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs
@@ -52,7 +52,7 @@ public static class DependencyInjection
         // Agent orchestration
         services.Configure<AgentOrchestrationOptions>(
             configuration.GetSection(AgentOrchestrationOptions.SectionName));
-        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
+        services.AddSingleton<ISidecarClient, SidecarClient>();
         services.AddSingleton<IPromptTemplateService>(sp =>
         {
             var opts = sp.GetRequiredService<IOptions<AgentOrchestrationOptions>>().Value;
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/AgentExecutionContext.cs b/src/PersonalBrandAssistant.Infrastructure/Services/AgentExecutionContext.cs
deleted file mode 100644
index 89453be..0000000
--- a/src/PersonalBrandAssistant.Infrastructure/Services/AgentExecutionContext.cs
+++ /dev/null
@@ -1,12 +0,0 @@
-namespace PersonalBrandAssistant.Infrastructure.Services;
-
-public static class AgentExecutionContext
-{
-    private static readonly AsyncLocal<Guid?> _currentExecutionId = new();
-
-    public static Guid? CurrentExecutionId
-    {
-        get => _currentExecutionId.Value;
-        set => _currentExecutionId.Value = value;
-    }
-}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/ChatClientFactory.cs b/src/PersonalBrandAssistant.Infrastructure/Services/ChatClientFactory.cs
deleted file mode 100644
index 7bc783d..0000000
--- a/src/PersonalBrandAssistant.Infrastructure/Services/ChatClientFactory.cs
+++ /dev/null
@@ -1,105 +0,0 @@
-using System.Collections.Concurrent;
-using Anthropic;
-using Microsoft.Extensions.AI;
-using Microsoft.Extensions.Configuration;
-using Microsoft.Extensions.DependencyInjection;
-using Microsoft.Extensions.Logging;
-using PersonalBrandAssistant.Application.Common.Interfaces;
-using PersonalBrandAssistant.Domain.Enums;
-
-namespace PersonalBrandAssistant.Infrastructure.Services;
-
-public sealed class ChatClientFactory : IChatClientFactory, IDisposable
-{
-    private static readonly IReadOnlyDictionary<ModelTier, string> DefaultModels =
-        new Dictionary<ModelTier, string>
-        {
-            [ModelTier.Fast] = "claude-haiku-4-5",
-            [ModelTier.Standard] = "claude-sonnet-4-5-20250929",
-            [ModelTier.Advanced] = "claude-opus-4-6"
-        };
-
-    private readonly AnthropicClient _anthropicClient;
-    private readonly IReadOnlyDictionary<ModelTier, string> _modelMappings;
-    private readonly IServiceScopeFactory _scopeFactory;
-    private readonly ILogger<ChatClientFactory> _logger;
-    private readonly ConcurrentDictionary<ModelTier, IChatClient> _clientCache = new();
-    private volatile bool _disposed;
-
-    public ChatClientFactory(
-        IConfiguration configuration,
-        IServiceScopeFactory scopeFactory,
-        ILogger<ChatClientFactory> logger)
-    {
-        _scopeFactory = scopeFactory;
-        _logger = logger;
-
-        var apiKey = configuration["AgentOrchestration:ApiKey"];
-        if (string.IsNullOrWhiteSpace(apiKey))
-            throw new InvalidOperationException(
-                "AgentOrchestration:ApiKey is not configured. Set it via User Secrets (dev) or Azure Key Vault (prod).");
-
-        _anthropicClient = new AnthropicClient { ApiKey = apiKey };
-
-        var modelsSection = configuration.GetSection("AgentOrchestration:Models");
-        var mappings = new Dictionary<ModelTier, string>();
-
-        foreach (var tier in Enum.GetValues<ModelTier>())
-        {
-            var configuredModel = modelsSection[tier.ToString()];
-            if (!string.IsNullOrWhiteSpace(configuredModel))
-            {
-                mappings[tier] = configuredModel;
-            }
-            else if (DefaultModels.TryGetValue(tier, out var defaultModel))
-            {
-                mappings[tier] = defaultModel;
-                _logger.LogWarning(
-                    "No model configured for tier {Tier}, using default: {ModelId}",
-                    tier, defaultModel);
-            }
-        }
-
-        _modelMappings = mappings;
-    }
-
-    public IChatClient CreateClient(ModelTier tier)
-    {
-        ObjectDisposedException.ThrowIf(_disposed, this);
-
-        return _clientCache.GetOrAdd(tier, t =>
-        {
-            var modelId = GetModelId(t);
-            var innerClient = _anthropicClient.AsIChatClient(modelId);
-            var wrappedClient = new TokenTrackingDecorator(innerClient, _scopeFactory, modelId);
-
-            _logger.LogInformation("Created chat client for tier {Tier} with model {ModelId}", t, modelId);
-            return wrappedClient;
-        });
-    }
-
-    public IChatClient CreateStreamingClient(ModelTier tier)
-    {
-        // IChatClient supports both streaming and non-streaming via the same interface
-        return CreateClient(tier);
-    }
-
-    internal string GetModelId(ModelTier tier)
-    {
-        if (_modelMappings.TryGetValue(tier, out var modelId))
-            return modelId;
-
-        throw new InvalidOperationException($"No model configured for tier: {tier}");
-    }
-
-    public void Dispose()
-    {
-        _disposed = true;
-        foreach (var client in _clientCache.Values)
-        {
-            if (client is IDisposable disposable)
-                disposable.Dispose();
-        }
-        _clientCache.Clear();
-    }
-}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/TokenTracker.cs b/src/PersonalBrandAssistant.Infrastructure/Services/TokenTracker.cs
index c32182b..e267fa8 100644
--- a/src/PersonalBrandAssistant.Infrastructure/Services/TokenTracker.cs
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/TokenTracker.cs
@@ -104,14 +104,8 @@ public sealed class TokenTracker : ITokenTracker
 
     internal decimal CalculateCost(string modelId, int inputTokens, int outputTokens)
     {
-        if (!_options.Pricing.TryGetValue(modelId, out var pricing))
-        {
-            _logger.LogWarning(
-                "No pricing configured for model {ModelId}, recording cost as 0", modelId);
-            return 0m;
-        }
-
-        return (inputTokens / 1_000_000m * pricing.InputPerMillion)
-            + (outputTokens / 1_000_000m * pricing.OutputPerMillion);
+        // Cost tracking is handled by the sidecar/CLI layer.
+        // Token counts are recorded for budget monitoring but cost is not calculated here.
+        return 0m;
     }
 }
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/TokenTrackingDecorator.cs b/src/PersonalBrandAssistant.Infrastructure/Services/TokenTrackingDecorator.cs
deleted file mode 100644
index 41f1348..0000000
--- a/src/PersonalBrandAssistant.Infrastructure/Services/TokenTrackingDecorator.cs
+++ /dev/null
@@ -1,105 +0,0 @@
-using Microsoft.Extensions.AI;
-using Microsoft.Extensions.DependencyInjection;
-using Microsoft.Extensions.Logging;
-using PersonalBrandAssistant.Application.Common.Interfaces;
-
-namespace PersonalBrandAssistant.Infrastructure.Services;
-
-public sealed class TokenTrackingDecorator : DelegatingChatClient
-{
-    private readonly IServiceScopeFactory _scopeFactory;
-    private readonly string _modelId;
-    private readonly ILogger<TokenTrackingDecorator>? _logger;
-
-    public TokenTrackingDecorator(
-        IChatClient innerClient,
-        IServiceScopeFactory scopeFactory,
-        string modelId,
-        ILogger<TokenTrackingDecorator>? logger = null)
-        : base(innerClient)
-    {
-        _scopeFactory = scopeFactory;
-        _modelId = modelId;
-        _logger = logger;
-    }
-
-    public override async Task<ChatResponse> GetResponseAsync(
-        IEnumerable<ChatMessage> messages,
-        ChatOptions? options = null,
-        CancellationToken cancellationToken = default)
-    {
-        var response = await base.GetResponseAsync(messages, options, cancellationToken);
-        await TryRecordUsageAsync(response.Usage, cancellationToken);
-        return response;
-    }
-
-    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
-        IEnumerable<ChatMessage> messages,
-        ChatOptions? options = null,
-        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
-    {
-        var accumulatedUsage = new UsageDetails();
-
-        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
-        {
-            if (update.Contents is not null)
-            {
-                foreach (var content in update.Contents)
-                {
-                    if (content is UsageContent usageContent)
-                    {
-                        accumulatedUsage = usageContent.Details;
-                    }
-                }
-            }
-
-            yield return update;
-        }
-
-        if (accumulatedUsage.InputTokenCount > 0 || accumulatedUsage.OutputTokenCount > 0)
-            await TryRecordUsageAsync(accumulatedUsage, cancellationToken);
-    }
-
-    private async Task TryRecordUsageAsync(UsageDetails? usage, CancellationToken ct)
-    {
-        if (usage is null)
-            return;
-
-        var executionId = AgentExecutionContext.CurrentExecutionId;
-        if (executionId is null)
-            return;
-
-        try
-        {
-            var inputTokens = checked((int)(usage.InputTokenCount ?? 0));
-            var outputTokens = checked((int)(usage.OutputTokenCount ?? 0));
-
-            var cacheReadTokens = 0;
-            var cacheCreationTokens = 0;
-            if (usage.AdditionalCounts is not null)
-            {
-                if (usage.AdditionalCounts.TryGetValue("CacheReadInputTokens", out var cacheRead))
-                    cacheReadTokens = checked((int)cacheRead);
-                if (usage.AdditionalCounts.TryGetValue("CacheCreationInputTokens", out var cacheCreation))
-                    cacheCreationTokens = checked((int)cacheCreation);
-            }
-
-            await using var scope = _scopeFactory.CreateAsyncScope();
-            var tracker = scope.ServiceProvider.GetRequiredService<ITokenTracker>();
-            await tracker.RecordUsageAsync(
-                executionId.Value,
-                _modelId,
-                inputTokens,
-                outputTokens,
-                cacheReadTokens,
-                cacheCreationTokens,
-                ct);
-        }
-        catch (Exception ex) when (ex is not OperationCanceledException)
-        {
-            _logger?.LogWarning(ex,
-                "Failed to record token usage for execution {ExecutionId}, model {ModelId}",
-                executionId, _modelId);
-        }
-    }
-}
diff --git a/tests/PersonalBrandAssistant.Application.Tests/Common/Models/AgentModelsTests.cs b/tests/PersonalBrandAssistant.Application.Tests/Common/Models/AgentModelsTests.cs
index cc96844..206b7a5 100644
--- a/tests/PersonalBrandAssistant.Application.Tests/Common/Models/AgentModelsTests.cs
+++ b/tests/PersonalBrandAssistant.Application.Tests/Common/Models/AgentModelsTests.cs
@@ -1,4 +1,3 @@
-using Microsoft.Extensions.AI;
 using Moq;
 using PersonalBrandAssistant.Application.Common.Interfaces;
 using PersonalBrandAssistant.Application.Common.Models;
@@ -104,9 +103,8 @@ public class AgentModelsTests
             BrandProfile = brandProfile,
             Content = contentModel,
             PromptService = Mock.Of<IPromptTemplateService>(),
-            ChatClient = Mock.Of<IChatClient>(),
+            SidecarClient = Mock.Of<ISidecarClient>(),
             Parameters = new Dictionary<string, string> { ["key"] = "value" },
-            ModelTier = ModelTier.Standard,
         };
 
         Assert.Equal("Test Brand", context.BrandProfile.Name);
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/AgentOrchestratorTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/AgentOrchestratorTests.cs
index 4f55b88..88c8a88 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/AgentOrchestratorTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/AgentOrchestratorTests.cs
@@ -1,5 +1,4 @@
 using Microsoft.EntityFrameworkCore;
-using Microsoft.Extensions.AI;
 using Microsoft.Extensions.Logging;
 using Microsoft.Extensions.Options;
 using Moq;
@@ -17,7 +16,7 @@ namespace PersonalBrandAssistant.Infrastructure.Tests.Agents;
 public class AgentOrchestratorTests
 {
     private readonly Mock<ITokenTracker> _tokenTracker = new();
-    private readonly Mock<IChatClientFactory> _chatClientFactory = new();
+    private readonly Mock<ISidecarClient> _sidecarClient = new();
     private readonly Mock<IPromptTemplateService> _promptTemplateService = new();
     private readonly Mock<IApplicationDbContext> _dbContext = new();
     private readonly Mock<IWorkflowEngine> _workflowEngine = new();
@@ -30,7 +29,6 @@ public class AgentOrchestratorTests
     private readonly AgentOrchestrationOptions _options = new()
     {
         ExecutionTimeoutSeconds = 30,
-        MaxRetriesPerExecution = 3,
     };
 
     private AgentOrchestrator CreateOrchestrator(
@@ -70,7 +68,7 @@ public class AgentOrchestratorTests
         return new AgentOrchestrator(
             capabilities ?? [],
             _tokenTracker.Object,
-            _chatClientFactory.Object,
+            _sidecarClient.Object,
             _promptTemplateService.Object,
             _dbContext.Object,
             _workflowEngine.Object,
@@ -299,46 +297,46 @@ public class AgentOrchestratorTests
             Times.Once);
     }
 
-    // --- Retry and Fallback Tests ---
+    // --- Token Tracking Tests ---
 
     [Fact]
-    public async Task ExecuteAsync_RetriesOnTransientError()
+    public async Task ExecuteAsync_RecordsTokenUsage_FromSidecarEvents()
     {
-        var callCount = 0;
-        var capability = new Mock<IAgentCapability>();
-        capability.Setup(x => x.Type).Returns(AgentCapabilityType.Writer);
-        capability.Setup(x => x.DefaultModelTier).Returns(ModelTier.Standard);
-        capability.Setup(x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
-            .Returns<AgentContext, CancellationToken>((_, _) =>
-            {
-                callCount++;
-                if (callCount == 1)
-                    throw new HttpRequestException("Service unavailable", null,
-                        System.Net.HttpStatusCode.ServiceUnavailable);
-                return Task.FromResult(Result<AgentOutput>.Success(new AgentOutput
-                {
-                    GeneratedText = "output",
-                    CreatesContent = false,
-                }));
-            });
-
+        var output = new AgentOutput
+        {
+            GeneratedText = "output",
+            CreatesContent = false,
+            InputTokens = 200,
+            OutputTokens = 75,
+        };
+        var capability = CreateCapabilityMock(AgentCapabilityType.Writer, output: output);
         var orchestrator = CreateOrchestrator([capability.Object]);
         var task = new AgentTask(AgentCapabilityType.Writer, null, new());
 
-        var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);
+        await orchestrator.ExecuteAsync(task, CancellationToken.None);
 
-        Assert.True(result.IsSuccess);
-        Assert.Equal(2, callCount);
+        _tokenTracker.Verify(
+            x => x.RecordUsageAsync(
+                It.IsAny<Guid>(),
+                "sidecar",
+                200,
+                75,
+                It.IsAny<int>(),
+                It.IsAny<int>(),
+                It.IsAny<CancellationToken>()),
+            Times.Once);
     }
 
+    // --- Failure Tests ---
+
     [Fact]
-    public async Task ExecuteAsync_DoesNotRetry_OnNonTransientError()
+    public async Task ExecuteAsync_FailsAndNotifies_OnCapabilityException()
     {
         var capability = new Mock<IAgentCapability>();
         capability.Setup(x => x.Type).Returns(AgentCapabilityType.Writer);
         capability.Setup(x => x.DefaultModelTier).Returns(ModelTier.Standard);
         capability.Setup(x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
-            .ReturnsAsync(Result<AgentOutput>.Failure(ErrorCode.ValidationFailed, "Bad prompt"));
+            .ThrowsAsync(new InvalidOperationException("Sidecar connection lost"));
 
         var orchestrator = CreateOrchestrator([capability.Object]);
         var task = new AgentTask(AgentCapabilityType.Writer, null, new());
@@ -346,67 +344,36 @@ public class AgentOrchestratorTests
         var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);
 
         Assert.False(result.IsSuccess);
-        capability.Verify(
-            x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()),
+        _notificationService.Verify(
+            x => x.SendAsync(
+                NotificationType.ContentFailed, It.IsAny<string>(), It.IsAny<string>(),
+                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
             Times.Once);
     }
 
     [Fact]
-    public async Task ExecuteAsync_DowngradesModelTier_OnSecondTransientFailure()
-    {
-        var tiers = new List<ModelTier>();
-        var callCount = 0;
-
-        var capability = new Mock<IAgentCapability>();
-        capability.Setup(x => x.Type).Returns(AgentCapabilityType.Writer);
-        capability.Setup(x => x.DefaultModelTier).Returns(ModelTier.Advanced);
-        capability.Setup(x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
-            .Returns<AgentContext, CancellationToken>((ctx, _) =>
-            {
-                tiers.Add(ctx.ModelTier);
-                callCount++;
-                if (callCount <= 2)
-                    throw new HttpRequestException("Rate limited", null,
-                        System.Net.HttpStatusCode.TooManyRequests);
-                return Task.FromResult(Result<AgentOutput>.Success(new AgentOutput
-                {
-                    GeneratedText = "output",
-                    CreatesContent = false,
-                }));
-            });
-
-        var orchestrator = CreateOrchestrator([capability.Object]);
-        var task = new AgentTask(AgentCapabilityType.Writer, null, new());
-
-        var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);
-
-        Assert.True(result.IsSuccess);
-        Assert.Equal(ModelTier.Advanced, tiers[0]);
-        Assert.Equal(ModelTier.Advanced, tiers[1]);
-        Assert.Equal(ModelTier.Standard, tiers[2]);
-    }
-
-    [Fact]
-    public async Task ExecuteAsync_FailsPermanently_AfterMaxRetries_SendsNotification()
+    public async Task ExecuteAsync_PassesSidecarClient_InAgentContext()
     {
+        AgentContext? capturedContext = null;
         var capability = new Mock<IAgentCapability>();
         capability.Setup(x => x.Type).Returns(AgentCapabilityType.Writer);
         capability.Setup(x => x.DefaultModelTier).Returns(ModelTier.Standard);
         capability.Setup(x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
-            .ThrowsAsync(new HttpRequestException("Server error", null,
-                System.Net.HttpStatusCode.InternalServerError));
+            .Callback<AgentContext, CancellationToken>((ctx, _) => capturedContext = ctx)
+            .ReturnsAsync(Result<AgentOutput>.Success(new AgentOutput
+            {
+                GeneratedText = "output",
+                CreatesContent = false,
+            }));
 
         var orchestrator = CreateOrchestrator([capability.Object]);
         var task = new AgentTask(AgentCapabilityType.Writer, null, new());
 
-        var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);
+        await orchestrator.ExecuteAsync(task, CancellationToken.None);
 
-        Assert.False(result.IsSuccess);
-        _notificationService.Verify(
-            x => x.SendAsync(
-                NotificationType.ContentFailed, It.IsAny<string>(), It.IsAny<string>(),
-                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
-            Times.Once);
+        Assert.NotNull(capturedContext);
+        Assert.Same(_sidecarClient.Object, capturedContext!.SidecarClient);
+        Assert.Null(capturedContext.SessionId);
     }
 
     // --- Status Query Tests ---
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/AnalyticsAgentCapabilityTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/AnalyticsAgentCapabilityTests.cs
index 4144ab8..eede43d 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/AnalyticsAgentCapabilityTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/AnalyticsAgentCapabilityTests.cs
@@ -1,4 +1,4 @@
-using Microsoft.Extensions.AI;
+using System.Runtime.CompilerServices;
 using Microsoft.Extensions.Logging;
 using Moq;
 using PersonalBrandAssistant.Application.Common.Interfaces;
@@ -11,13 +11,13 @@ namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;
 public class AnalyticsAgentCapabilityTests
 {
     private readonly Mock<IPromptTemplateService> _promptService;
-    private readonly Mock<IChatClient> _chatClient;
+    private readonly Mock<ISidecarClient> _sidecarClient;
     private readonly AnalyticsAgentCapability _capability;
 
     public AnalyticsAgentCapabilityTests()
     {
         _promptService = new Mock<IPromptTemplateService>();
-        _chatClient = new Mock<IChatClient>();
+        _sidecarClient = new Mock<ISidecarClient>();
         _capability = new AnalyticsAgentCapability(
             new Mock<ILogger<AnalyticsAgentCapability>>().Object);
     }
@@ -28,9 +28,8 @@ public class AnalyticsAgentCapabilityTests
             ExecutionId = Guid.NewGuid(),
             BrandProfile = TestBrandProfile.Create(),
             PromptService = _promptService.Object,
-            ChatClient = _chatClient.Object,
+            SidecarClient = _sidecarClient.Object,
             Parameters = parameters ?? new Dictionary<string, string>(),
-            ModelTier = ModelTier.Fast
         };
 
     [Fact]
@@ -49,7 +48,7 @@ public class AnalyticsAgentCapabilityTests
     public async Task ExecuteAsync_SetsCreatesContentFalse()
     {
         SetupPrompts("analytics", "performance-insights");
-        SetupChatResponse("Your top performing content was...");
+        SetupSidecarResponse("Your top performing content was...");
 
         var context = CreateContext();
         var result = await _capability.ExecuteAsync(context, CancellationToken.None);
@@ -62,7 +61,7 @@ public class AnalyticsAgentCapabilityTests
     public async Task ExecuteAsync_ReturnsRecommendations()
     {
         SetupPrompts("analytics", "performance-insights");
-        SetupChatResponse("Recommendation: Post more on Tuesdays for better engagement.");
+        SetupSidecarResponse("Recommendation: Post more on Tuesdays for better engagement.");
 
         var context = CreateContext();
         var result = await _capability.ExecuteAsync(context, CancellationToken.None);
@@ -79,13 +78,20 @@ public class AnalyticsAgentCapabilityTests
             .ReturnsAsync("task prompt");
     }
 
-    private void SetupChatResponse(string text)
+    private void SetupSidecarResponse(string text)
     {
-        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
-        _chatClient.Setup(c => c.GetResponseAsync(
-                It.IsAny<IList<ChatMessage>>(),
-                It.IsAny<ChatOptions>(),
+        _sidecarClient.Setup(c => c.SendTaskAsync(
+                It.IsAny<string>(),
+                It.IsAny<string?>(),
                 It.IsAny<CancellationToken>()))
-            .ReturnsAsync(response);
+            .Returns(CreateSidecarEvents(text));
+    }
+
+    private static async IAsyncEnumerable<SidecarEvent> CreateSidecarEvents(
+        string text, [EnumeratorCancellation] CancellationToken ct = default)
+    {
+        yield return new ChatEvent("assistant", text, null, null);
+        yield return new TaskCompleteEvent("mock-session", 100, 50);
+        await Task.CompletedTask;
     }
 }
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/EngagementAgentCapabilityTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/EngagementAgentCapabilityTests.cs
index 5626378..701f6a4 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/EngagementAgentCapabilityTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/EngagementAgentCapabilityTests.cs
@@ -1,4 +1,4 @@
-using Microsoft.Extensions.AI;
+using System.Runtime.CompilerServices;
 using Microsoft.Extensions.Logging;
 using Moq;
 using PersonalBrandAssistant.Application.Common.Interfaces;
@@ -11,13 +11,13 @@ namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;
 public class EngagementAgentCapabilityTests
 {
     private readonly Mock<IPromptTemplateService> _promptService;
-    private readonly Mock<IChatClient> _chatClient;
+    private readonly Mock<ISidecarClient> _sidecarClient;
     private readonly EngagementAgentCapability _capability;
 
     public EngagementAgentCapabilityTests()
     {
         _promptService = new Mock<IPromptTemplateService>();
-        _chatClient = new Mock<IChatClient>();
+        _sidecarClient = new Mock<ISidecarClient>();
         _capability = new EngagementAgentCapability(
             new Mock<ILogger<EngagementAgentCapability>>().Object);
     }
@@ -28,9 +28,8 @@ public class EngagementAgentCapabilityTests
             ExecutionId = Guid.NewGuid(),
             BrandProfile = TestBrandProfile.Create(),
             PromptService = _promptService.Object,
-            ChatClient = _chatClient.Object,
+            SidecarClient = _sidecarClient.Object,
             Parameters = parameters ?? new Dictionary<string, string>(),
-            ModelTier = ModelTier.Fast
         };
 
     [Fact]
@@ -49,7 +48,7 @@ public class EngagementAgentCapabilityTests
     public async Task ExecuteAsync_SetsCreatesContentFalse()
     {
         SetupPrompts("engagement", "response-suggestion");
-        SetupChatResponse("Here are some response suggestions...");
+        SetupSidecarResponse("Here are some response suggestions...");
 
         var context = CreateContext();
         var result = await _capability.ExecuteAsync(context, CancellationToken.None);
@@ -62,7 +61,7 @@ public class EngagementAgentCapabilityTests
     public async Task ExecuteAsync_ReturnsSuggestionsAsOutput()
     {
         SetupPrompts("engagement", "response-suggestion");
-        SetupChatResponse("Suggestion 1: Reply with gratitude\nSuggestion 2: Ask a follow-up");
+        SetupSidecarResponse("Suggestion 1: Reply with gratitude\nSuggestion 2: Ask a follow-up");
 
         var context = CreateContext();
         var result = await _capability.ExecuteAsync(context, CancellationToken.None);
@@ -79,13 +78,20 @@ public class EngagementAgentCapabilityTests
             .ReturnsAsync("task prompt");
     }
 
-    private void SetupChatResponse(string text)
+    private void SetupSidecarResponse(string text)
     {
-        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
-        _chatClient.Setup(c => c.GetResponseAsync(
-                It.IsAny<IList<ChatMessage>>(),
-                It.IsAny<ChatOptions>(),
+        _sidecarClient.Setup(c => c.SendTaskAsync(
+                It.IsAny<string>(),
+                It.IsAny<string?>(),
                 It.IsAny<CancellationToken>()))
-            .ReturnsAsync(response);
+            .Returns(CreateSidecarEvents(text));
+    }
+
+    private static async IAsyncEnumerable<SidecarEvent> CreateSidecarEvents(
+        string text, [EnumeratorCancellation] CancellationToken ct = default)
+    {
+        yield return new ChatEvent("assistant", text, null, null);
+        yield return new TaskCompleteEvent("mock-session", 100, 50);
+        await Task.CompletedTask;
     }
 }
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/RepurposeAgentCapabilityTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/RepurposeAgentCapabilityTests.cs
index a10d606..0746d69 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/RepurposeAgentCapabilityTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/RepurposeAgentCapabilityTests.cs
@@ -1,4 +1,4 @@
-using Microsoft.Extensions.AI;
+using System.Runtime.CompilerServices;
 using Microsoft.Extensions.Logging;
 using Moq;
 using PersonalBrandAssistant.Application.Common.Interfaces;
@@ -11,13 +11,13 @@ namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;
 public class RepurposeAgentCapabilityTests
 {
     private readonly Mock<IPromptTemplateService> _promptService;
-    private readonly Mock<IChatClient> _chatClient;
+    private readonly Mock<ISidecarClient> _sidecarClient;
     private readonly RepurposeAgentCapability _capability;
 
     public RepurposeAgentCapabilityTests()
     {
         _promptService = new Mock<IPromptTemplateService>();
-        _chatClient = new Mock<IChatClient>();
+        _sidecarClient = new Mock<ISidecarClient>();
         _capability = new RepurposeAgentCapability(
             new Mock<ILogger<RepurposeAgentCapability>>().Object);
     }
@@ -36,9 +36,8 @@ public class RepurposeAgentCapabilityTests
                 TargetPlatforms = [PlatformType.TwitterX]
             },
             PromptService = _promptService.Object,
-            ChatClient = _chatClient.Object,
+            SidecarClient = _sidecarClient.Object,
             Parameters = parameters ?? new Dictionary<string, string> { ["template"] = "blog-to-thread" },
-            ModelTier = ModelTier.Standard
         };
 
     [Fact]
@@ -57,7 +56,7 @@ public class RepurposeAgentCapabilityTests
     public async Task ExecuteAsync_LoadsRepurposeTemplates()
     {
         SetupPrompts("repurpose", "blog-to-thread");
-        SetupChatResponse("Repurposed thread content");
+        SetupSidecarResponse("Repurposed thread content");
 
         var context = CreateContext();
         await _capability.ExecuteAsync(context, CancellationToken.None);
@@ -70,7 +69,7 @@ public class RepurposeAgentCapabilityTests
     public async Task ExecuteAsync_SetsCreatesContentTrue()
     {
         SetupPrompts("repurpose", "blog-to-thread");
-        SetupChatResponse("Thread content from blog.");
+        SetupSidecarResponse("Thread content from blog.");
 
         var context = CreateContext();
         var result = await _capability.ExecuteAsync(context, CancellationToken.None);
@@ -89,7 +88,7 @@ public class RepurposeAgentCapabilityTests
         _promptService.Setup(p => p.RenderAsync("repurpose", "system", It.IsAny<Dictionary<string, object>>()))
             .ReturnsAsync("system prompt");
 
-        SetupChatResponse("Repurposed content.");
+        SetupSidecarResponse("Repurposed content.");
 
         var context = CreateContext();
         await _capability.ExecuteAsync(context, CancellationToken.None);
@@ -106,13 +105,20 @@ public class RepurposeAgentCapabilityTests
             .ReturnsAsync("task prompt");
     }
 
-    private void SetupChatResponse(string text)
+    private void SetupSidecarResponse(string text)
     {
-        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
-        _chatClient.Setup(c => c.GetResponseAsync(
-                It.IsAny<IList<ChatMessage>>(),
-                It.IsAny<ChatOptions>(),
+        _sidecarClient.Setup(c => c.SendTaskAsync(
+                It.IsAny<string>(),
+                It.IsAny<string?>(),
                 It.IsAny<CancellationToken>()))
-            .ReturnsAsync(response);
+            .Returns(CreateSidecarEvents(text));
+    }
+
+    private static async IAsyncEnumerable<SidecarEvent> CreateSidecarEvents(
+        string text, [EnumeratorCancellation] CancellationToken ct = default)
+    {
+        yield return new ChatEvent("assistant", text, null, null);
+        yield return new TaskCompleteEvent("mock-session", 100, 50);
+        await Task.CompletedTask;
     }
 }
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/SocialAgentCapabilityTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/SocialAgentCapabilityTests.cs
index 97db332..d7f26ad 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/SocialAgentCapabilityTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/SocialAgentCapabilityTests.cs
@@ -1,4 +1,4 @@
-using Microsoft.Extensions.AI;
+using System.Runtime.CompilerServices;
 using Microsoft.Extensions.Logging;
 using Moq;
 using PersonalBrandAssistant.Application.Common.Interfaces;
@@ -11,13 +11,13 @@ namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;
 public class SocialAgentCapabilityTests
 {
     private readonly Mock<IPromptTemplateService> _promptService;
-    private readonly Mock<IChatClient> _chatClient;
+    private readonly Mock<ISidecarClient> _sidecarClient;
     private readonly SocialAgentCapability _capability;
 
     public SocialAgentCapabilityTests()
     {
         _promptService = new Mock<IPromptTemplateService>();
-        _chatClient = new Mock<IChatClient>();
+        _sidecarClient = new Mock<ISidecarClient>();
         _capability = new SocialAgentCapability(
             new Mock<ILogger<SocialAgentCapability>>().Object);
     }
@@ -28,9 +28,8 @@ public class SocialAgentCapabilityTests
             ExecutionId = Guid.NewGuid(),
             BrandProfile = TestBrandProfile.Create(),
             PromptService = _promptService.Object,
-            ChatClient = _chatClient.Object,
+            SidecarClient = _sidecarClient.Object,
             Parameters = parameters ?? new Dictionary<string, string>(),
-            ModelTier = ModelTier.Fast
         };
 
     [Fact]
@@ -49,7 +48,7 @@ public class SocialAgentCapabilityTests
     public async Task ExecuteAsync_LoadsSocialTemplates()
     {
         SetupPrompts("social", "post");
-        SetupChatResponse("{\"text\": \"Check out this post!\", \"hashtags\": [\"#tech\"]}");
+        SetupSidecarResponse("{\"text\": \"Check out this post!\", \"hashtags\": [\"#tech\"]}");
 
         var context = CreateContext();
         await _capability.ExecuteAsync(context, CancellationToken.None);
@@ -62,7 +61,7 @@ public class SocialAgentCapabilityTests
     public async Task ExecuteAsync_SetsCreatesContentTrue()
     {
         SetupPrompts("social", "post");
-        SetupChatResponse("{\"text\": \"Social post content\", \"hashtags\": [\"#brand\"]}");
+        SetupSidecarResponse("{\"text\": \"Social post content\", \"hashtags\": [\"#brand\"]}");
 
         var context = CreateContext();
         var result = await _capability.ExecuteAsync(context, CancellationToken.None);
@@ -75,7 +74,7 @@ public class SocialAgentCapabilityTests
     public async Task ExecuteAsync_UsesThreadTemplateFromParameters()
     {
         SetupPrompts("social", "thread");
-        SetupChatResponse("{\"text\": \"Thread content\", \"hashtags\": []}");
+        SetupSidecarResponse("{\"text\": \"Thread content\", \"hashtags\": []}");
 
         var context = CreateContext(new Dictionary<string, string> { ["template"] = "thread" });
         await _capability.ExecuteAsync(context, CancellationToken.None);
@@ -87,7 +86,7 @@ public class SocialAgentCapabilityTests
     public async Task ExecuteAsync_ReturnsGeneratedText()
     {
         SetupPrompts("social", "post");
-        SetupChatResponse("{\"text\": \"Amazing content here!\", \"hashtags\": [\"#ai\", \"#tech\"]}");
+        SetupSidecarResponse("{\"text\": \"Amazing content here!\", \"hashtags\": [\"#ai\", \"#tech\"]}");
 
         var context = CreateContext();
         var result = await _capability.ExecuteAsync(context, CancellationToken.None);
@@ -104,13 +103,20 @@ public class SocialAgentCapabilityTests
             .ReturnsAsync("task prompt");
     }
 
-    private void SetupChatResponse(string text)
+    private void SetupSidecarResponse(string text)
     {
-        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
-        _chatClient.Setup(c => c.GetResponseAsync(
-                It.IsAny<IList<ChatMessage>>(),
-                It.IsAny<ChatOptions>(),
+        _sidecarClient.Setup(c => c.SendTaskAsync(
+                It.IsAny<string>(),
+                It.IsAny<string?>(),
                 It.IsAny<CancellationToken>()))
-            .ReturnsAsync(response);
+            .Returns(CreateSidecarEvents(text));
+    }
+
+    private static async IAsyncEnumerable<SidecarEvent> CreateSidecarEvents(
+        string text, [EnumeratorCancellation] CancellationToken ct = default)
+    {
+        yield return new ChatEvent("assistant", text, null, null);
+        yield return new TaskCompleteEvent("mock-session", 100, 50);
+        await Task.CompletedTask;
     }
 }
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/WriterAgentCapabilityTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/WriterAgentCapabilityTests.cs
index bfa4a48..c02fff7 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/WriterAgentCapabilityTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/WriterAgentCapabilityTests.cs
@@ -1,4 +1,4 @@
-using Microsoft.Extensions.AI;
+using System.Runtime.CompilerServices;
 using Microsoft.Extensions.Logging;
 using Moq;
 using PersonalBrandAssistant.Application.Common.Interfaces;
@@ -11,13 +11,13 @@ namespace PersonalBrandAssistant.Infrastructure.Tests.Agents.Capabilities;
 public class WriterAgentCapabilityTests
 {
     private readonly Mock<IPromptTemplateService> _promptService;
-    private readonly Mock<IChatClient> _chatClient;
+    private readonly Mock<ISidecarClient> _sidecarClient;
     private readonly WriterAgentCapability _capability;
 
     public WriterAgentCapabilityTests()
     {
         _promptService = new Mock<IPromptTemplateService>();
-        _chatClient = new Mock<IChatClient>();
+        _sidecarClient = new Mock<ISidecarClient>();
         _capability = new WriterAgentCapability(
             new Mock<ILogger<WriterAgentCapability>>().Object);
     }
@@ -28,9 +28,8 @@ public class WriterAgentCapabilityTests
             ExecutionId = Guid.NewGuid(),
             BrandProfile = TestBrandProfile.Create(),
             PromptService = _promptService.Object,
-            ChatClient = _chatClient.Object,
+            SidecarClient = _sidecarClient.Object,
             Parameters = parameters ?? new Dictionary<string, string>(),
-            ModelTier = ModelTier.Standard
         };
 
     [Fact]
@@ -53,7 +52,7 @@ public class WriterAgentCapabilityTests
         _promptService.Setup(p => p.RenderAsync("writer", "blog-post", It.IsAny<Dictionary<string, object>>()))
             .ReturnsAsync("task prompt");
 
-        SetupChatResponse("# My Blog Post\n\nThis is the body content.");
+        SetupSidecarResponse("# My Blog Post\n\nThis is the body content.");
 
         var context = CreateContext();
         await _capability.ExecuteAsync(context, CancellationToken.None);
@@ -70,7 +69,7 @@ public class WriterAgentCapabilityTests
         _promptService.Setup(p => p.RenderAsync("writer", "article", It.IsAny<Dictionary<string, object>>()))
             .ReturnsAsync("article prompt");
 
-        SetupChatResponse("# Article Title\n\nArticle body.");
+        SetupSidecarResponse("# Article Title\n\nArticle body.");
 
         var context = CreateContext(new Dictionary<string, string> { ["template"] = "article" });
         await _capability.ExecuteAsync(context, CancellationToken.None);
@@ -82,7 +81,7 @@ public class WriterAgentCapabilityTests
     public async Task ExecuteAsync_ReturnsAgentOutputWithCreatesContentTrue()
     {
         SetupPrompts("writer", "blog-post");
-        SetupChatResponse("# My Title\n\nContent body here.");
+        SetupSidecarResponse("# My Title\n\nContent body here.");
 
         var context = CreateContext();
         var result = await _capability.ExecuteAsync(context, CancellationToken.None);
@@ -95,7 +94,7 @@ public class WriterAgentCapabilityTests
     public async Task ExecuteAsync_ParsesTitleFromResponse()
     {
         SetupPrompts("writer", "blog-post");
-        SetupChatResponse("# The Great Title\n\nSome content body.");
+        SetupSidecarResponse("# The Great Title\n\nSome content body.");
 
         var context = CreateContext();
         var result = await _capability.ExecuteAsync(context, CancellationToken.None);
@@ -108,7 +107,7 @@ public class WriterAgentCapabilityTests
     public async Task ExecuteAsync_ReturnsFailureOnEmptyResponse()
     {
         SetupPrompts("writer", "blog-post");
-        SetupChatResponse("");
+        SetupSidecarResponse("");
 
         var context = CreateContext();
         var result = await _capability.ExecuteAsync(context, CancellationToken.None);
@@ -126,7 +125,7 @@ public class WriterAgentCapabilityTests
         _promptService.Setup(p => p.RenderAsync("writer", "blog-post", It.IsAny<Dictionary<string, object>>()))
             .ReturnsAsync("task prompt");
 
-        SetupChatResponse("# Title\n\nBody");
+        SetupSidecarResponse("# Title\n\nBody");
 
         var context = CreateContext();
         await _capability.ExecuteAsync(context, CancellationToken.None);
@@ -137,20 +136,20 @@ public class WriterAgentCapabilityTests
     }
 
     [Fact]
-    public async Task ExecuteAsync_ReturnsFailureOnChatClientException()
+    public async Task ExecuteAsync_ReturnsFailureOnSidecarException()
     {
         SetupPrompts("writer", "blog-post");
-        _chatClient.Setup(c => c.GetResponseAsync(
-                It.IsAny<IList<ChatMessage>>(),
-                It.IsAny<ChatOptions>(),
+        _sidecarClient.Setup(c => c.SendTaskAsync(
+                It.IsAny<string>(),
+                It.IsAny<string?>(),
                 It.IsAny<CancellationToken>()))
-            .ThrowsAsync(new HttpRequestException("API unavailable"));
+            .Throws(new InvalidOperationException("Sidecar connection lost"));
 
         var context = CreateContext();
         var result = await _capability.ExecuteAsync(context, CancellationToken.None);
 
         Assert.False(result.IsSuccess);
-        Assert.DoesNotContain("API unavailable", result.Errors[0]);
+        Assert.DoesNotContain("Sidecar connection lost", result.Errors[0]);
     }
 
     [Fact]
@@ -163,7 +162,7 @@ public class WriterAgentCapabilityTests
         _promptService.Setup(p => p.RenderAsync("writer", "blog-post", It.IsAny<Dictionary<string, object>>()))
             .ReturnsAsync("task prompt");
 
-        SetupChatResponse("# Title\n\nBody");
+        SetupSidecarResponse("# Title\n\nBody");
 
         var context = CreateContext(new Dictionary<string, string> { ["topic"] = "AI" });
         await _capability.ExecuteAsync(context, CancellationToken.None);
@@ -182,13 +181,21 @@ public class WriterAgentCapabilityTests
             .ReturnsAsync("task prompt");
     }
 
-    private void SetupChatResponse(string text)
+    private void SetupSidecarResponse(string text)
     {
-        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
-        _chatClient.Setup(c => c.GetResponseAsync(
-                It.IsAny<IList<ChatMessage>>(),
-                It.IsAny<ChatOptions>(),
+        _sidecarClient.Setup(c => c.SendTaskAsync(
+                It.IsAny<string>(),
+                It.IsAny<string?>(),
                 It.IsAny<CancellationToken>()))
-            .ReturnsAsync(response);
+            .Returns(CreateSidecarEvents(text));
+    }
+
+    private static async IAsyncEnumerable<SidecarEvent> CreateSidecarEvents(
+        string text, [EnumeratorCancellation] CancellationToken ct = default)
+    {
+        if (!string.IsNullOrEmpty(text))
+            yield return new ChatEvent("assistant", text, null, null);
+        yield return new TaskCompleteEvent("mock-session", 100, 50);
+        await Task.CompletedTask;
     }
 }
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/AgentServiceRegistrationTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/AgentServiceRegistrationTests.cs
index 0e2426e..34877ab 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/AgentServiceRegistrationTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/AgentServiceRegistrationTests.cs
@@ -24,8 +24,8 @@ public class AgentServiceRegistrationTests : IClassFixture<AgentServiceRegistrat
         {
             builder.ConfigureTestServices(services =>
             {
-                // Replace ChatClientFactory with mock to avoid real Anthropic API key requirement
-                services.AddSingleton<IChatClientFactory>(new MockChatClientFactory());
+                // Replace SidecarClient with mock to avoid real sidecar connection
+                services.AddSingleton<ISidecarClient>(new MockSidecarClient());
             });
         });
         _scope = factory.Services.CreateScope();
@@ -33,10 +33,10 @@ public class AgentServiceRegistrationTests : IClassFixture<AgentServiceRegistrat
     }
 
     [Fact]
-    public void IChatClientFactory_Resolves()
+    public void ISidecarClient_Resolves()
     {
         var sp = CreateServiceProvider();
-        var service = sp.GetService<IChatClientFactory>();
+        var service = sp.GetService<ISidecarClient>();
         Assert.NotNull(service);
     }
 
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/PlatformServiceRegistrationTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/PlatformServiceRegistrationTests.cs
index 8a08697..5e3d454 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/PlatformServiceRegistrationTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/PlatformServiceRegistrationTests.cs
@@ -27,7 +27,7 @@ public class PlatformServiceRegistrationTests : IClassFixture<PlatformServiceReg
         {
             builder.ConfigureTestServices(services =>
             {
-                services.AddSingleton<IChatClientFactory>(new MockChatClientFactory());
+                services.AddSingleton<ISidecarClient>(new MockSidecarClient());
             });
         });
         _scope = factory.Services.CreateScope();
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockChatClient.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockChatClient.cs
deleted file mode 100644
index ccb9f0d..0000000
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockChatClient.cs
+++ /dev/null
@@ -1,71 +0,0 @@
-using System.Runtime.CompilerServices;
-using Microsoft.Extensions.AI;
-
-namespace PersonalBrandAssistant.Infrastructure.Tests.Mocks;
-
-public sealed class MockChatClient : IChatClient
-{
-    private readonly string _responseText;
-    private readonly int _inputTokens;
-    private readonly int _outputTokens;
-    private int _callCount;
-    private readonly int _failFirstNCalls;
-
-    public MockChatClient(
-        string responseText = "Mock response",
-        int inputTokens = 100,
-        int outputTokens = 50,
-        int failFirstNCalls = 0)
-    {
-        _responseText = responseText;
-        _inputTokens = inputTokens;
-        _outputTokens = outputTokens;
-        _failFirstNCalls = failFirstNCalls;
-    }
-
-    public int CallCount => _callCount;
-
-    public ChatClientMetadata Metadata { get; } = new("MockChatClient", null, "mock-model");
-
-    public async Task<ChatResponse> GetResponseAsync(
-        IEnumerable<ChatMessage> messages,
-        ChatOptions? options = null,
-        CancellationToken cancellationToken = default)
-    {
-        var count = Interlocked.Increment(ref _callCount);
-        if (count <= _failFirstNCalls)
-            throw new HttpRequestException("Simulated transient failure");
-
-        await Task.CompletedTask;
-
-        return new ChatResponse(new ChatMessage(ChatRole.Assistant, _responseText))
-        {
-            Usage = new UsageDetails
-            {
-                InputTokenCount = _inputTokens,
-                OutputTokenCount = _outputTokens,
-            },
-        };
-    }
-
-    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
-        IEnumerable<ChatMessage> messages,
-        ChatOptions? options = null,
-        [EnumeratorCancellation] CancellationToken cancellationToken = default)
-    {
-        var count = Interlocked.Increment(ref _callCount);
-        if (count <= _failFirstNCalls)
-            throw new HttpRequestException("Simulated transient failure");
-
-        var words = _responseText.Split(' ');
-        foreach (var word in words)
-        {
-            await Task.Yield();
-            yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ");
-        }
-    }
-
-    public object? GetService(Type serviceType, object? serviceKey = null) => null;
-
-    public void Dispose() { }
-}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockChatClientFactory.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockChatClientFactory.cs
deleted file mode 100644
index c54a868..0000000
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockChatClientFactory.cs
+++ /dev/null
@@ -1,18 +0,0 @@
-using Microsoft.Extensions.AI;
-using PersonalBrandAssistant.Application.Common.Interfaces;
-using PersonalBrandAssistant.Domain.Enums;
-
-namespace PersonalBrandAssistant.Infrastructure.Tests.Mocks;
-
-public sealed class MockChatClientFactory : IChatClientFactory
-{
-    private readonly MockChatClient _client;
-
-    public MockChatClientFactory(MockChatClient? client = null)
-    {
-        _client = client ?? new MockChatClient();
-    }
-
-    public IChatClient CreateClient(ModelTier tier) => _client;
-    public IChatClient CreateStreamingClient(ModelTier tier) => _client;
-}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockSidecarClient.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockSidecarClient.cs
new file mode 100644
index 0000000..5532fa3
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockSidecarClient.cs
@@ -0,0 +1,24 @@
+using System.Runtime.CompilerServices;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Mocks;
+
+public sealed class MockSidecarClient : ISidecarClient
+{
+    public bool IsConnected => true;
+
+    public Task<SidecarSession> ConnectAsync(CancellationToken ct)
+        => Task.FromResult(new SidecarSession("mock-session", DateTimeOffset.UtcNow));
+
+    public async IAsyncEnumerable<SidecarEvent> SendTaskAsync(
+        string task, string? sessionId, [EnumeratorCancellation] CancellationToken ct)
+    {
+        yield return new ChatEvent("assistant", "Mock response", null, null);
+        yield return new TaskCompleteEvent("mock-session", 100, 50);
+        await Task.CompletedTask;
+    }
+
+    public Task AbortAsync(string? sessionId, CancellationToken ct)
+        => Task.CompletedTask;
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ChatClientFactoryTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ChatClientFactoryTests.cs
deleted file mode 100644
index 6325403..0000000
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ChatClientFactoryTests.cs
+++ /dev/null
@@ -1,302 +0,0 @@
-using Microsoft.Extensions.AI;
-using Microsoft.Extensions.Configuration;
-using Microsoft.Extensions.DependencyInjection;
-using Microsoft.Extensions.Logging;
-using Moq;
-using PersonalBrandAssistant.Application.Common.Interfaces;
-using PersonalBrandAssistant.Domain.Enums;
-using PersonalBrandAssistant.Infrastructure.Services;
-
-namespace PersonalBrandAssistant.Infrastructure.Tests.Services;
-
-public class ChatClientFactoryTests
-{
-    private readonly Mock<IServiceScopeFactory> _scopeFactory;
-    private readonly Mock<ILogger<ChatClientFactory>> _logger;
-
-    public ChatClientFactoryTests()
-    {
-        _scopeFactory = new Mock<IServiceScopeFactory>();
-        _logger = new Mock<ILogger<ChatClientFactory>>();
-    }
-
-    private IConfiguration BuildConfig(
-        string apiKey = "test-api-key",
-        Dictionary<string, string?>? models = null)
-    {
-        var configData = new Dictionary<string, string?>
-        {
-            ["AgentOrchestration:ApiKey"] = apiKey
-        };
-
-        if (models is not null)
-        {
-            foreach (var (tier, modelId) in models)
-            {
-                configData[$"AgentOrchestration:Models:{tier}"] = modelId;
-            }
-        }
-
-        return new ConfigurationBuilder()
-            .AddInMemoryCollection(configData)
-            .Build();
-    }
-
-    private ChatClientFactory CreateFactory(
-        string apiKey = "test-api-key",
-        Dictionary<string, string?>? models = null)
-    {
-        return new ChatClientFactory(
-            BuildConfig(apiKey, models),
-            _scopeFactory.Object,
-            _logger.Object);
-    }
-
-    [Theory]
-    [InlineData(ModelTier.Fast, "claude-haiku-4-5")]
-    [InlineData(ModelTier.Standard, "claude-sonnet-4-5-20250929")]
-    [InlineData(ModelTier.Advanced, "claude-opus-4-6")]
-    public void CreateClient_MapsDefaultTierToCorrectModelId(ModelTier tier, string expectedModelId)
-    {
-        var factory = CreateFactory();
-
-        var modelId = factory.GetModelId(tier);
-
-        Assert.Equal(expectedModelId, modelId);
-    }
-
-    [Fact]
-    public void CreateClient_ReturnsWrappedChatClient()
-    {
-        var factory = CreateFactory();
-
-        var client = factory.CreateClient(ModelTier.Standard);
-
-        Assert.NotNull(client);
-        Assert.IsType<TokenTrackingDecorator>(client);
-    }
-
-    [Fact]
-    public void CreateClient_UsesConfiguredModelIds()
-    {
-        var models = new Dictionary<string, string?>
-        {
-            ["Fast"] = "custom-fast-model",
-            ["Standard"] = "custom-standard-model",
-            ["Advanced"] = "custom-advanced-model"
-        };
-        var factory = CreateFactory(models: models);
-
-        Assert.Equal("custom-fast-model", factory.GetModelId(ModelTier.Fast));
-        Assert.Equal("custom-standard-model", factory.GetModelId(ModelTier.Standard));
-        Assert.Equal("custom-advanced-model", factory.GetModelId(ModelTier.Advanced));
-    }
-
-    [Fact]
-    public void Constructor_ThrowsWhenApiKeyIsMissing()
-    {
-        Assert.Throws<InvalidOperationException>(() =>
-            CreateFactory(apiKey: ""));
-    }
-
-    [Fact]
-    public void CreateStreamingClient_ReturnsSameClientAsCreateClient()
-    {
-        var factory = CreateFactory();
-
-        var client = factory.CreateClient(ModelTier.Fast);
-        var streamingClient = factory.CreateStreamingClient(ModelTier.Fast);
-
-        Assert.Same(client, streamingClient);
-    }
-
-    [Fact]
-    public void CreateClient_CachesClientsPerTier()
-    {
-        var factory = CreateFactory();
-
-        var first = factory.CreateClient(ModelTier.Standard);
-        var second = factory.CreateClient(ModelTier.Standard);
-
-        Assert.Same(first, second);
-    }
-
-    [Fact]
-    public void CreateClient_DifferentTiersReturnDifferentClients()
-    {
-        var factory = CreateFactory();
-
-        var fast = factory.CreateClient(ModelTier.Fast);
-        var standard = factory.CreateClient(ModelTier.Standard);
-
-        Assert.NotSame(fast, standard);
-    }
-}
-
-public class AgentExecutionContextTests
-{
-    [Fact]
-    public void CurrentExecutionId_DefaultsToNull()
-    {
-        Assert.Null(AgentExecutionContext.CurrentExecutionId);
-    }
-
-    [Fact]
-    public void CurrentExecutionId_CanBeSetAndRead()
-    {
-        var id = Guid.NewGuid();
-        AgentExecutionContext.CurrentExecutionId = id;
-
-        Assert.Equal(id, AgentExecutionContext.CurrentExecutionId);
-
-        // Clean up
-        AgentExecutionContext.CurrentExecutionId = null;
-    }
-
-    [Fact]
-    public async Task CurrentExecutionId_IsIsolatedPerAsyncFlow()
-    {
-        var id1 = Guid.NewGuid();
-        var id2 = Guid.NewGuid();
-
-        Guid? capturedInTask1 = null;
-        Guid? capturedInTask2 = null;
-
-        var task1 = Task.Run(() =>
-        {
-            AgentExecutionContext.CurrentExecutionId = id1;
-            Thread.Sleep(50);
-            capturedInTask1 = AgentExecutionContext.CurrentExecutionId;
-        });
-
-        var task2 = Task.Run(() =>
-        {
-            AgentExecutionContext.CurrentExecutionId = id2;
-            Thread.Sleep(50);
-            capturedInTask2 = AgentExecutionContext.CurrentExecutionId;
-        });
-
-        await Task.WhenAll(task1, task2);
-
-        Assert.Equal(id1, capturedInTask1);
-        Assert.Equal(id2, capturedInTask2);
-    }
-}
-
-public class TokenTrackingDecoratorTests
-{
-    private readonly Mock<ITokenTracker> _tokenTracker;
-    private readonly Mock<IServiceScopeFactory> _scopeFactory;
-
-    public TokenTrackingDecoratorTests()
-    {
-        _tokenTracker = new Mock<ITokenTracker>();
-        _scopeFactory = new Mock<IServiceScopeFactory>();
-
-        var scope = new Mock<IServiceScope>();
-        var asyncScope = new Mock<IAsyncDisposable>();
-        var serviceProvider = new Mock<IServiceProvider>();
-        serviceProvider.Setup(sp => sp.GetService(typeof(ITokenTracker)))
-            .Returns(_tokenTracker.Object);
-        scope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
-
-        _scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
-    }
-
-    [Fact]
-    public async Task GetResponseAsync_RecordsUsageWhenExecutionIdIsSet()
-    {
-        var executionId = Guid.NewGuid();
-        var innerClient = new Mock<IChatClient>();
-        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, "hello")])
-        {
-            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 50 }
-        };
-        innerClient.Setup(c => c.GetResponseAsync(
-            It.IsAny<IEnumerable<ChatMessage>>(),
-            It.IsAny<ChatOptions?>(),
-            It.IsAny<CancellationToken>()))
-            .ReturnsAsync(response);
-
-        var decorator = new TokenTrackingDecorator(innerClient.Object, _scopeFactory.Object, "claude-test");
-
-        AgentExecutionContext.CurrentExecutionId = executionId;
-        try
-        {
-            await decorator.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);
-
-            _tokenTracker.Verify(t => t.RecordUsageAsync(
-                executionId, "claude-test", 100, 50, 0, 0,
-                It.IsAny<CancellationToken>()), Times.Once);
-        }
-        finally
-        {
-            AgentExecutionContext.CurrentExecutionId = null;
-        }
-    }
-
-    [Fact]
-    public async Task GetResponseAsync_SkipsRecordingWhenNoExecutionId()
-    {
-        var innerClient = new Mock<IChatClient>();
-        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, "hello")])
-        {
-            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 50 }
-        };
-        innerClient.Setup(c => c.GetResponseAsync(
-            It.IsAny<IEnumerable<ChatMessage>>(),
-            It.IsAny<ChatOptions?>(),
-            It.IsAny<CancellationToken>()))
-            .ReturnsAsync(response);
-
-        var decorator = new TokenTrackingDecorator(innerClient.Object, _scopeFactory.Object, "claude-test");
-
-        AgentExecutionContext.CurrentExecutionId = null;
-        await decorator.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);
-
-        _tokenTracker.Verify(t => t.RecordUsageAsync(
-            It.IsAny<Guid>(), It.IsAny<string>(),
-            It.IsAny<int>(), It.IsAny<int>(),
-            It.IsAny<int>(), It.IsAny<int>(),
-            It.IsAny<CancellationToken>()), Times.Never);
-    }
-
-    [Fact]
-    public async Task GetResponseAsync_DoesNotThrowWhenTrackerFails()
-    {
-        var executionId = Guid.NewGuid();
-        var innerClient = new Mock<IChatClient>();
-        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, "hello")])
-        {
-            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 50 }
-        };
-        innerClient.Setup(c => c.GetResponseAsync(
-            It.IsAny<IEnumerable<ChatMessage>>(),
-            It.IsAny<ChatOptions?>(),
-            It.IsAny<CancellationToken>()))
-            .ReturnsAsync(response);
-
-        _tokenTracker.Setup(t => t.RecordUsageAsync(
-            It.IsAny<Guid>(), It.IsAny<string>(),
-            It.IsAny<int>(), It.IsAny<int>(),
-            It.IsAny<int>(), It.IsAny<int>(),
-            It.IsAny<CancellationToken>()))
-            .ThrowsAsync(new InvalidOperationException("DB unavailable"));
-
-        var decorator = new TokenTrackingDecorator(innerClient.Object, _scopeFactory.Object, "claude-test");
-
-        AgentExecutionContext.CurrentExecutionId = executionId;
-        try
-        {
-            var result = await decorator.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);
-
-            // Should still return the response despite tracker failure
-            Assert.NotNull(result);
-            Assert.Equal("hello", result.Messages[0].Text);
-        }
-        finally
-        {
-            AgentExecutionContext.CurrentExecutionId = null;
-        }
-    }
-}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/TokenTrackerTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/TokenTrackerTests.cs
index 1fb2603..ac730be 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/TokenTrackerTests.cs
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/TokenTrackerTests.cs
@@ -26,12 +26,6 @@ public class TokenTrackerTests
         {
             DailyBudget = 10.00m,
             MonthlyBudget = 100.00m,
-            Pricing = new Dictionary<string, ModelPricingOptions>
-            {
-                ["claude-haiku-4-5"] = new() { InputPerMillion = 1.00m, OutputPerMillion = 5.00m },
-                ["claude-sonnet-4-5-20250929"] = new() { InputPerMillion = 3.00m, OutputPerMillion = 15.00m },
-                ["claude-opus-4-6"] = new() { InputPerMillion = 5.00m, OutputPerMillion = 25.00m }
-            }
         };
     }
 
@@ -55,71 +49,53 @@ public class TokenTrackerTests
         var tracker = CreateTracker();
 
         await tracker.RecordUsageAsync(
-            execution.Id, "claude-sonnet-4-5-20250929",
+            execution.Id, "sidecar",
             1000, 500, 0, 0, CancellationToken.None);
 
         Assert.Equal(1000, execution.InputTokens);
         Assert.Equal(500, execution.OutputTokens);
-        Assert.Equal("claude-sonnet-4-5-20250929", execution.ModelId);
+        Assert.Equal("sidecar", execution.ModelId);
         _dbContext.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
     }
 
     [Fact]
-    public async Task RecordUsageAsync_CalculatesCostFromModelPricing()
+    public async Task RecordUsageAsync_RecordsCostAsZero()
     {
         var execution = TestEntityFactory.CreateRunningAgentExecution();
         SetupDbSet([execution]);
         var tracker = CreateTracker();
 
-        // Sonnet: 3.00 input/M, 15.00 output/M
-        // Cost = (1000/1M * 3.00) + (500/1M * 15.00) = 0.003 + 0.0075 = 0.0105
         await tracker.RecordUsageAsync(
-            execution.Id, "claude-sonnet-4-5-20250929",
+            execution.Id, "sidecar",
             1000, 500, 0, 0, CancellationToken.None);
 
-        Assert.Equal(0.0105m, execution.Cost);
+        Assert.Equal(0m, execution.Cost);
     }
 
     [Fact]
-    public void CalculateCost_ReturnsZeroForUnknownModel()
+    public void CalculateCost_ReturnsZeroForAnyModel()
     {
         var tracker = CreateTracker();
 
-        var cost = tracker.CalculateCost("unknown-model", 1000, 500);
+        var cost = tracker.CalculateCost("sidecar", 1000, 500);
 
         Assert.Equal(0m, cost);
     }
 
-    [Theory]
-    [InlineData("claude-haiku-4-5", 1000, 500, 0.0035)]       // (1K/1M*1.00) + (500/1M*5.00)
-    [InlineData("claude-sonnet-4-5-20250929", 1000, 500, 0.0105)] // (1K/1M*3.00) + (500/1M*15.00)
-    [InlineData("claude-opus-4-6", 1000, 500, 0.0175)]          // (1K/1M*5.00) + (500/1M*25.00)
-    public void CalculateCost_UsesCorrectRatesPerModel(
-        string modelId, int inputTokens, int outputTokens, decimal expectedCost)
-    {
-        var tracker = CreateTracker();
-
-        var cost = tracker.CalculateCost(modelId, inputTokens, outputTokens);
-
-        Assert.Equal(expectedCost, cost);
-    }
-
     [Fact]
     public async Task GetCostForPeriodAsync_SumsCostsInDateRange()
     {
-        // Create completed executions with costs
         var e1 = TestEntityFactory.CreateRunningAgentExecution();
-        e1.RecordUsage("claude-haiku-4-5", 1000, 500, 0, 0, 5.00m);
+        e1.RecordUsage("sidecar", 1000, 500, 0, 0, 5.00m);
         e1.Complete("output1");
 
         var e2 = TestEntityFactory.CreateRunningAgentExecution();
-        e2.RecordUsage("claude-sonnet-4-5-20250929", 2000, 1000, 0, 0, 10.00m);
+        e2.RecordUsage("sidecar", 2000, 1000, 0, 0, 10.00m);
         e2.Complete("output2");
 
         SetupDbSet([e1, e2]);
         var tracker = CreateTracker();
 
-        // Both should be within the range (CompletedAt is just set to "now")
         var from = DateTimeOffset.UtcNow.AddMinutes(-1);
         var to = DateTimeOffset.UtcNow.AddMinutes(1);
 
@@ -132,11 +108,10 @@ public class TokenTrackerTests
     public async Task GetCostForPeriodAsync_ExcludesNonCompletedExecutions()
     {
         var running = TestEntityFactory.CreateRunningAgentExecution();
-        running.RecordUsage("claude-haiku-4-5", 1000, 500, 0, 0, 5.00m);
-        // Not completed — should be excluded
+        running.RecordUsage("sidecar", 1000, 500, 0, 0, 5.00m);
 
         var completed = TestEntityFactory.CreateRunningAgentExecution();
-        completed.RecordUsage("claude-sonnet-4-5-20250929", 2000, 1000, 0, 0, 10.00m);
+        completed.RecordUsage("sidecar", 2000, 1000, 0, 0, 10.00m);
         completed.Complete("done");
 
         SetupDbSet([running, completed]);
@@ -153,7 +128,7 @@ public class TokenTrackerTests
     [Fact]
     public async Task IsOverBudgetAsync_ReturnsFalseWhenUnderBudget()
     {
-        SetupDbSet([]); // No executions = no spend
+        SetupDbSet([]);
         var tracker = CreateTracker();
 
         var result = await tracker.IsOverBudgetAsync(CancellationToken.None);
@@ -167,9 +142,8 @@ public class TokenTrackerTests
         SetupDbSet([]);
         var tracker = CreateTracker();
 
-        // Should not throw
         await tracker.RecordUsageAsync(
-            Guid.NewGuid(), "claude-haiku-4-5",
+            Guid.NewGuid(), "sidecar",
             1000, 500, 0, 0, CancellationToken.None);
 
         _dbContext.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
