using System.Collections.Frozen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services;

namespace PersonalBrandAssistant.Infrastructure.Agents;

public class AgentOrchestrator : IAgentOrchestrator
{
    private readonly FrozenDictionary<AgentCapabilityType, IAgentCapability> _capabilities;
    private readonly ITokenTracker _tokenTracker;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IPromptTemplateService _promptTemplateService;
    private readonly IApplicationDbContext _dbContext;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly INotificationService _notificationService;
    private readonly AgentOrchestrationOptions _options;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(
        IEnumerable<IAgentCapability> capabilities,
        ITokenTracker tokenTracker,
        IChatClientFactory chatClientFactory,
        IPromptTemplateService promptTemplateService,
        IApplicationDbContext dbContext,
        IWorkflowEngine workflowEngine,
        INotificationService notificationService,
        IOptions<AgentOrchestrationOptions> options,
        ILogger<AgentOrchestrator> logger)
    {
        _capabilities = capabilities.ToFrozenDictionary(c => c.Type);
        _tokenTracker = tokenTracker;
        _chatClientFactory = chatClientFactory;
        _promptTemplateService = promptTemplateService;
        _dbContext = dbContext;
        _workflowEngine = workflowEngine;
        _notificationService = notificationService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<AgentExecutionResult>> ExecuteAsync(AgentTask task, CancellationToken ct)
    {
        try
        {
            if (await _tokenTracker.IsOverBudgetAsync(ct))
            {
                _logger.LogWarning("Agent execution rejected: budget exceeded for {AgentType}", task.Type);
                await _notificationService.SendAsync(
                    NotificationType.ContentFailed,
                    "Budget Exceeded",
                    $"Agent execution for {task.Type} was rejected because the budget has been exceeded.",
                    task.ContentId, ct);
                return Result<AgentExecutionResult>.Failure(ErrorCode.ValidationFailed, "Budget exceeded");
            }

            if (!_capabilities.TryGetValue(task.Type, out var capability))
            {
                return Result<AgentExecutionResult>.Failure(
                    ErrorCode.ValidationFailed, $"No capability registered for {task.Type}");
            }

            var modelTier = capability.DefaultModelTier;
            var execution = AgentExecution.Create(task.Type, modelTier, task.ContentId);
            _dbContext.AgentExecutions.Add(execution);
            await _dbContext.SaveChangesAsync(ct);

            var brandProfile = await LoadBrandProfileAsync(ct);
            var content = task.ContentId.HasValue
                ? await LoadContentModelAsync(task.ContentId.Value, ct)
                : null;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.ExecutionTimeoutSeconds));

            execution.MarkRunning();
            await _dbContext.SaveChangesAsync(ct);

            var currentTier = modelTier;
            var maxRetries = _options.MaxRetriesPerExecution;
            Exception? lastException = null;

            for (var attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (attempt > 0 && await _tokenTracker.IsOverBudgetAsync(timeoutCts.Token))
                    {
                        execution.Fail("Budget exceeded during retries");
                        await _dbContext.SaveChangesAsync(ct);
                        return Result<AgentExecutionResult>.Failure(
                            ErrorCode.ValidationFailed, "Budget exceeded during retries");
                    }

                    AgentExecutionContext.CurrentExecutionId = execution.Id;

                    var context = BuildAgentContext(execution.Id, brandProfile, content,
                        currentTier, task.Parameters);

                    var result = await capability.ExecuteAsync(context, timeoutCts.Token);

                    if (!result.IsSuccess)
                    {
                        execution.Fail(string.Join("; ", result.Errors));
                        await _dbContext.SaveChangesAsync(ct);
                        return Result<AgentExecutionResult>.Failure(result.ErrorCode, result.Errors.ToArray());
                    }

                    var output = result.Value!;
                    await RecordUsageAsync(execution, currentTier, output, ct);
                    execution.Complete(TruncateSummary(output.GeneratedText));
                    await _dbContext.SaveChangesAsync(ct);

                    Guid? createdContentId = null;
                    if (output.CreatesContent)
                    {
                        createdContentId = await CreateContentFromOutputAsync(
                            task.Type, output, task.ContentId, ct);
                    }

                    return Result<AgentExecutionResult>.Success(new AgentExecutionResult(
                        execution.Id, AgentExecutionStatus.Completed, output, createdContentId));
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    execution.Cancel();
                    await _dbContext.SaveChangesAsync(ct);
                    return Result<AgentExecutionResult>.Failure(ErrorCode.InternalError, "Execution timed out");
                }
                catch (Exception ex) when (IsTransientError(ex))
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "Transient error on attempt {Attempt} for {AgentType}",
                        attempt + 1, task.Type);

                    if (attempt < maxRetries)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                        await Task.Delay(delay, ct);

                        if (attempt >= 1)
                        {
                            var downgraded = DowngradeModelTier(currentTier);
                            if (downgraded.HasValue)
                            {
                                _logger.LogInformation("Downgrading model tier from {From} to {To}",
                                    currentTier, downgraded.Value);
                                currentTier = downgraded.Value;
                            }
                        }
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    break;
                }
                finally
                {
                    AgentExecutionContext.CurrentExecutionId = null;
                }
            }

            var errorMessage = lastException?.Message ?? "Unknown error";
            execution.Fail(errorMessage);
            await _dbContext.SaveChangesAsync(ct);

            await _notificationService.SendAsync(
                NotificationType.ContentFailed,
                "Agent Execution Failed",
                $"Agent {task.Type} failed permanently.",
                task.ContentId, ct);

            return Result<AgentExecutionResult>.Failure(ErrorCode.InternalError, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in AgentOrchestrator.ExecuteAsync for {AgentType}", task.Type);
            return Result<AgentExecutionResult>.Failure(ErrorCode.InternalError, ex.Message);
        }
    }

    public async Task<Result<AgentExecution>> GetExecutionStatusAsync(Guid executionId, CancellationToken ct)
    {
        var execution = await _dbContext.AgentExecutions.FindAsync([executionId], ct);
        return execution is null
            ? Result<AgentExecution>.NotFound($"Execution {executionId} not found")
            : Result<AgentExecution>.Success(execution);
    }

    public async Task<Result<AgentExecution[]>> ListExecutionsAsync(Guid? contentId, CancellationToken ct)
    {
        IQueryable<AgentExecution> query = _dbContext.AgentExecutions;
        if (contentId.HasValue)
        {
            query = query.Where(e => e.ContentId == contentId.Value);
        }
        var executions = await query.OrderByDescending(e => e.CreatedAt).ToArrayAsync(ct);
        return Result<AgentExecution[]>.Success(executions);
    }

    private AgentContext BuildAgentContext(
        Guid executionId,
        BrandProfilePromptModel brandProfile,
        ContentPromptModel? content,
        ModelTier tier,
        Dictionary<string, string> parameters)
    {
        var chatClient = _chatClientFactory.CreateClient(tier);
        return new AgentContext
        {
            ExecutionId = executionId,
            BrandProfile = brandProfile,
            Content = content,
            PromptService = _promptTemplateService,
            ChatClient = chatClient,
            Parameters = parameters,
            ModelTier = tier,
        };
    }

    private async Task<BrandProfilePromptModel> LoadBrandProfileAsync(CancellationToken ct)
    {
        var profile = await _dbContext.BrandProfiles
            .FirstOrDefaultAsync(p => p.IsActive, ct);

        if (profile is null)
        {
            return new BrandProfilePromptModel
            {
                Name = "Default",
                PersonaDescription = "",
                ToneDescriptors = [],
                StyleGuidelines = "",
                PreferredTerms = [],
                AvoidedTerms = [],
                Topics = [],
                ExampleContent = [],
            };
        }

        return new BrandProfilePromptModel
        {
            Name = profile.Name,
            PersonaDescription = profile.PersonaDescription,
            ToneDescriptors = profile.ToneDescriptors,
            StyleGuidelines = profile.StyleGuidelines,
            PreferredTerms = profile.VocabularyPreferences.PreferredTerms,
            AvoidedTerms = profile.VocabularyPreferences.AvoidTerms,
            Topics = profile.Topics,
            ExampleContent = profile.ExampleContent,
        };
    }

    private async Task<ContentPromptModel?> LoadContentModelAsync(Guid contentId, CancellationToken ct)
    {
        var content = await _dbContext.Contents.FindAsync([contentId], ct);
        if (content is null) return null;

        return new ContentPromptModel
        {
            Title = content.Title,
            Body = content.Body,
            ContentType = content.ContentType,
            Status = content.Status,
            TargetPlatforms = content.TargetPlatforms,
        };
    }

    private async Task RecordUsageAsync(
        AgentExecution execution, ModelTier actualTier, AgentOutput output, CancellationToken ct)
    {
        if (output.InputTokens > 0 || output.OutputTokens > 0)
        {
            await _tokenTracker.RecordUsageAsync(
                execution.Id,
                actualTier.ToString(),
                output.InputTokens,
                output.OutputTokens,
                output.CacheReadTokens,
                output.CacheCreationTokens,
                ct);
        }
    }

    private async Task<Guid> CreateContentFromOutputAsync(
        AgentCapabilityType capabilityType,
        AgentOutput output,
        Guid? parentContentId,
        CancellationToken ct)
    {
        var contentType = MapCapabilityToContentType(capabilityType);
        var content = Content.Create(contentType, output.GeneratedText, output.Title);
        if (parentContentId.HasValue)
        {
            content.ParentContentId = parentContentId;
        }

        _dbContext.Contents.Add(content);
        await _dbContext.SaveChangesAsync(ct);

        var transitionResult = await _workflowEngine.TransitionAsync(
            content.Id, ContentStatus.Review,
            "Agent-generated content", ActorType.Agent, ct);

        if (!transitionResult.IsSuccess)
        {
            _logger.LogError("Failed to transition content {ContentId} to Review: {Errors}",
                content.Id, string.Join("; ", transitionResult.Errors));
        }

        return content.Id;
    }

    private static ContentType MapCapabilityToContentType(AgentCapabilityType capabilityType) =>
        capabilityType switch
        {
            AgentCapabilityType.Writer => ContentType.BlogPost,
            AgentCapabilityType.Social => ContentType.SocialPost,
            AgentCapabilityType.Repurpose => ContentType.Thread,
            _ => throw new ArgumentOutOfRangeException(nameof(capabilityType),
                $"Capability type {capabilityType} does not map to a content type"),
        };

    private static bool IsTransientError(Exception ex) =>
        ex is HttpRequestException httpEx &&
            httpEx.StatusCode is
                System.Net.HttpStatusCode.TooManyRequests or
                System.Net.HttpStatusCode.InternalServerError or
                System.Net.HttpStatusCode.BadGateway or
                System.Net.HttpStatusCode.ServiceUnavailable or
                System.Net.HttpStatusCode.GatewayTimeout;

    private static ModelTier? DowngradeModelTier(ModelTier current) =>
        current switch
        {
            ModelTier.Advanced => ModelTier.Standard,
            ModelTier.Standard => ModelTier.Fast,
            _ => null,
        };

    private static string? TruncateSummary(string? text) =>
        text?.Length > 500 ? text[..500] : text;
}
