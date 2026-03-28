using System.Text;
using Microsoft.Extensions.Logging;
using PersonalBrandAssistant.Application.Common.Errors;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Agents.Capabilities;

public abstract class AgentCapabilityBase : IAgentCapability
{
    private readonly ILogger _logger;

    protected AgentCapabilityBase(ILogger logger)
    {
        _logger = logger;
    }

    public abstract AgentCapabilityType Type { get; }
    public abstract ModelTier DefaultModelTier { get; }
    protected abstract string AgentName { get; }
    protected abstract string DefaultTemplate { get; }
    protected abstract bool CreatesContent { get; }

    public async Task<Result<AgentOutput>> ExecuteAsync(AgentContext context, CancellationToken ct)
    {
        try
        {
            var templateName = context.Parameters.GetValueOrDefault("template", DefaultTemplate);
            var variables = BuildVariables(context);

            var systemPrompt = await context.PromptService.RenderAsync(AgentName, "system", variables);
            var taskPrompt = await context.PromptService.RenderAsync(AgentName, templateName, variables);

            var responseBuilder = new StringBuilder();
            var inputTokens = 0;
            var outputTokens = 0;
            var cacheReadTokens = 0;
            var cacheCreationTokens = 0;
            var cost = 0m;
            var fileChanges = new List<string>();

            await foreach (var evt in context.SidecarClient.SendTaskAsync(taskPrompt, systemPrompt, context.SessionId, ct))
            {
                switch (evt)
                {
                    case ChatEvent { Text: not null } chat:
                        responseBuilder.Append(chat.Text);
                        break;

                    case FileChangeEvent fileChange:
                        fileChanges.Add($"{fileChange.ChangeType}:{fileChange.FilePath}");
                        break;

                    case TaskCompleteEvent complete:
                        inputTokens = complete.InputTokens;
                        outputTokens = complete.OutputTokens;
                        cacheReadTokens = complete.CacheReadTokens;
                        cacheCreationTokens = complete.CacheCreationTokens;
                        cost = complete.Cost;
                        break;

                    case ErrorEvent error:
                        _logger.LogError("{Capability} received error from sidecar: {Error}", Type, error.Message);
                        return Result<AgentOutput>.Failure(ErrorCode.InternalError, error.Message);
                }
            }

            var responseText = responseBuilder.ToString();

            if (string.IsNullOrWhiteSpace(responseText))
            {
                _logger.LogWarning("{Capability} received empty response from sidecar", Type);
                return Result<AgentOutput>.Failure(ErrorCode.InternalError,
                    $"{Type} capability received empty response from sidecar");
            }

            return BuildOutput(responseText, inputTokens, outputTokens, cacheReadTokens, cacheCreationTokens, cost, fileChanges);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Capability} failed during execution", Type);
            return Result<AgentOutput>.Failure(ErrorCode.InternalError,
                $"{Type} capability encountered an unexpected error");
        }
    }

    protected virtual Dictionary<string, object> BuildVariables(AgentContext context)
    {
        var variables = new Dictionary<string, object>
        {
            ["brand"] = context.BrandProfile
        };

        if (context.Content is not null)
            variables["content"] = context.Content;

        variables["task"] = context.Parameters;

        return variables;
    }

    protected virtual Result<AgentOutput> BuildOutput(
        string responseText, int inputTokens, int outputTokens,
        int cacheReadTokens, int cacheCreationTokens, decimal cost, List<string> fileChanges)
    {
        var metadata = new Dictionary<string, string>();
        if (fileChanges.Count > 0)
            metadata["file_changes"] = string.Join(";", fileChanges);

        return Result<AgentOutput>.Success(new AgentOutput
        {
            GeneratedText = responseText,
            CreatesContent = CreatesContent,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheReadTokens = cacheReadTokens,
            CacheCreationTokens = cacheCreationTokens,
            Cost = cost,
            Metadata = metadata,
        });
    }
}
