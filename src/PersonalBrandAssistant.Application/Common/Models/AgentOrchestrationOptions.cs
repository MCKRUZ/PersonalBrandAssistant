namespace PersonalBrandAssistant.Application.Common.Models;

public class AgentOrchestrationOptions
{
    public const string SectionName = "AgentOrchestration";

    public decimal DailyBudget { get; init; } = 10.00m;
    public decimal MonthlyBudget { get; init; } = 100.00m;
    public string DefaultModelTier { get; init; } = "Standard";
    public Dictionary<string, string> Models { get; init; } = new();
    public Dictionary<string, ModelPricingOptions> Pricing { get; init; } = new();
    public string PromptsPath { get; init; } = "prompts";
    public int MaxRetriesPerExecution { get; init; } = 3;
    public int ExecutionTimeoutSeconds { get; init; } = 180;
    public bool LogPromptContent { get; init; }
}

public record ModelPricingOptions
{
    public decimal InputPerMillion { get; init; }
    public decimal OutputPerMillion { get; init; }
}
