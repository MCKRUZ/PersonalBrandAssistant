# Analytics Dashboard Research

## Part 1: Codebase Research

### Existing Analytics Infrastructure

#### IEngagementAggregator & Implementation
- `FetchLatestAsync(contentPlatformStatusId)` - Fetches and stores engagement for a single platform post
- `GetPerformanceAsync(contentId)` - Returns `ContentPerformanceReport` with latest engagement by platform
- `GetTopContentAsync(from, to, limit)` - Ranks content by total engagement in date range
- `CleanupSnapshotsAsync()` - Purges old snapshots (>30d deleted, 7-30d consolidated to 1/day)
- Validates rate limits before fetching via `IRateLimiter`
- Batches snapshot queries to avoid N+1; groups in-memory not SQL

#### Key Entities
- **EngagementSnapshot**: `ContentPlatformStatusId`, `Likes`, `Comments`, `Shares`, `Impressions?`, `Clicks?`, `FetchedAt`
- **ContentPlatformStatus**: Links `ContentId` to `Platform` (PlatformType), `PlatformPostId`, `PostUrl`, `PublishedAt`, `Status`
- **EngagementTask/Execution/Action**: Automation task scheduling, execution tracking, individual action results

#### Models
- `ContentPerformanceReport(ContentId, LatestByPlatform dict, TotalEngagement, LlmCost, CostPerEngagement)`
- `TopPerformingContent(ContentId, Title, TotalEngagement, EngagementByPlatform dict)`
- `EngagementStats(Likes, Comments, Shares, Impressions, Clicks, PlatformSpecific dict)`
- `PlatformProfile(PlatformUserId, DisplayName, AvatarUrl, FollowerCount?)`

#### Existing API Endpoints (`AnalyticsEndpoints.cs`)
- `GET /api/analytics/content/{id}` -> ContentPerformanceReport
- `GET /api/analytics/top?from=&to=&limit=10` -> TopPerformingContent[]
- `POST /api/analytics/content/{id}/refresh` -> EngagementSnapshot (202)

### Platform Adapter Patterns
- `ISocialPlatform` interface with `GetEngagementAsync(platformPostId)` and `GetProfileAsync()`
- `PlatformAdapterBase` abstract class handles: token management, rate limiting, token refresh on 401, error mapping
- Template method: `ExecuteWithTokenAsync()` -> subclass `ExecuteGetEngagementAsync()`
- Typed `HttpClient` per adapter, registered in DI

### Frontend Patterns

#### Analytics Store (NgRx Signals)
```typescript
interface AnalyticsState {
  topContent: readonly TopPerformingContent[];
  selectedReport: ContentPerformanceReport | undefined;
  dateRange: DateRange;
  loading: boolean;
}
```
- `rxMethod` with `switchMap`/`tapResponse` pattern
- `withComputed()` for derived state

#### Existing Chart Component
- `EngagementChartComponent` uses **PrimeNG UIChart** (Chart.js wrapper)
- Bar chart (horizontal) for top content, Doughnut for platform breakdown
- Uses `PLATFORM_COLORS` map from `shared/utils/platform-icons.ts`

#### Angular Models (`analytics.model.ts`)
```typescript
interface EngagementSnapshot { platform, likes, shares, comments, views, clicks, impressions, collectedAt }
interface ContentPerformanceReport { contentId, title?, contentType, publishedAt?, totalEngagement, engagementByPlatform[], generatedAt }
interface TopPerformingContent { contentId, title?, contentType, totalEngagement, platforms[], publishedAt? }
```

### Testing Setup
- **Framework:** xUnit + Moq + MockQueryable.Moq
- **Pattern:** Mock `IApplicationDbContext` DbSets via MockQueryable, factory methods for SUT creation
- **Angular:** Jasmine/Karma with `HttpTestingController`

### Infrastructure Patterns
- **Secrets:** User Secrets (dev), encrypted platform tokens in DB
- **Caching:** `IMemoryCache` for rate limit decisions only; no dashboard caching exists
- **Background Services:** `EngagementAggregationProcessor` (every 4h), `EngagementScheduler` (every 60s)
- **DI:** Services are Scoped, encryption/datetime Singleton

### Configuration
```json
"ContentEngine": {
  "EngagementRetentionDays": 30,
  "EngagementAggregationIntervalHours": 4
}
```

---

## Part 2: Web Research

### Google Analytics 4 Data API in .NET

**Recommended Package:** `Google.Analytics.Data.V1Beta` (gRPC-based, preferred)

**Service Account Auth:**
```csharp
BetaAnalyticsDataClient client = new BetaAnalyticsDataClientBuilder
{
    CredentialsPath = "path/to/service-account.json"
}.Build();
```

**RunReport Usage:**
```csharp
RunReportRequest request = new RunReportRequest
{
    Property = "properties/" + propertyId,
    Dimensions = { new Dimension { Name = "date" } },
    Metrics = { new Metric { Name = "activeUsers" }, new Metric { Name = "sessions" } },
    DateRanges = { new DateRange { StartDate = "30daysAgo", EndDate = "today" } },
};
var response = client.RunReport(request);
```

**Rate Limits:** 200K core tokens/day, 40K/hour, 10 concurrent requests. Most requests consume <10 tokens. Batch endpoint available.

### Google Search Console API in .NET

**Package:** `Google.Apis.SearchConsole.v1`

**Auth:**
```csharp
var credential = GoogleCredential.FromFile("service-account.json")
    .CreateScoped(SearchConsoleService.Scope.Webmasters);
var service = new SearchConsoleService(new BaseClientService.Initializer
{
    HttpClientInitializer = credential,
    ApplicationName = "PersonalBrandAssistant"
});
```

**Query:** Dimensions: query, page, date, country, device. Metrics: impressions, clicks, CTR, position. Max 25K rows/request, pagination via startRow.

**Rate Limits:** 1,200 QPM per site, 40K QPM per project.

**Caching Note:** GA4 data is 24-48h delayed; Search Console is 2-3 days delayed. Cache aggressively.

### Chart.js in Angular 19

**Recommendation: Use ng2-charts v8+** (actively maintained, standalone component support)

```bash
ng add ng2-charts  # auto-configures app.config.ts
```

**Setup:**
```typescript
// app.config.ts
import { provideCharts, withDefaultRegisterables } from 'ng2-charts';
providers: [provideCharts(withDefaultRegisterables())]

// component
import { BaseChartDirective } from 'ng2-charts';
@Component({
  standalone: true,
  imports: [BaseChartDirective],
  template: `<canvas baseChart [data]="chartData" [options]="chartOptions" [type]="'line'"></canvas>`
})
```

**Dark Theme:** Set `Chart.defaults.color` and `Chart.defaults.borderColor` globally at app startup. No built-in dark mode.

**Performance:** Use `animation: false`, `pointRadius: 0`, `@defer (on viewport)` for off-screen charts. ng2-charts handles cleanup in `ngOnDestroy`.

**Note:** PrimeNG's UIChart already wraps Chart.js and is used in the existing `EngagementChartComponent`. Consider whether to use ng2-charts or stick with PrimeNG UIChart for consistency.

### Substack API/Scraping

**No official API exists.** Options ranked by reliability:

1. **RSS Feed** (safest, stable): `https://matthewkruczek.substack.com/feed` - returns ~20 recent posts with title, description, link, pubDate, full HTML body. No auth required.

2. **Undocumented API** (fragile):
   - `/api/v1/archive?sort=new&limit=12&offset=0` - paginated post list (no auth for free content)
   - `/api/v1/posts/{slug}` - single post details
   - `/api/v1/posts/{id}/comments` - comments

3. **Subscriber counts** - NOT reliably available programmatically

**TOS:** Substack prohibits scraping. RSS feeds are intentionally public. For own publication data at low volume, enforcement risk is minimal.

**Recommendation:** RSS feed primary, `/api/v1/archive` supplementary with 1-2h cache TTL. Build thin `HttpClient` wrapper (no .NET library exists).

### Analytics Dashboard Caching

**Recommended: HybridCache** (ASP.NET Core 9+/.NET 10)

```csharp
builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromMinutes(30),
        LocalCacheExpiration = TimeSpan.FromMinutes(5)
    };
});
```

**Usage:**
```csharp
return await cache.GetOrCreateAsync(
    $"dashboard-{period}",
    async cancel => await BuildDashboardFromApis(period, cancel),
    options, tags, cancellationToken: ct);
```

**Provides:** L1 in-memory + L2 distributed, stampede protection, tag-based invalidation. Works without Redis (single-server), easy to add Redis later.

**Recommended TTLs:**

| Data Source | TTL | Rationale |
|---|---|---|
| GA4 analytics | 1-4 hours | Data is 24-48h delayed |
| Search Console | 6-12 hours | Data is 2-3 days delayed |
| Social engagement | 15-30 minutes | More real-time |
| Substack posts | 1-2 hours | Infrequent publishing |
| Aggregated dashboard | 5 min L1, 30 min L2 | Balance freshness/load |

**UI:** Display "Last refreshed" timestamp, manual refresh button, staleness indicators.

### Sources
- GA4: developers.google.com/analytics/devguides/reporting/data/v1
- GSC: developers.google.com/webmaster-tools
- ng2-charts: github.com/valor-software/ng2-charts
- Chart.js Performance: chartjs.org/docs/latest/general/performance.html
- HybridCache: learn.microsoft.com/aspnet/core/performance/caching/hybrid
- Substack API reverse-eng: iam.slys.dev/p/no-official-api-no-problem-how-i
