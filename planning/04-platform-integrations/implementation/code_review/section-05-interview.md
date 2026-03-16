# Code Review Interview: Section 05 - DatabaseRateLimiter

**Date:** 2026-03-15

## User Decisions

### CRITICAL-01: YouTube quota reset race condition
**Decision:** Fix — added `ResetYouTubeQuotaWithRetryAsync` with `DbUpdateConcurrencyException` catch and retry (max 3 attempts). On concurrency conflict, reloads entity and checks if another request already reset the quota.

### WARN-01: FirstAsync vs Result<T> pattern
**Decision:** Fix with Result<T> — updated `IRateLimiter` interface to return `Result<RateLimitDecision>`, `Result<bool>`, and `Result<RateLimitStatus>`. All methods now use `FirstOrDefaultAsync` with `Result.NotFound` on missing platform.

## Auto-fixes Applied

### WARN-02: Instagram cache invalidation
Added `_cache.Remove(CacheKey(platform, endpoint))` in `RecordRequestAsync` when platform is Instagram. Added test `RecordRequestAsync_InvalidatesCacheForInstagram`.

### WARN-03: YouTube quota check ordering in GetStatusAsync
Moved YouTube daily quota check before endpoint aggregation for early short-circuit.

### WARN-04: Fragile default comparison for nearestReset
Replaced `DefaultIfEmpty().Min()` with explicit `ToList()` + `Count > 0` check.

### WARN-05: Path mismatch
Folder renamed from `Services/Platform/` to `Services/PlatformServices/` to avoid namespace collision with `Domain.Entities.Platform`. Will update section doc.

## Additional Tests Added

- `CanMakeRequestAsync_ReturnsNotFound_WhenPlatformMissing` — SUGGEST-02 coverage
- `GetStatusAsync_ReturnsNotLimited_WhenNoEndpoints` — SUGGEST-02 coverage
- `GetStatusAsync_ReturnsLimited_WhenYouTubeDailyQuotaExceeded` — SUGGEST-03 coverage
- `RecordRequestAsync_SetsYouTubeDailyQuotaUsed` — SUGGEST-04 coverage
- `RecordRequestAsync_InvalidatesCacheForInstagram` — WARN-02 verification

## Items Let Go

- SUGGEST-01: FakeTimeProvider shared helper — can be extracted later when more tests need it
- SUGGEST-05: RemainingCalls=0 with ResetAt=null — current behavior (allows request) is reasonable
- SUGGEST-06: DST comment — added inline comment on `CalculateNextMidnightPacific`

## Final Test Count: 16 passing (was 11, added 5)
