# Section 12: Integration Testing

## Overview

This section covers end-to-end integration tests for the analytics dashboard feature. It validates backend API endpoints using `WebApplicationFactory` with mocked external services (GA4, Search Console, Substack RSS), Angular component tests for dashboard rendering, and verifies cross-cutting concerns like partial failure handling and cache behavior.

This is the final section in the analytics dashboard implementation. It depends on all prior sections (1-11) being complete.

## Dependencies

- **Section 01** (Backend Models): All DTOs, interfaces, and configuration classes must exist
- **Section 02** (Google Analytics Service): `IGoogleAnalyticsService` implementation
- **Section 03** (Substack Service): `ISubstackService` implementation
- **Section 04** (Dashboard Aggregator): `IDashboardAggregator` implementation
- **Section 05** (Caching/Resilience): `HybridCache` integration and Polly policies
- **Section 06** (API Endpoints): All 5 new routes in `AnalyticsEndpoints.cs` plus health check
- **Section 07** (Frontend Models/Service): TypeScript interfaces and `AnalyticsService` methods
- **Section 08** (Analytics Store): Rewritten `AnalyticsStore` with dashboard state
- **Section 09** (Dashboard Page/KPIs): `AnalyticsDashboardComponent`, `DashboardKpiCardsComponent`, `DateRangeSelectorComponent`
- **Section 10** (Charts): `EngagementTimelineChartComponent`, `PlatformBreakdownChartComponent`, `TopContentTableComponent`
- **Section 11** (Platform/Website/Substack): `PlatformHealthCardsComponent`, `WebsiteAnalyticsSectionComponent`, `SubstackSectionComponent`

## Architecture Context

The analytics dashboard aggregates data from multiple sources:

| Source | Transport | Auth |
|--------|-----------|------|
| Social platforms | Database (EngagementSnapshots, ContentPlatformStatuses) | N/A |
| Google Analytics (GA4) | gRPC via `BetaAnalyticsDataClient` | Service account JSON |
| Google Search Console | REST via `SearchConsoleService` | Service account JSON |
| Substack | HTTP RSS feed | None |

All API endpoints live under `/api/analytics/` and are protected by `ApiKeyMiddleware`. The dashboard aggregator orchestrates all sources, returning partial results when individual sources fail.

**API Routes to test:**

| Route | Returns |
|-------|---------|
| `GET /api/analytics/dashboard?period=7d` | `DashboardSummary` |
| `GET /api/analytics/engagement-timeline?period=7d` | `DailyEngagement[]` |
| `GET /api/analytics/platform-summary?period=7d` | `PlatformSummary[]` |
| `GET /api/analytics/website?period=7d` | `WebsiteAnalyticsResponse` |
| `GET /api/analytics/substack` | `SubstackPost[]` |
| `GET /api/analytics/health` | Connectivity status |

---

## Tests First

### Backend Integration Tests

#### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AnalyticsEndpointTests.cs`

This test class uses `WebApplicationFactory<Program>` via the existing `CustomWebApplicationFactory` pattern with Testcontainers PostgreSQL. External services (`IGoogleAnalyticsService`, `ISubstackService`) are replaced with mocks in `ConfigureTestServices` so no real GA4/Substack calls are made.

```csharp
/// <summary>
/// Integration tests for the analytics dashboard API endpoints.
/// Uses WebApplicationFactory with mocked external services (GA4, Search Console, Substack).
/// Database-backed data (social engagement) uses real PostgreSQL via Testcontainers.
/// </summary>
public class AnalyticsEndpointTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    // Setup: create unique DB, CustomWebApplicationFactory, seed engagement data,
    // replace IGoogleAnalyticsService and ISubstackService with Moq stubs in ConfigureTestServices.
    // Use _factory.CreateAuthenticatedClient() for all requests (X-Api-Key header).

    // --- Dashboard Summary ---

    [Fact] 
    public async Task GetDashboard_Returns200_WithDashboardSummaryShape() { }
    /// Verify response deserializes to DashboardSummary with all expected properties non-default.

    [Fact]
    public async Task GetDashboard_WithPeriod7d_UsesCorrectDateRange() { }
    /// Seed engagement data for specific dates. Request ?period=7d. Verify TotalEngagement
    /// only includes data from the last 7 days, not older records.

    [Fact]
    public async Task GetDashboard_InvalidPeriod_Returns400() { }
    /// Request ?period=abc. Expect 400 Bad Request.

    [Fact]
    public async Task GetDashboard_PeriodTakesPrecedenceOverFromTo() { }
    /// Pass both ?period=7d&from=...&to=... Verify period wins (response matches 7d window).

    // --- Engagement Timeline ---

    [Fact]
    public async Task GetEngagementTimeline_Returns200_WithDailyEngagementArray() { }
    /// Verify response is a JSON array of DailyEngagement objects with date, platforms, total.

    // --- Platform Summary ---

    [Fact]
    public async Task GetPlatformSummary_Returns200_WithPlatformSummaryArray() { }
    /// Verify each platform has postCount, avgEngagement, isAvailable fields.

    // --- Website Analytics ---

    [Fact]
    public async Task GetWebsite_Returns200_WithWebsiteAnalyticsResponse() { }
    /// Verify response contains overview, topPages, trafficSources, searchQueries sections.
    /// Uses mocked IGoogleAnalyticsService returning canned data.

    // --- Substack ---

    [Fact]
    public async Task GetSubstack_Returns200_WithSubstackPostArray() { }
    /// Uses mocked ISubstackService returning canned SubstackPost list.

    // --- Health Check ---

    [Fact]
    public async Task GetHealth_Returns200_WithConnectivityStatus() { }
    /// Verify response includes status for ga4, searchConsole, substack keys.

    // --- Partial Failure ---

    [Fact]
    public async Task GetDashboard_WhenGA4Fails_StillReturnsSocialData() { }
    /// Configure mock IGoogleAnalyticsService to return Result.Failure.
    /// Verify response still returns 200 with social engagement data populated,
    /// websiteUsers = 0 or null, and an error indicator.

    [Fact]
    public async Task GetWebsite_WhenGA4Fails_Returns200_WithErrorSection() { }
    /// Mock GA4 failure. Verify 200 response with error string in the response body
    /// rather than a 500.

    // --- Cache Behavior ---

    [Fact]
    public async Task GetDashboard_SecondCall_ReturnsCachedResult() { }
    /// Call GET /api/analytics/dashboard twice with same period.
    /// Verify the mock aggregator/service was only invoked once (cached second time).

    [Fact]
    public async Task GetDashboard_WithRefreshTrue_BypassesCache() { }
    /// Call GET /api/analytics/dashboard, then GET /api/analytics/dashboard?refresh=true.
    /// Verify the mock was invoked twice.

    // --- Auth ---

    [Fact]
    public async Task AllAnalyticsEndpoints_WithoutApiKey_Return401() { }
    /// Use _factory.CreateClient() (no auth header). Hit each of the 6 endpoints.
    /// Verify all return 401.
}
```

**Test setup pattern:** Follow the existing `ContentEndpointsTests` pattern:
- `IClassFixture<PostgresFixture>` for shared Testcontainers PostgreSQL container
- `IAsyncLifetime` for per-test database creation/teardown
- `CustomWebApplicationFactory` with a unique connection string per test class
- Override external services in `ConfigureTestServices` using Moq:

```csharp
builder.ConfigureTestServices(services =>
{
    // Replace external services with mocks
    services.AddSingleton(mockGoogleAnalyticsService.Object);
    services.AddSingleton(mockSubstackService.Object);
    // Optionally wrap IDashboardAggregator with a spy for cache verification
});
```

**Seeding test data:** Use `ApplicationDbContext` to insert `Content`, `ContentPlatformStatus`, and `EngagementSnapshot` records for known date ranges so engagement totals are predictable and verifiable.

---

#### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AnalyticsServices/DashboardAggregatorIntegrationTests.cs`

These tests exercise `DashboardAggregator` with a real database (Testcontainers) but mocked external services.

```csharp
/// <summary>
/// Integration tests for DashboardAggregator with real DB queries but mocked GA4/Substack.
/// Verifies SQL query correctness, date range handling, and partial failure composition.
/// </summary>
[Collection("Postgres")]
public class DashboardAggregatorIntegrationTests : IAsyncLifetime
{
    // Seed known engagement data, mock GA4/Substack, instantiate real DashboardAggregator.

    [Fact]
    public async Task GetSummaryAsync_AggregatesEngagementFromMultiplePlatforms() { }
    /// Seed snapshots for Twitter and YouTube. Verify TotalEngagement = sum of all likes+comments+shares.

    [Fact]
    public async Task GetSummaryAsync_PreviousPeriodComparison_CorrectDates() { }
    /// Seed data for current 7d and previous 7d windows. Verify PreviousEngagement matches
    /// only the earlier window.

    [Fact]
    public async Task GetTimelineAsync_FillsMissingDatesWithZeros() { }
    /// Seed data for day 1 and day 3 only. Request 3-day range. Verify day 2 has zero values.

    [Fact]
    public async Task GetPlatformSummariesAsync_MarksLinkedInAsUnavailable() { }
    /// Verify LinkedIn entry has isAvailable=false.

    [Fact]
    public async Task GetSummaryAsync_WhenGA4Fails_ReturnsSocialDataWithNullWebsiteUsers() { }
    /// Mock GA4 to return failure. Verify social engagement is still calculated correctly.
}
```

---

### Frontend Component Tests

#### File: `src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.spec.ts`

```typescript
/**
 * Integration-level component test for the main analytics dashboard.
 * Renders the full dashboard with stubbed store, verifies all child components mount.
 */
describe('AnalyticsDashboardComponent (integration)', () => {
  // Setup: provide AnalyticsStore as a mock/stub with known state values.
  // Import all child components (KPI cards, charts, platform cards, etc.).
  // Use ComponentFixture for rendering.

  it('should render KPI cards section when summary data is available');
  it('should render engagement timeline chart when timeline data is available');
  it('should render platform health cards for each platform');
  it('should render website analytics section when websiteData is present');
  it('should render substack section when substackPosts are present');
  it('should show loading skeletons when loading is true');
  it('should show staleness indicator when lastRefreshedAt is old');
  it('should call store.loadDashboard on init');
  it('should call store.setPeriod when date range selector emits periodChanged');
  it('should call store.refreshDashboard when refresh button is clicked');
});
```

#### File: `src/PersonalBrandAssistant.Web/src/app/features/analytics/store/analytics.store.spec.ts`

```typescript
/**
 * Store tests for the rewritten AnalyticsStore.
 * Verifies parallel loading, state management, period changes, and partial failure handling.
 */
describe('AnalyticsStore', () => {
  // Setup: provide AnalyticsService as a Jasmine spy object.
  // Use TestBed to inject the store.

  it('should start with loading=false and null summary');
  it('loadDashboard dispatches parallel requests and populates all state slices');
  // Spy on all 5+ service methods. Call loadDashboard. Verify all spies called.
  // Flush responses. Verify state has summary, timeline, platformSummaries, etc.

  it('loadDashboard sets loading=true during fetch, false after');
  it('setPeriod updates period state and triggers reload');
  it('refreshDashboard passes refresh=true query parameter');
  // Verify service methods called with { refresh: true } or ?refresh=true param.

  it('lastRefreshedAt is updated after successful load');
  it('partial API failure still populates available sections');
  // Mock one service to throw. Verify other sections still populate.
});
```

#### File: `src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.spec.ts`

```typescript
/**
 * Service tests verifying correct URL construction and parameter passing.
 * Uses HttpTestingController.
 */
describe('AnalyticsService', () => {
  // Setup: provideHttpClient, provideHttpClientTesting, inject AnalyticsService + HttpTestingController.
  // afterEach: httpMock.verify()

  it('getDashboardSummary calls correct URL with period parameter');
  // Call getDashboardSummary('7d'). Expect GET to /api/analytics/dashboard?period=7d.

  it('getEngagementTimeline passes days parameter');
  it('getPlatformSummaries calls platform-summary endpoint');
  it('getWebsiteAnalytics calls website endpoint');
  it('getSubstackPosts calls substack endpoint');
  it('getDashboardSummary with refresh=true appends refresh param');
});
```

#### File: `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/dashboard-kpi-cards.component.spec.ts`

```typescript
describe('DashboardKpiCardsComponent', () => {
  it('renders all 6 KPI cards with correct values');
  // Provide a DashboardSummary input. Query DOM for 6 card elements. Verify text content.

  it('shows up trend indicator when % change is positive');
  it('shows down trend indicator when % change is negative');
  it('shows "N/A" when change is null');
  // Set previousEngagement = 0. Verify "N/A" text instead of infinity/percentage.
});
```

#### File: `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/engagement-timeline-chart.component.spec.ts`

```typescript
describe('EngagementTimelineChartComponent', () => {
  it('renders UIChart with correct number of datasets');
  // Provide timeline data with 3 platforms. Verify chart data has 3+1 (total) datasets.

  it('handles empty timeline data without errors');
});
```

#### File: `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/platform-health-cards.component.spec.ts`

```typescript
describe('PlatformHealthCardsComponent', () => {
  it('renders one card per platform');
  it('shows "Coming Soon" badge for LinkedIn (isAvailable=false)');
  it('displays follower count when available');
  it('shows N/A for follower count when null');
});
```

#### File: `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/website-analytics-section.component.spec.ts`

```typescript
describe('WebsiteAnalyticsSectionComponent', () => {
  it('renders GA4 overview metrics (users, sessions, page views)');
  it('renders top pages table');
  it('renders search queries table with CTR and position');
});
```

#### File: `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/substack-section.component.spec.ts`

```typescript
describe('SubstackSectionComponent', () => {
  it('renders a post entry for each SubstackPost');
  it('formats publishedAt as a human-readable date');
  it('shows empty state when no posts');
});
```

#### File: `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/date-range-selector.component.spec.ts`

```typescript
describe('DateRangeSelectorComponent', () => {
  it('emits periodChanged with "7d" when 7D button clicked');
  it('highlights the active preset button');
  it('emits custom date range from calendar selection');
});
```

#### File: `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/top-content-table.component.spec.ts`

```typescript
describe('TopContentTableComponent', () => {
  it('renders rows for each content item');
  it('shows engagement rate with correct color coding (green > 5%, yellow 2-5%, red < 2%)');
});
```

---

## Implementation Details

### 1. Extend `CustomWebApplicationFactory` for Analytics Tests

**File to modify:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/CustomWebApplicationFactory.cs`

The existing factory already removes background services and configures test infrastructure. For analytics integration tests, the caller creates mock instances of `IGoogleAnalyticsService` and `ISubstackService` and passes them to a custom setup, or uses a derived factory class.

Recommended approach: create a helper method or a derived class that additionally registers mock analytics services.

```csharp
/// <summary>
/// Extends CustomWebApplicationFactory to inject mock GA4 and Substack services.
/// Used by AnalyticsEndpointTests.
/// </summary>
public class AnalyticsWebApplicationFactory : CustomWebApplicationFactory
{
    private readonly IGoogleAnalyticsService _mockGa4;
    private readonly ISubstackService _mockSubstack;

    public AnalyticsWebApplicationFactory(
        string connectionString,
        IGoogleAnalyticsService mockGa4,
        ISubstackService mockSubstack) : base(connectionString)
    {
        _mockGa4 = mockGa4;
        _mockSubstack = mockSubstack;
    }

    // Override ConfigureWebHost to also register mock services via ConfigureTestServices.
    // Call base first, then add: services.AddSingleton(_mockGa4); services.AddSingleton(_mockSubstack);
    // Also set GoogleAnalytics:CredentialsPath to a dummy value so startup doesn't fail.
}
```

Alternatively, keep the existing factory and register mocks directly in each test's `InitializeAsync` using the `WebApplicationFactory.WithWebHostBuilder` pattern:

```csharp
_factory = new CustomWebApplicationFactory(_connectionString);
_factory = _factory.WithWebHostBuilder(builder =>
{
    builder.ConfigureTestServices(services =>
    {
        services.AddSingleton<IGoogleAnalyticsService>(mockGa4.Object);
        services.AddSingleton<ISubstackService>(mockSubstack.Object);
    });
});
```

Choose whichever approach fits cleanly with the codebase -- the `WithWebHostBuilder` approach avoids creating a new class and is more flexible for per-test mock configuration.

### 2. Mock Configuration for GA4 and Substack

For backend integration tests, configure mocks to return realistic canned data:

**GA4 mock setup:**
- `GetOverviewAsync` returns `Result.Success(new WebsiteOverview(120, 95, 340, 2.5, 45.0, 60))`
- `GetTopPagesAsync` returns a list of 5 `PageViewEntry` records
- `GetTrafficSourcesAsync` returns 3 `TrafficSourceEntry` records (Organic, Direct, Social)
- `GetTopQueriesAsync` returns 5 `SearchQueryEntry` records

**Substack mock setup:**
- `GetRecentPostsAsync` returns 3 `SubstackPost` records with known titles and dates

**Failure scenario mocks:**
- For partial failure tests, configure `GetOverviewAsync` to return `Result.Failure("GA4 unavailable")` while keeping Substack mock healthy (and vice versa)

### 3. Test Data Seeding for Social Engagement

Seed the PostgreSQL test database with:
- 2-3 `Content` records with `Status = Published` and known `PublishedAt` dates
- `ContentPlatformStatus` records linking content to Twitter and YouTube
- `EngagementSnapshot` records with known likes/comments/shares values at specific dates
- This gives predictable totals for TotalEngagement, timeline grouping, and platform summaries

Example seed data for a 7-day test window:

| Content | Platform | Date | Likes | Comments | Shares |
|---------|----------|------|-------|----------|--------|
| Post A  | Twitter  | Day 1 | 10  | 3        | 2      |
| Post A  | Twitter  | Day 3 | 15  | 5        | 4      |
| Post B  | YouTube  | Day 2 | 20  | 8        | 1      |

Expected TotalEngagement for 7d = (10+3+2) + (15+5+4) + (20+8+1) = 68

### 4. Cache Verification Strategy

To verify caching behavior in integration tests:
1. Wrap the mock `IDashboardAggregator` (or the underlying services) in a call-counting decorator or use Moq's `Times.Once()` / `Times.Exactly(2)` verification
2. First call to `GET /api/analytics/dashboard` should invoke the aggregator
3. Second identical call should return cached result (aggregator not invoked again)
4. Call with `?refresh=true` should invoke the aggregator again

If verifying at the service mock level is simpler (since HybridCache wraps the aggregator's internal calls), mock `IGoogleAnalyticsService` directly and count invocations.

### 5. Angular Test Setup Patterns

Follow the existing project conventions seen in `api.service.spec.ts`:
- Use `TestBed.configureTestingModule` with `provideHttpClient()` and `provideHttpClientTesting()`
- For component tests, provide the store as a mock using `jasmine.createSpyObj` or use `signalStore` testing utilities
- Use `ComponentFixture.debugElement.queryAll(By.css(...))` for DOM assertions
- Always call `httpMock.verify()` in `afterEach` for service tests

For store tests with `signalStore`, instantiate the store via `TestBed.inject(AnalyticsStore)` with a mocked `AnalyticsService` provided. Use `fakeAsync`/`tick` to control async timing.

### 6. Test Commands

**Backend tests:**
```bash
cd /c/Users/kruz7/OneDrive/Documents/Code\ Repos/MCKRUZ/personal-brand-assistant
dotnet test tests/PersonalBrandAssistant.Infrastructure.Tests --filter "FullyQualifiedName~AnalyticsEndpointTests"
dotnet test tests/PersonalBrandAssistant.Infrastructure.Tests --filter "FullyQualifiedName~DashboardAggregatorIntegrationTests"
```

**Frontend tests:**
```bash
cd /c/Users/kruz7/OneDrive/Documents/Code\ Repos/MCKRUZ/personal-brand-assistant/src/PersonalBrandAssistant.Web
npx ng test --watch=false --browsers=ChromeHeadless
```

### 7. File Summary

**New files to create:**

| File | Purpose |
|------|---------|
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/AnalyticsEndpointTests.cs` | WebApplicationFactory integration tests for all 6 analytics endpoints |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AnalyticsServices/DashboardAggregatorIntegrationTests.cs` | DB-backed aggregator tests with mocked external services |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics-dashboard.component.spec.ts` | Main dashboard component rendering tests |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/store/analytics.store.spec.ts` | Store parallel loading, state management, partial failure tests |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.spec.ts` | HTTP endpoint URL/param verification tests |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/dashboard-kpi-cards.component.spec.ts` | KPI card rendering and trend indicator tests |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/engagement-timeline-chart.component.spec.ts` | Chart dataset verification |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/platform-health-cards.component.spec.ts` | Platform card rendering, LinkedIn "Coming Soon" |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/website-analytics-section.component.spec.ts` | GA4/GSC data rendering |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/substack-section.component.spec.ts` | RSS post listing rendering |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/date-range-selector.component.spec.ts` | Period selection event emission |
| `src/PersonalBrandAssistant.Web/src/app/features/analytics/components/top-content-table.component.spec.ts` | Table rendering with color-coded engagement rates |

**Files to modify:**

| File | Change |
|------|--------|
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/CustomWebApplicationFactory.cs` | Optionally add analytics mock registration support, or use `WithWebHostBuilder` in tests |

### 8. Acceptance Criteria

- All 6 backend analytics endpoints return 200 with correct response shapes in integration tests
- Invalid period parameter returns 400
- Partial failure (GA4 down) still returns 200 with available social data
- Cache hit verified: second identical request does not re-invoke data source mocks
- `?refresh=true` verified: cache is bypassed
- All endpoints return 401 without API key
- Angular dashboard component mounts and renders all child sections when data is available
- Store correctly manages loading states during parallel fetches
- KPI cards display trend indicators (up/down/N/A) correctly
- LinkedIn platform card shows "Coming Soon" state
- All Angular specs pass in headless Chrome

---

## Implementation Notes

- **Scope:** Most test files from the plan already existed from prior sections (01-11). This section created the one missing piece: `DashboardAggregatorIntegrationTests.cs` with 9 Testcontainers-backed integration tests.
- **Aggregator behavior clarified:** GetEngagementForPeriodAsync takes the **latest snapshot per ContentPlatformStatusId**, not sum of all snapshots. Test data and assertions account for this.
- **Previous period seeding:** Date window must be calculated from the period boundaries (From-7 to From-1), not arbitrary past dates.
- **EF1002 suppression:** `ExecuteSqlRawAsync` with interpolated DB names triggers EF SQL injection warning. Suppressed with `#pragma` since DB names come from the test fixture (safe).
- **No CustomWebApplicationFactory modification needed:** Existing `AnalyticsEndpointTests` already covers endpoint-level integration with mocked aggregator. The new tests exercise the aggregator directly.
- **Test count:** 9 new backend integration tests. All Angular specs (60+) from prior sections continue passing.