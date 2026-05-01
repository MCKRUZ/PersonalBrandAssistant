using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models.Skills;

namespace PersonalBrandAssistant.Infrastructure.Agents;

public sealed class ContextBudgetTracker : IContextBudgetTracker
{
    private readonly Dictionary<string, int> _components = new();
    private readonly ContextBudgetOptions _options;

    public ContextBudgetTracker(IOptions<ContextBudgetOptions> options)
    {
        _options = options.Value;
    }

    public int TotalTokens => _components.Values.Sum();

    public void RecordTokens(string component, int tokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        if (tokens < 0)
            throw new ArgumentOutOfRangeException(nameof(tokens), "Token count cannot be negative");

        _components.TryGetValue(component, out var existing);
        _components[component] = existing + tokens;
    }

    public BudgetAssessment AssessContinuation()
    {
        var total = TotalTokens;
        var remaining = _options.HardMaxTokens - total;

        if (total >= _options.StopThreshold)
            return new BudgetAssessment(BudgetDecision.Stop, "Context budget exhausted", total, remaining);

        if (total >= _options.NudgeThreshold)
            return new BudgetAssessment(BudgetDecision.Nudge, "Approaching context limit", total, remaining);

        return new BudgetAssessment(BudgetDecision.Continue, "Within budget", total, remaining);
    }
}
