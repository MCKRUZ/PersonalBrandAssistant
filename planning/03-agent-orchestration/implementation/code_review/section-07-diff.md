diff --git a/planning/03-agent-orchestration/implementation/deep_implement_config.json b/planning/03-agent-orchestration/implementation/deep_implement_config.json
index 69d3a66..8494644 100644
--- a/planning/03-agent-orchestration/implementation/deep_implement_config.json
+++ b/planning/03-agent-orchestration/implementation/deep_implement_config.json
@@ -39,6 +39,10 @@
     "section-05-prompt-system": {
       "status": "complete",
       "commit_hash": "1da6786"
+    },
+    "section-06-chat-client-factory": {
+      "status": "complete",
+      "commit_hash": "dd88f49"
     }
   },
   "pre_commit": {
diff --git a/src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs b/src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs
new file mode 100644
index 0000000..c5c3b9b
--- /dev/null
+++ b/src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs
@@ -0,0 +1,17 @@
+namespace PersonalBrandAssistant.Application.Common.Models;
+
+public class AgentOrchestrationOptions
+{
+    public const string SectionName = "AgentOrchestration";
+
+    public decimal DailyBudget { get; init; } = 10.00m;
+    public decimal MonthlyBudget { get; init; } = 100.00m;
+    public string PromptsPath { get; init; } = "prompts";
+    public Dictionary<string, ModelPricingOptions> Pricing { get; init; } = new();
+}
+
+public class ModelPricingOptions
+{
+    public decimal InputPerMillion { get; init; }
+    public decimal OutputPerMillion { get; init; }
+}
diff --git a/src/PersonalBrandAssistant.Infrastructure/Services/TokenTracker.cs b/src/PersonalBrandAssistant.Infrastructure/Services/TokenTracker.cs
new file mode 100644
index 0000000..9a08bec
--- /dev/null
+++ b/src/PersonalBrandAssistant.Infrastructure/Services/TokenTracker.cs
@@ -0,0 +1,102 @@
+using Microsoft.EntityFrameworkCore;
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Enums;
+
+namespace PersonalBrandAssistant.Infrastructure.Services;
+
+public sealed class TokenTracker : ITokenTracker
+{
+    private readonly IApplicationDbContext _dbContext;
+    private readonly AgentOrchestrationOptions _options;
+    private readonly ILogger<TokenTracker> _logger;
+
+    public TokenTracker(
+        IApplicationDbContext dbContext,
+        IOptions<AgentOrchestrationOptions> options,
+        ILogger<TokenTracker> logger)
+    {
+        _dbContext = dbContext;
+        _options = options.Value;
+        _logger = logger;
+    }
+
+    public async Task RecordUsageAsync(
+        Guid executionId,
+        string modelId,
+        int inputTokens,
+        int outputTokens,
+        int cacheReadTokens,
+        int cacheCreationTokens,
+        CancellationToken ct)
+    {
+        var cost = CalculateCost(modelId, inputTokens, outputTokens);
+
+        var execution = await _dbContext.AgentExecutions
+            .FirstOrDefaultAsync(e => e.Id == executionId, ct);
+
+        if (execution is null)
+        {
+            _logger.LogWarning(
+                "AgentExecution {ExecutionId} not found for token recording", executionId);
+            return;
+        }
+
+        execution.RecordUsage(modelId, inputTokens, outputTokens,
+            cacheReadTokens, cacheCreationTokens, cost);
+
+        await _dbContext.SaveChangesAsync(ct);
+    }
+
+    public async Task<decimal> GetCostForPeriodAsync(
+        DateTimeOffset from,
+        DateTimeOffset to,
+        CancellationToken ct)
+    {
+        return await _dbContext.AgentExecutions
+            .Where(e => e.Status == AgentExecutionStatus.Completed
+                && e.CompletedAt != null
+                && e.CompletedAt >= from
+                && e.CompletedAt <= to)
+            .SumAsync(e => e.Cost, ct);
+    }
+
+    public async Task<decimal> GetBudgetRemainingAsync(CancellationToken ct)
+    {
+        var todayStart = new DateTimeOffset(
+            DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
+        var monthStart = new DateTimeOffset(
+            new DateOnly(todayStart.Year, todayStart.Month, 1),
+            TimeOnly.MinValue, TimeSpan.Zero);
+        var now = DateTimeOffset.UtcNow;
+
+        var dailySpend = await GetCostForPeriodAsync(todayStart, now, ct);
+        var monthlySpend = await GetCostForPeriodAsync(monthStart, now, ct);
+
+        var dailyRemaining = _options.DailyBudget - dailySpend;
+        var monthlyRemaining = _options.MonthlyBudget - monthlySpend;
+
+        return Math.Min(dailyRemaining, monthlyRemaining);
+    }
+
+    public async Task<bool> IsOverBudgetAsync(CancellationToken ct)
+    {
+        var remaining = await GetBudgetRemainingAsync(ct);
+        return remaining <= 0;
+    }
+
+    internal decimal CalculateCost(string modelId, int inputTokens, int outputTokens)
+    {
+        if (!_options.Pricing.TryGetValue(modelId, out var pricing))
+        {
+            _logger.LogWarning(
+                "No pricing configured for model {ModelId}, recording cost as 0", modelId);
+            return 0m;
+        }
+
+        return (inputTokens / 1_000_000m * pricing.InputPerMillion)
+            + (outputTokens / 1_000_000m * pricing.OutputPerMillion);
+    }
+}
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/PersonalBrandAssistant.Infrastructure.Tests.csproj b/tests/PersonalBrandAssistant.Infrastructure.Tests/PersonalBrandAssistant.Infrastructure.Tests.csproj
index 636ddae..ae34b64 100644
--- a/tests/PersonalBrandAssistant.Infrastructure.Tests/PersonalBrandAssistant.Infrastructure.Tests.csproj
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/PersonalBrandAssistant.Infrastructure.Tests.csproj
@@ -8,6 +8,7 @@
     <PackageReference Include="coverlet.collector" Version="6.0.4" />
     <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.5" />
     <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
+    <PackageReference Include="MockQueryable.Moq" Version="7.0.3" />
     <PackageReference Include="Moq" Version="4.20.72" />
     <PackageReference Include="Testcontainers.PostgreSql" Version="4.11.0" />
     <PackageReference Include="xunit" Version="2.9.3" />
diff --git a/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/TokenTrackerTests.cs b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/TokenTrackerTests.cs
new file mode 100644
index 0000000..1fb2603
--- /dev/null
+++ b/tests/PersonalBrandAssistant.Infrastructure.Tests/Services/TokenTrackerTests.cs
@@ -0,0 +1,177 @@
+using Microsoft.Extensions.Logging;
+using Microsoft.Extensions.Options;
+using MockQueryable.Moq;
+using Moq;
+using PersonalBrandAssistant.Application.Common.Interfaces;
+using PersonalBrandAssistant.Application.Common.Models;
+using PersonalBrandAssistant.Domain.Entities;
+using PersonalBrandAssistant.Domain.Enums;
+using PersonalBrandAssistant.Infrastructure.Services;
+using PersonalBrandAssistant.Infrastructure.Tests.Utilities;
+
+namespace PersonalBrandAssistant.Infrastructure.Tests.Services;
+
+public class TokenTrackerTests
+{
+    private readonly Mock<IApplicationDbContext> _dbContext;
+    private readonly Mock<ILogger<TokenTracker>> _logger;
+    private readonly AgentOrchestrationOptions _options;
+
+    public TokenTrackerTests()
+    {
+        _dbContext = new Mock<IApplicationDbContext>();
+        _logger = new Mock<ILogger<TokenTracker>>();
+
+        _options = new AgentOrchestrationOptions
+        {
+            DailyBudget = 10.00m,
+            MonthlyBudget = 100.00m,
+            Pricing = new Dictionary<string, ModelPricingOptions>
+            {
+                ["claude-haiku-4-5"] = new() { InputPerMillion = 1.00m, OutputPerMillion = 5.00m },
+                ["claude-sonnet-4-5-20250929"] = new() { InputPerMillion = 3.00m, OutputPerMillion = 15.00m },
+                ["claude-opus-4-6"] = new() { InputPerMillion = 5.00m, OutputPerMillion = 25.00m }
+            }
+        };
+    }
+
+    private TokenTracker CreateTracker(AgentOrchestrationOptions? options = null)
+    {
+        var opts = Options.Create(options ?? _options);
+        return new TokenTracker(_dbContext.Object, opts, _logger.Object);
+    }
+
+    private void SetupDbSet(List<AgentExecution> executions)
+    {
+        var mockDbSet = executions.AsQueryable().BuildMockDbSet();
+        _dbContext.Setup(db => db.AgentExecutions).Returns(mockDbSet.Object);
+    }
+
+    [Fact]
+    public async Task RecordUsageAsync_UpdatesAgentExecutionWithTokenCounts()
+    {
+        var execution = TestEntityFactory.CreateRunningAgentExecution();
+        SetupDbSet([execution]);
+        var tracker = CreateTracker();
+
+        await tracker.RecordUsageAsync(
+            execution.Id, "claude-sonnet-4-5-20250929",
+            1000, 500, 0, 0, CancellationToken.None);
+
+        Assert.Equal(1000, execution.InputTokens);
+        Assert.Equal(500, execution.OutputTokens);
+        Assert.Equal("claude-sonnet-4-5-20250929", execution.ModelId);
+        _dbContext.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
+    }
+
+    [Fact]
+    public async Task RecordUsageAsync_CalculatesCostFromModelPricing()
+    {
+        var execution = TestEntityFactory.CreateRunningAgentExecution();
+        SetupDbSet([execution]);
+        var tracker = CreateTracker();
+
+        // Sonnet: 3.00 input/M, 15.00 output/M
+        // Cost = (1000/1M * 3.00) + (500/1M * 15.00) = 0.003 + 0.0075 = 0.0105
+        await tracker.RecordUsageAsync(
+            execution.Id, "claude-sonnet-4-5-20250929",
+            1000, 500, 0, 0, CancellationToken.None);
+
+        Assert.Equal(0.0105m, execution.Cost);
+    }
+
+    [Fact]
+    public void CalculateCost_ReturnsZeroForUnknownModel()
+    {
+        var tracker = CreateTracker();
+
+        var cost = tracker.CalculateCost("unknown-model", 1000, 500);
+
+        Assert.Equal(0m, cost);
+    }
+
+    [Theory]
+    [InlineData("claude-haiku-4-5", 1000, 500, 0.0035)]       // (1K/1M*1.00) + (500/1M*5.00)
+    [InlineData("claude-sonnet-4-5-20250929", 1000, 500, 0.0105)] // (1K/1M*3.00) + (500/1M*15.00)
+    [InlineData("claude-opus-4-6", 1000, 500, 0.0175)]          // (1K/1M*5.00) + (500/1M*25.00)
+    public void CalculateCost_UsesCorrectRatesPerModel(
+        string modelId, int inputTokens, int outputTokens, decimal expectedCost)
+    {
+        var tracker = CreateTracker();
+
+        var cost = tracker.CalculateCost(modelId, inputTokens, outputTokens);
+
+        Assert.Equal(expectedCost, cost);
+    }
+
+    [Fact]
+    public async Task GetCostForPeriodAsync_SumsCostsInDateRange()
+    {
+        // Create completed executions with costs
+        var e1 = TestEntityFactory.CreateRunningAgentExecution();
+        e1.RecordUsage("claude-haiku-4-5", 1000, 500, 0, 0, 5.00m);
+        e1.Complete("output1");
+
+        var e2 = TestEntityFactory.CreateRunningAgentExecution();
+        e2.RecordUsage("claude-sonnet-4-5-20250929", 2000, 1000, 0, 0, 10.00m);
+        e2.Complete("output2");
+
+        SetupDbSet([e1, e2]);
+        var tracker = CreateTracker();
+
+        // Both should be within the range (CompletedAt is just set to "now")
+        var from = DateTimeOffset.UtcNow.AddMinutes(-1);
+        var to = DateTimeOffset.UtcNow.AddMinutes(1);
+
+        var totalCost = await tracker.GetCostForPeriodAsync(from, to, CancellationToken.None);
+
+        Assert.Equal(15.00m, totalCost);
+    }
+
+    [Fact]
+    public async Task GetCostForPeriodAsync_ExcludesNonCompletedExecutions()
+    {
+        var running = TestEntityFactory.CreateRunningAgentExecution();
+        running.RecordUsage("claude-haiku-4-5", 1000, 500, 0, 0, 5.00m);
+        // Not completed — should be excluded
+
+        var completed = TestEntityFactory.CreateRunningAgentExecution();
+        completed.RecordUsage("claude-sonnet-4-5-20250929", 2000, 1000, 0, 0, 10.00m);
+        completed.Complete("done");
+
+        SetupDbSet([running, completed]);
+        var tracker = CreateTracker();
+
+        var from = DateTimeOffset.UtcNow.AddMinutes(-1);
+        var to = DateTimeOffset.UtcNow.AddMinutes(1);
+
+        var totalCost = await tracker.GetCostForPeriodAsync(from, to, CancellationToken.None);
+
+        Assert.Equal(10.00m, totalCost);
+    }
+
+    [Fact]
+    public async Task IsOverBudgetAsync_ReturnsFalseWhenUnderBudget()
+    {
+        SetupDbSet([]); // No executions = no spend
+        var tracker = CreateTracker();
+
+        var result = await tracker.IsOverBudgetAsync(CancellationToken.None);
+
+        Assert.False(result);
+    }
+
+    [Fact]
+    public async Task RecordUsageAsync_HandlesExecutionNotFound()
+    {
+        SetupDbSet([]);
+        var tracker = CreateTracker();
+
+        // Should not throw
+        await tracker.RecordUsageAsync(
+            Guid.NewGuid(), "claude-haiku-4-5",
+            1000, 500, 0, 0, CancellationToken.None);
+
+        _dbContext.Verify(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
+    }
+}
