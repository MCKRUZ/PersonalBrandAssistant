using PersonalBrandAssistant.Domain.Common;

namespace PersonalBrandAssistant.Domain.Entities;

public class AgentExecutionLog : EntityBase
{
    private const int MaxContentLength = 2000;

    private AgentExecutionLog() { }

    public Guid AgentExecutionId { get; private init; }
    public int StepNumber { get; private init; }
    public string StepType { get; private init; } = string.Empty;
    public string? Content { get; private init; }
    public int TokensUsed { get; private init; }
    public DateTimeOffset Timestamp { get; private init; }

    public static AgentExecutionLog Create(
        Guid agentExecutionId,
        int stepNumber,
        string stepType,
        string? content,
        int tokensUsed) =>
        new()
        {
            AgentExecutionId = agentExecutionId,
            StepNumber = stepNumber,
            StepType = stepType,
            Content = content?.Length > MaxContentLength ? content[..MaxContentLength] : content,
            TokensUsed = tokensUsed,
            Timestamp = DateTimeOffset.UtcNow,
        };
}
