# Section 06: API Endpoints

## Overview

This section wires up five new analytics dashboard routes plus a health check endpoint in `AnalyticsEndpoints.cs`. It also extends the existing `/api/analytics/top` response with `impressions` and `engagementRate` fields, registers all new services from prior sections into `DependencyInjection.cs`, and adds period/date-range query parameter parsing logic.

**Dependencies:** This section requires sections 01-05 to be implemented first. Specifically:
- Section 01 (backend models): All DTOs, interfaces (`IDashboardAggregator`, `IGoogleAnalyticsService`, `ISubstackService`), configuration options (`GoogleAnalyticsOptions`, `SubstackOptions`)
- Section 04 (dashboard aggregator): `DashboardAggregator` implementation
- Section 05 (caching/resilience): `HybridCache` integration, Polly policies, `BetaAnalyticsDataClient` singleton registration

**Blocks:** Section 07 (frontend models/service) depends on the endpoint contracts defined here.

---

## Files Created or Modified (Actual)

| File | Action | Notes |
|------|--------|-------|
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AnalyticsEndpointTests.cs` | **Created** | 9 integration tests with lightweight TestFactory (no Postgres) |
| `src/PersonalBrandAssistant.Api/Endpoints/AnalyticsEndpoints.cs` | **Modified** | 6 new routes + `ParseDateRange` helper with `IDateTimeProvider` |
| `src/PersonalBrandAssistant.Application/Common/Models/WebsiteAnalyticsResponse.cs` | **Modified** | `Overview` made nullable (`WebsiteOverview?`) for partial failure |
| `src/PersonalBrandAssistant.Application/Common/Models/TopPerformingContent.cs` | **Already done** | `Impressions`/`EngagementRate` fields existed from prior section |
| `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` | **Already done** | All analytics services registered in sections 01-05 |

**Deviations from plan:**
- `TopPerformingContent` fields and DI registrations were already implemented in prior sections
- `ParseDateRange` injects `IDateTimeProvider` (code review fix) instead of using `DateTime.UtcNow`
- `WebsiteAnalyticsResponse.Overview` made nullable for proper partial failure handling (code review fix)
- Health endpoint probes GA4 and Search Console independently (code review fix)
- Added from/to date range validation: inverted range check, 365-day max (code review fix)

All paths are relative to `C:\Users\kruz7\OneDrive\Documents\Code Repos\MCKRUZ\personal-brand-assistant`.

---

## Tests FIRST

### Integration Tests: `AnalyticsEndpointTests.cs`

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AnalyticsEndpointTests.cs`

These tests use the existing `CustomWebApplicationFactory` and `PostgresFixture` pattern established in `HealthEndpointTests.cs`. The new test class must mock `IDashboardAggregator`, `IGoogleAnalyticsService`, and `ISubstackService` via `ConfigureTestServices` so the endpoints can be tested without real external APIs.

The test class follows the same `IClassFixture<PostgresFixture>` + `IAsyncLifetime` pattern. Create an authenticated client using `_factory.CreateAuthenticatedClient()` which sets the `X-Api-Key` header.

**Test stubs:**

```csharp
namespace PersonalBrandAssistant.Infrastructure.Tests.Api;

/// <summary>
/// Integration tests for the analytics dashboard API endpoints.
/// Uses WebApplicationFactory with mocked data sources.
/// </summary>
public class AnalyticsEndpointTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    // Follow HealthEndpointTests pattern for InitializeAsync/DisposeAsync.
    // Register mock IDashboardAggregator, IGoogleAnalyticsService, ISubstackService
    // in ConfigureTestServices override of CustomWebApplicationFactory.

    [Fact]
    /// GET /api/analytics/dashboard returns 200 with DashboardSummary JSON shape.
    /// Mock IDashboardAggregator.GetSummaryAsync to return a known DashboardSummary.
    /// Verify response contains totalEngagement, previousEngagement, engagementRate, etc.
    public async Task GetDashboard_Returns200_WithDashboardSummary() { }

    [Fact]
    /// GET /api/analytics/dashboard?period=7d parses period and passes correct
    /// from/to dates to IDashboardAggregator.GetSummaryAsync.
    /// Verify the date range spans exactly 7 days ending at today.
    public async Task GetDashboard_WithPeriod7d_UsesCorrectDateRange() { }

    [Fact]
    /// GET /api/analytics/engagement-timeline returns 200 with array of DailyEngagement.
    /// Mock IDashboardAggregator.GetTimelineAsync to return 3 days of data.
    /// Verify JSON array length and presence of date/total/platforms fields.
    public async Task GetEngagementTimeline_Returns200_WithDailyEngagementArray() { }

    [Fact]
    /// GET /api/analytics/platform-summary returns 200 with array of PlatformSummary.
    /// Mock returns 4 platforms. Verify each has platform, postCount, avgEngagement fields.
    public async Task GetPlatformSummary_Returns200_WithPlatformSummaryArray() { }

    [Fact]
    /// GET /api/analytics/website returns 200 with WebsiteAnalyticsResponse.
    /// Mock IGoogleAnalyticsService to return overview + topPages + trafficSources + searchQueries.
    /// Verify composite response shape.
    public async Task GetWebsiteAnalytics_Returns200_WithCompositeResponse() { }

    [Fact]
    /// GET /api/analytics/substack returns 200 with SubstackPost array.
    /// Mock ISubstackService.GetRecentPostsAsync to return 5 posts.
    /// Verify array length, title, url, publishedAt presence.
    public async Task GetSubstackPosts_Returns200_WithSubstackPostArray() { }

    [Fact]
    /// GET /api/analytics/health returns 200 with connectivity status object.
    /// Should include ga4, searchConsole, substack boolean fields.
    public async Task GetAnalyticsHealth_Returns200_WithConnectivityStatus() { }

    [Fact]
    /// GET /api/analytics/dashboard?period=invalid returns 400.
    /// Valid values: 1d, 7d, 14d, 30d, 90d. Anything else is rejected.
    public async Task GetDashboard_WithInvalidPeriod_Returns400() { }

    [Fact]
    /// GET /api/analytics/dashboard?period=30d&from=2025-01-01&to=2025-01-31
    /// When both period and from/to are provided, period takes precedence.
    /// Verify the date range passed to the aggregator matches the period (30d from today),
    /// not the explicit from/to values.
    public async Task GetDashboard_PeriodAndFromTo_PeriodTakesPrecedence() { }
}
```

**Key testing considerations:**

- The `CustomWebApplicationFactory` needs to be extended (or a derived version created) that replaces `IDashboardAggregator`, `IGoogleAnalyticsService`, and `ISubstackService` with mocks. You can do this by creating a factory subclass that accepts `Action<IServiceCollection>` for additional service overrides, or by creating mocks inline in `ConfigureTestServices`.
- All endpoints require the `X-Api-Key` header (they sit behind `ApiKeyMiddleware`). Use `CreateAuthenticatedClient()`.
- For verifying date ranges, capture the arguments passed to mock methods using Moq's `Callback` or `It.Is<>()` matchers.

---

## Implementation Details

### 1. Period/Date Range Parsing Logic

All six new endpoints accept these query parameters:
- `period` -- one of: `1d`, `7d`, `14d`, `30d`, `90d`
- `from` and `to` -- `DateTimeOffset` strings (ISO 8601)
- `refresh` -- optional boolean, defaults to `false`

**Precedence rule:** If `period` is provided, it takes precedence over `from`/`to`. The date range is calculated as:
- `to` = today at 23:59:59 UTC
- `from` = `to - period + 1 day` at 00:00:00 UTC

For example, `period=7d` means the last 7 complete days including today.

**Previous period calculation** (used by dashboard summary for comparison):
- `previousTo = from - 1 day`
- `previousFrom = previousTo - (to - from)` (equal-length mirror window)

Create a private static helper method in `AnalyticsEndpoints.cs`:

```csharp
/// <summary>
/// Parses period or from/to query params into a date range.
/// Returns Result.Failure with ValidationFailed if period is invalid and no from/to provided.
/// </summary>
private static Result<(DateTimeOffset From, DateTimeOffset To)> ParseDateRange(
    string? period, DateTimeOffset? from, DateTimeOffset? to)
```

**Valid period values:** `1d`, `7d`, `14d`, `30d`, `90d`. Return `Result.Failure(ErrorCode.ValidationFailed, "Invalid period...")` for anything else (when no `from`/`to` fallback).

If neither `period` nor `from`/`to` is provided, default to `30d`.

### 2. Extend `AnalyticsEndpoints.cs`

**File:** `src/PersonalBrandAssistant.Api/Endpoints/AnalyticsEndpoints.cs`

The existing file has 3 routes under `/api/analytics`. Add 6 more to the same route group. The new handler methods inject `IDashboardAggregator`, `IGoogleAnalyticsService`, and `ISubstackService` via Minimal API parameter injection.

**New routes to add:**

| Route | Method | Handler | Injected Services | Returns |
|---|---|---|---|---|
| `/api/analytics/dashboard` | GET | `GetDashboard` | `IDashboardAggregator` | `DashboardSummary` |
| `/api/analytics/engagement-timeline` | GET | `GetTimeline` | `IDashboardAggregator` | `DailyEngagement[]` |
| `/api/analytics/platform-summary` | GET | `GetPlatformSummaries` | `IDashboardAggregator` | `PlatformSummary[]` |
| `/api/analytics/website` | GET | `GetWebsiteAnalytics` | `IGoogleAnalyticsService` | `WebsiteAnalyticsResponse` |
| `/api/analytics/substack` | GET | `GetSubstackPosts` | `ISubstackService` | `SubstackPost[]` |
| `/api/analytics/health` | GET | `GetAnalyticsHealth` | `IGoogleAnalyticsService`, `ISubstackService` | Anonymous object |

**Handler pattern (all follow the same shape):**

Each handler:
1. Calls `ParseDateRange(period, from, to)` to validate and resolve the date range
2. Returns `400` if parsing fails
3. Calls the injected service method with the resolved `from`/`to`
4. Returns the result via `result.ToHttpResult()` (uses existing `ResultExtensions`)

For the `refresh` parameter: if `true`, the handler should call cache invalidation before the service call. Since section 05 integrates `HybridCache`, the `IDashboardAggregator` implementation already handles caching internally. The `refresh` parameter should be passed through to the aggregator or handled at the endpoint level by removing relevant cache tags before calling the service. The simplest approach: add an optional `bool refresh = false` parameter and pass it through to the aggregator methods (which will call `HybridCache.RemoveByTagAsync()` when true).

**`GetWebsiteAnalytics` handler specifics:**

This endpoint calls `IGoogleAnalyticsService` directly (not through the aggregator) and composes a response from four calls:
1. `GetOverviewAsync(from, to, ct)`
2. `GetTopPagesAsync(from, to, 20, ct)`
3. `GetTrafficSourcesAsync(from, to, ct)`
4. `GetTopQueriesAsync(from, to, 20, ct)`

Run all four in parallel with `Task.WhenAll`. Compose into a `WebsiteAnalyticsResponse` record (defined in section 01). If any sub-call fails, still return available data with `null` for the failed sections.

**`GetAnalyticsHealth` handler:**

Returns a simple anonymous object (or a small record) with connectivity status:

```csharp
/// Returns { ga4: true/false, searchConsole: true/false, substack: true/false }
/// Each field represents whether the service can successfully connect.
/// Call lightweight probe methods or catch exceptions on trivial requests.
```

This endpoint does not need `period`/`from`/`to` parameters.

### 3. Extend `TopPerformingContent` Record

**File:** `src/PersonalBrandAssistant.Application/Common/Models/TopPerformingContent.cs`

The current record:

```csharp
public record TopPerformingContent(
    Guid ContentId,
    string Title,
    int TotalEngagement,
    IReadOnlyDictionary<PlatformType, int> EngagementByPlatform);
```

Add two new fields required by the frontend top content table:

```csharp
public record TopPerformingContent(
    Guid ContentId,
    string Title,
    int TotalEngagement,
    IReadOnlyDictionary<PlatformType, int> EngagementByPlatform,
    int? Impressions,
    decimal? EngagementRate);
```

Both are nullable because impressions data may not be available for all platforms. `EngagementRate` is calculated as `TotalEngagement / Impressions * 100` when impressions is non-null and non-zero.

**Impact:** The `EngagementAggregator.GetTopContentAsync()` implementation in `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/EngagementAggregator.cs` must be updated to populate these new fields. Find where `TopPerformingContent` instances are constructed and add `Impressions` and `EngagementRate` values. If impression data is not available in the current engagement snapshot queries, pass `null` for both fields initially -- the dashboard aggregator or a future section can fill them in.

### 4. Register New Services in `DependencyInjection.cs`

**File:** `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs`

Add the following registrations within the `AddInfrastructure` method. Place them in a clearly commented "Analytics dashboard services" block after the existing content engine services:

```csharp
// Analytics dashboard services
services.Configure<GoogleAnalyticsOptions>(
    configuration.GetSection(GoogleAnalyticsOptions.SectionName));
services.Configure<SubstackOptions>(
    configuration.GetSection(SubstackOptions.SectionName));

// GA4 client - singleton, thread-safe, reusable
services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<GoogleAnalyticsOptions>>().Value;
    return new BetaAnalyticsDataClientBuilder
    {
        CredentialsPath = opts.CredentialsPath
    }.Build();
});

services.AddScoped<IGoogleAnalyticsService, GoogleAnalyticsService>();

services.AddHttpClient<ISubstackService, SubstackService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "PersonalBrandAssistant/1.0 (+https://github.com/MCKRUZ/personal-brand-assistant)");
});

services.AddScoped<IDashboardAggregator, DashboardAggregator>();
```

**Required `using` additions:**
- `Google.Analytics.Data.V1Beta` (for `BetaAnalyticsDataClientBuilder`)
- `PersonalBrandAssistant.Infrastructure.Services.AnalyticsServices` (for implementation classes)
- The `GoogleAnalyticsOptions` and `SubstackOptions` types from the Application models namespace

**HybridCache registration** (if not already added by section 05):

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

The NuGet package `Microsoft.Extensions.Caching.Hybrid` must be referenced in the Infrastructure `.csproj` (should already be added by section 05).

### 5. Docker Configuration

Ensure the `docker-compose.yml` API service section maps the secrets volume and sets the GA4 credentials path environment variable:

```yaml
volumes:
  - ./secrets:/app/secrets:ro
environment:
  GoogleAnalytics__CredentialsPath: /app/secrets/google-analytics-sa.json
```

This is a configuration-only change, no code files involved.

---

## Cross-Cutting Conventions to Apply

### Date semantics
- `from` is inclusive at `00:00:00 UTC`
- `to` is inclusive end-of-day `23:59:59.999 UTC`
- All dates stored/transmitted as `DateTimeOffset` in UTC

### Division by zero / null semantics
- Engagement rate: return `0` when impressions denominator is 0
- % change: return `null` when previous period value is 0 (frontend shows "N/A")
- Cost per engagement: return `null` when total engagement is 0

### Partial failure model
The `/api/analytics/website` endpoint composes multiple sub-calls. If one fails, return available data with `null` for the failed section rather than failing the entire response.

### API key requirement
All new endpoints are under `/api/analytics` which is covered by the existing `ApiKeyMiddleware`. No additional authentication configuration is needed. The `/api/analytics/health` endpoint should also require the API key (it is not exempt like `/health`).

---

## Implementation Checklist

1. Write `AnalyticsEndpointTests.cs` with all 9 test stubs (RED phase)
2. Add `ParseDateRange` static helper to `AnalyticsEndpoints.cs`
3. Add six new route registrations in `MapAnalyticsEndpoints`
4. Implement each handler method (`GetDashboard`, `GetTimeline`, `GetPlatformSummaries`, `GetWebsiteAnalytics`, `GetSubstackPosts`, `GetAnalyticsHealth`)
5. Extend `TopPerformingContent` with `Impressions` and `EngagementRate` fields
6. Update `EngagementAggregator.GetTopContentAsync()` to populate the new fields
7. Register all new services in `DependencyInjection.cs`
8. Update `CustomWebApplicationFactory` to remove/mock analytics background services if any are introduced
9. Verify all 9 endpoint tests pass (GREEN phase)
10. Verify `dotnet build` succeeds for the entire solution