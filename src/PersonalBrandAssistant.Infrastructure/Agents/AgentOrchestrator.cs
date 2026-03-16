using System.Collections.Frozen;
using System.Net.WebSockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Agents;

public class AgentOrchestrator : IAgentOrchestrator
{
    private readonly FrozenDictionary<AgentCapabilityType, IAgentCapability> _capabilities;
    private readonly ITokenTracker _tokenTracker;
    private readonly ISidecarClient _sidecarClient;
    private readonly IPromptTemplateService _promptTemplateService;
    private readonly IApplicationDbContext _dbContext;
    private readonly IWorkflowEngine _workflowEngine;
    private readonly INotificationService _notificationService;
    private readonly AgentOrchestrationOptions _options;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(
        IEnumerable<IAgentCapability> capabilities,
        ITokenTracker tokenTracker,
        ISidecarClient sidecarClient,
        IPromptTemplateService promptTemplateService,
        IApplicationDbContext dbContext,
        IWorkflowEngine workflowEngine,
        INotificationService notificationService,
        IOptions<AgentOrchestrationOptions> options,
        ILogger<AgentOrchestrator> logger)
    {
        _capabilities = capabilities.ToFrozenDictionary(c => c.Type);
        _tokenTracker = tokenTracker;
        _sidecarClient = sidecarClient;
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

            var execution = AgentExecution.Create(task.Type, capability.DefaultModelTier, task.ContentId);
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

            try
            {
                var context = BuildAgentContext(execution.Id, brandProfile, content, task.Parameters);
                var result = await ExecuteWithRetryAsync(capability, context, task.Type, timeoutCts.Token);

                if (!result.IsSuccess)
                {
                    execution.Fail(string.Join("; ", result.Errors));
                    await _dbContext.SaveChangesAsync(ct);
                    return Result<AgentExecutionResult>.Failure(result.ErrorCode, result.Errors.ToArray());
                }

                var output = result.Value!;
                await RecordUsageAsync(execution, output, ct);
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

                await _notificationService.SendAsync(
                    NotificationType.ContentFailed,
                    "Agent Execution Timed Out",
                    $"Agent {task.Type} timed out after {_options.ExecutionTimeoutSeconds}s.",
                    task.ContentId, ct);

                return Result<AgentExecutionResult>.Failure(ErrorCode.InternalError, "Execution timed out");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent {AgentType} failed during execution", task.Type);
                execution.Fail(ex.Message);
                await _dbContext.SaveChangesAsync(ct);

                await _notificationService.SendAsync(
                    NotificationType.ContentFailed,
                    "Agent Execution Failed",
                    $"Agent {task.Type} failed permanently.",
                    task.ContentId, ct);

                return Result<AgentExecutionResult>.Failure(ErrorCode.InternalError, ex.Message);
            }
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

    private async Task<Result<AgentOutput>> ExecuteWithRetryAsync(
        IAgentCapability capability,
        AgentContext context,
        AgentCapabilityType type,
        CancellationToken ct)
    {
        var maxAttempts = _options.MaxRetries + 1;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var result = await capability.ExecuteAsync(context, ct);

                if (!result.IsSuccess && attempt < maxAttempts && IsTransientError(result))
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                    _logger.LogWarning(
                        "Agent {AgentType} attempt {Attempt}/{Max} returned transient error, retrying in {Delay}s",
                        type, attempt, maxAttempts, delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                    continue;
                }

                return result;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransientSidecarError(ex))
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                _logger.LogWarning(ex,
                    "Agent {AgentType} attempt {Attempt}/{Max} failed with transient error, retrying in {Delay}s",
                    type, attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
        }

        return Result<AgentOutput>.Failure(ErrorCode.InternalError,
            $"{type} capability failed after {maxAttempts} attempts");
    }

    private static bool IsTransientSidecarError(Exception ex) =>
        ex is WebSocketException or InvalidOperationException { Message: "Not connected to sidecar" };

    private static bool IsTransientError(Result<AgentOutput> result) =>
        result.ErrorCode == ErrorCode.InternalError
        && result.Errors.Any(e => e.Contains("sidecar", StringComparison.OrdinalIgnoreCase));

    private AgentContext BuildAgentContext(
        Guid executionId,
        BrandProfilePromptModel brandProfile,
        ContentPromptModel? content,
        Dictionary<string, string> parameters)
    {
        return new AgentContext
        {
            ExecutionId = executionId,
            BrandProfile = brandProfile,
            Content = content,
            PromptService = _promptTemplateService,
            SidecarClient = _sidecarClient,
            // Each execution runs in a fresh sidecar session. Session reuse for
            // multi-turn workflows (e.g., outline-then-draft) is deferred to section-04.
            SessionId = null,
            Parameters = parameters,
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
        AgentExecution execution, AgentOutput output, CancellationToken ct)
    {
        if (output.InputTokens > 0 || output.OutputTokens > 0)
        {
            await _tokenTracker.RecordUsageAsync(
                execution.Id,
                "sidecar",
                output.InputTokens,
                output.OutputTokens,
                output.CacheReadTokens,
                output.CacheCreationTokens,
                output.Cost,
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

    private static string? TruncateSummary(string? text) =>
        text?.Length > 500 ? text[..500] : text;
}
