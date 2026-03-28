using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IDailyContentOrchestrator
{
    Task<AutomationRunResult> ExecuteAsync(ContentAutomationOptions options, CancellationToken ct);
}
