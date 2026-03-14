# Section 07 — Code Review Interview

## Auto-Fixed

### W-02 - Two DB queries in GetBudgetRemainingAsync
- Optimized to single query: fetch monthly executions, then filter daily in-memory
- **Status:** APPLIED

### W-05 - Missing input validation
- Added `ArgumentException.ThrowIfNullOrWhiteSpace(modelId)` and `ArgumentOutOfRangeException.ThrowIfNegative` for token counts
- **Status:** APPLIED

### ModelPricingOptions as record
- Changed from `class` to `record` per project conventions
- **Status:** APPLIED

## Let Go

### W-01 - RecordUsage overwrites vs accumulates
- Domain entity design from section-01. Each execution maps to a single capability invocation. Changing to accumulation would be a domain model change outside scope.

### W-03 - Running executions invisible to budget
- Accepted per plan's design decision: "Budget check is not transactional. Small race window acceptable for single-user app."

### W-04 - Cache tokens not in cost calculation
- Plan explicitly states: "Cache read/creation tokens do not affect cost currently."

### TimeProvider injection
- Nice-to-have but would add complexity for minimal benefit in a single-user app. Deferred.
