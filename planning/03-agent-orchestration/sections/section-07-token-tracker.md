# Section 07 -- Token Tracker

## Overview

This section implements the `TokenTracker` service in the Infrastructure layer. It is responsible for:

- Recording token usage (input, output, cache read, cache creation) against `AgentExecution` entities
- Calculating costs based on a configurable per-model pricing table
- Enforcing daily and monthly budget limits
- Querying cost totals for arbitrary date ranges

The `TokenTracker` is a scoped service that depends on `IApplicationDbContext` for persistence and reads pricing/budget configuration from `IOptions<AgentOrchestrationOptions>`.

## Dependencies

- **Section 01 (Domain Entities):** `AgentExecution` entity with `RecordUsage()` method
- **Section 03 (Interfaces):** `ITokenTracker` interface definition in the Application layer
- **Section 04 (EF Core Config):** `AgentExecution` DbSet registered in `IApplicationDbContext`

## File Paths

| File | Action |
|------|--------|
| `src/PersonalBrandAssistant.Infrastructure/Services/TokenTracker.cs` | Create |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/TokenTrackerTests.cs` | Create |

## Configuration

The `TokenTracker` reads from the `AgentOrchestration` configuration section:

```
AgentOrchestration:DailyBudget = 10.00        (decimal)
AgentOrchestration:MonthlyBudget = 100.00      (decimal)
AgentOrchestration:Pricing:<modelId>:InputPerMillion = <rate>
AgentOrchestration:Pricing:<modelId>:OutputPerMillion = <rate>
```

Pricing entries per model:

| Model ID | Input (per million tokens) | Output (per million tokens) |
|----------|----------------------------|------------------------------|
| `claude-haiku-4-5` | 1.00 | 5.00 |
| `claude-sonnet-4-5-20250929` | 3.00 | 15.00 |
| `claude-opus-4-6` | 5.00 | 25.00 |

These values are bound to an options class (`AgentOrchestrationOptions`) registered elsewhere (Section 11). The options class should contain:

```csharp
public class AgentOrchestrationOptions
{
    public decimal DailyBudget { get; init; } = 10.00m;
    public decimal MonthlyBudget { get; init; } = 100.00m;
    public Dictionary<string, ModelPricingOptions> Pricing { get; init; } = new();
}

public class ModelPricingOptions
{
    public decimal InputPerMillion { get; init; }
    public decimal OutputPerMillion { get; init; }
}
```

## Tests First

All tests go in `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/TokenTrackerTests.cs`. Tests use xUnit, Moq, and AAA pattern. The `IApplicationDbContext` is mocked using MockQueryable.Moq.

### Test Stubs

```csharp
// Test: RecordUsageAsync updates AgentExecution with token counts
//   Arrange: Create AgentExecution (status Running) in mock DbSet.
//   Act: Call RecordUsageAsync with executionId, modelId, input/output/cache tokens.
//   Assert: Entity's RecordUsage() was invoked. SaveChangesAsync was called.

// Test: RecordUsageAsync calculates cost based on model pricing config
//   Arrange: Configure pricing for "claude-sonnet-4-5-20250929" (3.00 input, 15.00 output per million).
//   Act: Call RecordUsageAsync with 1000 input tokens and 500 output tokens.
//   Assert: Cost = (1000/1_000_000 * 3.00) + (500/1_000_000 * 15.00) = 0.0105

// Test: GetCostForPeriodAsync sums costs in date range
//   Arrange: Seed multiple AgentExecution entities with varying CompletedAt dates and costs.
//   Act: Call GetCostForPeriodAsync with a from/to range that includes some but not all.
//   Assert: Returns sum of costs for executions within the range only.

// Test: GetBudgetRemainingAsync returns daily budget minus today's spend
//   Arrange: Set DailyBudget = 10.00. Seed executions for today totaling 3.50 cost.
//   Act: Call GetBudgetRemainingAsync.
//   Assert: Returns 6.50.

// Test: IsOverBudgetAsync returns true when daily budget exceeded
//   Arrange: Set DailyBudget = 5.00. Seed today's executions totaling 6.00.
//   Assert: Returns true.

// Test: IsOverBudgetAsync returns true when monthly budget exceeded
//   Arrange: Set MonthlyBudget = 50.00. Seed this month's executions totaling 55.00.
//   Assert: Returns true.

// Test: IsOverBudgetAsync returns false when under both budgets
//   Arrange: Set DailyBudget = 10.00, MonthlyBudget = 100.00. Today = 2.00, month = 30.00.
//   Assert: Returns false.

// Test: Cost calculation uses correct per-million-token rates per model
//   Arrange: Configure different pricing for Haiku vs Sonnet vs Opus.
//   Act: Call RecordUsageAsync with same token counts but different model IDs.
//   Assert: Each produces a different cost matching its model's pricing.
```

## Implementation Details

### TokenTracker Class

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/TokenTracker.cs`

**Namespace:** `PersonalBrandAssistant.Infrastructure.Services`

**Constructor dependencies:**
- `IApplicationDbContext dbContext`
- `IOptions<AgentOrchestrationOptions> options`

### Method: RecordUsageAsync

Cost calculation logic:
1. Look up pricing for `modelId` in `options.Pricing` dictionary.
2. If model not found in pricing, use a fallback of 0 cost (log a warning -- do not throw).
3. Calculate: `cost = (inputTokens / 1_000_000m * pricing.InputPerMillion) + (outputTokens / 1_000_000m * pricing.OutputPerMillion)`.
4. Cache read/creation tokens do not affect cost currently.
5. Find the `AgentExecution` entity from the DbSet, call `entity.RecordUsage(...)`.
6. Call `dbContext.SaveChangesAsync(ct)`.

### Method: GetCostForPeriodAsync

Single LINQ query against `dbContext.AgentExecutions` filtering on `CompletedAt` range and `Status == Completed`, then `.SumAsync(e => e.Cost)`.

### Method: GetBudgetRemainingAsync

1. Get today's start and this month's start.
2. Query daily and monthly spend via `GetCostForPeriodAsync`.
3. Return `Math.Min(dailyBudget - dailySpend, monthlyBudget - monthlySpend)`.

### Method: IsOverBudgetAsync

Calls `GetBudgetRemainingAsync` and returns `remaining <= 0`.

### Design Decisions

1. **Cost includes only completed executions.** Failed/cancelled executions have partial usage but don't count toward budget.
2. **Budget check is not transactional.** Small race window acceptable for single-user app. Log warning if spend exceeds budget post-hoc.
3. **No caching of budget queries.** Each call hits the database. Add short-lived cache if needed later.
4. **Pricing fallback.** Missing model ID in config records cost as 0 with a warning.

## Verification

```bash
dotnet test tests/PersonalBrandAssistant.Infrastructure.Tests --filter "TokenTrackerTests"
```

---

## Implementation Notes (Post-Implementation)

### What was built
All files from the inventory were created as planned. Additionally, `AgentOrchestrationOptions` and `ModelPricingOptions` were created in the Application layer as the options class needed by TokenTracker.

### Actual file paths
| File | Action |
|------|--------|
| `src/PersonalBrandAssistant.Infrastructure/Services/TokenTracker.cs` | Created |
| `src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs` | Created |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/TokenTrackerTests.cs` | Created |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/PersonalBrandAssistant.Infrastructure.Tests.csproj` | Modified (added MockQueryable.Moq 7.0.3) |

### Deviations from plan
1. **ModelPricingOptions as `record`:** Changed from `class` to `record` per project conventions (immutable patterns).
2. **AgentOrchestrationOptions includes `PromptsPath`:** Added `PromptsPath` property (used by PromptTemplateService from section-05) alongside the budget/pricing properties.
3. **Single DB query for budget:** `GetBudgetRemainingAsync` uses a single query to fetch monthly executions, then filters daily in-memory — instead of two separate calls to `GetCostForPeriodAsync`.
4. **Input validation:** Added `ArgumentException.ThrowIfNullOrWhiteSpace(modelId)` and `ArgumentOutOfRangeException.ThrowIfNegative` for token counts in `RecordUsageAsync`.
5. **ILogger injection:** Constructor takes `ILogger<TokenTracker>` for warning on missing executions and unknown model pricing.
6. **MockQueryable.Moq v7.0.3:** Used `.AsQueryable().BuildMockDbSet()` pattern matching existing test infrastructure.

### Test count
10 tests total: RecordUsage (2), CalculateCost (2 including Theory with 3 models), GetCostForPeriod (2), IsOverBudget (1), HandleExecutionNotFound (1). All passing.
