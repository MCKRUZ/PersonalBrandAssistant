# Section 02: Google Analytics Service

## Overview

This section implements the `GoogleAnalyticsService` in the Infrastructure layer, providing GA4 Data API and Google Search Console integration. The service fulfills the `IGoogleAnalyticsService` interface defined in Section 01 (backend models) and is consumed by the `DashboardAggregator` in Section 04.

**Depends on:** Section 01 (backend models -- `IGoogleAnalyticsService`, `GoogleAnalyticsOptions`, `WebsiteOverview`, `PageViewEntry`, `TrafficSourceEntry`, `SearchQueryEntry`)

**Blocks:** Section 04 (dashboard aggregator), Section 06 (API endpoints)

---

## Background

The Personal Brand Assistant tracks content across 7 platforms but has no website analytics integration. This service connects to two Google APIs:

1. **GA4 Data API** (`Google.Analytics.Data.V1Beta`) -- gRPC-based, provides page views, sessions, users, bounce rate, and session duration for the `matthewkruczek.ai` property.
2. **Search Console API** (`Google.Apis.SearchConsole.v1`) -- REST-based, provides organic search query data (clicks, impressions, CTR, position).

Both APIs authenticate via a Google Cloud service account JSON key file. The file path is configurable via `GoogleAnalytics:CredentialsPath` (default: `secrets/google-analytics-sa.json`).

**Configuration values (from Section 01):**

```csharp
public class GoogleAnalyticsOptions
{
    public const string SectionName = "GoogleAnalytics";
    public string CredentialsPath { get; set; } = "secrets/google-analytics-sa.json";
    public string PropertyId { get; set; } = "261358185";
    public string SiteUrl { get; set; } = "https://matthewkruczek.ai/";
}
```

**Interface to implement (from Section 01):**

```csharp
public interface IGoogleAnalyticsService
{
    Task<Result<WebsiteOverview>> GetOverviewAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<Result<IReadOnlyList<PageViewEntry>>> GetTopPagesAsync(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);
    Task<Result<IReadOnlyList<TrafficSourceEntry>>> GetTrafficSourcesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<Result<IReadOnlyList<SearchQueryEntry>>> GetTopQueriesAsync(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);
}
```

**Models returned (from Section 01):**

```csharp
public record WebsiteOverview(int ActiveUsers, int Sessions, int PageViews, double AvgSessionDuration, double BounceRate, int NewUsers);
public record PageViewEntry(string PagePath, int Views, int Users);
public record TrafficSourceEntry(string Channel, int Sessions, int Users);
public record SearchQueryEntry(string Query, int Clicks, int Impressions, double Ctr, double Position);
```

**Date range semantics:** `from` is inclusive at `00:00:00 UTC`, `to` is inclusive end-of-day `23:59:59 UTC`. GA4 date strings use `yyyy-MM-dd` format. Convert `DateTimeOffset` parameters by taking `.UtcDateTime.ToString("yyyy-MM-dd")`.

**Error handling:** Use the project's `Result<T>` pattern. Return `Result<T>.Failure(ErrorCode.InternalError, ...)` when API calls fail. Never throw from the service -- all exceptions should be caught and wrapped.

---

## NuGet Packages to Add

Add these to `src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj`:

- `Google.Analytics.Data.V1Beta` -- GA4 Data API client (gRPC-based)
- `Google.Apis.SearchConsole.v1` -- Search Console REST client

These packages transitively bring in `Google.Apis.Auth` for service account authentication.

---

## Tests First

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Analytics/GoogleAnalyticsServiceTests.cs`

The GA4 client (`BetaAnalyticsDataClient`) and Search Console service (`SearchConsoleService`) are difficult to mock directly because they are concrete Google SDK types. The recommended approach is to create thin wrapper interfaces around the Google SDK calls, then mock those wrappers in tests.

### Wrapper Interfaces

Define two internal interfaces in the Infrastructure layer that wrap the Google SDK calls. This enables clean unit testing without gRPC/REST mocking complexity.

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/IGa4Client.cs`

```csharp
/// <summary>
/// Thin wrapper around BetaAnalyticsDataClient.RunReportAsync for testability.
/// </summary>
internal interface IGa4Client
{
    Task<RunReportResponse> RunReportAsync(RunReportRequest request, CancellationToken ct);
}
```

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/ISearchConsoleClient.cs`

```csharp
/// <summary>
/// Thin wrapper around SearchConsoleService.Searchanalytics.Query for testability.
/// </summary>
internal interface ISearchConsoleClient
{
    Task<SearchAnalyticsQueryResponse> QueryAsync(string siteUrl, SearchAnalyticsQueryRequest request, CancellationToken ct);
}
```

### Test Class Structure

Seven unit tests covering the core GA4 and Search Console interaction paths, plus two integration tests for credential loading.

```csharp
namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Analytics;

public class GoogleAnalyticsServiceTests
{
    // Shared test setup: create GoogleAnalyticsService with mocked IGa4Client, 
    // mocked ISearchConsoleClient, real GoogleAnalyticsOptions, and a test ILogger.

    [Fact]
    public async Task GetOverviewAsync_ReturnsWebsiteOverview_WithCorrectMetricMapping()
    {
        // Arrange: Mock IGa4Client.RunReportAsync to return a RunReportResponse with
        //   one row containing metrics: activeUsers=150, sessions=200, screenPageViews=500,
        //   averageSessionDuration=120.5, bounceRate=0.45, newUsers=80
        // Act: Call service.GetOverviewAsync(from, to, ct)
        // Assert: Result.IsSuccess, Value matches WebsiteOverview(150, 200, 500, 120.5, 0.45, 80)
    }

    [Fact]
    public async Task GetOverviewAsync_ReturnsFailure_WhenGa4ClientThrowsRpcException()
    {
        // Arrange: Mock IGa4Client to throw Grpc.Core.RpcException (StatusCode.Unavailable)
        // Act: Call service.GetOverviewAsync(...)
        // Assert: Result.IsSuccess == false, ErrorCode == InternalError, error message contains "GA4"
    }

    [Fact]
    public async Task GetOverviewAsync_HandlesEmptyResponse_ReturnsZeroFilledOverview()
    {
        // Arrange: Mock IGa4Client to return RunReportResponse with empty Rows collection
        // Act: Call service.GetOverviewAsync(...)
        // Assert: Result.IsSuccess, Value is WebsiteOverview(0, 0, 0, 0, 0, 0)
    }

    [Fact]
    public async Task GetTopPagesAsync_ReturnsSortedByViewsDescending_RespectsLimit()
    {
        // Arrange: Mock IGa4Client with 5 rows of (pagePath, screenPageViews, activeUsers)
        //   data, limit=3
        // Act: Call service.GetTopPagesAsync(from, to, 3, ct)
        // Assert: Result has exactly 3 entries, sorted by Views descending
    }

    [Fact]
    public async Task GetTrafficSourcesAsync_GroupsSessionsByChannel()
    {
        // Arrange: Mock IGa4Client with rows containing sessionDefaultChannelGroup dimension
        //   and sessions + activeUsers metrics
        // Act: Call service.GetTrafficSourcesAsync(from, to, ct)
        // Assert: Each TrafficSourceEntry has correct Channel, Sessions, Users values
    }

    [Fact]
    public async Task GetTopQueriesAsync_ReturnsSearchQueryEntries_FromSearchConsole()
    {
        // Arrange: Mock ISearchConsoleClient.QueryAsync to return SearchAnalyticsQueryResponse
        //   with rows containing keys=["ai tools"], clicks=50, impressions=1000, ctr=0.05, position=3.2
        // Act: Call service.GetTopQueriesAsync(from, to, 20, ct)
        // Assert: Result contains SearchQueryEntry("ai tools", 50, 1000, 0.05, 3.2)
    }

    [Fact]
    public async Task GetTopQueriesAsync_ReturnsFailure_WhenSearchConsoleThrowsGoogleApiException()
    {
        // Arrange: Mock ISearchConsoleClient to throw Google.GoogleApiException
        // Act: Call service.GetTopQueriesAsync(...)
        // Assert: Result.IsSuccess == false, ErrorCode == InternalError
    }
}
```

### Credential Integration Tests (separate class)

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Analytics/GoogleAnalyticsCredentialTests.cs`

```csharp
namespace PersonalBrandAssistant.Infrastructure.Tests.Services.Analytics;

public class GoogleAnalyticsCredentialTests
{
    [Fact]
    public void ServiceAccountCredentialLoading_SucceedsWithValidJsonFile()
    {
        // Arrange: Write a valid-shaped (but fake) service account JSON to a temp file
        // Act: Attempt to load via GoogleCredential.FromFile(tempPath)
        // Assert: Credential is non-null, no exception thrown
    }

    [Fact]
    public void ServiceAccountCredentialLoading_FailsGracefully_WithMissingFile()
    {
        // Arrange: Non-existent path
        // Act/Assert: GoogleCredential.FromFile throws FileNotFoundException (or equivalent)
        //   This validates the service's startup check behavior
    }
}
```

---

## Implementation Details

### File: `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/GoogleAnalyticsService.cs`

This is the main implementation file. The class implements `IGoogleAnalyticsService` and depends on `IGa4Client`, `ISearchConsoleClient`, `IOptions<GoogleAnalyticsOptions>`, and `ILogger<GoogleAnalyticsService>`.

**Constructor dependencies:**
- `IGa4Client ga4Client` -- thin wrapper around `BetaAnalyticsDataClient`
- `ISearchConsoleClient searchConsoleClient` -- thin wrapper around Search Console
- `IOptions<GoogleAnalyticsOptions> options` -- configuration
- `ILogger<GoogleAnalyticsService> logger`

**`GetOverviewAsync` implementation:**
1. Build a `RunReportRequest` with `Property = $"properties/{options.PropertyId}"`
2. Set date range using `from.UtcDateTime.ToString("yyyy-MM-dd")` and `to.UtcDateTime.ToString("yyyy-MM-dd")`
3. Request metrics: `activeUsers`, `sessions`, `screenPageViews`, `averageSessionDuration`, `bounceRate`, `newUsers`
4. No dimensions needed (aggregate totals)
5. Call `ga4Client.RunReportAsync(request, ct)`
6. If response has no rows, return zero-filled `WebsiteOverview`
7. Parse the single aggregate row, mapping each metric value by index to the corresponding `WebsiteOverview` field
8. Wrap in `Result.Success()`
9. Catch `RpcException` and `Exception`, log error, return `Result.Failure(ErrorCode.InternalError, ...)`

**`GetTopPagesAsync` implementation:**
1. Build `RunReportRequest` with dimension `pagePath`, metrics `screenPageViews` + `activeUsers`
2. Set `Limit = limit` on the request
3. Add `OrderBy` on `screenPageViews` descending
4. Map each row to `PageViewEntry(pagePath, views, users)`
5. Return as `IReadOnlyList<PageViewEntry>`

**`GetTrafficSourcesAsync` implementation:**
1. Build `RunReportRequest` with dimension `sessionDefaultChannelGroup`, metrics `sessions` + `activeUsers`
2. Map each row to `TrafficSourceEntry(channel, sessions, users)`

**`GetTopQueriesAsync` implementation:**
1. Build `SearchAnalyticsQueryRequest` with:
   - `StartDate = from.UtcDateTime.ToString("yyyy-MM-dd")`
   - `EndDate = to.UtcDateTime.ToString("yyyy-MM-dd")`
   - `Dimensions = new[] { "query" }`
   - `RowLimit = limit`
2. Call `searchConsoleClient.QueryAsync(options.SiteUrl, request, ct)`
3. Map each row to `SearchQueryEntry(query, clicks, impressions, ctr, position)`
4. Handle null response rows (Search Console returns null if no data) by returning empty list
5. Catch `GoogleApiException`, log, return failure

**Key parsing detail:** GA4's `RunReportResponse` returns metric values as strings in `row.MetricValues[i].Value`. Parse with `int.TryParse` for integer metrics and `double.TryParse` for floating-point. Default to 0 on parse failure.

### File: `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/Ga4ClientWrapper.cs`

Production implementation of `IGa4Client` that delegates to `BetaAnalyticsDataClient`:

```csharp
/// <summary>
/// Production wrapper around BetaAnalyticsDataClient.
/// Registered as Singleton because BetaAnalyticsDataClient is thread-safe and reusable.
/// </summary>
internal sealed class Ga4ClientWrapper : IGa4Client
{
    private readonly BetaAnalyticsDataClient _client;

    public Ga4ClientWrapper(BetaAnalyticsDataClient client) => _client = client;

    public Task<RunReportResponse> RunReportAsync(RunReportRequest request, CancellationToken ct)
        => _client.RunReportAsync(request, ct).AsTask();
    // Note: BetaAnalyticsDataClient returns AsyncUnaryCall<T>, call .AsTask() or similar
}
```

### File: `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/SearchConsoleClientWrapper.cs`

Production implementation of `ISearchConsoleClient`:

```csharp
/// <summary>
/// Production wrapper around SearchConsoleService.Searchanalytics.Query.
/// </summary>
internal sealed class SearchConsoleClientWrapper : ISearchConsoleClient
{
    private readonly SearchConsoleService _service;

    public SearchConsoleClientWrapper(SearchConsoleService service) => _service = service;

    public Task<SearchAnalyticsQueryResponse> QueryAsync(
        string siteUrl, SearchAnalyticsQueryRequest request, CancellationToken ct)
    {
        var query = _service.Searchanalytics.Query(request, siteUrl);
        return query.ExecuteAsync(ct);
    }
}
```

---

## Dependency Injection Registration

Add to `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` in the `AddInfrastructure` method:

1. **Bind configuration:**
   ```csharp
   services.Configure<GoogleAnalyticsOptions>(
       configuration.GetSection(GoogleAnalyticsOptions.SectionName));
   ```

2. **Register GA4 client (Singleton):**
   Build the `BetaAnalyticsDataClient` from the service account JSON file. If the credentials file is missing, log a warning and register a no-op/failing implementation so the app still starts.
   ```csharp
   services.AddSingleton<BetaAnalyticsDataClient>(sp =>
   {
       var opts = sp.GetRequiredService<IOptions<GoogleAnalyticsOptions>>().Value;
       return new BetaAnalyticsDataClientBuilder
       {
           CredentialsPath = opts.CredentialsPath
       }.Build();
   });
   services.AddSingleton<IGa4Client, Ga4ClientWrapper>();
   ```

3. **Register Search Console client (Singleton):**
   ```csharp
   services.AddSingleton<SearchConsoleService>(sp =>
   {
       var opts = sp.GetRequiredService<IOptions<GoogleAnalyticsOptions>>().Value;
       var credential = GoogleCredential.FromFile(opts.CredentialsPath)
           .CreateScoped(SearchConsoleService.Scope.WebmastersReadonly);
       return new SearchConsoleService(new Google.Apis.Services.BaseClientService.Initializer
       {
           HttpClientInitializer = credential,
           ApplicationName = "PersonalBrandAssistant"
       });
   });
   services.AddSingleton<ISearchConsoleClient, SearchConsoleClientWrapper>();
   ```

4. **Register the service (Scoped):**
   ```csharp
   services.AddScoped<IGoogleAnalyticsService, GoogleAnalyticsService>();
   ```

**Important:** The Singleton registrations for `BetaAnalyticsDataClient` and `SearchConsoleService` will throw at resolution time if the credentials file does not exist. Wrap in a try/catch at registration or use a factory that logs warnings. The GA4/GSC permissions validation startup check (Section 05) will handle this gracefully.

---

## Docker Configuration

Add to the `api` service in `docker-compose.yml`:

```yaml
volumes:
  - ./secrets:/app/secrets:ro
environment:
  GoogleAnalytics__CredentialsPath: /app/secrets/google-analytics-sa.json
```

The `secrets/` directory is `.gitignore`-d and contains the service account JSON key. The `:ro` mount makes it read-only in the container.

---

## Rate Limit Awareness

GA4 Data API allows approximately 40,000 tokens/hour (most report requests cost fewer than 10 tokens). Search Console allows 1,200 queries per minute. For a single-user dashboard with caching (Section 05), these limits are effectively unreachable. No special throttling logic is needed in this service beyond the caching layer added in Section 05.

---

## File Summary

| File | Action | Description |
|------|--------|-------------|
| `src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj` | Modify | Add `Google.Analytics.Data.V1Beta` and `Google.Apis.SearchConsole.v1` package references |
| `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/IGa4Client.cs` | Create | Internal testability wrapper for GA4 Data API |
| `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/ISearchConsoleClient.cs` | Create | Internal testability wrapper for Search Console API |
| `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/Ga4ClientWrapper.cs` | Create | Production implementation of `IGa4Client` |
| `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/SearchConsoleClientWrapper.cs` | Create | Production implementation of `ISearchConsoleClient` |
| `src/PersonalBrandAssistant.Infrastructure/Services/AnalyticsServices/GoogleAnalyticsService.cs` | Create | Main service implementing `IGoogleAnalyticsService` |
| `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` | Modify | Register GA4 client, Search Console client, wrappers, options, and service |
| `docker-compose.yml` | Modify | Add secrets volume mount and environment variable for credentials path |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Analytics/GoogleAnalyticsServiceTests.cs` | Create | 7 unit tests for service behavior |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Analytics/GoogleAnalyticsCredentialTests.cs` | Create | 2 integration tests for credential loading |

---

## Implementation Notes

- **Service made `internal sealed`:** GoogleAnalyticsService is internal since its constructor depends on internal wrapper interfaces. DI resolves via the public `IGoogleAnalyticsService` interface.
- **DynamicProxyGenAssembly2:** Added to InternalsVisibleTo in csproj to allow Moq to proxy internal interfaces.
- **Deprecated GoogleCredential.FromFile:** Used with `#pragma warning disable CS0618` since the new `CredentialFactory` API is not available in the installed package version.
- **Code review fix:** Refactored DI to register `GoogleCredential` as a shared singleton, eliminating duplicate file reads.
- **Docker compose changes deferred:** Skipped docker-compose.yml modification (secrets volume already configured separately).
- **Test count:** 11 tests (7 service unit + 2 credential + 2 from section-01 interface tests matching filter). All passing.