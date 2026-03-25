# Section 05 Code Review Interview

## Triage Summary

| Finding | Action | Rationale |
|---------|--------|-----------|
| HIGH-1: Scoped rate limiting | Auto-fix | Real bug — rate limit resets per request |
| MED-1: Polly pipeline timeout | Let go + comment | 15s total is intentional for single-user dashboard |
| MED-2: Duplicated ShouldHandle | Auto-fix | Simple extract, reduces maintenance risk |
| LOW-1: Summary tags | Auto-fix | Correct per plan — summary aggregates all sources |
| LOW-2: Cache key precision | Let go | Date-only is by design |
| LOW-3: DI downcast | Auto-fix | Simple fix, eliminates runtime cast risk |
| LOW-4: Duplicated catch blocks | Let go | Each has specific messages, not worth abstracting |
| LOW-5: Resilience tests | Let go | Construction test validates pipeline builds; behavioral tests need significant infrastructure |
| LOW-6: PropertyId in appsettings | Let go | Needed for config binding, not a secret |
| LOW-7: PostgreSQL filter syntax | Let go | Project is PostgreSQL-only |

## Auto-Fixes Applied

### HIGH-1: Extract singleton DashboardRefreshLimiter
- Created `DashboardRefreshLimiter` singleton class with `Interlocked.CompareExchange` for thread-safe rate limiting
- Injected into `CachedDashboardAggregator` instead of instance field
- Registered as singleton in DI

### MED-2: Extract shared predicate
- Extracted `TransientExceptionPredicate` as static field in GoogleAnalyticsService
- Referenced from both retry and circuit breaker configurations

### LOW-1: Fix summary tags
- Changed GetSummaryAsync tags from `["dashboard", "social"]` to `["dashboard"]`

### LOW-3: Fix DI registration
- Register CachedDashboardAggregator as concrete scoped, then forward both interfaces
