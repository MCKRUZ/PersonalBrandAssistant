namespace PersonalBrandAssistant.Application.Common.Models;

public class AgentOrchestrationOptions
{
    public const string SectionName = "AgentOrchestration";

    public decimal DailyBudget { get; init; } = 10.00m;
    public decimal MonthlyBudget { get; init; } = 100.00m;
    public string PromptsPath { get; init; } = "prompts";
    public int MaxRetries { get; init; } = 2;
    public int ExecutionTimeoutSeconds { get; init; } = 180;
    public bool LogPromptContent { get; init; }
}
