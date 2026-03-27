<!-- PROJECT_CONFIG
runtime: dotnet-angular
test_command: dotnet test && cd src/PersonalBrandAssistant.Web && npx ng test --watch=false --browsers=ChromeHeadless
END_PROJECT_CONFIG -->

<!-- SECTION_MANIFEST
section-01-backend-models
section-02-google-analytics-service
section-03-substack-service
section-04-dashboard-aggregator
section-05-caching-resilience
section-06-api-endpoints
section-07-frontend-models-service
section-08-analytics-store
section-09-dashboard-page-kpis
section-10-charts-components
section-11-platform-website-substack
section-12-integration-testing
END_MANIFEST -->

# Implementation Sections Index

## Dependency Graph

| Section | Depends On | Blocks | Parallelizable |
|---------|------------|--------|----------------|
| section-01-backend-models | - | 02, 03, 04 | Yes |
| section-02-google-analytics-service | 01 | 04, 06 | Yes |
| section-03-substack-service | 01 | 04, 06 | Yes |
| section-04-dashboard-aggregator | 01, 02, 03 | 05, 06 | No |
| section-05-caching-resilience | 04 | 06 | No |
| section-06-api-endpoints | 04, 05 | 07 | No |
| section-07-frontend-models-service | 06 | 08 | Yes |
| section-08-analytics-store | 07 | 09 | No |
| section-09-dashboard-page-kpis | 08 | 10, 11 | Yes |
| section-10-charts-components | 09 | 12 | Yes |
| section-11-platform-website-substack | 09 | 12 | Yes |
| section-12-integration-testing | 10, 11 | - | No |

## Execution Order

1. section-01-backend-models (no dependencies)
2. section-02-google-analytics-service, section-03-substack-service (parallel after 01)
3. section-04-dashboard-aggregator (after 02 AND 03)
4. section-05-caching-resilience (after 04)
5. section-06-api-endpoints (after 05)
6. section-07-frontend-models-service (after 06)
7. section-08-analytics-store (after 07)
8. section-09-dashboard-page-kpis (after 08)
9. section-10-charts-components, section-11-platform-website-substack (parallel after 09)
10. section-12-integration-testing (final, after 10 AND 11)

## Section Summaries

### section-01-backend-models
All DTOs, interfaces, configuration options, and domain models for the analytics dashboard. GoogleAnalyticsOptions, SubstackOptions, DashboardSummary, DailyEngagement, PlatformDailyMetrics, PlatformSummary, WebsiteOverview, PageViewEntry, TrafficSourceEntry, SearchQueryEntry, SubstackPost, WebsiteAnalyticsResponse. Interfaces: IGoogleAnalyticsService, ISubstackService, IDashboardAggregator.

### section-02-google-analytics-service
GA4 Data API integration using Google.Analytics.Data.V1Beta NuGet package. Search Console integration using Google.Apis.SearchConsole.v1. Service account JWT authentication. Methods: GetOverviewAsync, GetTopPagesAsync, GetTrafficSourcesAsync, GetTopQueriesAsync. Unit tests with mocked Google clients.

### section-03-substack-service
RSS feed parser for matthewkruczek.substack.com. Uses System.ServiceModel.Syndication. Parses feed into SubstackPost records. HTML stripping for summaries. HttpClient with timeout. Unit tests with sample RSS XML.

### section-04-dashboard-aggregator
Orchestrates all data sources into dashboard responses. GetSummaryAsync (KPI aggregation with period comparison), GetTimelineAsync (daily engagement by platform), GetPlatformSummariesAsync (per-platform health). Handles partial failures (returns available data + error per section). Unit tests with Moq.

### section-05-caching-resilience
HybridCache integration with tag-based invalidation and per-source TTLs. Polly resilience policies (timeout, retry, circuit breaker) for GA4 gRPC, Search Console, and Substack HTTP clients. Refresh rate limiting. Database index migrations.

### section-06-api-endpoints
Five new routes in AnalyticsEndpoints.cs: dashboard, engagement-timeline, platform-summary, website, substack. Health check endpoint. Period/date range parsing. Extend existing /api/analytics/top with impressions and engagement rate. DI registration for all new services.

### section-07-frontend-models-service
TypeScript interfaces for all dashboard response types. Extend AnalyticsService with new endpoint methods. DashboardPeriod type and date range utilities.

### section-08-analytics-store
Rewrite AnalyticsStore with full dashboard state. loadDashboard using forkJoin for parallel requests. Period management. Refresh with cache bypass. Loading/error states per section. Computed signals for derived values.

### section-09-dashboard-page-kpis
Main AnalyticsDashboardComponent (rewrite). DashboardKpiCardsComponent with 6 metrics + trend indicators. DateRangeSelectorComponent with preset buttons (1D, 7D, 14D, 30D, 90D) + custom date picker. Staleness indicator display.

### section-10-charts-components
EngagementTimelineChartComponent (PrimeNG UIChart line chart, multi-platform). PlatformBreakdownChartComponent (horizontal stacked bar). Dark theme Chart.js defaults. TopContentTableComponent (PrimeNG Table with engagement rate color coding).

### section-11-platform-website-substack
PlatformHealthCardsComponent (per-platform cards with brand colors, follower counts, avg engagement). WebsiteAnalyticsSectionComponent (GA4 metrics cards, top pages table, traffic sources, search queries). SubstackSectionComponent (RSS post listing). LinkedIn "Coming Soon" state.

### section-12-integration-testing
End-to-end tests with mocked external APIs. WebApplicationFactory integration tests for all new endpoints. Angular component tests for dashboard rendering. Verify partial failure handling. Verify cache behavior.
