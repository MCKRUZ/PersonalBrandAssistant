diff --git a/planning/03-agent-orchestration/implementation/deep_implement_config.json b/planning/03-agent-orchestration/implementation/deep_implement_config.json
index dd3fdb3..b7a0026 100644
--- a/planning/03-agent-orchestration/implementation/deep_implement_config.json
+++ b/planning/03-agent-orchestration/implementation/deep_implement_config.json
@@ -47,6 +47,10 @@
     "section-07-token-tracker": {
       "status": "complete",
       "commit_hash": "583de9b"
+    },
+    "section-08-agent-capabilities": {
+      "status": "complete",
+      "commit_hash": "df02842"
     }
   },
   "pre_commit": {
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs b/src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs
index 7969fd6..4ca0549 100644
--- a/src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs
+++ b/src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs
@@ -7,6 +7,8 @@ public class AgentOrchestrationOptions
     public decimal DailyBudget { get; init; } = 10.00m;
     public decimal MonthlyBudget { get; init; } = 100.00m;
     public string PromptsPath { get; init; } = "prompts";
+    public int ExecutionTimeoutSeconds { get; init; } = 120;
+    public int MaxRetriesPerExecution { get; init; } = 3;
     public Dictionary<string, ModelPricingOptions> Pricing { get; init; } = new();
 }
 
diff --git a/src/PersonalBrandAssistant.Infrastructure/Agents/AgentOrchestrator.cs b/src/PersonalBrandAssistant.Infrastructure/Agents/AgentOrchestrator.cs
new file mode 100644
index 0000000..82d7d66
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Agents/AgentOrchestrator.cs
@@ -0,0 +1,333 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services;
+
+namespace PersonalBrandAssistant.Infrastructure.Agents;
+
+public class AgentOrchestrator : IAgentOrchestrator
+{
+    private readonly Dictionary<AgentCapabilityType, IAgentCapability> _capabilities;
+    private readonly ITokenTracker _tokenTracker;
+    private readonly IChatClientFactory _chatClientFactory;
+    private readonly IPromptTemplateService _promptTemplateService;
+    private readonly IApplicationDbContext _dbContext;
+    private readonly IWorkflowEngine _workflowEngine;
+    private readonly INotificationService _notificationService;
+    private readonly AgentOrchestrationOptions _options;
+    private readonly ILogger<AgentOrchestrator> _logger;
+
+    public AgentOrchestrator(
+        IEnumerable<IAgentCapability> capabilities,
+        ITokenTracker tokenTracker,
+        IChatClientFactory chatClientFactory,
+        IPromptTemplateService promptTemplateService,
+        IApplicationDbContext dbContext,
+        IWorkflowEngine workflowEngine,
+        INotificationService notificationService,
+        IOptions<AgentOrchestrationOptions> options,
+        ILogger<AgentOrchestrator> logger)
+    {
+        _capabilities = capabilities.ToDictionary(c => c.Type);
+        _tokenTracker = tokenTracker;
+        _chatClientFactory = chatClientFactory;
+        _promptTemplateService = promptTemplateService;
+        _dbContext = dbContext;
+        _workflowEngine = workflowEngine;
+        _notificationService = notificationService;
+        _options = options.Value;
+        _logger = logger;
+    }
+
+    public async Task<Result<AgentExecutionResult>> ExecuteAsync(AgentTask task, CancellationToken ct)
+    {
+        try
+        {
+            if (await _tokenTracker.IsOverBudgetAsync(ct))
+            {
+                _logger.LogWarning("Agent execution rejected: budget exceeded for {AgentType}", task.Type);
+                await _notificationService.SendAsync(
+                    NotificationType.ContentFailed,
+                    "Budget Exceeded",
+                    $"Agent execution for {task.Type} was rejected because the budget has been exceeded.",
+                    task.ContentId, ct);
+                return Result<AgentExecutionResult>.Failure(ErrorCode.ValidationFailed, "Budget exceeded");
+            }
+
+            if (!_capabilities.TryGetValue(task.Type, out var capability))
+            {
+                return Result<AgentExecutionResult>.Failure(
+                    ErrorCode.ValidationFailed, $"No capability registered for {task.Type}");
+            }
+
+            var modelTier = capability.DefaultModelTier;
+            var execution = AgentExecution.Create(task.Type, modelTier, task.ContentId);
+            _dbContext.AgentExecutions.Add(execution);
+            await _dbContext.SaveChangesAsync(ct);
+
+            var brandProfile = await LoadBrandProfileAsync(ct);
+            var content = task.ContentId.HasValue
+                ? await LoadContentModelAsync(task.ContentId.Value, ct)
+                : null;
+
+            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
+            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.ExecutionTimeoutSeconds));
+
+            execution.MarkRunning();
+            await _dbContext.SaveChangesAsync(ct);
+
+            var currentTier = modelTier;
+            var maxRetries = _options.MaxRetriesPerExecution;
+            Exception? lastException = null;
+
+            for (var attempt = 0; attempt <= maxRetries; attempt++)
+            {
+                try
+                {
+                    AgentExecutionContext.CurrentExecutionId = execution.Id;
+
+                    var context = BuildAgentContext(execution.Id, brandProfile, content,
+                        currentTier, task.Parameters);
+
+                    var result = await capability.ExecuteAsync(context, timeoutCts.Token);
+
+                    if (!result.IsSuccess)
+                    {
+                        execution.Fail(string.Join("; ", result.Errors));
+                        await _dbContext.SaveChangesAsync(ct);
+                        return Result<AgentExecutionResult>.Failure(result.ErrorCode, result.Errors.ToArray());
+                    }
+
+                    var output = result.Value!;
+                    await RecordUsageAsync(execution, output, ct);
+                    execution.Complete(TruncateSummary(output.GeneratedText));
+                    await _dbContext.SaveChangesAsync(ct);
+
+                    Guid? createdContentId = null;
+                    if (output.CreatesContent)
+                    {
+                        createdContentId = await CreateContentFromOutputAsync(
+                            task.Type, output, task.ContentId, ct);
+                    }
+
+                    return Result<AgentExecutionResult>.Success(new AgentExecutionResult(
+                        execution.Id, AgentExecutionStatus.Completed, output, createdContentId));
+                }
+                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
+                {
+                    execution.Cancel();
+                    await _dbContext.SaveChangesAsync(ct);
+                    return Result<AgentExecutionResult>.Failure(ErrorCode.InternalError, "Execution timed out");
+                }
+                catch (Exception ex) when (IsTransientError(ex))
+                {
+                    lastException = ex;
+                    _logger.LogWarning(ex, "Transient error on attempt {Attempt} for {AgentType}",
+                        attempt + 1, task.Type);
+
+                    if (attempt < maxRetries)
+                    {
+                        // Downgrade tier on attempt 2+ (index 1+)
+                        if (attempt >= 1)
+                        {
+                            var downgraded = DowngradeModelTier(currentTier);
+                            if (downgraded.HasValue)
+                            {
+                                _logger.LogInformation("Downgrading model tier from {From} to {To}",
+                                    currentTier, downgraded.Value);
+                                currentTier = downgraded.Value;
+                            }
+                        }
+                        continue;
+                    }
+                }
+                catch (Exception ex)
+                {
+                    lastException = ex;
+                    break; // Non-transient error — fail immediately
+                }
+                finally
+                {
+                    AgentExecutionContext.CurrentExecutionId = null;
+                }
+            }
+
+            // Permanent failure after retries exhausted or non-transient error
+            var errorMessage = lastException?.Message ?? "Unknown error";
+            execution.Fail(errorMessage);
+            await _dbContext.SaveChangesAsync(ct);
+
+            await _notificationService.SendAsync(
+                NotificationType.ContentFailed,
+                "Agent Execution Failed",
+                $"Agent {task.Type} failed after retries: {errorMessage}",
+                task.ContentId, ct);
+
+            return Result<AgentExecutionResult>.Failure(ErrorCode.InternalError, errorMessage);
+        }
+        catch (Exception ex)
+        {
+            _logger.LogError(ex, "Unhandled error in AgentOrchestrator.ExecuteAsync for {AgentType}", task.Type);
+            return Result<AgentExecutionResult>.Failure(ErrorCode.InternalError, ex.Message);
+        }
+    }
+
+    public async Task<Result<AgentExecution>> GetExecutionStatusAsync(Guid executionId, CancellationToken ct)
+    {
+        var execution = await _dbContext.AgentExecutions.FindAsync([executionId], ct);
+        return execution is null
+            ? Result<AgentExecution>.NotFound($"Execution {executionId} not found")
+            : Result<AgentExecution>.Success(execution);
+    }
+
+    public async Task<Result<AgentExecution[]>> ListExecutionsAsync(Guid? contentId, CancellationToken ct)
+    {
+        IQueryable<AgentExecution> query = _dbContext.AgentExecutions;
+        if (contentId.HasValue)
+        {
+            query = query.Where(e => e.ContentId == contentId.Value);
+        }
+        var executions = await query.OrderByDescending(e => e.CreatedAt).ToArrayAsync(ct);
+        return Result<AgentExecution[]>.Success(executions);
+    }
+
+    private AgentContext BuildAgentContext(
+        Guid executionId,
+        BrandProfilePromptModel brandProfile,
+        ContentPromptModel? content,
+        ModelTier tier,
+        Dictionary<string, string> parameters)
+    {
+        var chatClient = _chatClientFactory.CreateClient(tier);
+        return new AgentContext
+        {
+            ExecutionId = executionId,
+            BrandProfile = brandProfile,
+            Content = content,
+            PromptService = _promptTemplateService,
+            ChatClient = chatClient,
+            Parameters = parameters,
+            ModelTier = tier,
+        };
+    }
+
+    private async Task<BrandProfilePromptModel> LoadBrandProfileAsync(CancellationToken ct)
+    {
+        var profile = await _dbContext.BrandProfiles
+            .FirstOrDefaultAsync(p => p.IsActive, ct);
+
+        if (profile is null)
+        {
+            return new BrandProfilePromptModel
+            {
+                Name = "Default",
+                PersonaDescription = "",
+                ToneDescriptors = [],
+                StyleGuidelines = "",
+                PreferredTerms = [],
+                AvoidedTerms = [],
+                Topics = [],
+                ExampleContent = [],
+            };
+        }
+
+        return new BrandProfilePromptModel
+        {
+            Name = profile.Name,
+            PersonaDescription = profile.PersonaDescription,
+            ToneDescriptors = profile.ToneDescriptors,
+            StyleGuidelines = profile.StyleGuidelines,
+            PreferredTerms = profile.VocabularyPreferences.PreferredTerms,
+            AvoidedTerms = profile.VocabularyPreferences.AvoidTerms,
+            Topics = profile.Topics,
+            ExampleContent = profile.ExampleContent,
+        };
+    }
+
+    private async Task<ContentPromptModel?> LoadContentModelAsync(Guid contentId, CancellationToken ct)
+    {
+        var content = await _dbContext.Contents.FindAsync([contentId], ct);
+        if (content is null) return null;
+
+        return new ContentPromptModel
+        {
+            Title = content.Title,
+            Body = content.Body,
+            ContentType = content.ContentType,
+            Status = content.Status,
+            TargetPlatforms = content.TargetPlatforms,
+        };
+    }
+
+    private async Task RecordUsageAsync(AgentExecution execution, AgentOutput output, CancellationToken ct)
+    {
+        if (output.InputTokens > 0 || output.OutputTokens > 0)
+        {
+            await _tokenTracker.RecordUsageAsync(
+                execution.Id,
+                execution.ModelUsed.ToString(),
+                output.InputTokens,
+                output.OutputTokens,
+                output.CacheReadTokens,
+                output.CacheCreationTokens,
+                ct);
+        }
+    }
+
+    private async Task<Guid> CreateContentFromOutputAsync(
+        AgentCapabilityType capabilityType,
+        AgentOutput output,
+        Guid? parentContentId,
+        CancellationToken ct)
+    {
+        var contentType = MapCapabilityToContentType(capabilityType);
+        var content = Content.Create(contentType, output.GeneratedText, output.Title);
+        if (parentContentId.HasValue)
+        {
+            content.ParentContentId = parentContentId;
+        }
+
+        _dbContext.Contents.Add(content);
+        await _dbContext.SaveChangesAsync(ct);
+
+        await _workflowEngine.TransitionAsync(
+            content.Id, ContentStatus.Review,
+            "Agent-generated content", ActorType.Agent, ct);
+
+        return content.Id;
+    }
+
+    private static ContentType MapCapabilityToContentType(AgentCapabilityType capabilityType) =>
+        capabilityType switch
+        {
+            AgentCapabilityType.Writer => ContentType.BlogPost,
+            AgentCapabilityType.Social => ContentType.SocialPost,
+            AgentCapabilityType.Repurpose => ContentType.Thread,
+            _ => ContentType.SocialPost,
+        };
+
+    private static bool IsTransientError(Exception ex) =>
+        ex is HttpRequestException httpEx &&
+            httpEx.StatusCode is
+                System.Net.HttpStatusCode.TooManyRequests or
+                System.Net.HttpStatusCode.InternalServerError or
+                System.Net.HttpStatusCode.BadGateway or
+                System.Net.HttpStatusCode.ServiceUnavailable or
+                System.Net.HttpStatusCode.GatewayTimeout;
+
+    private static ModelTier? DowngradeModelTier(ModelTier current) =>
+        current switch
+        {
+            ModelTier.Advanced => ModelTier.Standard,
+            ModelTier.Standard => ModelTier.Fast,
+            _ => null,
+        };
+
+    private static string? TruncateSummary(string? text) =>
+        text?.Length > 500 ? text[..500] : text;
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/AgentOrchestratorTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/AgentOrchestratorTests.cs
new file mode 100644
index 0000000..587665a
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/AgentOrchestratorTests.cs
@@ -0,0 +1,438 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.AI;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Errors;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Domain.ValueObjects;
+using PersonalBrandAssistant.Infrastructure.Agents;
+using PersonalBrandAssistant.Infrastructure.Tests.Helpers;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Agents;
+
+public class AgentOrchestratorTests
+{
+    private readonly Mock<ITokenTracker> _tokenTracker = new();
+    private readonly Mock<IChatClientFactory> _chatClientFactory = new();
+    private readonly Mock<IPromptTemplateService> _promptTemplateService = new();
+    private readonly Mock<IApplicationDbContext> _dbContext = new();
+    private readonly Mock<IWorkflowEngine> _workflowEngine = new();
+    private readonly Mock<INotificationService> _notificationService = new();
+    private readonly Mock<ILogger<AgentOrchestrator>> _logger = new();
+
+    private readonly Mock<DbSet<AgentExecution>> _executionDbSet = new();
+    private readonly Mock<DbSet<Content>> _contentDbSet = new();
+
+    private readonly AgentOrchestrationOptions _options = new()
+    {
+        ExecutionTimeoutSeconds = 30,
+        MaxRetriesPerExecution = 3,
+    };
+
+    private AgentOrchestrator CreateOrchestrator(
+        IEnumerable<IAgentCapability>? capabilities = null)
+    {
+        var brandProfile = new BrandProfile
+        {
+            Name = "Test Brand",
+            PersonaDescription = "Test persona",
+            StyleGuidelines = "Be concise",
+            IsActive = true,
+        };
+        brandProfile.ToneDescriptors = ["professional"];
+        brandProfile.Topics = ["tech"];
+        brandProfile.ExampleContent = ["Sample post"];
+        brandProfile.VocabularyPreferences = new VocabularyConfig
+        {
+            PreferredTerms = ["AI"],
+            AvoidTerms = ["synergy"],
+        };
+
+        var brandProfileDbSet = AsyncQueryableHelpers.CreateAsyncDbSetMock(
+            new List<BrandProfile> { brandProfile });
+
+        _dbContext.Setup(x => x.AgentExecutions).Returns(_executionDbSet.Object);
+        _dbContext.Setup(x => x.Contents).Returns(_contentDbSet.Object);
+        _dbContext.Setup(x => x.BrandProfiles).Returns(brandProfileDbSet.Object);
+        _dbContext.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
+            .ReturnsAsync(1);
+
+        return new AgentOrchestrator(
+            capabilities ?? [],
+            _tokenTracker.Object,
+            _chatClientFactory.Object,
+            _promptTemplateService.Object,
+            _dbContext.Object,
+            _workflowEngine.Object,
+            _notificationService.Object,
+            Options.Create(_options),
+            _logger.Object);
+    }
+
+    private static Mock<IAgentCapability> CreateCapabilityMock(
+        AgentCapabilityType type,
+        ModelTier defaultTier = ModelTier.Standard,
+        AgentOutput? output = null)
+    {
+        var mock = new Mock<IAgentCapability>();
+        mock.Setup(x => x.Type).Returns(type);
+        mock.Setup(x => x.DefaultModelTier).Returns(defaultTier);
+        mock.Setup(x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<AgentOutput>.Success(output ?? new AgentOutput
+            {
+                GeneratedText = "Test output",
+                CreatesContent = false,
+            }));
+        return mock;
+    }
+
+    // --- Task Routing Tests ---
+
+    [Fact]
+    public async Task ExecuteAsync_RoutesWriterTask_ToWriterCapability()
+    {
+        var writerCapability = CreateCapabilityMock(AgentCapabilityType.Writer);
+        var orchestrator = CreateOrchestrator([writerCapability.Object]);
+        var task = new AgentTask(AgentCapabilityType.Writer, null, new());
+
+        await orchestrator.ExecuteAsync(task, CancellationToken.None);
+
+        writerCapability.Verify(
+            x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()),
+            Times.Once);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_RoutesSocialTask_ToSocialCapability()
+    {
+        var socialCapability = CreateCapabilityMock(AgentCapabilityType.Social, ModelTier.Fast);
+        var orchestrator = CreateOrchestrator([socialCapability.Object]);
+        var task = new AgentTask(AgentCapabilityType.Social, null, new());
+
+        await orchestrator.ExecuteAsync(task, CancellationToken.None);
+
+        socialCapability.Verify(
+            x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()),
+            Times.Once);
+    }
+
+    [Theory]
+    [InlineData(AgentCapabilityType.Writer)]
+    [InlineData(AgentCapabilityType.Social)]
+    [InlineData(AgentCapabilityType.Repurpose)]
+    [InlineData(AgentCapabilityType.Engagement)]
+    [InlineData(AgentCapabilityType.Analytics)]
+    public async Task ExecuteAsync_RoutesEachType_ToCorrectCapability(AgentCapabilityType type)
+    {
+        var capability = CreateCapabilityMock(type);
+        var orchestrator = CreateOrchestrator([capability.Object]);
+        var task = new AgentTask(type, null, new());
+
+        await orchestrator.ExecuteAsync(task, CancellationToken.None);
+
+        capability.Verify(
+            x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()),
+            Times.Once);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_ReturnsValidationFailed_WhenOverBudget()
+    {
+        _tokenTracker.Setup(x => x.IsOverBudgetAsync(It.IsAny<CancellationToken>()))
+            .ReturnsAsync(true);
+        var orchestrator = CreateOrchestrator();
+        var task = new AgentTask(AgentCapabilityType.Writer, null, new());
+
+        var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.ValidationFailed, result.ErrorCode);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_CreatesAgentExecution_BeforeCallingCapability()
+    {
+        var callOrder = new List<string>();
+
+        _executionDbSet.Setup(x => x.Add(It.IsAny<AgentExecution>()))
+            .Callback<AgentExecution>(e =>
+            {
+                callOrder.Add("add_execution");
+                Assert.Equal(AgentExecutionStatus.Pending, e.Status);
+            });
+
+        var capability = CreateCapabilityMock(AgentCapabilityType.Writer);
+        capability.Setup(x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
+            .Callback<AgentContext, CancellationToken>((_, _) => callOrder.Add("execute"))
+            .ReturnsAsync(Result<AgentOutput>.Success(new AgentOutput
+            {
+                GeneratedText = "output",
+                CreatesContent = false,
+            }));
+
+        var orchestrator = CreateOrchestrator([capability.Object]);
+        var task = new AgentTask(AgentCapabilityType.Writer, null, new());
+
+        await orchestrator.ExecuteAsync(task, CancellationToken.None);
+
+        Assert.Equal(["add_execution", "execute"], callOrder);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_SetsExecutionToCompleted_OnSuccess()
+    {
+        AgentExecution? capturedExecution = null;
+        _executionDbSet.Setup(x => x.Add(It.IsAny<AgentExecution>()))
+            .Callback<AgentExecution>(e => capturedExecution = e);
+
+        var capability = CreateCapabilityMock(AgentCapabilityType.Writer);
+        var orchestrator = CreateOrchestrator([capability.Object]);
+        var task = new AgentTask(AgentCapabilityType.Writer, null, new());
+
+        var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.NotNull(capturedExecution);
+        Assert.Equal(AgentExecutionStatus.Completed, capturedExecution!.Status);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_SetsExecutionToFailed_OnCapabilityFailure()
+    {
+        AgentExecution? capturedExecution = null;
+        _executionDbSet.Setup(x => x.Add(It.IsAny<AgentExecution>()))
+            .Callback<AgentExecution>(e => capturedExecution = e);
+
+        var capability = new Mock<IAgentCapability>();
+        capability.Setup(x => x.Type).Returns(AgentCapabilityType.Writer);
+        capability.Setup(x => x.DefaultModelTier).Returns(ModelTier.Standard);
+        capability.Setup(x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<AgentOutput>.Failure(ErrorCode.InternalError, "Prompt error"));
+
+        var orchestrator = CreateOrchestrator([capability.Object]);
+        var task = new AgentTask(AgentCapabilityType.Writer, null, new());
+
+        var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.NotNull(capturedExecution);
+        Assert.Equal(AgentExecutionStatus.Failed, capturedExecution!.Status);
+    }
+
+    // --- Content Creation Tests ---
+
+    [Fact]
+    public async Task ExecuteAsync_CreatesContent_WhenOutputCreatesContentIsTrue()
+    {
+        var output = new AgentOutput
+        {
+            GeneratedText = "Blog post body",
+            Title = "My Post",
+            CreatesContent = true,
+        };
+        var capability = CreateCapabilityMock(AgentCapabilityType.Writer, output: output);
+        var orchestrator = CreateOrchestrator([capability.Object]);
+        var task = new AgentTask(AgentCapabilityType.Writer, null, new());
+
+        var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        _contentDbSet.Verify(x => x.Add(It.IsAny<Content>()), Times.Once);
+        Assert.NotNull(result.Value!.CreatedContentId);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_DoesNotCreateContent_WhenOutputCreatesContentIsFalse()
+    {
+        var output = new AgentOutput
+        {
+            GeneratedText = "Analysis report",
+            CreatesContent = false,
+        };
+        var capability = CreateCapabilityMock(AgentCapabilityType.Analytics, output: output);
+        var orchestrator = CreateOrchestrator([capability.Object]);
+        var task = new AgentTask(AgentCapabilityType.Analytics, null, new());
+
+        var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        _contentDbSet.Verify(x => x.Add(It.IsAny<Content>()), Times.Never);
+        Assert.Null(result.Value!.CreatedContentId);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_SubmitsToWorkflow_WhenContentIsCreated()
+    {
+        var output = new AgentOutput
+        {
+            GeneratedText = "Post content",
+            CreatesContent = true,
+        };
+        _workflowEngine
+            .Setup(x => x.TransitionAsync(
+                It.IsAny<Guid>(), ContentStatus.Review, It.IsAny<string?>(),
+                ActorType.Agent, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<MediatR.Unit>.Success(MediatR.Unit.Value));
+
+        var capability = CreateCapabilityMock(AgentCapabilityType.Social, ModelTier.Fast, output);
+        var orchestrator = CreateOrchestrator([capability.Object]);
+        var task = new AgentTask(AgentCapabilityType.Social, null, new());
+
+        await orchestrator.ExecuteAsync(task, CancellationToken.None);
+
+        _workflowEngine.Verify(
+            x => x.TransitionAsync(
+                It.IsAny<Guid>(), ContentStatus.Review,
+                It.Is<string?>(s => s != null && s.Contains("Agent")),
+                ActorType.Agent,
+                It.IsAny<CancellationToken>()),
+            Times.Once);
+    }
+
+    // --- Retry and Fallback Tests ---
+
+    [Fact]
+    public async Task ExecuteAsync_RetriesOnTransientError()
+    {
+        var callCount = 0;
+        var capability = new Mock<IAgentCapability>();
+        capability.Setup(x => x.Type).Returns(AgentCapabilityType.Writer);
+        capability.Setup(x => x.DefaultModelTier).Returns(ModelTier.Standard);
+        capability.Setup(x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
+            .Returns<AgentContext, CancellationToken>((_, _) =>
+            {
+                callCount++;
+                if (callCount == 1)
+                    throw new HttpRequestException("Service unavailable", null,
+                        System.Net.HttpStatusCode.ServiceUnavailable);
+                return Task.FromResult(Result<AgentOutput>.Success(new AgentOutput
+                {
+                    GeneratedText = "output",
+                    CreatesContent = false,
+                }));
+            });
+
+        var orchestrator = CreateOrchestrator([capability.Object]);
+        var task = new AgentTask(AgentCapabilityType.Writer, null, new());
+
+        var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(2, callCount);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_DoesNotRetry_OnNonTransientError()
+    {
+        var capability = new Mock<IAgentCapability>();
+        capability.Setup(x => x.Type).Returns(AgentCapabilityType.Writer);
+        capability.Setup(x => x.DefaultModelTier).Returns(ModelTier.Standard);
+        capability.Setup(x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync(Result<AgentOutput>.Failure(ErrorCode.ValidationFailed, "Bad prompt"));
+
+        var orchestrator = CreateOrchestrator([capability.Object]);
+        var task = new AgentTask(AgentCapabilityType.Writer, null, new());
+
+        var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        capability.Verify(
+            x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()),
+            Times.Once);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_DowngradesModelTier_OnSecondTransientFailure()
+    {
+        var tiers = new List<ModelTier>();
+        var callCount = 0;
+
+        var capability = new Mock<IAgentCapability>();
+        capability.Setup(x => x.Type).Returns(AgentCapabilityType.Writer);
+        capability.Setup(x => x.DefaultModelTier).Returns(ModelTier.Advanced);
+        capability.Setup(x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
+            .Returns<AgentContext, CancellationToken>((ctx, _) =>
+            {
+                tiers.Add(ctx.ModelTier);
+                callCount++;
+                if (callCount <= 2)
+                    throw new HttpRequestException("Rate limited", null,
+                        System.Net.HttpStatusCode.TooManyRequests);
+                return Task.FromResult(Result<AgentOutput>.Success(new AgentOutput
+                {
+                    GeneratedText = "output",
+                    CreatesContent = false,
+                }));
+            });
+
+        var orchestrator = CreateOrchestrator([capability.Object]);
+        var task = new AgentTask(AgentCapabilityType.Writer, null, new());
+
+        var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(ModelTier.Advanced, tiers[0]);
+        Assert.Equal(ModelTier.Advanced, tiers[1]);
+        Assert.Equal(ModelTier.Standard, tiers[2]);
+    }
+
+    [Fact]
+    public async Task ExecuteAsync_FailsPermanently_AfterMaxRetries_SendsNotification()
+    {
+        var capability = new Mock<IAgentCapability>();
+        capability.Setup(x => x.Type).Returns(AgentCapabilityType.Writer);
+        capability.Setup(x => x.DefaultModelTier).Returns(ModelTier.Standard);
+        capability.Setup(x => x.ExecuteAsync(It.IsAny<AgentContext>(), It.IsAny<CancellationToken>()))
+            .ThrowsAsync(new HttpRequestException("Server error", null,
+                System.Net.HttpStatusCode.InternalServerError));
+
+        var orchestrator = CreateOrchestrator([capability.Object]);
+        var task = new AgentTask(AgentCapabilityType.Writer, null, new());
+
+        var result = await orchestrator.ExecuteAsync(task, CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        _notificationService.Verify(
+            x => x.SendAsync(
+                NotificationType.ContentFailed, It.IsAny<string>(), It.IsAny<string>(),
+                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
+            Times.Once);
+    }
+
+    // --- Status Query Tests ---
+
+    [Fact]
+    public async Task GetExecutionStatusAsync_ReturnsExecution_ById()
+    {
+        var execution = AgentExecution.Create(AgentCapabilityType.Writer, ModelTier.Standard);
+        var executionId = execution.Id;
+
+        _executionDbSet.Setup(x => x.FindAsync(new object[] { executionId }, It.IsAny<CancellationToken>()))
+            .ReturnsAsync(execution);
+
+        var orchestrator = CreateOrchestrator();
+
+        var result = await orchestrator.GetExecutionStatusAsync(executionId, CancellationToken.None);
+
+        Assert.True(result.IsSuccess);
+        Assert.Equal(executionId, result.Value!.Id);
+    }
+
+    [Fact]
+    public async Task GetExecutionStatusAsync_ReturnsNotFound_ForUnknownId()
+    {
+        _executionDbSet.Setup(x => x.FindAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
+            .ReturnsAsync((AgentExecution?)null);
+
+        var orchestrator = CreateOrchestrator();
+
+        var result = await orchestrator.GetExecutionStatusAsync(Guid.NewGuid(), CancellationToken.None);
+
+        Assert.False(result.IsSuccess);
+        Assert.Equal(ErrorCode.NotFound, result.ErrorCode);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Helpers/AsyncQueryableHelpers.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Helpers/AsyncQueryableHelpers.cs
new file mode 100644
index 0000000..83f8ad6
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Helpers/AsyncQueryableHelpers.cs
@@ -0,0 +1,98 @@
+using System.Linq.Expressions;
+using Microsoft.EntityFrameworkCore;
+using Microsoft.EntityFrameworkCore.Query;
+using Moq;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Helpers;
+
+public static class AsyncQueryableHelpers
+{
+    public static Mock<DbSet<T>> CreateAsyncDbSetMock<T>(IEnumerable<T> data) where T : class
+    {
+        var queryable = data.AsQueryable();
+        var mockSet = new Mock<DbSet<T>>();
+
+        mockSet.As<IAsyncEnumerable<T>>()
+            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
+            .Returns(new TestAsyncEnumerator<T>(queryable.GetEnumerator()));
+
+        mockSet.As<IQueryable<T>>()
+            .Setup(m => m.Provider)
+            .Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
+
+        mockSet.As<IQueryable<T>>()
+            .Setup(m => m.Expression)
+            .Returns(queryable.Expression);
+
+        mockSet.As<IQueryable<T>>()
+            .Setup(m => m.ElementType)
+            .Returns(queryable.ElementType);
+
+        mockSet.As<IQueryable<T>>()
+            .Setup(m => m.GetEnumerator())
+            .Returns(queryable.GetEnumerator());
+
+        return mockSet;
+    }
+}
+
+internal class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
+{
+    private readonly IQueryProvider _inner;
+
+    internal TestAsyncQueryProvider(IQueryProvider inner) => _inner = inner;
+
+    public IQueryable CreateQuery(Expression expression) =>
+        new TestAsyncEnumerable<TEntity>(expression);
+
+    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
+        new TestAsyncEnumerable<TElement>(expression);
+
+    public object? Execute(Expression expression) => _inner.Execute(expression);
+
+    public TResult Execute<TResult>(Expression expression) => _inner.Execute<TResult>(expression);
+
+    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
+    {
+        var expectedResultType = typeof(TResult).GetGenericArguments()[0];
+        var executionResult = typeof(IQueryProvider)
+            .GetMethod(
+                name: nameof(IQueryProvider.Execute),
+                genericParameterCount: 1,
+                types: [typeof(Expression)])!
+            .MakeGenericMethod(expectedResultType)
+            .Invoke(this, [expression]);
+
+        return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!
+            .MakeGenericMethod(expectedResultType)
+            .Invoke(null, [executionResult])!;
+    }
+}
+
+internal class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
+{
+    public TestAsyncEnumerable(IEnumerable<T> enumerable) : base(enumerable) { }
+    public TestAsyncEnumerable(Expression expression) : base(expression) { }
+
+    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
+        new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
+
+    IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
+}
+
+internal class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
+{
+    private readonly IEnumerator<T> _inner;
+
+    public TestAsyncEnumerator(IEnumerator<T> inner) => _inner = inner;
+
+    public T Current => _inner.Current;
+
+    public ValueTask<bool> MoveNextAsync() => new(_inner.MoveNext());
+
+    public ValueTask DisposeAsync()
+    {
+        _inner.Dispose();
+        return ValueTask.CompletedTask;
+    }
+}
