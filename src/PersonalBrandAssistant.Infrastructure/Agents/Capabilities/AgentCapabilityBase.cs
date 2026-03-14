using Microsoft.Extensions.AI;
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

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, taskPrompt)
            };

            var response = await context.ChatClient.GetResponseAsync(messages, cancellationToken: ct);
            var responseText = response.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(responseText))
            {
                _logger.LogWarning("{Capability} received empty response from chat client", Type);
                return Result<AgentOutput>.Failure(ErrorCode.InternalError,
                    $"{Type} capability received empty response from model");
            }

            return BuildOutput(responseText, response.Usage);
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

    protected virtual Result<AgentOutput> BuildOutput(string responseText, UsageDetails? usage)
    {
        return Result<AgentOutput>.Success(new AgentOutput
        {
            GeneratedText = responseText,
            CreatesContent = CreatesContent,
            InputTokens = (int)(usage?.InputTokenCount ?? 0),
            OutputTokens = (int)(usage?.OutputTokenCount ?? 0)
        });
    }
}
