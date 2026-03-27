# Code Review: Section 09 — Content Analytics

**Verdict: WARNING — No CRITICAL, 3 HIGH, 6 MEDIUM items**

## HIGH Issues
1. **HIGH-1:** N+1 query in `GetPerformanceAsync` — sequential DB round-trips per platform status (foreach loop)
2. **HIGH-2:** `CleanupSnapshotsAsync` loads all deletable rows into memory — use `ExecuteDeleteAsync` for bulk
3. **HIGH-3:** `statusIds` computed but never used in `GetPerformanceAsync` — dead code

## MEDIUM Issues
1. **MEDIUM-1:** `GetTopContentAsync` materializes all snapshots — acceptable but note as candidate for SQL view
2. **MEDIUM-2:** `Dictionary` returned where `IReadOnlyDictionary` declared — wrap with `.AsReadOnly()`
3. **MEDIUM-3:** Rate-limit recording after SaveChanges — no atomicity
4. **MEDIUM-4:** `ErrorCode.Conflict` used for rate limiting — semantically incorrect
5. **MEDIUM-5:** No validation on `limit`/date range in `GetTopContentAsync`
6. **MEDIUM-6:** `DateTimeOffset.UtcNow` called directly — not testable for cleanup

## Test Coverage Gaps
1. **TEST-1:** No tests for `CleanupSnapshotsAsync`
2. **TEST-2:** No test for `GetPerformanceAsync` with no published statuses
3. **TEST-3:** No test for missing platform adapter
4. **TEST-4:** Reflection-based ID assignment is fragile

## Suggestions
1. Push `AgentExecution.Cost` sum to SQL instead of materializing
2. `EngagementSnapshot` entity should use init-only setters
3. Add cancellation checks before expensive operations
