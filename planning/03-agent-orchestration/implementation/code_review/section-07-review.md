# Section 07 - Token Tracker: Code Review

**Reviewer:** code-reviewer agent
**Date:** 2026-03-13
**Verdict:** WARNING -- Approve with requested changes

---

## Summary

This section introduces AgentOrchestrationOptions for budget/pricing configuration, TokenTracker for recording LLM usage and enforcing daily/monthly budgets, and 10 unit tests covering the core paths. The implementation is clean, well-structured, and under 200 lines across all files. The cost calculation math is correct, budget enforcement logic is sound, and test coverage hits the important cases.

There are no critical issues. Several medium-priority items should be addressed before or shortly after merge.

---

## Critical Issues

None found.

---

## Warnings (Should Fix)

### [W-01] RecordUsage overwrites rather than accumulates token counts

**Files:**
- `src/PersonalBrandAssistant.Domain/Entities/AgentExecution.cs:98-103`
- `src/PersonalBrandAssistant.Infrastructure/Services/TokenTracker.cs:47-48`

`AgentExecution.RecordUsage` does a straight assignment (`InputTokens = inputTokens`), not an additive accumulation. If `RecordUsageAsync` is called multiple times for the same execution (e.g., multi-turn agent conversations with multiple LLM calls), only the last tokens and cost are retained. The previous usage is silently discarded.

This is a design decision that should be made explicit. If single-call-per-execution is the intent, add a guard:

```csharp
// In AgentExecution.RecordUsage:
if (ModelId is not null)
    throw new InvalidOperationException("Usage already recorded for this execution.");
```

If multi-call accumulation is the intent, change to additive:

```csharp
ModelId = modelId;
InputTokens += inputTokens;
OutputTokens += outputTokens;
CacheReadTokens += cacheReadTokens;
CacheCreationTokens += cacheCreationTokens;
Cost += cost;
```

### [W-02] GetBudgetRemainingAsync makes two separate DB queries that could be consolidated

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/TokenTracker.cs:66-81`

The daily query is a subset of the monthly query. The monthly period always contains today. This means:
1. Two round-trips to the database when one would suffice.
2. A potential TOCTOU inconsistency -- a new execution could complete between the two queries, making the daily and monthly figures inconsistent with each other.

Fix: Query once for the monthly period and compute the daily subset from the same data, or use a single query that returns both aggregates.

### [W-03] GetCostForPeriodAsync only counts Completed executions -- running executions are invisible to budget checks

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/TokenTracker.cs:58-63`

If an agent is currently running and has already consumed tokens (via `RecordUsage`), those tokens are not counted toward budget because `Status != Completed`. An agent could therefore exceed the budget while running. The budget gate only prevents *new* executions from starting.

This is acceptable if budget enforcement is only a pre-flight check, but should be documented. Consider also counting `Running` executions that have non-zero `Cost`:

```csharp
.Where(e => (e.Status == AgentExecutionStatus.Completed && e.CompletedAt >= from && e.CompletedAt <= to)
          || (e.Status == AgentExecutionStatus.Running && e.Cost > 0))
```

### [W-04] CalculateCost ignores cache tokens

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/TokenTracker.cs:90-101`

The method accepts `inputTokens` and `outputTokens` but ignores `cacheReadTokens` and `cacheCreationTokens` entirely. Anthropic prices cache read tokens at a discount and cache creation tokens at a premium. Recording them in the entity but not in cost calculation means:
- Cost is overestimated when cache reads replace input tokens.
- Cost is underestimated when cache creation tokens are billed at higher rates.

If accurate cost tracking matters (and for a budget system it should), add cache pricing to `ModelPricingOptions`:

```csharp
public class ModelPricingOptions
{
    public decimal InputPerMillion { get; init; }
    public decimal OutputPerMillion { get; init; }
    public decimal CacheReadPerMillion { get; init; }      // typically 10% of input
    public decimal CacheCreationPerMillion { get; init; }   // typically 125% of input
}
```

### [W-05] Missing input validation in RecordUsageAsync

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/TokenTracker.cs:26-51`

`RecordUsageAsync` does not validate its parameters before hitting the database. The domain entity `RecordUsage` validates after the query, but the method should fail fast:
- `modelId` could be null/empty
- Token counts could be negative

Add guard clauses at the method entry:

```csharp
ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
ArgumentOutOfRangeException.ThrowIfNegative(inputTokens);
ArgumentOutOfRangeException.ThrowIfNegative(outputTokens);
```

---

## Suggestions (Consider Improving)

### [S-01] DateTimeOffset.UtcNow is called directly, making GetBudgetRemainingAsync difficult to test deterministically

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/TokenTracker.cs:68-73`

The method uses `DateTimeOffset.UtcNow` directly. Consider injecting `TimeProvider` (available in .NET 8+) so tests can control the clock. This is especially important for testing month-boundary behavior.

```csharp
public sealed class TokenTracker : ITokenTracker
{
    private readonly TimeProvider _timeProvider;
    // ...
    public TokenTracker(..., TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }
}
```

### [S-02] No test for IsOverBudgetAsync returning true

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/TokenTrackerTests.cs`

There is a test for `IsOverBudgetAsync` returning `false` (line 154), but no test verifying it returns `true` when spend exceeds the budget. This is the more important case -- the one that blocks agent execution. Add a test that sets up completed executions totaling more than `DailyBudget` or `MonthlyBudget` and asserts `true`.

### [S-03] No test for GetBudgetRemainingAsync returning the minimum of daily vs monthly remaining

The `Math.Min(dailyRemaining, monthlyRemaining)` logic at line 81 of `TokenTracker.cs` is untested. Consider a test where daily budget is nearly exhausted but monthly is fine, and vice versa.

### [S-04] ModelPricingOptions should be a record for value semantics

**File:** `src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs:14-17`

Per project coding style (immutability, prefer records):

```csharp
public record ModelPricingOptions
{
    public decimal InputPerMillion { get; init; }
    public decimal OutputPerMillion { get; init; }
}
```

### [S-05] AgentOrchestrationOptions.Pricing dictionary uses new() as default -- consider documenting expected keys

The pricing dictionary silently returns cost 0 for unconfigured models. This is safe but could mask configuration errors in production. Consider logging at startup if the pricing dictionary is empty, or validating required model entries during options validation.

### [S-06] GetCostForPeriodAsync uses inclusive upper bound -- consider exclusive upper bound

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/TokenTracker.cs:62`

Using `e.CompletedAt <= to` with inclusive upper bound means an execution completed at exactly `to` is included. Standard practice for time ranges is `[from, to)` (inclusive start, exclusive end) to avoid double-counting at boundaries. This is a minor point but worth being intentional about.

---

## Positive Observations

- Clean separation of concerns: domain entity owns mutation, infrastructure owns orchestration and persistence.
- `CalculateCost` using `decimal` division avoids floating-point precision issues -- correct choice for financial calculations.
- `internal` visibility on `CalculateCost` allows direct unit testing via `InternalsVisibleTo` without exposing it publicly.
- Good use of `sealed` on `TokenTracker`.
- Test helper `SetupDbSet` with `MockQueryable.Moq` keeps tests readable and DRY.
- `RecordUsageAsync` gracefully handles missing execution with a warning log rather than throwing.
- The `Theory` test with `InlineData` for per-model pricing is well-structured and verifiable.
- File sizes are well within the 200-400 line guideline.

---

## Verdict

**WARNING** -- No critical or high-severity blockers. Five warnings that should be addressed (W-01 through W-05), with W-01 (overwrite vs accumulate) and W-04 (cache token pricing) being the most impactful for correctness. The code is otherwise well-written and ready for merge after these items are resolved or explicitly deferred with documented rationale.
