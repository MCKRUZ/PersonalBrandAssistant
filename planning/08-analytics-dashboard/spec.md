# Analytics Dashboard - Feature Spec

## Overview

Build a comprehensive analytics dashboard for the Personal Brand Assistant that aggregates metrics across all social platforms, the personal website, and Substack newsletter. The dashboard provides a single-pane view of brand performance with KPI cards, time-series charts, platform health summaries, and content performance rankings.

## Data Sources

### Social Platforms (existing adapters)
- **Twitter/X** - Likes, Comments (replies), Shares (retweets), Impressions, bookmark_count, quote_count
- **YouTube** - Likes, Comments, Views (impressions), favoriteCount
- **Instagram** - Likes, Comments, Impressions (Meta Graph API)
- **Reddit** - Likes (ups), Comments, Score, downs

### Partially Implemented
- **LinkedIn** - Adapter exists but returns stub zeros. UGC API complexity deferred. Dashboard should show placeholder/coming soon state.

### New Integrations Required
- **Google Analytics 4 (GA4)** for matthewkruczek.ai
  - Property ID: `261358185`
  - Metrics: activeUsers, sessions, screenPageViews, averageSessionDuration, bounceRate, newUsers
  - Dimensions: pagePath, sessionDefaultChannelGroup, date
  - Auth: JWT service account (`jarvis-analytics@ultimate-vigil-486216-g9.iam.gserviceaccount.com`)
  - Credentials: `secrets/google-analytics-sa.json`

- **Google Search Console** for matthewkruczek.ai
  - Metrics: impressions, clicks, CTR, position
  - Dimensions: query, page, date, country, device
  - Same service account auth as GA4

- **Substack** (matthewkruczek.substack.com)
  - No official API. Use undocumented `/api/v1/archive` endpoint for post listing.
  - RSS feed at `https://matthewkruczek.substack.com/feed` for post titles/dates
  - Subscriber count, open rate, click rate not available via API - consider manual input or scraping

## Existing Infrastructure

### Backend
- `IEngagementAggregator` interface with `FetchLatestAsync`, `GetPerformanceAsync`, `GetTopContentAsync`
- `EngagementAggregator` implementation fetches from platform adapters, stores `EngagementSnapshot` entities
- `EngagementSnapshot` entity: Likes, Comments, Shares, Impressions, Clicks, FetchedAt
- `ContentPlatformStatus` links content to platform posts with engagement snapshots
- Existing endpoints: `GET /api/analytics/content/{id}`, `GET /api/analytics/top`, `POST /api/analytics/content/{id}/refresh`
- `SocialEngagementService` tracks automation stats (active tasks, success rate, actions)

### Frontend
- Angular models exist: `EngagementSnapshot`, `ContentPerformanceReport`, `TopPerformingContent`
- `AnalyticsService` exists with `getContentReport`, `getTopContent`, `refreshAnalytics`
- No dashboard component exists yet

## Dashboard Requirements

### Approved HTML Mockup
Reference: `publish/analytics-dashboard.html` (approved by stakeholder)

### 1. KPI Metric Cards (top row)
- **Total Engagement** - sum of likes+comments+shares across all social platforms
- **Total Impressions** - sum across all platforms + website page views
- **Engagement Rate** - (total engagement / total impressions) * 100
- **Content Published** - count of published content in period
- **Cost Per Engagement** - total LLM generation cost / total engagement
- Each KPI shows % change vs previous period

### 2. Engagement Over Time (line chart, Chart.js)
- Multi-line chart: daily engagement over last 30 days
- One line per platform (Twitter=blue, LinkedIn=blue-700, YouTube=red, Instagram=pink, Reddit=orange)
- Total engagement line (purple/violet)
- Configurable date range (7d, 14d, 30d, 90d)

### 3. Platform Breakdown (horizontal stacked bar chart)
- Each platform as a row
- Stacked bars: Likes, Comments, Shares
- Platform brand colors on labels

### 4. Top Performing Content (data table)
- Ranked by total engagement
- Columns: Title, Platform, Total Engagement, Impressions, Engagement Rate, Published Date
- Platform indicated by colored dot + name

### 5. Platform Health Cards
- One card per platform (Twitter/X, LinkedIn, YouTube, Instagram, Reddit, Website, Substack)
- Stats: follower/subscriber count, posts this month, avg engagement per post, best performing post
- Platform brand colors (accent bar on top)

### 6. Website Analytics Section (new)
- GA4 metrics: Users, Sessions, Page Views, Avg Session Duration, Bounce Rate
- Top pages by views
- Traffic sources breakdown
- Search Console: Top search queries, clicks, impressions, CTR

### 7. Substack Section (new)
- Post listing from archive/RSS
- Whatever metrics are scrapeable
- Graceful fallback if API unavailable

### 8. Engagement Automation Stats
- Active tasks count
- Total actions performed
- Success rate %
- Actions this week

## New API Endpoints Needed

- `GET /api/analytics/dashboard?period=30d` - Aggregated KPIs with period comparison
- `GET /api/analytics/engagement-timeline?days=30` - Daily engagement by platform
- `GET /api/analytics/platform-summary` - Per-platform health (followers, post count, avg engagement, top post)
- `GET /api/analytics/website` - GA4 + Search Console metrics
- `GET /api/analytics/substack` - Substack post/subscriber data

## Technical Considerations

- GA4/GSC integration uses JWT service account auth (RS256 signing, token exchange)
- Consider caching dashboard data (5-15 min TTL) to avoid hitting rate limits
- EngagementSnapshots already provide historical data for time-series
- Chart.js needs to be added to Angular project (or use ng2-charts wrapper)
- Dark theme must match existing PrimeNG dark theme (surface-950 background, purple/violet accent)
