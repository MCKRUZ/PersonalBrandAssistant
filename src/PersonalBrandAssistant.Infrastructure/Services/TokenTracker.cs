using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Enums;

namespace PersonalBrandAssistant.Infrastructure.Services;

public sealed class TokenTracker : ITokenTracker
{
    private readonly IApplicationDbContext _dbContext;
    private readonly AgentOrchestrationOptions _options;
    private readonly ILogger<TokenTracker> _logger;

    public TokenTracker(
        IApplicationDbContext dbContext,
        IOptions<AgentOrchestrationOptions> options,
        ILogger<TokenTracker> logger)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task RecordUsageAsync(
        Guid executionId,
        string modelId,
        int inputTokens,
        int outputTokens,
        int cacheReadTokens,
        int cacheCreationTokens,
        decimal cost,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentOutOfRangeException.ThrowIfNegative(inputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(outputTokens);

        var execution = await _dbContext.AgentExecutions
            .FirstOrDefaultAsync(e => e.Id == executionId, ct);

        if (execution is null)
        {
            _logger.LogWarning(
                "AgentExecution {ExecutionId} not found for token recording", executionId);
            return;
        }

        if (cost == 0m)
        {
            _logger.LogWarning(
                "Sidecar reported zero cost for execution {ExecutionId} — budget enforcement may be inaccurate",
                executionId);
        }

        execution.RecordUsage(modelId, inputTokens, outputTokens,
            cacheReadTokens, cacheCreationTokens, cost);

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<decimal> GetCostForPeriodAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        return await _dbContext.AgentExecutions
            .Where(e => e.Status == AgentExecutionStatus.Completed
                && e.CompletedAt != null
                && e.CompletedAt >= from
                && e.CompletedAt <= to)
            .SumAsync(e => e.Cost, ct);
    }

    public async Task<decimal> GetBudgetRemainingAsync(CancellationToken ct)
    {
        var todayStart = new DateTimeOffset(
            DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        var monthStart = new DateTimeOffset(
            new DateOnly(todayStart.Year, todayStart.Month, 1),
            TimeOnly.MinValue, TimeSpan.Zero);
        var now = DateTimeOffset.UtcNow;

        // Single query: get monthly spend, then filter for daily in-memory
        var monthlyExecutions = await _dbContext.AgentExecutions
            .Where(e => e.Status == AgentExecutionStatus.Completed
                && e.CompletedAt != null
                && e.CompletedAt >= monthStart
                && e.CompletedAt <= now)
            .Select(e => new { e.CompletedAt, e.Cost })
            .ToListAsync(ct);

        var monthlySpend = monthlyExecutions.Sum(e => e.Cost);
        var dailySpend = monthlyExecutions
            .Where(e => e.CompletedAt >= todayStart)
            .Sum(e => e.Cost);

        var dailyRemaining = _options.DailyBudget - dailySpend;
        var monthlyRemaining = _options.MonthlyBudget - monthlySpend;

        return Math.Min(dailyRemaining, monthlyRemaining);
    }

    public async Task<bool> IsOverBudgetAsync(CancellationToken ct)
    {
        var remaining = await GetBudgetRemainingAsync(ct);
        return remaining <= 0;
    }

}
