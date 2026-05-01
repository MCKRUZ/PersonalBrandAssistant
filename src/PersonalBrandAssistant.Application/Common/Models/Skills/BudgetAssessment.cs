namespace PersonalBrandAssistant.Application.Common.Models.Skills;

public enum BudgetDecision { Continue, Nudge, Stop }

public record BudgetAssessment(
    BudgetDecision Decision,
    string Reason,
    int TokensUsed,
    int TokensRemaining);
