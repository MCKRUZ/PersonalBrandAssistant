# Analytics Dashboard — Usage Guide

## Overview

The analytics dashboard aggregates data from social platforms (via EngagementSnapshots), Google Analytics (GA4 + Search Console), and Substack (RSS) into a unified view.

## API Endpoints

All endpoints require `X-Api-Key` header.

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/analytics/dashboard?period=7d` | GET | KPI summary with current vs previous period |
| `/api/analytics/engagement-timeline?period=7d` | GET | Daily engagement totals by platform |
| `/api/analytics/platform-summary?period=7d` | GET | Per-platform health: post count, avg engagement, top post |
| `/api/analytics/website?period=7d` | GET | GA4 overview, top pages, traffic sources, search queries |
| `/api/analytics/substack` | GET | Recent Substack posts from RSS feed |
| `/api/analytics/health` | GET | External service connectivity status |

**Period options:** `1d`, `7d`, `14d`, `30d`, `90d`, or `?from=YYYY-MM-DD&to=YYYY-MM-DD`
**Cache bypass:** Append `?refresh=true`

## Frontend Route

Navigate to `/analytics` in the Angular app.

## Dashboard Components

1. **KPI Cards** — 6 metrics with trend indicators (engagement, impressions, rate, content published, cost/engagement, website users)
2. **Engagement Timeline Chart** — Stacked area chart by platform over time
3. **Platform Breakdown Chart** — Doughnut chart of engagement by platform
4. **Platform Health Cards** — 5-column grid with brand colors, follower/post/engagement stats
5. **Top Content Table** — Sortable table with engagement rate color coding
6. **Website Analytics** — GA4 overview metrics, top pages, traffic sources, search queries
7. **Substack Section** — RSS post listing with links and summaries

## Configuration

### Google Analytics (appsettings.json)
```json
{
  "GoogleAnalytics": {
    "CredentialsPath": "secrets/google-analytics-sa.json",
    "PropertyId": "261358185",
    "SiteUrl": "https://matthewkruczek.ai/"
  }
}
```

### Substack (appsettings.json)
```json
{
  "Substack": {
    "FeedUrl": "https://matthewkruczek.substack.com/feed"
  }
}
```

## Caching & Resilience

- `HybridCache` with 5-minute TTL on all aggregated responses
- Polly retry policies on GA4 and Substack HTTP calls
- Partial failure tolerant: GA4 failure returns zeros for website metrics, social data still populates

## Running Tests

```bash
# Backend integration tests (requires Docker for Testcontainers)
dotnet test tests/PersonalBrandAssistant.Infrastructure.Tests --filter "FullyQualifiedName~Analytics"

# Frontend tests
cd src/PersonalBrandAssistant.Web && npx ng test --watch=false --browsers=ChromeHeadless
```
