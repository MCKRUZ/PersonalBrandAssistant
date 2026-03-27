# Analytics Dashboard - TDD Plan

Testing framework: xUnit + Moq + MockQueryable.Moq (backend), Jasmine/Karma (frontend). Follows existing project conventions.

---

## 3.1 Google Analytics Service

### Unit Tests (`GoogleAnalyticsServiceTests.cs`)
- Test: GetOverviewAsync returns WebsiteOverview with correct metric mapping from GA4 response
- Test: GetOverviewAsync returns failure when GA4 client throws RpcException
- Test: GetTopPagesAsync returns pages sorted by views descending, respects limit
- Test: GetTrafficSourcesAsync groups sessions by channel
- Test: GetTopQueriesAsync returns SearchQueryEntry list from Search Console response
- Test: GetTopQueriesAsync returns failure when Search Console service throws GoogleApiException
- Test: Service handles empty GA4 response (no rows) gracefully, returns zero-filled overview

### Integration Tests
- Test: Service account credential loading succeeds with valid JSON file
- Test: Service account credential loading fails gracefully with missing file

---

## 3.2 Substack Service

### Unit Tests (`SubstackServiceTests.cs`)
- Test: GetRecentPostsAsync parses valid RSS feed XML into SubstackPost list
- Test: GetRecentPostsAsync respects limit parameter
- Test: GetRecentPostsAsync returns failure when HttpClient throws HttpRequestException
- Test: GetRecentPostsAsync handles malformed RSS XML without crashing (returns failure)
- Test: GetRecentPostsAsync strips HTML from summary field
- Test: Posts are ordered by publishedAt descending

---

## 3.3 Dashboard Aggregator

### Unit Tests (`DashboardAggregatorTests.cs`)

#### GetSummaryAsync
- Test: Returns correct TotalEngagement summing likes+comments+shares across all snapshots in range
- Test: Returns correct TotalImpressions including GA4 page views
- Test: Calculates EngagementRate correctly (engagement / impressions * 100)
- Test: Returns 0 engagement rate when impressions is 0
- Test: Calculates previous period comparison with correct date offset
- Test: Returns null for % change when previous period value is 0
- Test: Returns partial data when GA4 fails but social data succeeds
- Test: Includes WebsiteUsers from GA4 overview
- Test: Calculates CostPerEngagement from AgentExecution records

#### GetTimelineAsync
- Test: Groups daily engagement by platform with correct likes/comments/shares breakdown
- Test: Fills missing dates with zero values (no gaps in timeline)
- Test: Handles 1-day range (single data point)
- Test: Handles 90-day range without performance issues (verify query shape)

#### GetPlatformSummariesAsync
- Test: Returns summary per active platform with correct post count
- Test: Calculates average engagement per post correctly
- Test: Identifies top performing post title per platform
- Test: Marks LinkedIn as isAvailable=false
- Test: Returns null followerCount when platform adapter doesn't provide it

---

## 3.4 Caching

### Unit Tests
- Test: Second call to GetSummaryAsync returns cached result (mock verifies single DB call)
- Test: refresh=true bypasses cache and fetches fresh data
- Test: Cache tags are correctly applied per data source
- Test: Refresh rate limiting rejects second refresh within 1 minute

---

## 3.5 API Endpoints

### Integration Tests (`AnalyticsEndpointTests.cs`)
- Test: GET /api/analytics/dashboard returns 200 with DashboardSummary shape
- Test: GET /api/analytics/dashboard?period=7d uses correct date range
- Test: GET /api/analytics/engagement-timeline returns array of DailyEngagement
- Test: GET /api/analytics/platform-summary returns array of PlatformSummary
- Test: GET /api/analytics/website returns WebsiteAnalyticsResponse with all sections
- Test: GET /api/analytics/substack returns SubstackPost array
- Test: GET /api/analytics/health returns connectivity status
- Test: Invalid period parameter returns 400
- Test: Both period and from/to provided: period takes precedence

---

## 3.8 Security & Resilience

### Tests
- Test: Substack URL validation rejects non-substack.com hostnames
- Test: HttpClient timeout triggers after configured duration
- Test: Circuit breaker opens after 3 consecutive failures
- Test: Retry policy retries on 429 status code with backoff

---

## 4.2 Analytics Store

### Store Tests (`analytics.store.spec.ts`)
- Test: loadDashboard dispatches parallel requests and populates all state slices
- Test: loadDashboard sets loading=true during fetch, false after
- Test: setPeriod updates period state and triggers reload
- Test: refreshDashboard passes refresh=true query parameter
- Test: lastRefreshedAt is updated after successful load
- Test: Partial API failure still populates available sections

---

## 4.3 Analytics Service

### Service Tests (`analytics.service.spec.ts`)
- Test: getDashboardSummary calls correct URL with period parameter
- Test: getEngagementTimeline passes days parameter
- Test: getPlatformSummaries calls platform-summary endpoint
- Test: getWebsiteAnalytics calls website endpoint
- Test: getSubstackPosts calls substack endpoint

---

## 4.4-4.6 Dashboard Components

### Component Tests
- Test: DashboardKpiCardsComponent renders all 6 KPI cards with correct values
- Test: KPI cards show up/down trend indicators based on % change
- Test: KPI cards show "N/A" when change is null
- Test: EngagementTimelineChartComponent renders UIChart with correct datasets
- Test: PlatformBreakdownChartComponent renders stacked bar with likes/comments/shares
- Test: PlatformHealthCardsComponent renders card per platform with brand colors
- Test: PlatformHealthCardsComponent shows "Coming Soon" for LinkedIn
- Test: WebsiteAnalyticsSectionComponent renders GA4 metrics and search query table
- Test: SubstackSectionComponent renders post list from RSS data
- Test: DateRangeSelectorComponent emits periodChanged on preset button click
- Test: DateRangeSelectorComponent emits custom date range from calendar
- Test: TopContentTableComponent shows engagement rate with correct color coding
