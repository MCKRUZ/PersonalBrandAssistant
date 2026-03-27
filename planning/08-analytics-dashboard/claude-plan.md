# Analytics Dashboard - Implementation Plan

## 1. Context & Goals

The Personal Brand Assistant (PBA) manages content across 7 platforms but lacks a unified view of brand performance. This plan adds a comprehensive analytics dashboard that aggregates engagement metrics from social platforms, website analytics from GA4/Search Console, and Substack post data from RSS into a single-page view.

**Stack:** .NET 10 backend (Minimal APIs), Angular 19 frontend (standalone components, NgRx signals), PostgreSQL, PrimeNG UI components.

**Approved mockup:** `publish/analytics-dashboard.html` — dark theme, purple/violet accent, Chart.js charts, responsive grid.

---

## 2. Architecture Overview

```
┌─────────────────────────────────────────────────────┐
│  Angular Dashboard Page                              │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐            │
│  │ KPI Cards│ │ Charts   │ │ Tables   │            │
│  └────┬─────┘ └────┬─────┘ └────┬─────┘            │
│       └─────────────┼────────────┘                   │
│              AnalyticsStore (NgRx signals)            │
│                      │                                │
│              AnalyticsService (HTTP)                   │
└──────────────────────┼──────────────────────────────┘
                       │ REST API
┌──────────────────────┼──────────────────────────────┐
│  .NET API            │                               │
│  ┌───────────────────▼──────────────────┐            │
│  │  AnalyticsEndpoints (5 new routes)    │            │
│  └───────────────────┬──────────────────┘            │
│  ┌───────────────────▼──────────────────┐            │
│  │  DashboardAggregator                  │            │
│  │  (orchestrates all data sources)      │            │
│  └──┬──────────┬───────────┬────────────┘            │
│     │          │           │                          │
│  ┌──▼───┐  ┌──▼────┐  ┌──▼────────┐                │
│  │Social│  │Website │  │ Substack  │                │
│  │Engage│  │Analytics│  │ RSS Feed │                │
│  │Aggreg│  │Service │  │ Service  │                │
│  └──┬───┘  └──┬────┘  └──────────┘                  │
│     │         │                                       │
│  Platform  GA4 API                                    │
│  Adapters  + GSC API                                  │
│                                                       │
│  ┌────────────────────────────────┐                  │
│  │  HybridCache (L1 in-memory)    │                  │
│  │  Tag-based invalidation        │                  │
│  └────────────────────────────────┘                  │
└──────────────────────────────────────────────────────┘
```

---

## 2.1 Cross-Cutting Conventions

### Date Range Semantics
- `from` is inclusive at `00:00:00 UTC`, `to` is inclusive end-of-day `23:59:59 UTC`
- `period` query param (1d/7d/14d/30d/90d) takes precedence over `from/to` unless both explicitly provided
- Previous period for comparison: `previousFrom = from - (to - from + 1day)`, `previousTo = from - 1day` (equal-length mirror window)
- All dates stored as `DateTimeOffset` in UTC; GA4 dates converted from property timezone

### Division by Zero / Null Semantics
- Engagement rate: return `0` when impressions denominator is 0
- % change: return `null` when previous period value is 0 (frontend shows "N/A" instead of infinity)
- Cost per engagement: return `null` when total engagement is 0

### Partial Failure Model
- Dashboard aggregator returns composite response with per-section nullable data
- Each section includes `generatedAt` timestamp and optional `error` string
- Frontend renders available sections and shows staleness/error indicators per failed section
- Never fail the entire response because one data source is down

### Platform Data Availability

| Platform | Engagement | Impressions | Followers | Status |
|---|---|---|---|---|
| Twitter/X | Yes | Yes | Yes | Active |
| YouTube | Yes | Yes (views) | Yes (subscribers) | Active |
| Instagram | Yes | Yes | Yes | Active |
| Reddit | Yes | No | No (karma only) | Active |
| LinkedIn | No | No | No | Coming Soon |
| Website | N/A | Yes (page views) | N/A | GA4/GSC |
| Substack | No | No | No | RSS Only |

Missing metrics treated as `null` (not 0). Excluded from aggregate denominators.

---

## 3. Backend Implementation

### 3.1 Google Analytics Service

**New interface:** `IGoogleAnalyticsService` in Application layer.

Methods:
```csharp
Task<Result<WebsiteOverview>> GetOverviewAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
Task<Result<IReadOnlyList<PageViewEntry>>> GetTopPagesAsync(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);
Task<Result<IReadOnlyList<TrafficSourceEntry>>> GetTrafficSourcesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
Task<Result<IReadOnlyList<SearchQueryEntry>>> GetTopQueriesAsync(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct);
```

**Implementation:** `GoogleAnalyticsService` in Infrastructure layer.

**NuGet packages:**
- `Google.Analytics.Data.V1Beta` — GA4 Data API (gRPC-based)
- `Google.Apis.SearchConsole.v1` — Search Console API

**Authentication:** Load service account JSON from configurable path (`GoogleAnalytics:CredentialsPath`). Create `BetaAnalyticsDataClient` via `BetaAnalyticsDataClientBuilder.CredentialsPath`. Create `SearchConsoleService` via `GoogleCredential.FromFile().CreateScoped()`.

**Configuration options:**

```csharp
public class GoogleAnalyticsOptions
{
    public const string SectionName = "GoogleAnalytics";
    public string CredentialsPath { get; set; } = "secrets/google-analytics-sa.json";
    public string PropertyId { get; set; } = "261358185";
    public string SiteUrl { get; set; } = "https://matthewkruczek.ai/";
}
```

**Models:**

```csharp
public record WebsiteOverview(int ActiveUsers, int Sessions, int PageViews, double AvgSessionDuration, double BounceRate, int NewUsers);
public record PageViewEntry(string PagePath, int Views, int Users);
public record TrafficSourceEntry(string Channel, int Sessions, int Users);
public record SearchQueryEntry(string Query, int Clicks, int Impressions, double Ctr, double Position);
```

**GA4 report:** Use `RunReportRequest` with `Property = "properties/261358185"`, dimensions `["date"]` or `["pagePath"]`, metrics `["activeUsers", "sessions", "screenPageViews", "averageSessionDuration", "bounceRate"]`, date range from parameters.

**Search Console query:** Use `SearchAnalyticsQueryRequest` with dimensions `["query"]`, start/end dates, row limit 20.

**Rate limit awareness:** GA4 allows 40K tokens/hour (most requests <10 tokens). Search Console allows 1,200 QPM. These are generous for a single-user dashboard. No special throttling needed beyond HybridCache.

### 3.2 Substack Service

**New interface:** `ISubstackService` in Application layer.

```csharp
Task<Result<IReadOnlyList<SubstackPost>>> GetRecentPostsAsync(int limit, CancellationToken ct);
```

**Implementation:** Parse RSS feed at configured URL. Use `System.ServiceModel.Syndication.SyndicationFeed.Load()` via `XmlReader` over `HttpClient` response stream.

**Model:**

```csharp
public record SubstackPost(string Title, string Url, DateTimeOffset PublishedAt, string? Summary);
```

**Configuration:**

```csharp
public class SubstackOptions
{
    public const string SectionName = "Substack";
    public string FeedUrl { get; set; } = "https://matthewkruczek.substack.com/feed";
}
```

**Error handling:** If RSS feed is unreachable, return `Result.Failure` with appropriate error. Dashboard shows empty Substack section with staleness indicator.

### 3.3 Dashboard Aggregator

**New interface:** `IDashboardAggregator` in Application layer. Orchestrates all data sources.

Methods:
```csharp
Task<Result<DashboardSummary>> GetSummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
Task<Result<IReadOnlyList<DailyEngagement>>> GetTimelineAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
Task<Result<IReadOnlyList<PlatformSummary>>> GetPlatformSummariesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
```

**DashboardSummary model:**

```csharp
public record DashboardSummary(
    int TotalEngagement, int PreviousEngagement,
    int TotalImpressions, int PreviousImpressions,
    decimal EngagementRate, decimal PreviousEngagementRate,
    int ContentPublished, int PreviousContentPublished,
    decimal CostPerEngagement, decimal PreviousCostPerEngagement,
    int WebsiteUsers, int PreviousWebsiteUsers,
    DateTimeOffset GeneratedAt);
```

**DailyEngagement model:**

```csharp
public record DailyEngagement(DateOnly Date, IReadOnlyList<PlatformDailyMetrics> Platforms, int Total);
public record PlatformDailyMetrics(string Platform, int Likes, int Comments, int Shares, int Total);
```

Note: Breakdown into likes/comments/shares per platform per day is needed for the stacked bar chart (platform breakdown) as well as the timeline.

**PlatformSummary model:**

```csharp
public record PlatformSummary(
    string Platform, int? FollowerCount, int PostCount,
    double AvgEngagement, string? TopPostTitle, string? TopPostUrl,
    bool IsAvailable);
```

**Implementation flow for `GetSummaryAsync`:**
1. Query `Contents` for published count in range
2. Query `EngagementSnapshots` joined through `ContentPlatformStatuses` for engagement totals
3. Call `IGoogleAnalyticsService.GetOverviewAsync()` for website users
4. Calculate period-over-period by running same queries for previous period (e.g., if 30d selected, compare to days 31-60)
5. Calculate engagement rate = total engagement / total impressions
6. Fetch LLM cost from `AgentExecutions` for cost-per-engagement

**Implementation flow for `GetTimelineAsync`:**
1. Query all `EngagementSnapshots` in date range, joined to `ContentPlatformStatuses` for platform type
2. Group by date and platform
3. Sum (likes + comments + shares) per group
4. Return list of `DailyEngagement` with per-platform breakdown

### 3.4 Caching Layer

**Package:** `Microsoft.Extensions.Caching.Hybrid`

**Registration:**
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

**Usage in DashboardAggregator:** Wrap each data-fetching method with `HybridCache.GetOrCreateAsync()`. Use tags like `"dashboard"`, `"ga4"`, `"social"` for targeted invalidation.

**TTLs by source:**
- Social engagement: 15 min
- GA4/website: 2 hours
- Search Console: 6 hours
- Substack RSS: 2 hours
- Aggregated dashboard summary: 5 min L1, 30 min L2

**Manual refresh:** Accept optional `?refresh=true` query parameter to bypass cache via `cache.RemoveByTagAsync()`.

### 3.5 New API Endpoints

Add to `AnalyticsEndpoints.cs`:

| Route | Method | Handler | Returns |
|---|---|---|---|
| `/api/analytics/dashboard` | GET | `GetDashboard` | `DashboardSummary` |
| `/api/analytics/engagement-timeline` | GET | `GetTimeline` | `DailyEngagement[]` |
| `/api/analytics/platform-summary` | GET | `GetPlatformSummaries` | `PlatformSummary[]` |
| `/api/analytics/website` | GET | `GetWebsiteAnalytics` | `WebsiteAnalyticsResponse` |
| `/api/analytics/substack` | GET | `GetSubstackPosts` | `SubstackPost[]` |

**Query parameters:** All endpoints accept `period` (1d/7d/14d/30d/90d) or `from`/`to` date strings, plus optional `refresh=true`.

**WebsiteAnalyticsResponse:** Combines `WebsiteOverview`, `PageViewEntry[]`, `TrafficSourceEntry[]`, `SearchQueryEntry[]` into one response to minimize frontend requests.

**Extend existing `/api/analytics/top`:** Add `impressions` and `engagementRate` fields to `TopPerformingContent` response. The frontend top content table needs these columns.

### 3.6 Dependency Injection

Register in `DependencyInjection.cs`:
- `IGoogleAnalyticsService` → `GoogleAnalyticsService` (Scoped, typed HttpClient for GSC)
- `ISubstackService` → `SubstackService` (Scoped, typed HttpClient)
- `IDashboardAggregator` → `DashboardAggregator` (Scoped)
- GA4 client: register `BetaAnalyticsDataClient` as Singleton (thread-safe, reusable)
- Bind `GoogleAnalyticsOptions` and `SubstackOptions` from configuration

### 3.7 Docker Configuration

Add to `docker-compose.yml` API service:
```yaml
volumes:
  - ./secrets:/app/secrets:ro
environment:
  GoogleAnalytics__CredentialsPath: /app/secrets/google-analytics-sa.json
```

### 3.8 Security & Resilience

**Authentication:** All analytics endpoints are behind the existing `ApiKeyMiddleware`. Rate-limit `refresh=true` (max 1 per minute) to prevent cache-busting abuse.

**Substack SSRF protection:** Validate `SubstackOptions.FeedUrl` hostname matches `*.substack.com`. Set `HttpClient` timeout to 10 seconds.

**GA4/GSC permissions validation:** Add a startup check that verifies the service account can access the GA4 property. Log warning (not crash) if access fails.

**HttpClient resilience:** Add Polly policies:
- Timeout: 15s for GA4 gRPC, 10s for Substack HTTP
- Retry: 2 retries with exponential backoff + jitter for 429/5xx
- Circuit breaker: Open after 3 consecutive failures, half-open after 30s

### 3.9 Database Indexes

Ensure indexes exist for dashboard query performance:
- `EngagementSnapshots(FetchedAt, ContentPlatformStatusId)` — timeline queries
- `ContentPlatformStatuses(ContentId, Platform)` — platform grouping
- `Contents(PublishedAt) WHERE Status = Published` — content count queries

### 3.10 Health Check

Add `GET /api/analytics/health` — returns connectivity status for GA4, GSC, and Substack RSS. Non-sensitive, used for monitoring.

---

## 4. Frontend Implementation

### 4.1 Component Structure

```
features/analytics/
  analytics-dashboard.component.ts       # Main page (rewrite existing)
  components/
    dashboard-kpi-cards.component.ts      # KPI metric cards row
    engagement-timeline-chart.component.ts # Line chart (UIChart)
    platform-breakdown-chart.component.ts  # Stacked bar chart (UIChart)
    platform-health-cards.component.ts     # Platform health grid
    website-analytics-section.component.ts # GA4 + GSC detail
    substack-section.component.ts          # RSS post listing
    date-range-selector.component.ts       # Preset buttons + custom picker
    top-content-table.component.ts         # Ranked content table (reuse/extend)
  services/
    analytics.service.ts                   # Extend with new endpoints
  store/
    analytics.store.ts                     # Rewrite with dashboard state
  models/
    dashboard.model.ts                     # New response types
```

### 4.2 Analytics Store (Rewrite)

**State shape:**

```typescript
interface AnalyticsDashboardState {
  readonly summary: DashboardSummary | null;
  readonly timeline: readonly DailyEngagement[];
  readonly platformSummaries: readonly PlatformSummary[];
  readonly websiteData: WebsiteAnalyticsResponse | null;
  readonly substackPosts: readonly SubstackPost[];
  readonly topContent: readonly TopPerformingContent[];
  readonly period: DashboardPeriod;  // '1d' | '7d' | '14d' | '30d' | '90d' | { from: string, to: string }
  readonly loading: boolean;
  readonly lastRefreshedAt: string | null;
}
```

**Methods:**
- `loadDashboard(period)` — Calls all 5+ endpoints in parallel via `forkJoin`, updates all state at once
- `refreshDashboard()` — Same as load but with `?refresh=true` to bypass cache
- `setPeriod(period)` — Updates period and triggers reload

**Computed signals:**
- `engagementChange()` — % change calculation from summary
- `isStale()` — Whether lastRefreshedAt is older than threshold

### 4.3 Angular Models

```typescript
interface DashboardSummary {
  readonly totalEngagement: number;
  readonly previousEngagement: number;
  readonly totalImpressions: number;
  readonly previousImpressions: number;
  readonly engagementRate: number;
  readonly previousEngagementRate: number;
  readonly contentPublished: number;
  readonly previousContentPublished: number;
  readonly costPerEngagement: number;
  readonly previousCostPerEngagement: number;
  readonly websiteUsers: number;
  readonly previousWebsiteUsers: number;
  readonly generatedAt: string;
}

interface DailyEngagement {
  readonly date: string;
  readonly byPlatform: Record<string, number>;
  readonly total: number;
}

interface PlatformSummary {
  readonly platform: string;
  readonly followerCount: number | null;
  readonly postCount: number;
  readonly avgEngagement: number;
  readonly topPostTitle: string | null;
  readonly topPostUrl: string | null;
  readonly isAvailable: boolean;
}

interface WebsiteAnalyticsResponse {
  readonly overview: WebsiteOverview;
  readonly topPages: readonly PageViewEntry[];
  readonly trafficSources: readonly TrafficSourceEntry[];
  readonly searchQueries: readonly SearchQueryEntry[];
}

interface SubstackPost {
  readonly title: string;
  readonly url: string;
  readonly publishedAt: string;
  readonly summary: string | null;
}
```

### 4.4 Chart Configuration

**Dark theme initialization** (set once at app startup or in dashboard component):

```typescript
Chart.defaults.color = '#71717a';
Chart.defaults.borderColor = 'rgba(255,255,255,0.06)';
```

**Engagement Timeline (line chart):** PrimeNG UIChart with `type="line"`. One dataset per platform + total. Use `PLATFORM_COLORS` from shared utils. Set `tension: 0.35`, `pointRadius: 0`, `fill: true` for total line only.

**Platform Breakdown (horizontal bar):** PrimeNG UIChart with `type="bar"`, `indexAxis: 'y'`, stacked scales. Three datasets: Likes, Comments, Shares.

### 4.5 Date Range Selector

Standalone component with:
- Row of PrimeNG Buttons as preset toggles (1D, 7D, 14D, 30D, 90D)
- PrimeNG Calendar in range mode for custom dates
- Output event `periodChanged` emitting the selected period
- Active preset highlighted with filled variant

### 4.6 Error & Staleness Handling

- Each data section checks `lastRefreshedAt` and shows "Updated X ago" text
- If a platform is unavailable (`isAvailable: false`), show the card with "Coming Soon" or "Data unavailable" badge
- Loading state: show `p-skeleton` placeholders per section
- LinkedIn specifically shows "Coming Soon" badge on its health card

---

## 5. File Organization

### Backend new files:
```
Application/Common/Interfaces/
  IGoogleAnalyticsService.cs
  ISubstackService.cs
  IDashboardAggregator.cs
Application/Common/Models/
  GoogleAnalyticsModels.cs    (WebsiteOverview, PageViewEntry, TrafficSourceEntry, SearchQueryEntry)
  DashboardModels.cs          (DashboardSummary, DailyEngagement, PlatformSummary)
  SubstackModels.cs           (SubstackPost)
  GoogleAnalyticsOptions.cs
  SubstackOptions.cs
Infrastructure/Services/
  AnalyticsServices/
    GoogleAnalyticsService.cs
    SubstackService.cs
    DashboardAggregator.cs
Api/Endpoints/
  AnalyticsEndpoints.cs       (extend with 5 new routes)
```

### Frontend new files:
```
features/analytics/
  analytics-dashboard.component.ts  (rewrite)
  components/
    dashboard-kpi-cards.component.ts
    engagement-timeline-chart.component.ts
    platform-breakdown-chart.component.ts
    platform-health-cards.component.ts
    website-analytics-section.component.ts
    substack-section.component.ts
    date-range-selector.component.ts
  store/analytics.store.ts          (rewrite)
  services/analytics.service.ts     (extend)
  models/dashboard.model.ts         (new)
```

---

## 6. Implementation Order

1. **Backend models & interfaces** — Define all DTOs and interfaces first
2. **Google Analytics service** — GA4 + Search Console integration with tests
3. **Substack RSS service** — Feed parser with tests
4. **Dashboard aggregator** — Orchestrate data sources, caching, period comparison
5. **API endpoints** — Wire up new routes
6. **Frontend models & service** — TypeScript interfaces, extend AnalyticsService
7. **Analytics store** — Rewrite with dashboard state
8. **Dashboard page & components** — KPI cards, charts, tables, platform cards
9. **Date range selector** — Preset + custom picker
10. **Website analytics section** — GA4/GSC detail view
11. **Substack section** — RSS post listing
12. **Integration testing** — End-to-end with mocked external APIs
