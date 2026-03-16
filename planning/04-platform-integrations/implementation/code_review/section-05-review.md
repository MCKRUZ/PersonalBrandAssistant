# Code Review: Section 05 - DatabaseRateLimiter

**Reviewer:** code-reviewer agent
**Date:** 2026-03-15
**Files reviewed:**
- `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/DatabaseRateLimiter.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/DatabaseRateLimiterTests.cs`

**Verdict: WARNING -- MEDIUM issues found, can merge after fixing CRITICAL-01, WARN-01, and WARN-02**

---

## Critical Issues (must fix)

### [CRITICAL-01] Race condition on YouTube quota reset -- no concurrency protection

**File:** DatabaseRateLimiter.cs:153-155

**Issue:** The YouTube quota reset path mutates DailyQuotaUsed and QuotaResetAt in place, then calls SaveChangesAsync. If two concurrent requests both see an expired QuotaResetAt, both will reset the quota to 0 and save. This race condition could allow requests through a legitimately exhausted quota window.

The Platform entity has a Version (uint) property that maps to a PostgreSQL xmin concurrency token (confirmed in section plan), but there is no DbUpdateConcurrencyException handling anywhere in the class.

**Fix:** Wrap the mutation + save in a try/catch for DbUpdateConcurrencyException and retry by reloading the entity. Use the existing xmin concurrency token to detect conflicts.

---

## Warnings (should fix)

### [WARN-01] FirstAsync throws instead of using Result pattern for missing platforms

**File:** DatabaseRateLimiter.cs:67-68, 91-92, 141-142

**Issue:** All three public methods use FirstAsync, which throws InvalidOperationException if no Platform entity matches. The project standard is to use the Result<T> pattern for error handling rather than letting exceptions propagate.

**Fix:** Use FirstOrDefaultAsync and return a meaningful error. For RecordRequestAsync (returns Task void), consider changing the interface to Task<Result> or throwing a typed PlatformNotFoundException.

### [WARN-02] Instagram cache is never invalidated after RecordRequestAsync

**File:** DatabaseRateLimiter.cs:44-55, 60-87

**Issue:** When RecordRequestAsync is called for Instagram (e.g., after a publish drops remaining from 10 to 0), the cache still holds the old RateLimitDecision. For up to 5 minutes, CanMakeRequestAsync will return stale data showing requests are allowed even though the limit is exhausted.

**Fix:** Invalidate the cache entry in RecordRequestAsync by calling _cache.Remove with the rate-limit cache key for Instagram.

### [WARN-03] GetStatusAsync YouTube quota check ordering could skip early return

**File:** DatabaseRateLimiter.cs:119-128

**Issue:** The YouTube daily quota check runs after the per-endpoint aggregation loop. If the daily quota is exceeded, the endpoint aggregation work is wasted. Move the YouTube quota check before the endpoint aggregation to short-circuit early.

### [WARN-04] nearestReset default value comparison is fragile

**File:** DatabaseRateLimiter.cs:112-116, 132

**Issue:** DefaultIfEmpty().Min() on an empty DateTimeOffset sequence produces default(DateTimeOffset) which is 0001-01-01. The check nearestReset == default on line 132 catches this, but if any real reset time somehow equaled default, it would be incorrectly nullified.

**Fix:** Collect reset times into a list first, then use Count > 0 to decide whether to call Min() or return null.

### [WARN-05] File path mismatch between plan and implementation

**File:** Both files

**Issue:** The plan specifies Services/Platform/DatabaseRateLimiter.cs but the implementation uses Services/PlatformServices/DatabaseRateLimiter.cs. Same for the test directory. Either update the plan or rename the directory.

---

## Suggestions (consider improving)

### [SUGGEST-01] FakeTimeProvider should be in a shared test helper

**File:** DatabaseRateLimiterTests.cs:443-452

**Issue:** FakeTimeProvider is defined inline as an internal class at the bottom of the test file. Other test files will need the same fake. .NET 8+ provides Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider in the Microsoft.Extensions.TimeProvider.Testing NuGet package.

**Fix:** Either use the official NuGet package or move to tests/.../Helpers/FakeTimeProvider.cs.

### [SUGGEST-02] Missing test: GetStatusAsync when no endpoints exist

The Endpoints.Count == 0 early-return path (lines 97-100) has no test coverage.

### [SUGGEST-03] Missing test: GetStatusAsync YouTube daily quota path

Lines 119-128 check YouTube quota in GetStatusAsync but no test exercises this path.

### [SUGGEST-04] Missing test: RecordRequestAsync YouTube DailyQuotaUsed tracking

Lines 76-79 set DailyQuotaUsed for YouTube but no test verifies this calculation.

### [SUGGEST-05] Missing edge case test: RemainingCalls=0 with ResetAt=null

When RemainingCalls == 0 and ResetAt is null, the implementation allows the request (line 171 condition is false). This may or may not be intended -- a test should document expected behavior for a permanently exhausted endpoint with no known reset time.

### [SUGGEST-06] CalculateNextMidnightPacific DST comment

**File:** DatabaseRateLimiter.cs:186-193

The method is correct for midnight specifically (midnight is unambiguous in both DST transitions), but a brief comment explaining this was considered would help future maintainers.

---

## Test Coverage Assessment

| Planned Test | Covered | Notes |
|---|---|---|
| CanMakeRequest - no state | Yes | |
| CanMakeRequest - remaining > 0 | Yes | |
| CanMakeRequest - remaining = 0 | Yes | |
| CanMakeRequest - YouTube quota exceeded | Yes | |
| RecordRequest - updates state | Yes | |
| RecordRequest - creates endpoint | Yes | |
| RecordRequest - updates existing | Yes | |
| GetStatus - aggregate | Yes | |
| YouTube quota reset (midnight PT) | Yes | |
| Instagram cache TTL | Yes | |
| CanMakeRequest - ResetAt in past | Yes | Bonus |
| GetStatus - no endpoints | **No** | SUGGEST-02 |
| GetStatus - YouTube quota | **No** | SUGGEST-03 |
| RecordRequest - YouTube quota | **No** | SUGGEST-04 |
| RemainingCalls=0, ResetAt=null | **No** | SUGGEST-05 |

**Estimated path coverage:** ~75%. Adding the four missing tests brings it above 80%.

---

## Summary

The implementation is clean, readable, and closely follows the section plan. Core logic is correct. The main concerns:

1. **Concurrency on YouTube quota reset** (CRITICAL-01) -- race condition needs a concurrency guard using the existing xmin token.
2. **Stale Instagram cache** (WARN-02) -- cache must be invalidated in RecordRequestAsync to prevent allowing requests past the limit.
3. **No Result pattern for missing platforms** (WARN-01) -- FirstAsync throws; should use FirstOrDefaultAsync with a meaningful return.
4. **Test gaps** -- four code paths lack coverage; adding them meets the 80% minimum.

**Recommendation:** Fix CRITICAL-01, WARN-01, and WARN-02 before merging. Remaining items can be addressed in follow-up.
