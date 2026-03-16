namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface ITokenTracker
{
    Task RecordUsageAsync(
        Guid executionId,
        string modelId,
        int inputTokens,
        int outputTokens,
        int cacheReadTokens,
        int cacheCreationTokens,
        decimal cost,
        CancellationToken ct);

    Task<decimal> GetCostForPeriodAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<decimal> GetBudgetRemainingAsync(CancellationToken ct);
    Task<bool> IsOverBudgetAsync(CancellationToken ct);
}
