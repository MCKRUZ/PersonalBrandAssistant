using PersonalBrandAssistant.Application.Common.Models.Skills;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IContextBudgetTracker
{
    void RecordTokens(string component, int tokens);
    BudgetAssessment AssessContinuation();
    int TotalTokens { get; }
}
