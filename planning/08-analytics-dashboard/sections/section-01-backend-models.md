# Section 01: Backend Models & Interfaces

## Overview

This section defines all DTOs, records, configuration options, and service interfaces needed by the analytics dashboard backend. These are pure type definitions with no business logic -- they live in the Application layer and are consumed by all subsequent sections (Google Analytics service, Substack service, Dashboard aggregator, API endpoints).

**Dependencies:** None -- this is the foundation section.
**Blocks:** section-02 (Google Analytics service), section-03 (Substack service), section-04 (Dashboard aggregator).

---

## Existing Code Context

The project uses a clean architecture with four layers:

- **Domain** (`PersonalBrandAssistant.Domain`) -- entities, enums, value objects
- **Application** (`PersonalBrandAssistant.Application`) -- interfaces in `Common/Interfaces/`, models in `Common/Models/`, error codes in `Common/Errors/`
- **Infrastructure** (`PersonalBrandAssistant.Infrastructure`) -- service implementations
- **Api** (`PersonalBrandAssistant.Api`) -- Minimal API endpoints

Key existing types this section interacts with:

- `Result<T>` at `src/PersonalBrandAssistant.Application/Common/Models/Result.cs` -- the standard result wrapper using `Result<T>.Success(value)` and `Result<T>.Failure(ErrorCode, errors)`.
- `ErrorCode` enum at `src/PersonalBrandAssistant.Application/Common/Errors/ErrorCode.cs` -- includes `None`, `ValidationFailed`, `NotFound`, `Conflict`, `Unauthorized`, `InternalError`, `RateLimited`.
- `TopPerformingContent` at `src/PersonalBrandAssistant.Application/Common/Models/TopPerformingContent.cs` -- existing record that needs extending with `Impressions` and `EngagementRate` fields.
- `EngagementSnapshot` entity at `src/PersonalBrandAssistant.Domain/Entities/EngagementSnapshot.cs` -- has `Likes`, `Comments`, `Shares`, `Impressions`, `Clicks`, `FetchedAt`, `ContentPlatformStatusId`.
- `ContentPlatformStatus` entity at `src/PersonalBrandAssistant.Domain/Entities/ContentPlatformStatus.cs` -- has `ContentId`, `Platform`, `Status`, `PublishedAt`.
- `Content` entity at `src/PersonalBrandAssistant.Domain/Entities/Content.cs` -- has `Status`, `PublishedAt`, `Title`.
- `AgentExecution` entity at `src/PersonalBrandAssistant.Domain/Entities/AgentExecution.cs` -- has `Cost` field for LLM cost tracking.
- `PlatformType` enum at `src/PersonalBrandAssistant.Domain/Enums/PlatformType.cs` -- `TwitterX, LinkedIn, Instagram, YouTube, Reddit, PersonalBlog, Substack`.

---

## Tests First

Since this section is exclusively type definitions (records, interfaces, options classes), the tests are lightweight compilation/shape verification tests. Place them in the existing Infrastructure test project.

### Test File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Models/AnalyticsDashboardModelTests.cs`

Test cases:

- **Test: DashboardSummary record can be instantiated with all properties** -- Verify record construction and that all properties are accessible. Confirms the record compiles and has the expected shape.
- **Test: DailyEngagement record holds per-platform breakdown** -- Construct with a list of `PlatformDailyMetrics` and verify `Total` sums correctly at the call site (total is a precomputed value, not auto-calculated).
- **Test: PlatformSummary marks LinkedIn as unavailable** -- Create a `PlatformSummary` with `IsAvailable = false` and null follower count.
- **Test: WebsiteOverview record has all GA4 metric properties** -- Instantiate and verify property access.
- **Test: SearchQueryEntry holds CTR and Position as doubles** -- Verify decimal precision is maintained.
- **Test: SubstackPost record has nullable Summary** -- Create with null summary, verify no exception.
- **Test: GoogleAnalyticsOptions has correct defaults** -- Instantiate and check `SectionName`, default `CredentialsPath`, `PropertyId`, and `SiteUrl`.
- **Test: SubstackOptions has correct defaults** -- Instantiate and check `SectionName` and default `FeedUrl`.
- **Test: TopPerformingContent includes Impressions and EngagementRate** -- Verify the extended record compiles with the two new nullable fields.

Each test is a simple instantiation + assertion. Stubs only:

```csharp
namespace PersonalBrandAssistant.Infrastructure.Tests.Models;

public class AnalyticsDashboardModelTests
{
    [Fact]
    public void DashboardSummary_CanBeInstantiated_WithAllProperties()
    {
        // Arrange & Act: construct DashboardSummary with sample values
        // Assert: all properties return expected values
    }

    [Fact]
    public void DailyEngagement_HoldsPerPlatformBreakdown()
    {
        // Arrange: create PlatformDailyMetrics list
        // Act: construct DailyEngagement
        // Assert: Platforms list and Total accessible
    }

    [Fact]
    public void PlatformSummary_SupportsUnavailablePlatform()
    {
        // Arrange & Act: create with IsAvailable = false, FollowerCount = null
        // Assert: properties reflect unavailable state
    }

    [Fact]
    public void GoogleAnalyticsOptions_HasCorrectDefaults()
    {
        // Assert: SectionName == "GoogleAnalytics", CredentialsPath, PropertyId, SiteUrl
    }

    [Fact]
    public void SubstackOptions_HasCorrectDefaults()
    {
        // Assert: SectionName == "Substack", FeedUrl contains substack.com
    }

    [Fact]
    public void TopPerformingContent_IncludesImpressionsAndEngagementRate()
    {
        // Arrange & Act: construct with new nullable fields
        // Assert: Impressions and EngagementRate accessible
    }
}
```

### Test File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Models/AnalyticsDashboardInterfaceTests.cs`

These are compile-time verification tests that confirm interfaces have the expected method signatures. Use a mock-based approach:

- **Test: IGoogleAnalyticsService can be mocked with all four methods** -- Create a `Mock<IGoogleAnalyticsService>` and set up all four methods. Verifies the interface shape.
- **Test: ISubstackService can be mocked with GetRecentPostsAsync** -- Create mock, set up method with limit parameter.
- **Test: IDashboardAggregator can be mocked with all three methods** -- Create mock, set up `GetSummaryAsync`, `GetTimelineAsync`, `GetPlatformSummariesAsync`.

```csharp
namespace PersonalBrandAssistant.Infrastructure.Tests.Models;

public class AnalyticsDashboardInterfaceTests
{
    [Fact]
    public void IGoogleAnalyticsService_HasExpectedMethodSignatures()
    {
        // Arrange: Mock<IGoogleAnalyticsService>
        // Act: Setup all 4 methods
        // Assert: no exceptions (interface shape is correct)
    }

    [Fact]
    public void ISubstackService_HasExpectedMethodSignature()
    {
        // Arrange: Mock<ISubstackService>
        // Act: Setup GetRecentPostsAsync
        // Assert: returns Result<IReadOnlyList<SubstackPost>>
    }

    [Fact]
    public void IDashboardAggregator_HasExpectedMethodSignatures()
    {
        // Arrange: Mock<IDashboardAggregator>
        // Act: Setup all 3 methods
        // Assert: correct return types
    }
}
```

---

## Implementation Details

### File 1: `src/PersonalBrandAssistant.Application/Common/Models/GoogleAnalyticsModels.cs`

Four records for GA4 and Search Console response mapping:

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

/// <summary>GA4 overview metrics for a date range.</summary>
public record WebsiteOverview(
    int ActiveUsers, int Sessions, int PageViews,
    double AvgSessionDuration, double BounceRate, int NewUsers);

/// <summary>GA4 page-level metrics.</summary>
public record PageViewEntry(string PagePath, int Views, int Users);

/// <summary>GA4 traffic source breakdown.</summary>
public record TrafficSourceEntry(string Channel, int Sessions, int Users);

/// <summary>Search Console query performance.</summary>
public record SearchQueryEntry(
    string Query, int Clicks, int Impressions, double Ctr, double Position);
```

### File 2: `src/PersonalBrandAssistant.Application/Common/Models/DashboardModels.cs`

Three records for the dashboard aggregator output:

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

/// <summary>KPI summary with current vs previous period comparison.</summary>
public record DashboardSummary(
    int TotalEngagement, int PreviousEngagement,
    int TotalImpressions, int PreviousImpressions,
    decimal EngagementRate, decimal PreviousEngagementRate,
    int ContentPublished, int PreviousContentPublished,
    decimal CostPerEngagement, decimal PreviousCostPerEngagement,
    int WebsiteUsers, int PreviousWebsiteUsers,
    DateTimeOffset GeneratedAt);

/// <summary>Daily engagement totals broken down by platform.</summary>
public record DailyEngagement(
    DateOnly Date,
    IReadOnlyList<PlatformDailyMetrics> Platforms,
    int Total);

/// <summary>Per-platform daily breakdown of likes/comments/shares.</summary>
public record PlatformDailyMetrics(
    string Platform, int Likes, int Comments, int Shares, int Total);
```

### File 3: `src/PersonalBrandAssistant.Application/Common/Models/PlatformSummaryModel.cs`

Separate file for the platform summary record (to keep models focused and small):

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

/// <summary>Per-platform health summary for the dashboard.</summary>
public record PlatformSummary(
    string Platform, int? FollowerCount, int PostCount,
    double AvgEngagement, string? TopPostTitle, string? TopPostUrl,
    bool IsAvailable);
```

### File 4: `src/PersonalBrandAssistant.Application/Common/Models/SubstackModels.cs`

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

/// <summary>A Substack post parsed from RSS feed.</summary>
public record SubstackPost(
    string Title, string Url, DateTimeOffset PublishedAt, string? Summary);
```

### File 5: `src/PersonalBrandAssistant.Application/Common/Models/WebsiteAnalyticsResponse.cs`

Composite response that the `/api/analytics/website` endpoint returns:

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

/// <summary>Combined GA4 + Search Console response.</summary>
public record WebsiteAnalyticsResponse(
    WebsiteOverview Overview,
    IReadOnlyList<PageViewEntry> TopPages,
    IReadOnlyList<TrafficSourceEntry> TrafficSources,
    IReadOnlyList<SearchQueryEntry> SearchQueries);
```

### File 6: `src/PersonalBrandAssistant.Application/Common/Models/GoogleAnalyticsOptions.cs`

Configuration options bound from `appsettings.json` section `"GoogleAnalytics"`:

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

public class GoogleAnalyticsOptions
{
    public const string SectionName = "GoogleAnalytics";

    /// <summary>Path to the Google service account JSON key file.</summary>
    public string CredentialsPath { get; set; } = "secrets/google-analytics-sa.json";

    /// <summary>GA4 property ID (numeric).</summary>
    public string PropertyId { get; set; } = "261358185";

    /// <summary>Site URL for Search Console queries (include trailing slash).</summary>
    public string SiteUrl { get; set; } = "https://matthewkruczek.ai/";
}
```

### File 7: `src/PersonalBrandAssistant.Application/Common/Models/SubstackOptions.cs`

Configuration options bound from `appsettings.json` section `"Substack"`:

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

public class SubstackOptions
{
    public const string SectionName = "Substack";

    /// <summary>RSS feed URL for Substack newsletter.</summary>
    public string FeedUrl { get; set; } = "https://matthewkruczek.substack.com/feed";
}
```

### File 8: `src/PersonalBrandAssistant.Application/Common/Interfaces/IGoogleAnalyticsService.cs`

```csharp
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

/// <summary>Abstracts GA4 Data API and Search Console API access.</summary>
public interface IGoogleAnalyticsService
{
    Task<Result<WebsiteOverview>> GetOverviewAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    Task<Result<IReadOnlyList<PageViewEntry>>> GetTopPagesAsync(
        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);

    Task<Result<IReadOnlyList<TrafficSourceEntry>>> GetTrafficSourcesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    Task<Result<IReadOnlyList<SearchQueryEntry>>> GetTopQueriesAsync(
        DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);
}
```

### File 9: `src/PersonalBrandAssistant.Application/Common/Interfaces/ISubstackService.cs`

```csharp
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

/// <summary>Parses Substack RSS feed into structured post data.</summary>
public interface ISubstackService
{
    Task<Result<IReadOnlyList<SubstackPost>>> GetRecentPostsAsync(
        int limit, CancellationToken ct);
}
```

### File 10: `src/PersonalBrandAssistant.Application/Common/Interfaces/IDashboardAggregator.cs`

```csharp
using PersonalBrandAssistant.Application.Common.Models;

namespace PersonalBrandAssistant.Application.Common.Interfaces;

/// <summary>Orchestrates all data sources into unified dashboard responses.</summary>
public interface IDashboardAggregator
{
    Task<Result<DashboardSummary>> GetSummaryAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    Task<Result<IReadOnlyList<DailyEngagement>>> GetTimelineAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    Task<Result<IReadOnlyList<PlatformSummary>>> GetPlatformSummariesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
```

### File 11 (Modification): `src/PersonalBrandAssistant.Application/Common/Models/TopPerformingContent.cs`

Extend the existing `TopPerformingContent` record to include `Impressions` and `EngagementRate` fields needed by the dashboard's top content table. The existing record:

```csharp
public record TopPerformingContent(
    Guid ContentId,
    string Title,
    int TotalEngagement,
    IReadOnlyDictionary<PlatformType, int> EngagementByPlatform);
```

Add two nullable fields to avoid breaking existing callers:

```csharp
public record TopPerformingContent(
    Guid ContentId,
    string Title,
    int TotalEngagement,
    IReadOnlyDictionary<PlatformType, int> EngagementByPlatform,
    int? Impressions = null,
    decimal? EngagementRate = null);
```

The `Impressions` field sums impressions from all `EngagementSnapshot` records for the content. The `EngagementRate` is calculated as `TotalEngagement / Impressions * 100` (null when impressions is 0 or unavailable). The default values (`null`) preserve backward compatibility.

---

## Cross-Cutting Design Decisions

These decisions affect how all the models are consumed downstream:

1. **Nullable vs zero:** Missing metrics use `null`, not `0`. For example, `PlatformSummary.FollowerCount` is `int?` because Reddit does not report followers. The frontend renders "N/A" for null values.

2. **`decimal` for rates, `double` for GA4 raw values:** Engagement rate and cost use `decimal` for precision. GA4 metrics like `AvgSessionDuration`, `BounceRate`, `Ctr`, and `Position` use `double` because they originate as floating-point from Google's API.

3. **`DateOnly` vs `DateTimeOffset`:** Timeline data uses `DateOnly` for the date dimension (no time component needed for daily grouping). All range boundaries use `DateTimeOffset` in UTC.

4. **Immutable records everywhere:** All models are C# records with init-only properties. No mutable state.

5. **`IReadOnlyList<T>` for collections:** All collection-typed properties use `IReadOnlyList<T>` (not `List<T>` or `IEnumerable<T>`) for immutability and materialization guarantees.

---

## File Summary

| File | Action | Description |
|------|--------|-------------|
| `src/PersonalBrandAssistant.Application/Common/Models/GoogleAnalyticsModels.cs` | Create | WebsiteOverview, PageViewEntry, TrafficSourceEntry, SearchQueryEntry |
| `src/PersonalBrandAssistant.Application/Common/Models/DashboardModels.cs` | Create | DashboardSummary, DailyEngagement, PlatformDailyMetrics |
| `src/PersonalBrandAssistant.Application/Common/Models/PlatformSummaryModel.cs` | Create | PlatformSummary |
| `src/PersonalBrandAssistant.Application/Common/Models/SubstackModels.cs` | Create | SubstackPost |
| `src/PersonalBrandAssistant.Application/Common/Models/WebsiteAnalyticsResponse.cs` | Create | WebsiteAnalyticsResponse composite |
| `src/PersonalBrandAssistant.Application/Common/Models/GoogleAnalyticsOptions.cs` | Create | Configuration options class |
| `src/PersonalBrandAssistant.Application/Common/Models/SubstackOptions.cs` | Create | Configuration options class |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IGoogleAnalyticsService.cs` | Create | GA4 + Search Console interface |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/ISubstackService.cs` | Create | Substack RSS interface |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IDashboardAggregator.cs` | Create | Dashboard orchestrator interface |
| `src/PersonalBrandAssistant.Application/Common/Models/TopPerformingContent.cs` | Modify | Add Impressions + EngagementRate fields |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Models/AnalyticsDashboardModelTests.cs` | Create | Record shape verification tests |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Models/AnalyticsDashboardInterfaceTests.cs` | Create | Interface signature verification tests |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentPipelineTests.cs` | Modify | Fix pre-existing missing IPipelineEventBroadcaster mock |

---

## Implementation Notes

- **Code review deviation:** `PlatformDailyMetrics.Platform` and `PlatformSummary.Platform` changed from `string` to `PlatformType` enum per code review for type safety and codebase consistency.
- **Pre-existing fix:** `ContentPipelineTests.cs` was missing `IPipelineEventBroadcaster` mock -- fixed to unblock test builds.
- **Test count:** 13 tests (10 model shape tests + 3 interface signature tests). All passing.