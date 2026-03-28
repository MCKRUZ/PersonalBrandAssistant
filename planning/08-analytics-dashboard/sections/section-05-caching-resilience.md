# Section 05: Caching & Resilience

## Overview

This section adds HybridCache integration with tag-based invalidation and per-source TTLs to the DashboardAggregator, adds Polly resilience policies (timeout, retry with exponential backoff, circuit breaker) to the GA4, Search Console, and Substack HTTP clients, implements refresh rate limiting, and adds database index migrations for dashboard query performance.

**Depends on:** section-04-dashboard-aggregator (DashboardAggregator must exist before wrapping it with caching)

**Blocks:** section-06-api-endpoints (endpoints rely on cached aggregator and resilience policies being in place)

---

## Background

The dashboard aggregates data from multiple external sources (GA4 gRPC, Search Console REST, Substack RSS, local database). Without caching, every page load would make 5+ external API calls. Without resilience policies, a slow or failing external service would degrade the entire dashboard.

Key design decisions from the architecture:
- **Partial failure model**: The dashboard never fails entirely because one data source is down. Each section includes a `generatedAt` timestamp and optional `error` string.
- **HybridCache** provides a two-level cache: L1 in-memory (fast, process-local) and L2 distributed (if configured later). For this single-user dashboard, L1 is sufficient.
- **Tag-based invalidation** allows targeted cache clearing (e.g., clear all social data without touching GA4 cache).
- **Refresh rate limiting**: The `?refresh=true` query parameter bypasses cache, but is rate-limited to 1 per minute to prevent abuse.

---

## Tests First

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AnalyticsServices/CachedDashboardAggregatorTests.cs`

```csharp
/// <summary>
/// Tests for CachedDashboardAggregator, the caching decorator around IDashboardAggregator.
/// Uses Moq to verify cache hit/miss behavior and tag-based invalidation.
/// </summary>
public class CachedDashboardAggregatorTests
{
    // Test: Second call to GetSummaryAsync returns cached result (mock verifies single underlying call)
    //   - Arrange: Mock IDashboardAggregator returning a DashboardSummary
    //   - Act: Call GetSummaryAsync twice with same parameters
    //   - Assert: Inner aggregator's GetSummaryAsync called exactly once

    // Test: refresh=true bypasses cache and fetches fresh data
    //   - Arrange: Mock returning different results on each call
    //   - Act: Call GetSummaryAsync, then call with forceRefresh=true
    //   - Assert: Inner aggregator called twice, second result returned

    // Test: Cache tags are correctly applied per data source
    //   - Arrange: Mock HybridCache to capture tag arguments
    //   - Act: Call GetSummaryAsync (uses "dashboard", "social" tags), GetWebsiteData (uses "ga4" tag)
    //   - Assert: Tags match expected values per source type

    // Test: Refresh rate limiting rejects second refresh within 1 minute
    //   - Arrange: Configure 1-minute rate limit
    //   - Act: Call with forceRefresh=true twice within 60 seconds
    //   - Assert: Second call returns cached data instead of refreshing (does not call inner aggregator again)
}
```

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AnalyticsServices/ResiliencePolicyTests.cs`

```csharp
/// <summary>
/// Tests for Polly resilience policies on external HTTP/gRPC clients.
/// Uses custom DelegatingHandler stubs to simulate failures.
/// </summary>
public class ResiliencePolicyTests
{
    // Test: Substack URL validation rejects non-substack.com hostnames
    //   - Arrange: Configure SubstackOptions with FeedUrl = "https://evil.com/feed"
    //   - Act: Attempt to create/validate SubstackService
    //   - Assert: Throws or returns validation failure

    // Test: HttpClient timeout triggers after configured duration
    //   - Arrange: Register HttpClient with 10s timeout, mock handler that delays 15s
    //   - Act: Make request
    //   - Assert: TaskCanceledException or TimeoutRejectedException thrown within ~10s

    // Test: Circuit breaker opens after 3 consecutive failures
    //   - Arrange: Mock handler returns 500 for all requests
    //   - Act: Make 3 requests (all fail), then make 4th request
    //   - Assert: 4th request fails immediately with BrokenCircuitException (not hitting handler)

    // Test: Retry policy retries on 429 status code with backoff
    //   - Arrange: Mock handler returns 429 twice, then 200
    //   - Act: Make single request
    //   - Assert: Handler called 3 times total, final result is success
}
```

---

## Implementation Details

### 1. NuGet Package Installation

Add to `src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj`:

```xml
<PackageReference Include="Microsoft.Extensions.Caching.Hybrid" Version="9.6.0" />
<PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="9.6.0" />
```

The `Microsoft.Extensions.Caching.Hybrid` package provides `HybridCache` with L1/L2 support and tag-based invalidation. The `Microsoft.Extensions.Http.Resilience` package provides the `.AddStandardResilienceHandler()` extension and underlying Polly v8 integration for `IHttpClientFactory`.

Note: `Microsoft.Extensions.Caching.Hybrid` requires the in-memory cache service (`AddMemoryCache()`) which is already registered in `DependencyInjection.cs` (line 138).

### 2. HybridCache Registration

Add to `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` in the `AddInfrastructure` method:

```csharp
services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(30),
        LocalCacheExpiration = TimeSpan.FromMinutes(5)
    };
});
```

Place this near the existing `services.AddMemoryCache()` call (around line 138). The default TTLs serve as fallbacks; individual cache entries override these with source-specific TTLs.

### 3. CachedDashboardAggregator (Decorator Pattern)

Create a new file at `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/CachedDashboardAggregator.cs`.

This class wraps the existing `DashboardAggregator` (from section-04) using the decorator pattern. It implements `IDashboardAggregator` and delegates to the inner aggregator, adding caching around each method.

**Design:**
- Constructor takes `IDashboardAggregator inner`, `HybridCache cache`, `ILogger<CachedDashboardAggregator> logger`, and `TimeProvider timeProvider`
- Each method builds a cache key from the method name + date range parameters (e.g., `"dashboard:summary:2026-03-01:2026-03-30"`)
- Uses `HybridCache.GetOrCreateAsync<T>(key, factory, options, tags, ct)` to wrap the inner call
- The `forceRefresh` parameter (passed via a method parameter or ambient context) triggers `cache.RemoveByTagAsync()` before the call

**Per-source TTLs (as `HybridCacheEntryOptions`):**

| Source | `Expiration` (L2) | `LocalCacheExpiration` (L1) | Tags |
|---|---|---|---|
| Social engagement | 15 min | 5 min | `"dashboard"`, `"social"` |
| GA4/website overview | 2 hours | 15 min | `"dashboard"`, `"ga4"` |
| Search Console | 6 hours | 30 min | `"dashboard"`, `"gsc"` |
| Substack RSS | 2 hours | 15 min | `"dashboard"`, `"substack"` |
| Aggregated dashboard summary | 30 min | 5 min | `"dashboard"` |
| Engagement timeline | 15 min | 5 min | `"dashboard"`, `"social"` |
| Platform summaries | 15 min | 5 min | `"dashboard"`, `"social"` |

**Refresh rate limiting:**
- Track `_lastRefreshAt` as a `DateTimeOffset?` field using `TimeProvider.GetUtcNow()`
- On `forceRefresh`, check if `_lastRefreshAt` is within 60 seconds. If so, skip the refresh and return cached data with a log warning.
- If allowed, call `cache.RemoveByTagAsync("dashboard")` to clear all dashboard cache entries, then update `_lastRefreshAt`

**DI registration approach:**
Register the inner `DashboardAggregator` as a concrete class, then register `IDashboardAggregator` to resolve `CachedDashboardAggregator` which wraps it:

```csharp
services.AddScoped<DashboardAggregator>();
services.AddScoped<IDashboardAggregator>(sp =>
    new CachedDashboardAggregator(
        sp.GetRequiredService<DashboardAggregator>(),
        sp.GetRequiredService<HybridCache>(),
        sp.GetRequiredService<ILogger<CachedDashboardAggregator>>(),
        sp.GetRequiredService<TimeProvider>()));
```

### 4. Polly Resilience Policies for HTTP Clients

Modify the existing HttpClient registrations in `DependencyInjection.cs` and add resilience policies for the new analytics HTTP clients.

**Substack HttpClient** (new, added by section-03):

```csharp
services.AddHttpClient("Substack", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "PersonalBrandAssistant/1.0 (+https://github.com/MCKRUZ/personal-brand-assistant)");
})
.AddStandardResilienceHandler(options =>
{
    options.Retry.MaxRetryAttempts = 2;
    options.Retry.UseJitter = true;
    options.Retry.BackoffType = DelayBackoffType.Exponential;
    options.Retry.ShouldHandle = args => ValueTask.FromResult(
        args.Outcome.Result?.StatusCode is HttpStatusCode.TooManyRequests
        or HttpStatusCode.ServiceUnavailable
        or HttpStatusCode.InternalServerError
        or HttpStatusCode.BadGateway
        or HttpStatusCode.GatewayTimeout
        || args.Outcome.Exception is HttpRequestException or TaskCanceledException);

    options.CircuitBreaker.FailureRatio = 1.0;  // Open after all attempts in sampling window fail
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    options.CircuitBreaker.MinimumThroughput = 3;
    options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);

    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
});
```

**GA4 gRPC client resilience:**
The GA4 `BetaAnalyticsDataClient` uses gRPC, not HttpClient. Polly resilience must be applied at the call site in `GoogleAnalyticsService` (from section-02). Wrap each gRPC call in a `ResiliencePipeline` built from `ResiliencePipelineBuilder`:

```csharp
// Built once in GoogleAnalyticsService constructor
private readonly ResiliencePipeline _resiliencePipeline = new ResiliencePipelineBuilder()
    .AddTimeout(TimeSpan.FromSeconds(15))
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = new PredicateBuilder().Handle<Grpc.Core.RpcException>(ex =>
            ex.StatusCode is Grpc.Core.StatusCode.Unavailable
            or Grpc.Core.StatusCode.DeadlineExceeded
            or Grpc.Core.StatusCode.ResourceExhausted)
    })
    .AddCircuitBreaker(new CircuitBreakerStrategyOptions
    {
        FailureRatio = 1.0,
        SamplingDuration = TimeSpan.FromSeconds(30),
        MinimumThroughput = 3,
        BreakDuration = TimeSpan.FromSeconds(30)
    })
    .Build();
```

Usage in each method: `await _resiliencePipeline.ExecuteAsync(async ct => await _client.RunReportAsync(request, ct), ct);`

**Search Console HttpClient** follows the same pattern as Substack since it uses REST/HTTP. Configure via `AddStandardResilienceHandler` with 15s attempt timeout.

### 5. Substack SSRF Protection

In the `SubstackService` constructor (created in section-03), validate the configured `FeedUrl`:

```csharp
/// Validate that FeedUrl hostname matches *.substack.com to prevent SSRF.
/// Throw InvalidOperationException at startup if validation fails.
```

Implementation: parse `SubstackOptions.FeedUrl` with `Uri.TryCreate`, verify `uri.Host.EndsWith(".substack.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("substack.com", ...)`. Log and throw if invalid. This runs at service construction time (scoped lifetime), so a bad configuration is caught on first request.

### 6. GA4/GSC Startup Permissions Check

Add a startup health probe in `GoogleAnalyticsService` (created in section-02). On first use (lazy initialization), attempt a minimal GA4 API call (e.g., `RunReport` for a single metric over 1 day). If it fails, log a `Warning` but do not throw -- the dashboard will show the GA4 section as unavailable.

This is a design-time validation, not a runtime blocker. The `DashboardAggregator` already handles partial failures.

### 7. Database Index Migration

Create a new EF Core migration to add performance indexes for dashboard queries. The migration adds indexes that do not already exist on the `EngagementSnapshots` and `Contents` tables.

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/Migrations/{timestamp}_AddDashboardIndexes.cs`

Generate via: `dotnet ef migrations add AddDashboardIndexes --project src/PersonalBrandAssistant.Infrastructure --startup-project src/PersonalBrandAssistant.Api`

**Required indexes:**

1. **`EngagementSnapshots(FetchedAt, ContentPlatformStatusId)`** -- The existing index is on `(ContentPlatformStatusId, FetchedAt)` which is optimized for per-content lookups. The dashboard timeline queries filter by `FetchedAt` range first, then join on `ContentPlatformStatusId`, so a reverse-order index is needed.

   Add to `EngagementSnapshotConfiguration.cs`:
   ```csharp
   builder.HasIndex(e => new { e.FetchedAt, e.ContentPlatformStatusId });
   ```

2. **`Contents(PublishedAt) WHERE Status = Published`** -- Filtered index for counting published content in a date range. The content table already has indexes on `Status` and `ScheduledAt`, but not on `PublishedAt` (which lives on `ContentPlatformStatus`, not `Content`). Since the aggregator counts content by `Content.Status == Published` within a date range, and existing `HasIndex(c => c.Status)` already covers filtering by status, this index is better expressed as a composite on `ContentPlatformStatus`:

   Add to `ContentPlatformStatusConfiguration.cs`:
   ```csharp
   builder.HasIndex(c => new { c.PublishedAt, c.Platform })
       .HasFilter("\"PublishedAt\" IS NOT NULL");
   ```

After adding the index definitions to the configuration files, run the EF migration command to generate the migration file. The migration `Up` method will contain `CreateIndex` calls; the `Down` method will contain matching `DropIndex` calls.

### 8. Analytics Health Check Endpoint

Add a `GET /api/analytics/health` endpoint (wired in section-06) that returns connectivity status for each external data source. The caching/resilience layer informs this: if the circuit breaker for GA4 is open, report it as degraded.

The health check model:

```csharp
public record AnalyticsHealthStatus(
    string Source,       // "GA4", "SearchConsole", "Substack"
    bool IsHealthy,
    string? LastError,
    DateTimeOffset? LastSuccessAt);
```

This endpoint does not need caching (it should always reflect current state). Wire it as a simple aggregation of the circuit breaker states and last-known-good timestamps.

### 9. Configuration Keys

Add the following to `appsettings.json` (or `appsettings.Development.json`):

```json
{
  "GoogleAnalytics": {
    "CredentialsPath": "secrets/google-analytics-sa.json",
    "PropertyId": "261358185",
    "SiteUrl": "https://matthewkruczek.ai/"
  },
  "Substack": {
    "FeedUrl": "https://matthewkruczek.substack.com/feed"
  }
}
```

These are read by `GoogleAnalyticsOptions` and `SubstackOptions` (defined in section-01) and validated at runtime.

---

## File Summary (Actual)

| Action | File Path |
|--------|-----------|
| Create | `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/CachedDashboardAggregator.cs` |
| Create | `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/DashboardRefreshLimiter.cs` |
| Create | `src/PersonalBrandAssistant.Application/Common/Interfaces/IDashboardCacheInvalidator.cs` |
| Create | `src/PersonalBrandAssistant.Application/Common/Models/AnalyticsHealthStatus.cs` |
| Modify | `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` (HybridCache, resilience handler, decorator DI, singleton limiter) |
| Modify | `src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj` (add HybridCache + Http.Resilience packages) |
| Modify | `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/EngagementSnapshotConfiguration.cs` (add FetchedAt-first index) |
| Modify | `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentPlatformStatusConfiguration.cs` (add PublishedAt+Platform filtered index) |
| Create | `src/PersonalBrandAssistant.Infrastructure/Migrations/*_AddDashboardIndexes.cs` (generated) |
| Modify | `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/GoogleAnalyticsService.cs` (Polly resilience pipeline with shared predicate) |
| Modify | `src/PersonalBrandAssistant.Api/appsettings.json` (GoogleAnalytics + Substack config sections) |
| Create | `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AnalyticsServices/CachedDashboardAggregatorTests.cs` (6 tests) |
| Create | `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AnalyticsServices/ResiliencePolicyTests.cs` (5 tests) |

---

## Implementation Notes

- **HybridCache serialization:** Result<T> has a private constructor and cannot be serialized by HybridCache. Caching the inner VALUE type instead, with FactoryFailureException thrown on failure to prevent caching failures.
- **Rate limiting singleton:** Code review identified that scoped lifetime resets rate limiting per HTTP request. Extracted `DashboardRefreshLimiter` as a thread-safe singleton using `Interlocked.CompareExchange`.
- **DI registration:** Registers `CachedDashboardAggregator` as concrete scoped, then forwards both `IDashboardAggregator` and `IDashboardCacheInvalidator` to it (avoids brittle downcast).
- **Shared predicate:** Extracted `TransientExceptionPredicate` as static field in GoogleAnalyticsService, shared between retry and circuit breaker strategies.
- **Summary cache tags:** Changed from `["dashboard", "social"]` to `["dashboard"]` only per plan (summary aggregates all sources).
- **SSRF already implemented:** SubstackService SSRF validation was already in place from section-03. Tests added here verify it.
- **Substack resilience:** `AddStandardResilienceHandler()` with retry (2 attempts, exponential+jitter), circuit breaker, and separate attempt/total timeouts.
- **GA4 resilience:** Direct `ResiliencePipeline` for gRPC/REST calls with 15s total timeout (intentional for single-user dashboard).
- **Items deferred:** GA4 startup health probe and Search Console HTTP resilience deferred to section-06 (API endpoints) where health check endpoint is wired.
- **Test count:** 11 new tests (6 cache + 5 resilience/SSRF). All passing.