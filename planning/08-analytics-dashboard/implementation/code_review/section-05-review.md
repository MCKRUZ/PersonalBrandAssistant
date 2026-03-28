# Section 05 - Caching and Resilience: Code Review

**Verdict: APPROVE -- One high item (thread safety), two medium items, and several suggestions. No critical issues.**

Well-designed section. The decorator pattern cleanly separates caching from aggregation logic, the FactoryFailureException trick correctly prevents HybridCache from caching failures, Polly pipeline configuration is sound, SSRF tests cover subdomain spoofing and scheme enforcement (fixing section-03 MED-1), and the DI registration correctly avoids circular resolution. The main concern is a thread-safety issue on the rate-limiter field in the scoped-lifetime decorator.

---

## CRITICAL Issues

None.

---

## HIGH Issues

### HIGH-1: _lastRefreshAt is not thread-safe and resets per scope

**File:** CachedDashboardAggregator.cs:54

**Issue:** _lastRefreshAt is a plain DateTimeOffset? field. Two problems:

1. **Thread safety:** If two concurrent requests on the same scope call TryInvalidateAsync, the read-check-write on _lastRefreshAt (lines 38-47) is a classic race condition. Both threads can read null (or a stale value), both pass the cooldown check, and both invalidate. For a single-user dashboard this is low-probability but architecturally unsound.

2. **Scoped lifetime kills rate limiting:** CachedDashboardAggregator is registered as Scoped. Each HTTP request gets a fresh instance with _lastRefreshAt = null. The 1-minute cooldown only protects against multiple TryInvalidateAsync calls *within the same HTTP request*, which is not a realistic attack vector. A user spamming the refresh button creates a new scope (and a new CachedDashboardAggregator) per request, bypassing the rate limit entirely.

**Fix:** Extract the rate-limiting state into a singleton:

    // Inject as singleton
    internal sealed class DashboardRefreshLimiter
    {
        private long _lastRefreshTicks;

        public bool TryAcquire(TimeProvider timeProvider, TimeSpan cooldown)
        {
            var nowTicks = timeProvider.GetUtcNow().UtcTicks;
            var previous = Interlocked.Read(ref _lastRefreshTicks);
            if (nowTicks - previous < cooldown.Ticks)
                return false;
            return Interlocked.CompareExchange(
                ref _lastRefreshTicks, nowTicks, previous) == previous;
        }
    }

Register as services.AddSingleton and inject into CachedDashboardAggregator. This makes the cooldown survive across scopes and handles concurrent access atomically.

Alternatively, promote CachedDashboardAggregator to singleton lifetime. This is feasible because HybridCache and TimeProvider are already singletons, and the inner IDashboardAggregator could be resolved from a scope factory. However, that is a larger change. The singleton limiter is simpler.

---

## MEDIUM Issues

### MED-1: Polly pipeline ordering -- outer timeout wraps retry, potentially cutting retries short

**File:** GoogleAnalyticsService.cs:36-65

**Issue:** The pipeline is built as AddTimeout(15s) -> AddRetry(2 attempts, exponential+jitter) -> AddCircuitBreaker. In Polly v8, strategies execute as an onion -- the first added is the outermost layer. This means the 15-second timeout wraps the entire retry sequence. With 2 retry attempts and exponential backoff (base ~2s with jitter), the total time can reach ~2s + ~4s + execution time. If the external call takes 5-8 seconds before failing, the outer 15s timeout can fire mid-retry, aborting a retry attempt that might have succeeded.

This is likely intentional (the plan describes it as a "total timeout"), but it differs from the Substack HttpClient configuration which has *both* AttemptTimeout (10s per attempt) and TotalRequestTimeout (30s overall). The GA4 pipeline has no per-attempt timeout, only a total 15s timeout.

**Recommendation:** Add a per-attempt timeout inside the retry, and increase the outer timeout to accommodate retries:

    _resiliencePipeline = new ResiliencePipelineBuilder()
        .AddTimeout(TimeSpan.FromSeconds(30))  // Total timeout across all retries
        .AddRetry(new RetryStrategyOptions { /* ... same ... */ })
        .AddTimeout(TimeSpan.FromSeconds(10))  // Per-attempt timeout
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions { /* ... same ... */ })
        .Build();

Or keep 15s total if that is the desired behavior, but document it explicitly in a comment so future maintainers understand the trade-off.

---

### MED-2: Duplicated ShouldHandle predicate between retry and circuit breaker strategies

**File:** GoogleAnalyticsService.cs:43-49, 57-63

**Issue:** The ShouldHandle predicate is copy-pasted identically between the retry and circuit breaker configurations. If the set of retryable exceptions changes, both must be updated in sync. This is a maintenance risk.

**Fix:** Extract to a shared predicate:

    private static readonly PredicateBuilder TransientExceptionPredicate =
        new PredicateBuilder()
            .Handle<RpcException>(ex =>
                ex.StatusCode is StatusCode.Unavailable
                or StatusCode.DeadlineExceeded
                or StatusCode.ResourceExhausted)
            .Handle<Google.GoogleApiException>()
            .Handle<HttpRequestException>();

Then reference it in both strategy options.

---

## LOW Issues / Suggestions

### LOW-1: DashboardSummary tagged as "social" -- overly broad invalidation

**File:** CachedDashboardAggregator.cs:72

**Issue:** GetSummaryAsync applies tags ["dashboard", "social"]. The dashboard summary includes GA4 website users, Substack data, cost data, and social engagement. Tagging it "social" means that a social-only invalidation (e.g., RemoveByTagAsync("social")) would also purge the summary, which contains GA4/cost data that has not changed. Conversely, a GA4-only invalidation (RemoveByTagAsync("ga4")) would *not* purge the summary even though it contains GA4 data.

**Suggestion:** Tag the summary with ["dashboard"] only (the universal tag), since it aggregates all sources. The section plan TTL table lists the summary with only the "dashboard" tag, which aligns with this recommendation. The current implementation deviates from the plan.

---

### LOW-2: Cache key uses date-only precision, losing time-of-day from DateTimeOffset

**File:** CachedDashboardAggregator.cs:59, 85, 112

**Issue:** Cache keys are formatted as dashboard:summary:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}. If two callers pass the same date but different times (e.g., 2026-03-01T00:00:00Z vs 2026-03-01T06:00:00Z), they share a cache key but the inner aggregator filters by the full DateTimeOffset. The first caller result is served for the second caller with different time bounds.

For this single-user dashboard, date-level granularity is likely fine (the API will probably always pass start-of-day/end-of-day). But if the API ever exposes hour-level filtering, this becomes a correctness bug.

**Suggestion:** If date-only granularity is by design, document it with a comment. If not, include the full ISO 8601 timestamp in the key.

---

### LOW-3: IDashboardCacheInvalidator resolution relies on downcast

**File:** DependencyInjection.cs:318-319

**Issue:** The IDashboardCacheInvalidator registration casts the IDashboardAggregator to CachedDashboardAggregator. This works because the factory for IDashboardAggregator always returns CachedDashboardAggregator. But if someone later wraps the aggregator in another decorator (e.g., logging, metrics), this cast fails at runtime with InvalidCastException. The coupling between these two registrations is implicit.

**Suggestion:** Register both interfaces from the same concrete instance:

    services.AddScoped<CachedDashboardAggregator>(sp =>
        new CachedDashboardAggregator(
            sp.GetRequiredService<DashboardAggregator>(),
            sp.GetRequiredService<HybridCache>(),
            sp.GetRequiredService<ILogger<CachedDashboardAggregator>>(),
            sp.GetRequiredService<TimeProvider>()));
    services.AddScoped<IDashboardAggregator>(
        sp => sp.GetRequiredService<CachedDashboardAggregator>());
    services.AddScoped<IDashboardCacheInvalidator>(
        sp => sp.GetRequiredService<CachedDashboardAggregator>());

This gives a single instance per scope shared between both interfaces, and the resolution chain is explicit.

---

### LOW-4: Resilience-specific catch blocks repeat across four methods

**File:** GoogleAnalyticsService.cs (lines 115-126, 186-197, 248-259, 302-313)

**Issue:** The catch (BrokenCircuitException) and catch (TimeoutRejectedException) blocks are duplicated four times with near-identical bodies. Each block follows the pattern: log warning, return Result.Failure with a service-specific message.

**Suggestion:** Extract a helper method or restructure each method to use a single try-catch and map exceptions to Result at one location. This is not blocking but would reduce the file from 336 lines to approximately 260.

---

### LOW-5: Resilience policy tests only verify construction, not behavior

**File:** ResiliencePolicyTests.cs:100-111

**Issue:** GoogleAnalyticsService_ConstructsWithResiliencePipeline only verifies the constructor does not throw. It does not test that the resilience pipeline actually retries on transient gRPC failures, opens the circuit breaker after consecutive failures, or respects the timeout. The section plan test specification calls for explicit tests of timeout behavior, circuit breaker opening, and retry with backoff.

**Suggestion:** Add integration-style tests that exercise the pipeline. Using Moq to make the IGa4Client throw RpcException(StatusCode.Unavailable) N times and then succeed, you can verify:
- The method is called MaxRetryAttempts + 1 times on transient failure
- After MinimumThroughput failures, the next call gets BrokenCircuitException immediately
- The Result.Failure message indicates circuit breaker / timeout as appropriate

This would significantly improve confidence in the resilience configuration. Current coverage for the resilience behavior specifically is minimal.

---

### LOW-6: CredentialsPath and PropertyId in appsettings.json

**File:** appsettings.json:120-124

**Issue:** PropertyId "261358185" is a GA4 property identifier. While this is not a secret (it is visible in the GA4 UI and is not an API key), committing it to the repository ties the configuration to a specific GA4 property. The CredentialsPath points to secrets/google-analytics-sa.json which is correctly gitignored.

**Suggestion:** Move PropertyId and SiteUrl to appsettings.Development.json (or User Secrets) to avoid committing environment-specific values to appsettings.json. Use empty placeholders in the committed file. Not a security risk, but a deployment hygiene improvement.

---

### LOW-7: Filtered index syntax is PostgreSQL-specific

**File:** ContentPlatformStatusConfiguration.cs (diff line 55-56)

**Issue:** The filter string uses PostgreSQL-style double-quoted identifiers in the HasFilter call. If the project ever targets SQL Server, this would need bracket-quoted identifiers. Since the project uses PostgreSQL (confirmed by Npgsql in the migration designer), this is fine today. Just a note for portability awareness.

---

## Test Coverage Assessment

| Scenario | Covered | Test |
|---|---|---|
| Cache hit on second call | Yes | GetSummaryAsync_SecondCallReturnsCachedResult |
| Invalidation bypasses cache | Yes | GetSummaryAsync_RefreshBypassesCache |
| Rate limit rejects within cooldown | Yes | TryInvalidateAsync_RejectsSecondRefreshWithinCooldown |
| Rate limit allows after cooldown | Yes | TryInvalidateAsync_AllowsRefreshAfterCooldown |
| Timeline caching | Yes | GetTimelineAsync_CachesResult |
| Platform summaries caching | Yes | GetPlatformSummariesAsync_CachesResult |
| SSRF: non-substack domain | Yes | SubstackService_RejectsNonSubstackUrl |
| SSRF: HTTP scheme | Yes | SubstackService_RejectsHttpUrl |
| SSRF: subdomain spoofing | Yes | SubstackService_RejectsSubdomainSpoofing |
| GA4 service construction | Yes | GoogleAnalyticsService_ConstructsWithResiliencePipeline |
| **Inner failure propagates as Result.Failure** | **Missing** | Verify FactoryFailureException round-trip |
| **Retry on transient gRPC failure** | **Missing** | See LOW-5 |
| **Circuit breaker opens after failures** | **Missing** | See LOW-5 |
| **Timeout fires after configured duration** | **Missing** | See LOW-5 |
| **Concurrent invalidation (thread safety)** | **Missing** | See HIGH-1 |
| **Rate limit across scopes** | **Missing** | See HIGH-1 |

10 of 16 relevant scenarios covered (62%). After addressing HIGH-1 (which requires new tests for cross-scope rate limiting) and LOW-5 (resilience pipeline tests), coverage reaches the 80% threshold.

---

## Summary

The caching decorator is well-structured and the FactoryFailureException pattern is a clean solution to HybridCache serialization constraints. The main actionable item is HIGH-1: the scoped lifetime renders rate limiting ineffective across requests. Extract the limiter state to a singleton. The Polly pipeline configuration is reasonable but should either add a per-attempt timeout (MED-1) or document the intentional 15s total cap. The duplicated ShouldHandle predicate (MED-2) is a quick extract-variable fix. The resilience policy tests need more substance (LOW-5) to actually verify retry/circuit-breaker behavior rather than just construction.
