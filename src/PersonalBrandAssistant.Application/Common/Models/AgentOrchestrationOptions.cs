namespace PersonalBrandAssistant.Application.Common.Models;

public class AgentOrchestrationOptions
{
    public const string SectionName = "AgentOrchestration";

    public decimal DailyBudget { get; init; } = 10.00m;
    public decimal MonthlyBudget { get; init; } = 100.00m;
    public string PromptsPath { get; init; } = "prompts";
    public Dictionary<string, ModelPricingOptions> Pricing { get; init; } = new();
}

public record ModelPricingOptions
{
    public decimal InputPerMillion { get; init; }
    public decimal OutputPerMillion { get; init; }
}
