# Analytics Tier 1 — Website (GA4 + Search Console) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a working analytics page that shows real, automated website analytics for matthewkruczek.ai from Google Analytics 4 and Google Search Console.

**Architecture:** Clean Architecture across `PBA.*`. A thin client seam (`IGa4Client`, `ISearchConsoleClient`) wraps the Google SDKs so the mapping logic in `GoogleAnalyticsService` is fully unit-testable without network. MediatR queries expose the data; Minimal-API endpoints under `/api/analytics` send those queries and return `Result<T>` via `.ToApiResult()`. The Angular standalone analytics page calls the endpoints and renders summary cards, a traffic chart, and tables with PrimeNG + chart.js.

**Tech Stack:** .NET 10, C#, MediatR, `Google.Analytics.Data.V1Beta`, `Google.Apis.SearchConsole.v1`, xUnit + Moq; Angular 19, PrimeNG 20, chart.js 4, Jasmine/Karma.

**Scope:** Tier 1 only (website). Tiers 2 (cross-platform inventory) and 3 (Medium/Substack scraping) are separate plans per the spec at `docs/superpowers/specs/2026-06-04-analytics-page-design.md`.

**Conventions verified in repo (do not deviate):**
- MediatR pattern: `public static class GetX { public record Query(...) : IRequest<Result<TDto>>; public sealed class Handler(deps) : IRequestHandler<Query, Result<TDto>> { ... } }`. Handlers are auto-discovered via `AddApplicationDependencies()`.
- `Result<T>` lives in `PBA.Domain.Common`: `Result<T>.Success(value)`, `Result<T>.Fail("msg")`, `Result<T>.ValidationFailure([...])`. Failures carry `ResultFailureType` + `Errors`. There is **no** `ErrorCode`.
- DTOs live under `PBA.Application.Features.<Feature>.Dtos`. Service interfaces live under `PBA.Application.Common.Interfaces`.
- Service implementations live under `PBA.Infrastructure.Services.<Area>`. Options classes live under `PBA.Infrastructure.Configuration` and expose a `const string SectionName`.
- Endpoints: `public static class XEndpoints { public static void MapXEndpoints(this IEndpointRouteBuilder app) { var group = app.MapGroup("/api/x").WithTags("X"); ... } }`, registered in `src/PBA.Api/Program.cs`. **No per-endpoint auth exists in v2** — match that; do not add auth.
- DI registration goes in `src/PBA.Infrastructure/DependencyInjection.cs`.
- Tests: backend unit/integration in `tests/PBA.Infrastructure.Tests` and `tests/PBA.Api.Tests`. Frontend in the component/service folder as `*.spec.ts`.
- Build/test commands: `dotnet build PBA.slnx`, `dotnet test PBA.slnx`. Frontend: from `src/PersonalBrandAssistant.Web`, `npm test -- --watch=false --browsers=ChromeHeadless`.

---

## File Structure

**Backend — create:**
- `src/PBA.Infrastructure/Configuration/GoogleAnalyticsOptions.cs` — options (PropertyId, SiteUrl, CredentialsPath).
- `src/PBA.Application/Features/Analytics/Dtos/WebsiteAnalyticsDtos.cs` — `WebsiteOverview`, `PageViewEntry`, `TrafficSourceEntry`, `SearchQueryEntry`, `WebsiteAnalyticsDto`, `AnalyticsHealthDto`.
- `src/PBA.Application/Common/Interfaces/IGa4Client.cs` — thin GA4 seam.
- `src/PBA.Application/Common/Interfaces/ISearchConsoleClient.cs` — thin GSC seam.
- `src/PBA.Application/Common/Interfaces/IGoogleAnalyticsService.cs` — service contract.
- `src/PBA.Infrastructure/Services/Analytics/Ga4Client.cs` — wraps `BetaAnalyticsDataClient`.
- `src/PBA.Infrastructure/Services/Analytics/SearchConsoleClient.cs` — wraps `SearchConsoleService`.
- `src/PBA.Infrastructure/Services/Analytics/GoogleAnalyticsService.cs` — mapping + Result handling.
- `src/PBA.Application/Features/Analytics/Queries/GetWebsiteAnalytics.cs` — MediatR query + handler.
- `src/PBA.Application/Features/Analytics/Queries/GetAnalyticsHealth.cs` — MediatR query + handler.
- `src/PBA.Api/Endpoints/AnalyticsEndpoints.cs` — `/api/analytics/website`, `/api/analytics/health`.

**Backend — modify:**
- `src/PBA.Infrastructure/PBA.Infrastructure.csproj` — add Google NuGet packages.
- `src/PBA.Infrastructure/DependencyInjection.cs` — register options, clients, service.
- `src/PBA.Api/Program.cs` — `app.MapAnalyticsEndpoints();`.
- `src/PBA.Api/appsettings.json` — `GoogleAnalytics` config section.

**Backend — tests:**
- `tests/PBA.Infrastructure.Tests/Services/Analytics/GoogleAnalyticsServiceTests.cs`
- `tests/PBA.Api.Tests/Endpoints/AnalyticsEndpointsTests.cs`

**Frontend — create:**
- `src/PersonalBrandAssistant.Web/src/app/features/analytics/models/analytics.model.ts`
- `src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.ts`
- `src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.spec.ts`
- `src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.component.spec.ts`

**Frontend — modify:**
- `src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.component.ts` — replace stub with the real page.

---

## Task 1: Add Google NuGet packages

**Files:**
- Modify: `src/PBA.Infrastructure/PBA.Infrastructure.csproj`

- [ ] **Step 1: Add package references**

In the main `<ItemGroup>` containing `<PackageReference>` entries, add:

```xml
<PackageReference Include="Google.Analytics.Data.V1Beta" Version="4.18.0" />
<PackageReference Include="Google.Apis.SearchConsole.v1" Version="1.69.0.3680" />
```

> Note: pin to the latest stable available on restore. If these exact versions fail to restore, run `dotnet add src/PBA.Infrastructure package Google.Analytics.Data.V1Beta` and `dotnet add src/PBA.Infrastructure package Google.Apis.SearchConsole.v1` to let NuGet resolve current versions.

- [ ] **Step 2: Restore and build**

Run: `dotnet build PBA.slnx`
Expected: PASS (build succeeds with the new packages restored).

- [ ] **Step 3: Commit**

```bash
git add src/PBA.Infrastructure/PBA.Infrastructure.csproj
git commit -m "chore: add Google Analytics Data + Search Console SDKs"
```

---

## Task 2: GoogleAnalyticsOptions

**Files:**
- Create: `src/PBA.Infrastructure/Configuration/GoogleAnalyticsOptions.cs`
- Modify: `src/PBA.Api/appsettings.json`

- [ ] **Step 1: Create the options class**

```csharp
namespace PBA.Infrastructure.Configuration;

public sealed class GoogleAnalyticsOptions
{
    public const string SectionName = "GoogleAnalytics";

    /// <summary>GA4 numeric property id, e.g. "261358185".</summary>
    public string PropertyId { get; init; } = string.Empty;

    /// <summary>Search Console site URL, e.g. "https://matthewkruczek.ai/".</summary>
    public string SiteUrl { get; init; } = string.Empty;

    /// <summary>Path to the service-account JSON, relative to content root.</summary>
    public string CredentialsPath { get; init; } = "secrets/google-analytics-sa.json";
}
```

- [ ] **Step 2: Add the config section**

In `src/PBA.Api/appsettings.json`, add a top-level section (sibling to existing sections):

```json
"GoogleAnalytics": {
  "PropertyId": "261358185",
  "SiteUrl": "https://matthewkruczek.ai/",
  "CredentialsPath": "secrets/google-analytics-sa.json"
}
```

- [ ] **Step 3: Build**

Run: `dotnet build PBA.slnx`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/PBA.Infrastructure/Configuration/GoogleAnalyticsOptions.cs src/PBA.Api/appsettings.json
git commit -m "feat: add GoogleAnalyticsOptions config"
```

---

## Task 3: Website analytics DTOs

**Files:**
- Create: `src/PBA.Application/Features/Analytics/Dtos/WebsiteAnalyticsDtos.cs`

- [ ] **Step 1: Create the DTOs**

```csharp
namespace PBA.Application.Features.Analytics.Dtos;

public sealed record WebsiteOverview(
    int ActiveUsers,
    int Sessions,
    int PageViews,
    double AvgSessionDuration,
    double BounceRate,
    int NewUsers);

public sealed record PageViewEntry(string PagePath, int Views, int UniqueUsers);

public sealed record TrafficSourceEntry(string Channel, int Sessions, int Users);

public sealed record SearchQueryEntry(string Query, int Clicks, int Impressions, double Ctr, double Position);

public sealed record WebsiteAnalyticsDto(
    WebsiteOverview Overview,
    IReadOnlyList<PageViewEntry> TopPages,
    IReadOnlyList<TrafficSourceEntry> TrafficSources,
    IReadOnlyList<SearchQueryEntry> SearchQueries);

public sealed record AnalyticsHealthDto(bool Ga4, bool SearchConsole);
```

- [ ] **Step 2: Build**

Run: `dotnet build PBA.slnx`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/PBA.Application/Features/Analytics/Dtos/WebsiteAnalyticsDtos.cs
git commit -m "feat: add website analytics DTOs"
```

---

## Task 4: Client seam interfaces (IGa4Client, ISearchConsoleClient)

**Files:**
- Create: `src/PBA.Application/Common/Interfaces/IGa4Client.cs`
- Create: `src/PBA.Application/Common/Interfaces/ISearchConsoleClient.cs`

These interfaces reference Google SDK request/response types directly, so `PBA.Application` needs the package references too (transitively available, but add explicitly for clarity).

- [ ] **Step 1: Add Google packages to PBA.Application**

Run:
```bash
dotnet add src/PBA.Application package Google.Analytics.Data.V1Beta
dotnet add src/PBA.Application package Google.Apis.SearchConsole.v1
```

- [ ] **Step 2: Create IGa4Client**

```csharp
using Google.Analytics.Data.V1Beta;

namespace PBA.Application.Common.Interfaces;

/// <summary>
/// Thin seam over the GA4 Data API gRPC client so report mapping is unit-testable.
/// </summary>
public interface IGa4Client
{
    Task<RunReportResponse> RunReportAsync(RunReportRequest request, CancellationToken ct);
}
```

- [ ] **Step 3: Create ISearchConsoleClient**

```csharp
using Google.Apis.SearchConsole.v1.Data;

namespace PBA.Application.Common.Interfaces;

/// <summary>
/// Thin seam over the Search Console Search Analytics API so query mapping is unit-testable.
/// </summary>
public interface ISearchConsoleClient
{
    Task<SearchAnalyticsQueryResponse> QueryAsync(
        string siteUrl, SearchAnalyticsQueryRequest request, CancellationToken ct);
}
```

- [ ] **Step 4: Build**

Run: `dotnet build PBA.slnx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PBA.Application/Common/Interfaces/IGa4Client.cs src/PBA.Application/Common/Interfaces/ISearchConsoleClient.cs src/PBA.Application/PBA.Application.csproj
git commit -m "feat: add GA4 and Search Console client seams"
```

---

## Task 5: IGoogleAnalyticsService interface

**Files:**
- Create: `src/PBA.Application/Common/Interfaces/IGoogleAnalyticsService.cs`

- [ ] **Step 1: Create the interface**

```csharp
using PBA.Application.Features.Analytics.Dtos;
using PBA.Domain.Common;

namespace PBA.Application.Common.Interfaces;

public interface IGoogleAnalyticsService
{
    Task<Result<WebsiteOverview>> GetOverviewAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<Result<IReadOnlyList<PageViewEntry>>> GetTopPagesAsync(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);
    Task<Result<IReadOnlyList<TrafficSourceEntry>>> GetTrafficSourcesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<Result<IReadOnlyList<SearchQueryEntry>>> GetTopQueriesAsync(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build PBA.slnx`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/PBA.Application/Common/Interfaces/IGoogleAnalyticsService.cs
git commit -m "feat: add IGoogleAnalyticsService contract"
```

---

## Task 6: GoogleAnalyticsService (TDD — mapping logic)

This is the core. Write the tests first (adapted from the V1 blueprint), watch them fail, then implement.

**Files:**
- Create: `tests/PBA.Infrastructure.Tests/Services/Analytics/GoogleAnalyticsServiceTests.cs`
- Create: `src/PBA.Infrastructure/Services/Analytics/GoogleAnalyticsService.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Google.Analytics.Data.V1Beta;
using Google.Apis.SearchConsole.v1.Data;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;
using PBA.Infrastructure.Services.Analytics;

namespace PBA.Infrastructure.Tests.Services.Analytics;

public class GoogleAnalyticsServiceTests
{
    private readonly Mock<IGa4Client> _ga4 = new();
    private readonly Mock<ISearchConsoleClient> _gsc = new();
    private readonly GoogleAnalyticsService _sut;

    private readonly DateTimeOffset _from = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly DateTimeOffset _to = new(2026, 3, 24, 0, 0, 0, TimeSpan.Zero);

    public GoogleAnalyticsServiceTests()
    {
        var options = Options.Create(new GoogleAnalyticsOptions
        {
            PropertyId = "261358185",
            SiteUrl = "https://matthewkruczek.ai/",
            CredentialsPath = "secrets/google-analytics-sa.json"
        });
        _sut = new GoogleAnalyticsService(
            _ga4.Object, _gsc.Object, options, Mock.Of<ILogger<GoogleAnalyticsService>>());
    }

    [Fact]
    public async Task GetOverviewAsync_MapsSixMetricValues_InOrder()
    {
        var response = new RunReportResponse
        {
            Rows =
            {
                new Row
                {
                    MetricValues =
                    {
                        new MetricValue { Value = "150" },
                        new MetricValue { Value = "200" },
                        new MetricValue { Value = "500" },
                        new MetricValue { Value = "120.5" },
                        new MetricValue { Value = "0.45" },
                        new MetricValue { Value = "80" }
                    }
                }
            }
        };
        _ga4.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _sut.GetOverviewAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(150, result.Value!.ActiveUsers);
        Assert.Equal(200, result.Value.Sessions);
        Assert.Equal(500, result.Value.PageViews);
        Assert.Equal(120.5, result.Value.AvgSessionDuration);
        Assert.Equal(0.45, result.Value.BounceRate);
        Assert.Equal(80, result.Value.NewUsers);
    }

    [Fact]
    public async Task GetOverviewAsync_EmptyResponse_ReturnsZeroFilled()
    {
        _ga4.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunReportResponse());

        var result = await _sut.GetOverviewAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.ActiveUsers);
        Assert.Equal(0, result.Value.Sessions);
        Assert.Equal(0, result.Value.PageViews);
    }

    [Fact]
    public async Task GetOverviewAsync_RpcException_ReturnsFailureMentioningGa4()
    {
        _ga4.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "down")));

        var result = await _sut.GetOverviewAsync(_from, _to, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Contains("GA4"));
    }

    [Fact]
    public async Task GetTopPagesAsync_ReturnsRows_RespectingLimit()
    {
        var response = new RunReportResponse
        {
            Rows =
            {
                PageRow("/blog/post-1", "300", "100"),
                PageRow("/blog/post-2", "200", "80"),
                PageRow("/about", "150", "60")
            }
        };
        _ga4.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _sut.GetTopPagesAsync(_from, _to, 3, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
        Assert.Equal("/blog/post-1", result.Value[0].PagePath);
        Assert.Equal(300, result.Value[0].Views);
        Assert.Equal(100, result.Value[0].UniqueUsers);
    }

    [Fact]
    public async Task GetTrafficSourcesAsync_MapsChannelSessionsUsers()
    {
        var response = new RunReportResponse
        {
            Rows =
            {
                ChannelRow("Organic Search", "120", "90"),
                ChannelRow("Direct", "80", "70")
            }
        };
        _ga4.Setup(c => c.RunReportAsync(It.IsAny<RunReportRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _sut.GetTrafficSourcesAsync(_from, _to, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Organic Search", result.Value![0].Channel);
        Assert.Equal(120, result.Value[0].Sessions);
        Assert.Equal(90, result.Value[0].Users);
    }

    [Fact]
    public async Task GetTopQueriesAsync_MapsSearchConsoleRows()
    {
        var response = new SearchAnalyticsQueryResponse
        {
            Rows =
            [
                new ApiDataRow { Keys = ["ai tools"], Clicks = 50, Impressions = 1000, Ctr = 0.05, Position = 3.2 }
            ]
        };
        _gsc.Setup(c => c.QueryAsync(It.IsAny<string>(), It.IsAny<SearchAnalyticsQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _sut.GetTopQueriesAsync(_from, _to, 20, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("ai tools", result.Value![0].Query);
        Assert.Equal(50, result.Value[0].Clicks);
        Assert.Equal(1000, result.Value[0].Impressions);
        Assert.Equal(0.05, result.Value[0].Ctr);
        Assert.Equal(3.2, result.Value[0].Position);
    }

    [Fact]
    public async Task GetTopQueriesAsync_GoogleApiException_ReturnsFailure()
    {
        _gsc.Setup(c => c.QueryAsync(It.IsAny<string>(), It.IsAny<SearchAnalyticsQueryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Google.GoogleApiException("SearchConsole", "Forbidden"));

        var result = await _sut.GetTopQueriesAsync(_from, _to, 20, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Contains("Search Console"));
    }

    [Fact]
    public async Task GetTopQueriesAsync_NullRows_ReturnsEmptyList()
    {
        _gsc.Setup(c => c.QueryAsync(It.IsAny<string>(), It.IsAny<SearchAnalyticsQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchAnalyticsQueryResponse());

        var result = await _sut.GetTopQueriesAsync(_from, _to, 20, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    private static Row PageRow(string path, string views, string users) => new()
    {
        DimensionValues = { new DimensionValue { Value = path } },
        MetricValues = { new MetricValue { Value = views }, new MetricValue { Value = users } }
    };

    private static Row ChannelRow(string channel, string sessions, string users) => new()
    {
        DimensionValues = { new DimensionValue { Value = channel } },
        MetricValues = { new MetricValue { Value = sessions }, new MetricValue { Value = users } }
    };
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PBA.slnx --filter "FullyQualifiedName~GoogleAnalyticsServiceTests"`
Expected: FAIL to compile / type `GoogleAnalyticsService` not found.

- [ ] **Step 3: Implement GoogleAnalyticsService**

```csharp
using Google.Analytics.Data.V1Beta;
using Google.Apis.SearchConsole.v1.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Analytics.Dtos;
using PBA.Domain.Common;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services.Analytics;

public sealed class GoogleAnalyticsService(
    IGa4Client ga4,
    ISearchConsoleClient searchConsole,
    IOptions<GoogleAnalyticsOptions> options,
    ILogger<GoogleAnalyticsService> logger) : IGoogleAnalyticsService
{
    private readonly GoogleAnalyticsOptions _options = options.Value;

    public async Task<Result<WebsiteOverview>> GetOverviewAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var request = new RunReportRequest
        {
            Property = $"properties/{_options.PropertyId}",
            DateRanges = { Range(from, to) },
            Metrics =
            {
                new Metric { Name = "activeUsers" },
                new Metric { Name = "sessions" },
                new Metric { Name = "screenPageViews" },
                new Metric { Name = "averageSessionDuration" },
                new Metric { Name = "bounceRate" },
                new Metric { Name = "newUsers" }
            }
        };

        try
        {
            var response = await ga4.RunReportAsync(request, ct);
            var row = response.Rows.FirstOrDefault();
            if (row is null)
                return Result<WebsiteOverview>.Success(new WebsiteOverview(0, 0, 0, 0, 0, 0));

            return Result<WebsiteOverview>.Success(new WebsiteOverview(
                ActiveUsers: ParseInt(row.MetricValues[0].Value),
                Sessions: ParseInt(row.MetricValues[1].Value),
                PageViews: ParseInt(row.MetricValues[2].Value),
                AvgSessionDuration: ParseDouble(row.MetricValues[3].Value),
                BounceRate: ParseDouble(row.MetricValues[4].Value),
                NewUsers: ParseInt(row.MetricValues[5].Value)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GA4 GetOverview failed");
            return Result<WebsiteOverview>.Fail($"GA4 request failed: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<PageViewEntry>>> GetTopPagesAsync(
        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct)
    {
        var request = new RunReportRequest
        {
            Property = $"properties/{_options.PropertyId}",
            DateRanges = { Range(from, to) },
            Dimensions = { new Dimension { Name = "pagePath" } },
            Metrics = { new Metric { Name = "screenPageViews" }, new Metric { Name = "totalUsers" } },
            OrderBys = { new OrderBy { Metric = new OrderBy.Types.MetricOrderBy { MetricName = "screenPageViews" }, Desc = true } },
            Limit = limit
        };

        try
        {
            var response = await ga4.RunReportAsync(request, ct);
            var rows = response.Rows
                .Select(r => new PageViewEntry(
                    r.DimensionValues[0].Value,
                    ParseInt(r.MetricValues[0].Value),
                    ParseInt(r.MetricValues[1].Value)))
                .ToList();
            return Result<IReadOnlyList<PageViewEntry>>.Success(rows);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GA4 GetTopPages failed");
            return Result<IReadOnlyList<PageViewEntry>>.Fail($"GA4 request failed: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<TrafficSourceEntry>>> GetTrafficSourcesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var request = new RunReportRequest
        {
            Property = $"properties/{_options.PropertyId}",
            DateRanges = { Range(from, to) },
            Dimensions = { new Dimension { Name = "sessionDefaultChannelGroup" } },
            Metrics = { new Metric { Name = "sessions" }, new Metric { Name = "totalUsers" } },
            OrderBys = { new OrderBy { Metric = new OrderBy.Types.MetricOrderBy { MetricName = "sessions" }, Desc = true } }
        };

        try
        {
            var response = await ga4.RunReportAsync(request, ct);
            var rows = response.Rows
                .Select(r => new TrafficSourceEntry(
                    r.DimensionValues[0].Value,
                    ParseInt(r.MetricValues[0].Value),
                    ParseInt(r.MetricValues[1].Value)))
                .ToList();
            return Result<IReadOnlyList<TrafficSourceEntry>>.Success(rows);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GA4 GetTrafficSources failed");
            return Result<IReadOnlyList<TrafficSourceEntry>>.Fail($"GA4 request failed: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<SearchQueryEntry>>> GetTopQueriesAsync(
        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct)
    {
        var request = new SearchAnalyticsQueryRequest
        {
            StartDate = from.ToString("yyyy-MM-dd"),
            EndDate = to.ToString("yyyy-MM-dd"),
            Dimensions = ["query"],
            RowLimit = limit
        };

        try
        {
            var response = await searchConsole.QueryAsync(_options.SiteUrl, request, ct);
            var rows = (response.Rows ?? [])
                .Select(r => new SearchQueryEntry(
                    r.Keys[0],
                    (int)(r.Clicks ?? 0),
                    (int)(r.Impressions ?? 0),
                    r.Ctr ?? 0,
                    r.Position ?? 0))
                .ToList();
            return Result<IReadOnlyList<SearchQueryEntry>>.Success(rows);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Search Console GetTopQueries failed");
            return Result<IReadOnlyList<SearchQueryEntry>>.Fail($"Search Console request failed: {ex.Message}");
        }
    }

    private static DateRange Range(DateTimeOffset from, DateTimeOffset to) => new()
    {
        StartDate = from.ToString("yyyy-MM-dd"),
        EndDate = to.ToString("yyyy-MM-dd")
    };

    private static int ParseInt(string? v) =>
        int.TryParse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var i) ? i : 0;

    private static double ParseDouble(string? v) =>
        double.TryParse(v, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test PBA.slnx --filter "FullyQualifiedName~GoogleAnalyticsServiceTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add tests/PBA.Infrastructure.Tests/Services/Analytics/GoogleAnalyticsServiceTests.cs src/PBA.Infrastructure/Services/Analytics/GoogleAnalyticsService.cs
git commit -m "feat: implement GoogleAnalyticsService with GA4/GSC mapping"
```

---

## Task 7: Concrete clients (Ga4Client, SearchConsoleClient)

These are thin SDK wrappers — no unit tests (they only forward to the SDK; the mapping is covered in Task 6). They are exercised by the integration/health test in Task 11.

> **Verify before implementing:** the exact builder/credential construction for `BetaAnalyticsDataClient` and `SearchConsoleService` may differ by SDK version. Confirm against current docs (Context7 `resolve-library-id` → `query-docs`, or Microsoft Learn) before finalizing. The shapes below match `Google.Analytics.Data.V1Beta` 4.x and `Google.Apis.SearchConsole.v1` 1.69.x.

**Files:**
- Create: `src/PBA.Infrastructure/Services/Analytics/Ga4Client.cs`
- Create: `src/PBA.Infrastructure/Services/Analytics/SearchConsoleClient.cs`

- [ ] **Step 1: Implement Ga4Client**

```csharp
using Google.Analytics.Data.V1Beta;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services.Analytics;

public sealed class Ga4Client : IGa4Client
{
    private readonly BetaAnalyticsDataClient _client;

    public Ga4Client(IOptions<GoogleAnalyticsOptions> options)
    {
        _client = new BetaAnalyticsDataClientBuilder
        {
            CredentialsPath = options.Value.CredentialsPath
        }.Build();
    }

    public Task<RunReportResponse> RunReportAsync(RunReportRequest request, CancellationToken ct) =>
        _client.RunReportAsync(request, ct);
}
```

- [ ] **Step 2: Implement SearchConsoleClient**

```csharp
using Google.Apis.Auth.OAuth2;
using Google.Apis.SearchConsole.v1;
using Google.Apis.SearchConsole.v1.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Options;
using PBA.Application.Common.Interfaces;
using PBA.Infrastructure.Configuration;

namespace PBA.Infrastructure.Services.Analytics;

public sealed class SearchConsoleClient : ISearchConsoleClient
{
    private readonly SearchConsoleService _service;

    public SearchConsoleClient(IOptions<GoogleAnalyticsOptions> options)
    {
        var credential = GoogleCredential
            .FromFile(options.Value.CredentialsPath)
            .CreateScoped(SearchConsoleService.Scope.WebmastersReadonly);

        _service = new SearchConsoleService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "PersonalBrandAssistant"
        });
    }

    public async Task<SearchAnalyticsQueryResponse> QueryAsync(
        string siteUrl, SearchAnalyticsQueryRequest request, CancellationToken ct) =>
        await _service.Searchanalytics.Query(request, siteUrl).ExecuteAsync(ct);
}
```

- [ ] **Step 3: Build**

Run: `dotnet build PBA.slnx`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/PBA.Infrastructure/Services/Analytics/Ga4Client.cs src/PBA.Infrastructure/Services/Analytics/SearchConsoleClient.cs
git commit -m "feat: add GA4 and Search Console SDK clients"
```

---

## Task 8: MediatR query — GetWebsiteAnalytics

**Files:**
- Create: `src/PBA.Application/Features/Analytics/Queries/GetWebsiteAnalytics.cs`

The handler fans out the four service calls in parallel and composes the DTO. Each sub-call that fails degrades to empty data rather than failing the whole request (the website page should still render whatever is available).

- [ ] **Step 1: Implement the query + handler**

```csharp
using MediatR;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Analytics.Dtos;
using PBA.Domain.Common;

namespace PBA.Application.Features.Analytics.Queries;

public static class GetWebsiteAnalytics
{
    public record Query(DateTimeOffset From, DateTimeOffset To) : IRequest<Result<WebsiteAnalyticsDto>>;

    public sealed class Handler(IGoogleAnalyticsService ga) : IRequestHandler<Query, Result<WebsiteAnalyticsDto>>
    {
        public async Task<Result<WebsiteAnalyticsDto>> Handle(Query request, CancellationToken ct)
        {
            var overviewTask = ga.GetOverviewAsync(request.From, request.To, ct);
            var pagesTask = ga.GetTopPagesAsync(request.From, request.To, 10, ct);
            var sourcesTask = ga.GetTrafficSourcesAsync(request.From, request.To, ct);
            var queriesTask = ga.GetTopQueriesAsync(request.From, request.To, 20, ct);

            await Task.WhenAll(overviewTask, pagesTask, sourcesTask, queriesTask);

            var overview = (await overviewTask).Value ?? new WebsiteOverview(0, 0, 0, 0, 0, 0);
            var pages = (await pagesTask).Value ?? [];
            var sources = (await sourcesTask).Value ?? [];
            var queries = (await queriesTask).Value ?? [];

            return Result<WebsiteAnalyticsDto>.Success(
                new WebsiteAnalyticsDto(overview, pages, sources, queries));
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build PBA.slnx`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/PBA.Application/Features/Analytics/Queries/GetWebsiteAnalytics.cs
git commit -m "feat: add GetWebsiteAnalytics query"
```

---

## Task 9: MediatR query — GetAnalyticsHealth

**Files:**
- Create: `src/PBA.Application/Features/Analytics/Queries/GetAnalyticsHealth.cs`

- [ ] **Step 1: Implement the query + handler**

```csharp
using MediatR;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Analytics.Dtos;
using PBA.Domain.Common;

namespace PBA.Application.Features.Analytics.Queries;

public static class GetAnalyticsHealth
{
    public record Query : IRequest<Result<AnalyticsHealthDto>>;

    public sealed class Handler(IGoogleAnalyticsService ga) : IRequestHandler<Query, Result<AnalyticsHealthDto>>
    {
        public async Task<Result<AnalyticsHealthDto>> Handle(Query request, CancellationToken ct)
        {
            var now = DateTimeOffset.UtcNow;
            var from = now.AddDays(-1);

            var ga4Ok = (await ga.GetOverviewAsync(from, now, ct)).IsSuccess;
            var gscOk = (await ga.GetTopQueriesAsync(from, now, 1, ct)).IsSuccess;

            return Result<AnalyticsHealthDto>.Success(new AnalyticsHealthDto(ga4Ok, gscOk));
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build PBA.slnx`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/PBA.Application/Features/Analytics/Queries/GetAnalyticsHealth.cs
git commit -m "feat: add GetAnalyticsHealth query"
```

---

## Task 10: AnalyticsEndpoints + DI registration + Program wiring

**Files:**
- Create: `src/PBA.Api/Endpoints/AnalyticsEndpoints.cs`
- Modify: `src/PBA.Infrastructure/DependencyInjection.cs`
- Modify: `src/PBA.Api/Program.cs`

The `period` query param (`7d`/`30d`/`90d`) is parsed to a date range; invalid values return 400. `period` takes precedence over `from`/`to`.

- [ ] **Step 1: Create the endpoints**

```csharp
using MediatR;
using PBA.Api.Extensions;
using PBA.Application.Features.Analytics.Queries;

namespace PBA.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analytics").WithTags("Analytics");

        group.MapGet("/website", async (
            string? period, DateTimeOffset? from, DateTimeOffset? to,
            ISender sender, CancellationToken ct) =>
        {
            if (!TryResolveRange(period, from, to, out var range))
                return Results.BadRequest($"Invalid period '{period}'. Use 7d, 30d, or 90d.");

            var result = await sender.Send(new GetWebsiteAnalytics.Query(range.From, range.To), ct);
            return result.ToApiResult();
        });

        group.MapGet("/health", async (ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetAnalyticsHealth.Query(), ct);
            return result.ToApiResult();
        });
    }

    private static bool TryResolveRange(
        string? period, DateTimeOffset? from, DateTimeOffset? to, out (DateTimeOffset From, DateTimeOffset To) range)
    {
        var today = DateTimeOffset.UtcNow.Date;
        range = default;

        if (!string.IsNullOrWhiteSpace(period))
        {
            int days = period switch { "7d" => 7, "30d" => 30, "90d" => 90, _ => -1 };
            if (days < 0) return false;
            range = (new DateTimeOffset(today.AddDays(-(days - 1)), TimeSpan.Zero), new DateTimeOffset(today, TimeSpan.Zero));
            return true;
        }

        if (from.HasValue && to.HasValue)
        {
            range = (from.Value, to.Value);
            return true;
        }

        // Default: last 30 days
        range = (new DateTimeOffset(today.AddDays(-29), TimeSpan.Zero), new DateTimeOffset(today, TimeSpan.Zero));
        return true;
    }
}
```

- [ ] **Step 2: Register services in DI**

In `src/PBA.Infrastructure/DependencyInjection.cs`, inside `AddInfrastructureDependencies` (before `return services;`), add:

```csharp
        services.Configure<GoogleAnalyticsOptions>(
            configuration.GetSection(GoogleAnalyticsOptions.SectionName));
        services.AddSingleton<IGa4Client, PBA.Infrastructure.Services.Analytics.Ga4Client>();
        services.AddSingleton<ISearchConsoleClient, PBA.Infrastructure.Services.Analytics.SearchConsoleClient>();
        services.AddScoped<IGoogleAnalyticsService, PBA.Infrastructure.Services.Analytics.GoogleAnalyticsService>();
```

> The two SDK clients are registered as singletons because they hold an authenticated connection built once from the service-account file.

- [ ] **Step 3: Wire endpoints in Program.cs**

In `src/PBA.Api/Program.cs`, after `app.MapFeedEndpoints();` (line ~51), add:

```csharp
app.MapAnalyticsEndpoints();
```

- [ ] **Step 4: Build**

Run: `dotnet build PBA.slnx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PBA.Api/Endpoints/AnalyticsEndpoints.cs src/PBA.Infrastructure/DependencyInjection.cs src/PBA.Api/Program.cs
git commit -m "feat: wire analytics endpoints and DI"
```

---

## Task 11: API integration tests

Verify the endpoint contract with mocked services (no real Google calls). Mirror the existing `WebApplicationFactory<Program>` pattern in `tests/PBA.Api.Tests`.

**Files:**
- Create: `tests/PBA.Api.Tests/Endpoints/AnalyticsEndpointsTests.cs`

> Check an existing test in `tests/PBA.Api.Tests/Endpoints/` first to copy the exact factory/setup helper this project uses (connection-string override, hosted-service removal). Reuse that helper rather than re-implementing it. The skeleton below assumes a `WebApplicationFactory<Program>` that lets you override services via `WithWebHostBuilder`.

- [ ] **Step 1: Write the tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PBA.Application.Common.Interfaces;
using PBA.Application.Features.Analytics.Dtos;
using PBA.Domain.Common;

namespace PBA.Api.Tests.Endpoints;

public class AnalyticsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Mock<IGoogleAnalyticsService> _ga = new();

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public AnalyticsEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private HttpClient Client() => _factory.WithWebHostBuilder(b =>
        b.ConfigureTestServices(s => s.AddScoped(_ => _ga.Object))).CreateClient();

    [Fact]
    public async Task GetWebsite_Returns200_WithCompositeShape()
    {
        _ga.Setup(g => g.GetOverviewAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WebsiteOverview>.Success(new WebsiteOverview(100, 150, 300, 120.5, 0.45, 80)));
        _ga.Setup(g => g.GetTopPagesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<PageViewEntry>>.Success([new("/blog", 50, 30)]));
        _ga.Setup(g => g.GetTrafficSourcesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TrafficSourceEntry>>.Success([new("Organic Search", 100, 80)]));
        _ga.Setup(g => g.GetTopQueriesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchQueryEntry>>.Success([new("ai tools", 50, 1000, 0.05, 3.2)]));

        var response = await Client().GetAsync("/api/analytics/website?period=30d");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(json.TryGetProperty("overview", out _));
        Assert.True(json.TryGetProperty("topPages", out _));
        Assert.True(json.TryGetProperty("trafficSources", out _));
        Assert.True(json.TryGetProperty("searchQueries", out _));
    }

    [Fact]
    public async Task GetWebsite_InvalidPeriod_Returns400()
    {
        var response = await Client().GetAsync("/api/analytics/website?period=nonsense");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetHealth_Returns200_WithGa4AndSearchConsoleFlags()
    {
        _ga.Setup(g => g.GetOverviewAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WebsiteOverview>.Success(new WebsiteOverview(0, 0, 0, 0, 0, 0)));
        _ga.Setup(g => g.GetTopQueriesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SearchQueryEntry>>.Success([]));

        var response = await Client().GetAsync("/api/analytics/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.True(json.GetProperty("ga4").GetBoolean());
        Assert.True(json.GetProperty("searchConsole").GetBoolean());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail, then pass**

Run: `dotnet test PBA.slnx --filter "FullyQualifiedName~AnalyticsEndpointsTests"`
Expected: PASS (3 tests). If the factory needs a DB connection string to boot, copy the override used by the sibling endpoint tests in this project.

- [ ] **Step 3: Commit**

```bash
git add tests/PBA.Api.Tests/Endpoints/AnalyticsEndpointsTests.cs
git commit -m "test: add analytics endpoint integration tests"
```

---

## Task 12: Frontend models

**Files:**
- Create: `src/PersonalBrandAssistant.Web/src/app/features/analytics/models/analytics.model.ts`

- [ ] **Step 1: Create the models**

```typescript
export interface WebsiteOverview {
  activeUsers: number;
  sessions: number;
  pageViews: number;
  avgSessionDuration: number;
  bounceRate: number;
  newUsers: number;
}

export interface PageViewEntry {
  pagePath: string;
  views: number;
  uniqueUsers: number;
}

export interface TrafficSourceEntry {
  channel: string;
  sessions: number;
  users: number;
}

export interface SearchQueryEntry {
  query: string;
  clicks: number;
  impressions: number;
  ctr: number;
  position: number;
}

export interface WebsiteAnalytics {
  overview: WebsiteOverview;
  topPages: PageViewEntry[];
  trafficSources: TrafficSourceEntry[];
  searchQueries: SearchQueryEntry[];
}

export interface AnalyticsHealth {
  ga4: boolean;
  searchConsole: boolean;
}

export type AnalyticsPeriod = '7d' | '30d' | '90d';
```

- [ ] **Step 2: Commit**

```bash
git add src/PersonalBrandAssistant.Web/src/app/features/analytics/models/analytics.model.ts
git commit -m "feat: add analytics frontend models"
```

---

## Task 13: Frontend service (TDD)

**Files:**
- Create: `src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.ts`
- Create: `src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.spec.ts`

- [ ] **Step 1: Write the failing spec**

```typescript
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { AnalyticsService } from './analytics.service';
import { WebsiteAnalytics } from '../models/analytics.model';

describe('AnalyticsService', () => {
  let service: AnalyticsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule],
      providers: [AnalyticsService],
    });
    service = TestBed.inject(AnalyticsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('requests website analytics with the period param', () => {
    const stub: WebsiteAnalytics = {
      overview: { activeUsers: 1, sessions: 2, pageViews: 3, avgSessionDuration: 4, bounceRate: 5, newUsers: 6 },
      topPages: [], trafficSources: [], searchQueries: [],
    };

    let result: WebsiteAnalytics | undefined;
    service.getWebsite('30d').subscribe(r => (result = r));

    const req = httpMock.expectOne('/api/analytics/website?period=30d');
    expect(req.request.method).toBe('GET');
    req.flush(stub);

    expect(result?.overview.activeUsers).toBe(1);
  });

  it('requests health', () => {
    service.getHealth().subscribe();
    const req = httpMock.expectOne('/api/analytics/health');
    expect(req.request.method).toBe('GET');
    req.flush({ ga4: true, searchConsole: true });
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run (from `src/PersonalBrandAssistant.Web`): `npm test -- --watch=false --browsers=ChromeHeadless`
Expected: FAIL (cannot find module `./analytics.service`).

- [ ] **Step 3: Implement the service**

```typescript
import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AnalyticsHealth, AnalyticsPeriod, WebsiteAnalytics } from '../models/analytics.model';

@Injectable({ providedIn: 'root' })
export class AnalyticsService {
  private readonly baseUrl = '/api/analytics';

  constructor(private readonly http: HttpClient) {}

  getWebsite(period: AnalyticsPeriod): Observable<WebsiteAnalytics> {
    const params = new HttpParams().set('period', period);
    return this.http.get<WebsiteAnalytics>(`${this.baseUrl}/website`, { params });
  }

  getHealth(): Observable<AnalyticsHealth> {
    return this.http.get<AnalyticsHealth>(`${this.baseUrl}/health`);
  }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `npm test -- --watch=false --browsers=ChromeHeadless`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.ts src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.spec.ts
git commit -m "feat: add analytics frontend service"
```

---

## Task 14: Analytics page component (period selector + website panel)

Replace the stub with a real page: a period selector, summary cards, a traffic-sources chart, a top-pages table, and a search-queries table. Uses Angular signals for state and PrimeNG components already in the project.

**Files:**
- Modify: `src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.component.ts`
- Create: `src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.component.spec.ts`

- [ ] **Step 1: Write the failing spec**

```typescript
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { AnalyticsComponent } from './analytics.component';
import { WebsiteAnalytics } from './models/analytics.model';

describe('AnalyticsComponent', () => {
  let httpMock: HttpTestingController;

  const stub: WebsiteAnalytics = {
    overview: { activeUsers: 123, sessions: 200, pageViews: 500, avgSessionDuration: 90, bounceRate: 0.4, newUsers: 80 },
    topPages: [{ pagePath: '/blog', views: 50, uniqueUsers: 30 }],
    trafficSources: [{ channel: 'Organic Search', sessions: 100, users: 80 }],
    searchQueries: [{ query: 'ai tools', clicks: 50, impressions: 1000, ctr: 0.05, position: 3.2 }],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AnalyticsComponent, HttpClientTestingModule],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loads website analytics on init and renders active users', () => {
    const fixture = TestBed.createComponent(AnalyticsComponent);
    fixture.detectChanges();

    httpMock.expectOne('/api/analytics/website?period=30d').flush(stub);
    // health call is fire-and-forget
    const health = httpMock.match('/api/analytics/health');
    health.forEach(r => r.flush({ ga4: true, searchConsole: true }));

    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('123');
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `npm test -- --watch=false --browsers=ChromeHeadless`
Expected: FAIL (component still the stub; no data rendered / imports missing).

- [ ] **Step 3: Implement the component**

```typescript
import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TableModule } from 'primeng/table';
import { ChartModule } from 'primeng/chart';
import { SelectButtonModule } from 'primeng/selectbutton';
import { FormsModule } from '@angular/forms';
import { AnalyticsService } from './services/analytics.service';
import { AnalyticsPeriod, WebsiteAnalytics } from './models/analytics.model';

@Component({
  selector: 'app-analytics',
  standalone: true,
  imports: [CommonModule, FormsModule, TableModule, ChartModule, SelectButtonModule],
  template: `
    <div class="p-4">
      <div class="flex align-items-center justify-content-between mb-3">
        <h2 class="m-0">Website Analytics</h2>
        <p-selectButton
          [options]="periodOptions"
          [ngModel]="period()"
          (ngModelChange)="changePeriod($event)"
          optionLabel="label" optionValue="value" />
      </div>

      @if (loading()) {
        <p>Loading…</p>
      } @else if (data(); as d) {
        <div class="grid mb-4">
          <div class="col-6 md:col-2"><div class="surface-card p-3 border-round"><div class="text-500">Users</div><div class="text-2xl font-bold">{{ d.overview.activeUsers }}</div></div></div>
          <div class="col-6 md:col-2"><div class="surface-card p-3 border-round"><div class="text-500">Sessions</div><div class="text-2xl font-bold">{{ d.overview.sessions }}</div></div></div>
          <div class="col-6 md:col-2"><div class="surface-card p-3 border-round"><div class="text-500">Page Views</div><div class="text-2xl font-bold">{{ d.overview.pageViews }}</div></div></div>
          <div class="col-6 md:col-2"><div class="surface-card p-3 border-round"><div class="text-500">New Users</div><div class="text-2xl font-bold">{{ d.overview.newUsers }}</div></div></div>
          <div class="col-6 md:col-2"><div class="surface-card p-3 border-round"><div class="text-500">Bounce</div><div class="text-2xl font-bold">{{ (d.overview.bounceRate * 100) | number:'1.0-1' }}%</div></div></div>
          <div class="col-6 md:col-2"><div class="surface-card p-3 border-round"><div class="text-500">Avg Sess (s)</div><div class="text-2xl font-bold">{{ d.overview.avgSessionDuration | number:'1.0-0' }}</div></div></div>
        </div>

        <div class="grid">
          <div class="col-12 md:col-5">
            <h3>Traffic Sources</h3>
            <p-chart type="doughnut" [data]="trafficChart()" />
          </div>
          <div class="col-12 md:col-7">
            <h3>Top Pages</h3>
            <p-table [value]="d.topPages" [paginator]="d.topPages.length > 10" [rows]="10">
              <ng-template pTemplate="header"><tr><th>Page</th><th>Views</th><th>Users</th></tr></ng-template>
              <ng-template pTemplate="body" let-row><tr><td>{{ row.pagePath }}</td><td>{{ row.views }}</td><td>{{ row.uniqueUsers }}</td></tr></ng-template>
            </p-table>
          </div>
        </div>

        <h3>Top Search Queries</h3>
        <p-table [value]="d.searchQueries" [paginator]="d.searchQueries.length > 10" [rows]="10">
          <ng-template pTemplate="header"><tr><th>Query</th><th>Clicks</th><th>Impressions</th><th>CTR</th><th>Position</th></tr></ng-template>
          <ng-template pTemplate="body" let-row>
            <tr><td>{{ row.query }}</td><td>{{ row.clicks }}</td><td>{{ row.impressions }}</td><td>{{ (row.ctr * 100) | number:'1.0-1' }}%</td><td>{{ row.position | number:'1.0-1' }}</td></tr>
          </ng-template>
        </p-table>
      } @else {
        <p>No analytics data available.</p>
      }
    </div>
  `,
})
export class AnalyticsComponent implements OnInit {
  readonly periodOptions = [
    { label: '7d', value: '7d' as AnalyticsPeriod },
    { label: '30d', value: '30d' as AnalyticsPeriod },
    { label: '90d', value: '90d' as AnalyticsPeriod },
  ];

  readonly period = signal<AnalyticsPeriod>('30d');
  readonly loading = signal(false);
  readonly data = signal<WebsiteAnalytics | null>(null);
  readonly trafficChart = signal<unknown>({});

  constructor(private readonly api: AnalyticsService) {}

  ngOnInit(): void {
    this.load();
    this.api.getHealth().subscribe();
  }

  changePeriod(p: AnalyticsPeriod): void {
    this.period.set(p);
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.api.getWebsite(this.period()).subscribe({
      next: d => {
        this.data.set(d);
        this.trafficChart.set({
          labels: d.trafficSources.map(s => s.channel),
          datasets: [{ data: d.trafficSources.map(s => s.sessions) }],
        });
        this.loading.set(false);
      },
      error: () => {
        this.data.set(null);
        this.loading.set(false);
      },
    });
  }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `npm test -- --watch=false --browsers=ChromeHeadless`
Expected: PASS.

> If `p-selectButton`/`p-chart`/`p-table` cause template compile errors in the test, confirm the import paths against the installed PrimeNG 20 (`node_modules/primeng/<module>`). All three exist in PrimeNG 20.

- [ ] **Step 5: Commit**

```bash
git add src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.component.ts src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.component.spec.ts
git commit -m "feat: build website analytics page"
```

---

## Task 15: Full verification

- [ ] **Step 1: Backend build + full test run**

Run: `dotnet test PBA.slnx`
Expected: PASS (all existing tests + the new analytics tests).

- [ ] **Step 2: Frontend build + test**

Run (from `src/PersonalBrandAssistant.Web`): `npm test -- --watch=false --browsers=ChromeHeadless` then `npm run build`
Expected: PASS, build succeeds.

- [ ] **Step 3: Manual smoke test (requires the service-account file present at `secrets/google-analytics-sa.json` and DB up)**

Run the API (`dotnet run --project src/PBA.Api`) and the web (`npm start`), open `/analytics`, and confirm:
- Summary cards show real numbers for matthewkruczek.ai.
- Switching 7d/30d/90d reloads the data.
- Traffic chart and tables render.
- `GET /api/analytics/health` returns `{ ga4: true, searchConsole: true }`.

> Per the project's verify-before-done rule, do not mark Tier 1 complete until this manual smoke test passes against the real GA4 property. If the service account lacks access to property `261358185` or the GSC site, fix access before claiming done.

- [ ] **Step 4: Final commit (if any verification fixes were needed)**

```bash
git add -A
git commit -m "test: verify analytics tier 1 end-to-end"
```

---

## Self-Review

**Spec coverage (Tier 1 scope):**
- GA4 metrics (users, sessions, pageviews, engagement/bounce, avg duration, top pages, traffic sources) → Tasks 3, 6, 8, 14. ✓
- GSC metrics (clicks, impressions, CTR, position, top queries) → Tasks 3, 6, 8, 14. ✓
- Composite `/website` + `/health` endpoints → Tasks 8, 9, 10. ✓
- Period selector (7d/30d/90d) → Tasks 10, 14. ✓
- Service-account auth, options → Tasks 2, 7. ✓
- Honest degradation (per-call failures → empty, page still renders) → Task 8 handler. ✓
- Caching (spec §4.1) → **deferred**: GA4 quota is not a concern at this property's volume for an interactive dashboard. Caching + the Hangfire `MetricsRefreshJob` are introduced in the Tier 2 plan where snapshot persistence exists. Noted as an intentional deferral, not a gap.
- API-key auth (spec §7) → **not applicable**: v2 has no endpoint auth; matching existing endpoints. Flagged for a future cross-cutting auth pass, out of scope here.

**Placeholder scan:** No TBDs. Every code step has complete code. The two "verify against SDK docs" notes (Tasks 7, 11) are explicit verification instructions, not placeholders — the code is fully written; the engineer confirms SDK construction shapes that legitimately vary by version.

**Type consistency:** DTO names (`WebsiteOverview`, `PageViewEntry`, `TrafficSourceEntry`, `SearchQueryEntry`, `WebsiteAnalyticsDto`, `AnalyticsHealthDto`) are identical across backend tasks and mirrored in the frontend models (camelCase). Method names (`GetOverviewAsync`, `GetTopPagesAsync`, `GetTrafficSourcesAsync`, `GetTopQueriesAsync`) match between `IGoogleAnalyticsService` (Task 5), implementation (Task 6), and handlers (Tasks 8, 9). Frontend `getWebsite`/`getHealth` match between service (Task 13) and component (Task 14).

**Tiers 2 & 3:** intentionally out of this plan. They get their own plans (cross-platform inventory; Medium/Substack scraping + `EngagementSnapshot` + security-reviewer pass) per the spec.
