# Analytics Dashboard - Complete Specification

## Overview

Build a comprehensive analytics dashboard for the Personal Brand Assistant that aggregates metrics across 7 data sources into a single-page view: Twitter/X, LinkedIn, YouTube, Instagram, Reddit, the personal website (matthewkruczek.ai via GA4 + Search Console), and Substack (via RSS). The dashboard provides KPI cards, time-series charts, platform health summaries, content performance rankings, and website analytics detail sections.

## Data Sources

### Social Platforms (existing adapters with real data)
| Platform | Metrics Available | Notes |
|---|---|---|
| Twitter/X | Likes, Comments, Shares, Impressions, bookmark_count, quote_count | Full v2 API |
| YouTube | Likes, Comments, Views (impressions), favoriteCount | Google Data API |
| Instagram | Likes, Comments, Impressions | Meta Graph API |
| Reddit | Likes (ups), Comments, Score, downs | Reddit API |
| LinkedIn | Stubbed to zeros | UGC API deferred - show "Coming Soon" |

### Website (new integration)
- **GA4** (Property `261358185`): activeUsers, sessions, screenPageViews, averageSessionDuration, bounceRate, newUsers. Dimensions: pagePath, sessionDefaultChannelGroup, date.
- **Google Search Console**: impressions, clicks, CTR, position. Dimensions: query, page, date.
- **Auth:** JWT service account (`jarvis-analytics@ultimate-vigil-486216-g9.iam.gserviceaccount.com`), credentials volume-mounted at configurable path from `secrets/google-analytics-sa.json`.

### Substack (new integration)
- **RSS feed only** (`https://matthewkruczek.substack.com/feed`): Post titles, dates, URLs, descriptions. No engagement metrics or subscriber counts.

## Dashboard Layout

### Design Reference
Approved HTML mockup: `publish/analytics-dashboard.html` (dark theme, purple/violet accent, PrimeNG-compatible).

### 1. KPI Cards (top row)
Unified KPIs spanning both social and website metrics:
- **Total Engagement** - sum of likes+comments+shares across all social platforms. % change vs previous period.
- **Total Impressions** - sum across all platforms + website page views. % change vs previous period.
- **Engagement Rate** - (total engagement / total impressions) * 100. Change in percentage points.
- **Content Published** - count of published content in selected period.
- **Cost Per Engagement** - total LLM generation cost / total engagement.
- **Website Users** - GA4 activeUsers in period. % change vs previous.

### 2. Date Range Controls
- **Presets:** 1D, 7D, 14D, 30D (default), 90D
- **Custom:** Date picker for arbitrary start/end range
- Manual refresh button (no auto-refresh)

### 3. Engagement Over Time (line chart)
- PrimeNG UIChart (Chart.js wrapper) for consistency
- Multi-line: daily engagement per platform + total line
- Colors: Twitter=#1d9bf0, LinkedIn=#0a66c2, YouTube=#ff0000, Instagram=#e1306c, Reddit=#ff4500, Total=#8b5cf6
- Responsive, dark theme via Chart.defaults overrides

### 4. Platform Breakdown (horizontal stacked bar)
- Each platform as a row
- Stacked segments: Likes, Comments, Shares
- Platform brand colors on Y-axis labels

### 5. Platform Health Cards
One card per active platform (Twitter/X, LinkedIn*, YouTube, Instagram, Reddit, Website, Substack):
- Follower/subscriber count (where available)
- Posts this period
- Avg engagement per post
- Best performing post title
- Platform brand color accent bar
- *LinkedIn shows "Coming Soon" for metrics

### 6. Top Performing Content (data table)
- PrimeNG Table with pagination
- Columns: Rank, Title, Platform (colored dot), Total Engagement, Impressions, Engagement Rate, Published Date
- Sorted by total engagement descending

### 7. Website Analytics Section (expandable)
- **GA4 Metrics:** Users, Sessions, Page Views, Avg Session Duration, Bounce Rate (as KPI-style cards)
- **Top Pages:** Table of top pages by views
- **Traffic Sources:** Breakdown by channel group
- **Search Console:** Top search queries with clicks, impressions, CTR, avg position

### 8. Substack Section
- Recent posts from RSS feed (title, date, URL link)
- Simple card/list layout, no engagement metrics

### 9. Engagement Automation Stats
- Active tasks count
- Total actions performed
- Success rate %
- Actions this week

## Error Handling
- Show last cached data with staleness indicator when a platform fails
- Never show empty/broken cards if cached data exists
- Staleness indicator: "Last updated X ago" text

## Backend Architecture

### New API Endpoints
- `GET /api/analytics/dashboard?period=30d` - Aggregated KPIs with period-over-period comparison
- `GET /api/analytics/engagement-timeline?days=30` - Daily engagement by platform for line chart
- `GET /api/analytics/platform-summary` - Per-platform health cards data
- `GET /api/analytics/website?period=30d` - GA4 + Search Console metrics
- `GET /api/analytics/substack` - RSS feed post listing

### New Services
- `IGoogleAnalyticsService` - GA4 Data API + Search Console integration
  - NuGet: `Google.Analytics.Data.V1Beta` (gRPC), `Google.Apis.SearchConsole.v1`
  - Auth: Service account JSON loaded from configurable file path
- `ISubstackService` - RSS feed parser
  - Use `System.ServiceModel.Syndication` or `CodeHollow.FeedReader`
- `IDashboardAggregator` - Combines all data sources into dashboard response models

### Caching
- HybridCache with tag-based invalidation
- TTLs: GA4 1-4h, Search Console 6-12h, Social 15-30min, Substack 1-2h, Aggregated dashboard 5min L1/30min L2
- Manual refresh bypasses cache

### Configuration
```json
"GoogleAnalytics": {
  "CredentialsPath": "secrets/google-analytics-sa.json",
  "PropertyId": "261358185",
  "SiteUrl": "https://matthewkruczek.ai/"
},
"Substack": {
  "FeedUrl": "https://matthewkruczek.substack.com/feed"
}
```

Docker: volume mount `secrets/` directory into container.

## Frontend Architecture

### New Components
- `AnalyticsDashboardComponent` - Main page (replaces or extends existing)
- `DashboardKpiCardsComponent` - KPI metric cards row
- `EngagementTimelineChartComponent` - Line chart
- `PlatformBreakdownChartComponent` - Stacked bar chart
- `PlatformHealthCardsComponent` - Platform cards grid
- `WebsiteAnalyticsSectionComponent` - GA4/GSC detail
- `SubstackSectionComponent` - RSS post listing
- `DateRangeSelectorComponent` - Preset + custom picker

### Store
Extend or replace `AnalyticsStore` with dashboard-specific state:
- Dashboard KPIs, timeline data, platform summaries, website data, substack posts
- Loading/error states per data source
- Date range state with preset/custom support

### Chart Library
PrimeNG UIChart (existing dependency). Configure dark theme via Chart.js defaults at app initialization.

## Technical Constraints
- Follow existing patterns: `Result<T>`, `rxMethod`/`tapResponse`, typed HttpClients
- Immutable state (spread operators, records)
- 200-400 line file max
- Test with xUnit + Moq (backend), Jasmine/Karma (frontend)
