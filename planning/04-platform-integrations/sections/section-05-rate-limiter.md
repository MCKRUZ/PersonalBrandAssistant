# Section 05: Rate Limiter (DatabaseRateLimiter)

## Overview

This section implements the `DatabaseRateLimiter` class, which reads and writes rate limit state from the Platform entity's `RateLimitState` JSONB column. The rate limiter provides rich decision responses (`RateLimitDecision` with `RetryAt` and `Reason`), supports per-endpoint tracking, YouTube daily quota with Pacific Time reset, and Instagram publishing limit caching.

## Dependencies

- **Section 02 (Interfaces & Models):** Provides `IRateLimiter`, `RateLimitDecision`, and `RateLimitStatus` types.
- **Section 03 (EF Core Config):** The Platform entity must be persistable with its `RateLimitState` JSONB column already configured.

## Existing Code Context

The Platform entity already exists at `src/PersonalBrandAssistant.Domain/Entities/Platform.cs` with a `RateLimitState` property of type `PlatformRateLimitState`, stored as JSONB via `JsonValueConverter`. The current `PlatformRateLimitState` value object is minimal:

```csharp
public class PlatformRateLimitState
{
    public int? RemainingCalls { get; set; }
    public DateTimeOffset? ResetAt { get; set; }
    public TimeSpan? WindowDuration { get; set; }
}
```

Section 01 extends this with `Endpoints`, `DailyQuotaUsed`, `DailyQuotaLimit`, `QuotaResetAt`.

The `PlatformType` enum: `TwitterX`, `LinkedIn`, `Instagram`, `YouTube`.

The `ApplicationDbContext` has `DbSet<Platform> Platforms`. Platform uses xmin concurrency tokens.

## Files Created/Modified

| File | Action |
|------|--------|
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/DatabaseRateLimiter.cs` | **Created** |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/DatabaseRateLimiterTests.cs` | **Created** |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IRateLimiter.cs` | **Modified** — return types changed to `Result<T>` |

**Deviation from plan:** Folder renamed from `Services/Platform/` to `Services/PlatformServices/` to avoid namespace collision with `Domain.Entities.Platform`.

Note: `PlatformRateLimitState.cs` modification is handled in Section 01.

## Tests (Write First)

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/DatabaseRateLimiterTests.cs`

Mock `IApplicationDbContext` using `AsyncQueryableHelpers`. Inject mocked DbContext, `ILogger<DatabaseRateLimiter>`, `TimeProvider`, and `IMemoryCache`.

```csharp
// Test: CanMakeRequestAsync returns Allowed=true when no rate limit state exists
//   Setup: Platform entity with default (empty) RateLimitState
//   Assert: decision.Allowed == true, decision.RetryAt == null

// Test: CanMakeRequestAsync returns Allowed=true when remaining > 0
//   Setup: Platform with endpoint entry where RemainingCalls = 5, ResetAt in future
//   Assert: decision.Allowed == true

// Test: CanMakeRequestAsync returns Allowed=false with RetryAt when remaining = 0
//   Setup: Platform with endpoint entry where RemainingCalls = 0, ResetAt = now + 15min
//   Assert: decision.Allowed == false, decision.RetryAt == ResetAt, Reason is descriptive

// Test: CanMakeRequestAsync returns Allowed=false with Reason for YouTube daily quota exceeded
//   Setup: YouTube Platform with DailyQuotaUsed = 10000, DailyQuotaLimit = 10000
//   Assert: decision.Allowed == false, Reason mentions "daily quota", RetryAt == QuotaResetAt

// Test: RecordRequestAsync updates Platform entity RateLimitState JSONB
//   Setup: Platform with empty RateLimitState
//   Act: RecordRequestAsync with remaining=99, resetAt=future
//   Assert: Platform.RateLimitState updated, SaveChangesAsync called

// Test: RecordRequestAsync creates endpoint entry if not exists
//   Setup: Platform with no entry for given endpoint
//   Act: RecordRequestAsync with endpoint="tweets", remaining=50
//   Assert: RateLimitState.Endpoints["tweets"] exists with RemainingCalls=50

// Test: RecordRequestAsync updates existing endpoint entry
//   Setup: Platform with existing "tweets" entry (RemainingCalls=50)
//   Act: RecordRequestAsync with endpoint="tweets", remaining=49
//   Assert: RateLimitState.Endpoints["tweets"].RemainingCalls == 49

// Test: GetStatusAsync returns aggregate status across endpoints
//   Setup: Platform with multiple endpoint entries, one at 0 remaining
//   Assert: IsLimited == true, RemainingCalls = minimum, ResetAt = nearest

// Test: YouTube quota resets at midnight PT (TimeZoneInfo America/Los_Angeles)
//   Setup: YouTube Platform with DailyQuotaUsed = 5000, QuotaResetAt in the past
//   Act: CanMakeRequestAsync
//   Assert: Allowed == true, DailyQuotaUsed reset to 0, QuotaResetAt recalculated

// Test: Instagram publishing limit cached with TTL
//   Setup: Use IMemoryCache. First call checks DB. Second call within TTL uses cache.
//   Assert: Only one DB query for two calls within 5 minutes
```

## Implementation Details

### DatabaseRateLimiter

File: `src/PersonalBrandAssistant.Infrastructure/Services/Platform/DatabaseRateLimiter.cs`

**Constructor dependencies:**
- `IApplicationDbContext` -- querying/updating Platform entities
- `ILogger<DatabaseRateLimiter>` -- structured logging
- `TimeProvider` -- testable time
- `IMemoryCache` -- Instagram publishing limit caching

**Scoped lifetime** (registered in Section 12).

#### CanMakeRequestAsync Logic

1. Load Platform entity by `PlatformType` from `DbContext.Platforms`
2. If `RateLimitState` is null or no endpoints, return `Allowed=true`
3. **YouTube special case:** Check `DailyQuotaUsed >= DailyQuotaLimit`. If `QuotaResetAt` is past, reset quota to 0 and recalculate `QuotaResetAt` to next midnight Pacific Time. If quota exceeded and reset in future, return `Allowed=false` with `RetryAt=QuotaResetAt`
4. **General endpoint check:** Look up endpoint in `Endpoints` dictionary. If `RemainingCalls == 0` and `ResetAt` in future, return `Allowed=false`. If `ResetAt` past, treat as reset. If no entry, return `Allowed=true`
5. **Instagram cache:** For Instagram, cache result in `IMemoryCache` with 5-minute TTL

#### RecordRequestAsync Logic

1. Load Platform entity by `PlatformType`
2. Get or create endpoint entry in `RateLimitState.Endpoints[endpoint]`
3. Set `RemainingCalls` and `ResetAt` from parameters
4. **YouTube quota:** Set `DailyQuotaUsed = DailyQuotaLimit - remaining`
5. Call `SaveChangesAsync`

#### GetStatusAsync Logic

1. Load Platform entity by `PlatformType`
2. Aggregate: `RemainingCalls` = minimum across endpoints, `ResetAt` = nearest future reset, `IsLimited` = true if any endpoint at 0 with future reset
3. For YouTube, also check daily quota status
4. Return `RateLimitStatus`

#### YouTube Midnight PT Calculation

```csharp
private DateTimeOffset CalculateNextMidnightPacific(DateTimeOffset now)
{
    var pacificZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
    var pacificNow = TimeZoneInfo.ConvertTime(now, pacificZone);
    var nextMidnight = pacificNow.Date.AddDays(1);
    var nextMidnightDto = new DateTimeOffset(nextMidnight, pacificZone.GetUtcOffset(nextMidnight));
    return nextMidnightDto.ToUniversalTime();
}
```

### Rate Limit Headers Mapping (for adapter reference)

| Platform | Headers | Mapping |
|----------|---------|---------|
| Twitter/X | `x-rate-limit-remaining`, `x-rate-limit-reset` | `remaining` = header, `resetAt` = epoch seconds |
| LinkedIn | `Retry-After` on 429 | `remaining` = 0, `resetAt` = now + seconds |
| Instagram | `content_publishing_limit` endpoint | Adapter queries API, passes result |
| YouTube | Tracked locally via quota costs | `remaining` = limit - used - cost |

This mapping lives in the adapters (Section 08), not in the rate limiter.

## Implementation Deviations

1. **Result<T> pattern:** All `IRateLimiter` methods now return `Result<T>` instead of raw values. Uses `FirstOrDefaultAsync` with `Result.NotFound` for missing platforms.
2. **Concurrency handling:** YouTube quota reset uses `ResetYouTubeQuotaWithRetryAsync` with `DbUpdateConcurrencyException` retry (max 3 attempts) leveraging the existing xmin concurrency token.
3. **Instagram cache invalidation:** `RecordRequestAsync` invalidates the Instagram cache entry after updating state.
4. **GetStatusAsync ordering:** YouTube daily quota check moved before endpoint aggregation for early short-circuit.
5. **16 tests** (vs 10 planned): Added tests for NotFound, empty endpoints, YouTube quota in GetStatus, YouTube DailyQuotaUsed tracking, and cache invalidation.
