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
            Pricing = new Dictionary<string, ModelPricingOptions>
            {
                ["claude-haiku-4-5"] = new() { InputPerMillion = 1.00m, OutputPerMillion = 5.00m },
                ["claude-sonnet-4-5-20250929"] = new() { InputPerMillion = 3.00m, OutputPerMillion = 15.00m },
                ["claude-opus-4-6"] = new() { InputPerMillion = 5.00m, OutputPerMillion = 25.00m }
            }
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
            execution.Id, "claude-sonnet-4-5-20250929",
            1000, 500, 0, 0, CancellationToken.None);

        Assert.Equal(1000, execution.InputTokens);
        Assert.Equal(500, execution.OutputTokens);
        Assert.Equal("claude-sonnet-4-5-20250929", execution.ModelId);
        _dbContext.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordUsageAsync_CalculatesCostFromModelPricing()
    {
        var execution = TestEntityFactory.CreateRunningAgentExecution();
        SetupDbSet([execution]);
        var tracker = CreateTracker();

        // Sonnet: 3.00 input/M, 15.00 output/M
        // Cost = (1000/1M * 3.00) + (500/1M * 15.00) = 0.003 + 0.0075 = 0.0105
        await tracker.RecordUsageAsync(
            execution.Id, "claude-sonnet-4-5-20250929",
            1000, 500, 0, 0, CancellationToken.None);

        Assert.Equal(0.0105m, execution.Cost);
    }

    [Fact]
    public void CalculateCost_ReturnsZeroForUnknownModel()
    {
        var tracker = CreateTracker();

        var cost = tracker.CalculateCost("unknown-model", 1000, 500);

        Assert.Equal(0m, cost);
    }

    [Theory]
    [InlineData("claude-haiku-4-5", 1000, 500, 0.0035)]       // (1K/1M*1.00) + (500/1M*5.00)
    [InlineData("claude-sonnet-4-5-20250929", 1000, 500, 0.0105)] // (1K/1M*3.00) + (500/1M*15.00)
    [InlineData("claude-opus-4-6", 1000, 500, 0.0175)]          // (1K/1M*5.00) + (500/1M*25.00)
    public void CalculateCost_UsesCorrectRatesPerModel(
        string modelId, int inputTokens, int outputTokens, decimal expectedCost)
    {
        var tracker = CreateTracker();

        var cost = tracker.CalculateCost(modelId, inputTokens, outputTokens);

        Assert.Equal(expectedCost, cost);
    }

    [Fact]
    public async Task GetCostForPeriodAsync_SumsCostsInDateRange()
    {
        // Create completed executions with costs
        var e1 = TestEntityFactory.CreateRunningAgentExecution();
        e1.RecordUsage("claude-haiku-4-5", 1000, 500, 0, 0, 5.00m);
        e1.Complete("output1");

        var e2 = TestEntityFactory.CreateRunningAgentExecution();
        e2.RecordUsage("claude-sonnet-4-5-20250929", 2000, 1000, 0, 0, 10.00m);
        e2.Complete("output2");

        SetupDbSet([e1, e2]);
        var tracker = CreateTracker();

        // Both should be within the range (CompletedAt is just set to "now")
        var from = DateTimeOffset.UtcNow.AddMinutes(-1);
        var to = DateTimeOffset.UtcNow.AddMinutes(1);

        var totalCost = await tracker.GetCostForPeriodAsync(from, to, CancellationToken.None);

        Assert.Equal(15.00m, totalCost);
    }

    [Fact]
    public async Task GetCostForPeriodAsync_ExcludesNonCompletedExecutions()
    {
        var running = TestEntityFactory.CreateRunningAgentExecution();
        running.RecordUsage("claude-haiku-4-5", 1000, 500, 0, 0, 5.00m);
        // Not completed — should be excluded

        var completed = TestEntityFactory.CreateRunningAgentExecution();
        completed.RecordUsage("claude-sonnet-4-5-20250929", 2000, 1000, 0, 0, 10.00m);
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
        SetupDbSet([]); // No executions = no spend
        var tracker = CreateTracker();

        var result = await tracker.IsOverBudgetAsync(CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task RecordUsageAsync_HandlesExecutionNotFound()
    {
        SetupDbSet([]);
        var tracker = CreateTracker();

        // Should not throw
        await tracker.RecordUsageAsync(
            Guid.NewGuid(), "claude-haiku-4-5",
            1000, 500, 0, 0, CancellationToken.None);

        _dbContext.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
