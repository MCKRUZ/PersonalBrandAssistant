# Analytics Dashboard - Interview Transcript

## Q1: Chart library choice
**Q:** The existing EngagementChartComponent uses PrimeNG's UIChart (Chart.js wrapper). The HTML mockup uses raw Chart.js. Should we stick with PrimeNG UIChart for consistency, switch to ng2-charts, or use raw Chart.js?

**A:** PrimeNG UIChart - stay consistent with the existing codebase.

## Q2: Date range periods
**Q:** What periods should be available and what should be the default?

**A:** Both preset buttons AND custom date picker. Presets: **1D, 7D, 14D, 30D, 90D** (default: 30D). The user specifically requested 1-day view be included.

## Q3: Website analytics placement
**Q:** How prominent should GA4 + Search Console data be? Same page, tabs, or unified?

**A:** Unified KPIs + separate detail sections. KPI cards at the top include website metrics alongside social metrics. Expandable detail sections below for deeper website analytics (top pages, search queries, traffic sources).

## Q4: Error handling for failed platform data
**Q:** When engagement data fails to load for a platform, how should the dashboard handle it?

**A:** Show last cached data with a staleness indicator (e.g., "Last updated 2 hours ago"). Never show empty/broken cards if cached data is available.

## Q5: GA4 credentials in Docker
**Q:** How should PBA access the google-analytics-sa.json file in Docker?

**A:** Volume mount. Mount `secrets/google-analytics-sa.json` into the container, reference the path via configuration.

## Q6: Dashboard auto-refresh
**Q:** Should the dashboard auto-refresh on an interval or only on user action?

**A:** Manual refresh only. User clicks Refresh or changes date range. Simpler, less API load.

## Q7: Substack integration scope
**Q:** Given Substack's API limitations, what's the minimum viable data?

**A:** RSS posts only (safe approach). Post list with titles, dates, URLs. No engagement metrics or subscriber counts. Simple and reliable. Can expand later if needed.
