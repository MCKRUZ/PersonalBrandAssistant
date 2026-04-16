# Section 05 — Context Budget Tracker

## Overview

Implements `IContextBudgetTracker` and `ContextBudgetTracker` — a Scoped, per-request context-window token accumulator that returns a continuation decision (`Continue / Nudge / Stop`) based on configurable thresholds.

Additive alongside `ITokenTracker` (which handles cost and DB persistence). The budget tracker only tracks context-window token consumption within a single agent execution; it does not persist anything.

**Dependencies:** section-01 (build), section-02 (interfaces and models)
**Parallelizable with:** section-03, section-06

---

## Contracts (from section-02 — reference only, do not re-define)

```csharp
// IContextBudgetTracker
public interface IContextBudgetTracker
{
    void RecordTokens(string component, int tokens);
    BudgetAssessment AssessContinuation();
    int TotalTokens { get; }
}

// BudgetAssessment
public record BudgetAssessment(BudgetDecision Decision, string Reason, int TokensUsed, int TokensRemaining);
public enum BudgetDecision { Continue, Nudge, Stop }

// ContextBudgetOptions
public class ContextBudgetOptions
{
    public const string SectionName = "ContextBudget";
    public int NudgeThreshold { get; init; } = 80_000;
    public int StopThreshold { get; init; } = 180_000;
    public int HardMaxTokens { get; init; } = 200_000;
}
```

---

## Tests First

**Test file:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/ContextBudgetTrackerTests.cs`

All tests use `ContextBudgetOptions` with explicit threshold values — never rely on defaults so tests remain robust against config changes.

Helper factory:

```csharp
private static ContextBudgetTracker CreateTracker(
    int nudge = 80_000, int stop = 180_000, int hard = 200_000)
{
    var options = new ContextBudgetOptions
    {
        NudgeThreshold = nudge,
        StopThreshold = stop,
        HardMaxTokens = hard
    };
    return new ContextBudgetTracker(Options.Create(options));
}
```

Test list:

```
# RecordTokens accumulation
RecordTokens_SingleComponent_TotalReflectsCount
RecordTokens_MultipleComponents_TotalSumsAll
RecordTokens_SameComponentTwice_Accumulates

# AssessContinuation threshold boundaries
AssessContinuation_Below80k_ReturnsContinue
AssessContinuation_At80k_ReturnsNudge
AssessContinuation_Between80kAnd180k_ReturnsNudge
AssessContinuation_At180k_ReturnsStop
AssessContinuation_Above180k_ReturnsStop

# Configurable thresholds
AssessContinuation_CustomNudgeThreshold_HonorsConfig
AssessContinuation_CustomStopThreshold_HonorsConfig

# BudgetAssessment field correctness
AssessContinuation_Continue_TokensUsedAndRemainingCorrect
AssessContinuation_Nudge_ReasonIsNonEmpty
AssessContinuation_Stop_TokensRemainingIsNegativeOrZero
```

---

## Implementation

**New file:** `src/PersonalBrandAssistant.Infrastructure/Agents/ContextBudgetTracker.cs`

Key decisions:

- Use plain `Dictionary<string, int>` — **not** `ConcurrentDictionary`. Scoped (one instance per request), always accessed on a single thread. Thread safety overhead is unnecessary.
- `TotalTokens` is a simple LINQ sum over `_components.Values`.
- `RecordTokens("system", 500)` called twice results in 1000 total for that component.
- `AssessContinuation` reads `TotalTokens` once and checks thresholds in order (stop first, then nudge).

Threshold logic:

```
total >= StopThreshold  → BudgetDecision.Stop
total >= NudgeThreshold → BudgetDecision.Nudge
otherwise               → BudgetDecision.Continue
```

`TokensRemaining` = `HardMaxTokens - TotalTokens` (zero or negative at/past stop threshold).

`Reason` strings (human-readable, non-empty for all decisions):
- Continue: `"Within budget"`
- Nudge: `"Approaching context limit"`
- Stop: `"Context budget exhausted"`

Constructor:

```csharp
public sealed class ContextBudgetTracker : IContextBudgetTracker
{
    private readonly Dictionary<string, int> _components = new();
    private readonly ContextBudgetOptions _options;

    public ContextBudgetTracker(IOptions<ContextBudgetOptions> options)
    {
        _options = options.Value;
    }
}
```

---

## Configuration

`ContextBudgetOptions` is bound from `"ContextBudget"` section (wired in section-10). The `appsettings.json` entry from section-01:

```json
"ContextBudget": {
  "NudgeThreshold": 80000,
  "StopThreshold": 180000,
  "HardMaxTokens": 200000
}
```

Note: "Thresholds assume a 200k context window model. Adjust via ContextBudgetOptions if using a model with a different context window."

---

## DI Registration

Handled in **section-10**:

```csharp
services.AddScoped<IContextBudgetTracker, ContextBudgetTracker>();
```

Do not add DI registration in this section.

---

## Checklist

1. Verify section-02 is complete.
2. Create `ContextBudgetTrackerTests.cs` with all 13 test stubs.
3. Run `dotnet test` — confirm 13 tests fail (red).
4. Create `ContextBudgetTracker.cs`.
5. Run `dotnet test` — confirm all 13 tests pass (green).
6. Verify `dotnet build` produces zero warnings.

---

## Actual Implementation Notes

**Files created:**
- `src/PersonalBrandAssistant.Infrastructure/Agents/ContextBudgetTracker.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/ContextBudgetTrackerTests.cs`

**Files modified:**
- `src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj` — pinned `System.Security.Cryptography.Xml` to `10.0.6` (patched GHSA-37gx-xxp4-5rgx, GHSA-w3x6-4m5h-cxqf)

**Deviations from plan:**
- Added input guards to `RecordTokens`: `ArgumentException.ThrowIfNullOrWhiteSpace(component)` and negative tokens check (code review finding)
- 16 tests total (13 planned + 3 guard tests from review)

**Final test count:** 16/16 passing
