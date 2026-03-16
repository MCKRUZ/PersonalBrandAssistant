using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using PersonalBrandAssistant.Application.Common.Interfaces;
using PersonalBrandAssistant.Application.Common.Models;
using PersonalBrandAssistant.Domain.Entities;
using PersonalBrandAssistant.Domain.Enums;
using PersonalBrandAssistant.Infrastructure.Services;
using PersonalBrandAssistant.Infrastructure.Tests.Utilities;

namespace PersonalBrandAssistant.Infrastructure.Tests.Services;

public class TokenTrackerTests
{
    private readonly Mock<IApplicationDbContext> _dbContext;
    private readonly Mock<ILogger<TokenTracker>> _logger;
    private readonly AgentOrchestrationOptions _options;

    public TokenTrackerTests()
    {
        _dbContext = new Mock<IApplicationDbContext>();
        _logger = new Mock<ILogger<TokenTracker>>();

        _options = new AgentOrchestrationOptions
        {
            DailyBudget = 10.00m,
            MonthlyBudget = 100.00m,
        };
    }

    private TokenTracker CreateTracker(AgentOrchestrationOptions? options = null)
    {
        var opts = Options.Create(options ?? _options);
        return new TokenTracker(_dbContext.Object, opts, _logger.Object);
    }

    private void SetupDbSet(List<AgentExecution> executions)
    {
        var mockDbSet = executions.AsQueryable().BuildMockDbSet();
        _dbContext.Setup(db => db.AgentExecutions).Returns(mockDbSet.Object);
    }

    [Fact]
    public async Task RecordUsageAsync_UpdatesAgentExecutionWithTokenCounts()
    {
        var execution = TestEntityFactory.CreateRunningAgentExecution();
        SetupDbSet([execution]);
        var tracker = CreateTracker();

        await tracker.RecordUsageAsync(
            execution.Id, "sidecar",
            1000, 500, 0, 0, 0.05m, CancellationToken.None);

        Assert.Equal(1000, execution.InputTokens);
        Assert.Equal(500, execution.OutputTokens);
        Assert.Equal("sidecar", execution.ModelId);
        _dbContext.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordUsageAsync_RecordsSidecarReportedCost()
    {
        var execution = TestEntityFactory.CreateRunningAgentExecution();
        SetupDbSet([execution]);
        var tracker = CreateTracker();

        await tracker.RecordUsageAsync(
            execution.Id, "sidecar",
            1000, 500, 50, 25, 0.08m, CancellationToken.None);

        Assert.Equal(0.08m, execution.Cost);
    }

    [Fact]
    public async Task GetCostForPeriodAsync_SumsCostsInDateRange()
    {
        var e1 = TestEntityFactory.CreateRunningAgentExecution();
        e1.RecordUsage("sidecar", 1000, 500, 0, 0, 5.00m);
        e1.Complete("output1");

        var e2 = TestEntityFactory.CreateRunningAgentExecution();
        e2.RecordUsage("sidecar", 2000, 1000, 0, 0, 10.00m);
        e2.Complete("output2");

        SetupDbSet([e1, e2]);
        var tracker = CreateTracker();

        var from = DateTimeOffset.UtcNow.AddMinutes(-1);
        var to = DateTimeOffset.UtcNow.AddMinutes(1);

        var totalCost = await tracker.GetCostForPeriodAsync(from, to, CancellationToken.None);

        Assert.Equal(15.00m, totalCost);
    }

    [Fact]
    public async Task GetCostForPeriodAsync_ExcludesNonCompletedExecutions()
    {
        var running = TestEntityFactory.CreateRunningAgentExecution();
        running.RecordUsage("sidecar", 1000, 500, 0, 0, 5.00m);

        var completed = TestEntityFactory.CreateRunningAgentExecution();
        completed.RecordUsage("sidecar", 2000, 1000, 0, 0, 10.00m);
        completed.Complete("done");

        SetupDbSet([running, completed]);
        var tracker = CreateTracker();

        var from = DateTimeOffset.UtcNow.AddMinutes(-1);
        var to = DateTimeOffset.UtcNow.AddMinutes(1);

        var totalCost = await tracker.GetCostForPeriodAsync(from, to, CancellationToken.None);

        Assert.Equal(10.00m, totalCost);
    }

    [Fact]
    public async Task IsOverBudgetAsync_ReturnsFalseWhenUnderBudget()
    {
        SetupDbSet([]);
        var tracker = CreateTracker();

        var result = await tracker.IsOverBudgetAsync(CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task RecordUsageAsync_HandlesExecutionNotFound()
    {
        SetupDbSet([]);
        var tracker = CreateTracker();

        await tracker.RecordUsageAsync(
            Guid.NewGuid(), "sidecar",
            1000, 500, 0, 0, 0.05m, CancellationToken.None);

        _dbContext.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
