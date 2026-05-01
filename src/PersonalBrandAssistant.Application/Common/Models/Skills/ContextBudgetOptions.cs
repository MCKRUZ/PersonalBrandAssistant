namespace PersonalBrandAssistant.Application.Common.Models.Skills;

public class ContextBudgetOptions
{
    public const string SectionName = "ContextBudget";

    // Thresholds assume a 200k context window model.
    // Adjust if using a model with a different context window.
    public int NudgeThreshold { get; init; } = 80_000;
    public int StopThreshold { get; init; } = 180_000;
    public int HardMaxTokens { get; init; } = 200_000;
}
