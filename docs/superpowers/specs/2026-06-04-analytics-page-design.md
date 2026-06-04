# Analytics Page — Design Spec

**Date:** 2026-06-04
**Branch:** v2-rebuild
**Status:** Draft for review
**Scope:** Analytics for four channels — **website (matthewkruczek.ai), Medium, LinkedIn, Substack**

---

## 1. Goal

Give Matt a single analytics page that surfaces **as much real, automated data as each platform actually allows** — no manual CSV uploads, no manual number entry. The page must be honest: where a platform exposes nothing, it says so rather than showing fake or zero numbers.

## 2. The hard constraint (verified June 2026)

There is a severe asymmetry across the four platforms. This drives the entire design.

| Platform | Engagement metrics via automation? | Reachable today | Method |
|---|---|---|---|
| **Website (GA4 + GSC)** | ✅ Full, deep, live | Users, sessions, pageviews, engagement rate, avg engagement time, bounce, top pages, traffic sources, geography, device, search clicks/impressions/CTR/position, top queries | GA4 Data API + Search Console API via existing service account. Self-serve, no liability. |
| **LinkedIn (personal)** | ❌ Genuinely impossible | Post inventory only (what PBA published: post, date, URL) | Personal post analytics require the partner-vetted Community Management API (registered org + verified Company Page). `w_member_social` is publish-only. No RSS. |
| **Medium** | ⚠️ Scrape-only | Post inventory via RSS (last 10). Views/reads/claps only by scraping the stats page with a stored session cookie. | RSS for inventory; authenticated `?format=json` scrape for stats (fragile, ToS-gray). |
| **Substack** | ⚠️ Scrape-only | Post inventory via RSS. Subscribers/opens/clicks only by replaying a stored dashboard session cookie. | RSS for inventory; authenticated dashboard scrape for stats (fragile, ToS-gray). |

**Conclusion:** The website is the substantive analytics surface. The other three are *inventory + best-effort scraped engagement*. LinkedIn engagement is not obtainable at all and the UI will say so plainly.

## 3. Codebase reality (verified)

- Solution `PBA.slnx` contains only `PBA.*` projects. The `PersonalBrandAssistant.Infrastructure.Tests` analytics tests are **orphaned V1 code** (reference `EngagementSnapshot`, `IApplicationDbContext`, `PersonalBrandAssistant.*` namespaces that do not exist in v2). They are a **design blueprint only** — reusable DTO/interface shapes and test scenarios, but must be re-authored against `PBA.*`.
- v2 engagement model: `ContentPlatformPublish` holds **latest** `Likes/Comments/Shares/Views/MetricsRefreshedAt` inline. No time-series exists.
- `IPlatformConnector` has **no metrics method** (only `PublishAsync`, `ValidateCredentialsAsync`, `GetCapabilities`). Reading engagement back is net-new.
- Hangfire is wired (`HangfireContentScheduler`, `PublishRetryHandler`, `Program.cs`) — established pattern for scheduled refresh/scrape jobs.
- Frontend: Angular 19 standalone, PrimeNG 20, `chart.js@4.5.1`, NgRx signals, feature-folder routing. **No new charting dependency required.**
- The analytics route/component already exist as a stub (`features/analytics/analytics.component.ts`, registered in `app.routes.ts`).
- GA4 property `261358185`, site `https://matthewkruczek.ai/`, service account at `secrets/google-analytics-sa.json` (gitignored).

## 4. Architecture

Clean Architecture across `PBA.*`, following existing v2 patterns (Minimal API endpoints, service injection, Options pattern, Result<T>).

### 4.1 Data model additions

- **Reuse** `ContentPlatformPublish` for latest per-post engagement (LinkedIn/Medium inventory + scraped values land here).
- **Add `EngagementSnapshot`** (new entity + table) for time-series, because Substack subscriber growth and Medium cumulative views have **no queryable history** — we must snapshot them periodically to chart trends:
  - `Id`, `Platform`, `ContentPlatformPublishId?` (null = platform-level snapshot e.g. follower/subscriber count), `Likes?`, `Comments?`, `Shares?`, `Impressions?`, `Views?`, `Followers?`, `Subscribers?`, `FetchedAt`, `Source` (Api | Rss | Scrape).
  - GA4/GSC are **not** snapshotted here — GA4 is itself the time-series source of truth; we query it by date dimension and cache results.
- **Reuse `PlatformCredential`** (already encrypted) to store Medium/Substack scrape **session cookies**.

### 4.2 Application layer (`PBA.Application/Common`)

Interfaces:
- `IGoogleAnalyticsService` — `GetOverviewAsync`, `GetTopPagesAsync`, `GetTrafficSourcesAsync`, `GetTopQueriesAsync` (returns `Result<T>`).
- `IGa4Client`, `ISearchConsoleClient` — thin wrappers over the Google SDKs (mockable seam, mirrors V1 blueprint).
- `IMediumAnalyticsService` — `GetRecentPostsAsync` (RSS), `GetPostStatsAsync` (scrape, best-effort).
- `ISubstackAnalyticsService` — `GetRecentPostsAsync` (RSS), `GetPublicationStatsAsync` (scrape, best-effort).
- `IDashboardAggregator` — `GetSummaryAsync`, `GetTimelineAsync`, `GetPlatformSummariesAsync`.
- `IDashboardCache` / `IDashboardCacheInvalidator` — cache aggregated results (GA4 quota protection).
- `IAnalyticsHealthService` — connectivity + staleness per source.

DTOs (`PBA.Application/Common/Models`) — reuse V1 shapes where they map cleanly:
- `DashboardSummary`, `DailyEngagement`, `PlatformDailyMetrics`, `PlatformSummary`
- `WebsiteOverview`, `PageViewEntry`, `TrafficSourceEntry`, `SearchQueryEntry`
- `PostInventoryEntry(Platform, Title, Url, PublishedAt, MetricsRefreshedAt?, EngagementAvailable)`
- `AnalyticsHealth(Ga4, SearchConsole, Medium, Substack, LinkedIn, plus per-source LastSyncedAt/IsStale)`

### 4.3 Infrastructure layer (`PBA.Infrastructure`)

- `Services/Analytics/GoogleAnalyticsService` — wraps `Ga4Client` + `SearchConsoleClient`. Maps GA4 metric/dimension responses → DTOs. Result<T>; RpcException/GoogleApiException → `ErrorCode.InternalError` with "GA4"/"SearchConsole" in the message.
- `Services/Analytics/Ga4Client`, `SearchConsoleClient` — service-account auth from `CredentialsPath`.
- `Services/Analytics/MediumAnalyticsService`, `SubstackAnalyticsService` — RSS parse (inventory) + delegate to scrapers (stats).
- `Analytics/Scraping/MediumStatsScraper`, `SubstackStatsScraper` — Tier 3, cookie-based, **best-effort**: success updates `ContentPlatformPublish` + writes `EngagementSnapshot`; failure logs + flips health to stale, never throws to the user.
- `Services/Analytics/DashboardAggregator` + `CachedDashboardAggregator` — aggregate over `ContentPlatformPublish`/`EngagementSnapshot` + GA4. LinkedIn forced `IsAvailable=false` in platform summaries.
- `Jobs/MetricsRefreshJob` — Hangfire recurring (e.g. every 6h): refresh GA4/GSC cache, fetch RSS inventories, run scrapers, write snapshots, update health.

Config (Options pattern, `IOptionsMonitor<T>`):
- `GoogleAnalyticsOptions { PropertyId, SiteUrl, CredentialsPath }`
- `MediumAnalyticsOptions { Username, ProfileUrl, Enabled }`
- `SubstackAnalyticsOptions { Publication, FeedUrl, Enabled }`
- Scrape cookies stored as `PlatformCredential` rows (encrypted), **not** in config.

NuGet: `Google.Analytics.Data.V1Beta`, `Google.Apis.SearchConsole.v1`. HTML scraping via `AngleSharp` (preferred) or `HtmlAgilityPack` — decide at implementation.

### 4.4 API layer (`PBA.Api/Endpoints/AnalyticsEndpoints.cs`)

Minimal API group, `X-Api-Key` auth (readonly key sufficient for GETs):
- `GET /api/analytics/dashboard?period=7d|30d|90d&from&to` — `period` wins over `from/to`; invalid period → 400.
- `GET /api/analytics/engagement-timeline?period=…`
- `GET /api/analytics/platform-summary?period=…`
- `GET /api/analytics/website?period=…` — composite `{ overview, topPages, trafficSources, searchQueries, geography, devices }`.
- `GET /api/analytics/inventory?platform=…` — post inventory (Medium/Substack/LinkedIn).
- `GET /api/analytics/health` — per-source connectivity + staleness.
- `POST /api/analytics/refresh` — write key; trigger `MetricsRefreshJob` on demand.

### 4.5 Frontend (`features/analytics`)

- `analytics.routes.ts`, `analytics.component.ts` (container), `analytics.store.ts` (NgRx signals), `analytics.service.ts` (→ `ApiService`).
- Subcomponents: `summary-cards`, `engagement-timeline-chart` (line), `website-panel` (overview cards + top-pages table + traffic-sources chart + search-queries table + geography/device), `platform-panel` (per-platform: inventory list, available metrics, **availability + staleness badges**), `post-inventory-table`.
- Period selector: presets 7d/30d/90d (custom range optional, later).
- **Honest states:** LinkedIn engagement → "Not available via API" badge; Medium/Substack scraped metrics → "Best-effort · last synced {time}" / "Stale" badge; never render fabricated numbers.

## 5. Build sequencing

1. **Tier 1 — Website (highest value, zero liability):** GA4 + GSC services, clients, options, caching, `/website` + `/health` endpoints, dashboard skeleton + summary cards + website panel. Real, automated, deep.
2. **Tier 2 — Cross-platform inventory (robust):** `EngagementSnapshot` migration, RSS services for Medium/Substack, inventory from `ContentPlatformPublish` + RSS, platform panels with honest availability, `/inventory` + `/platform-summary` + `/engagement-timeline`.
3. **Tier 3 — Best-effort scraped engagement (fragile, gated):** Medium/Substack scrapers, encrypted cookie storage via `PlatformCredential`, snapshot writes, growth/trend charts, staleness/alerting. **Requires `security-reviewer` pass** (credential handling, scraping). Behind `Enabled` config flags; degrades to Tier 2 on failure.

## 6. Testing

- Unit (xUnit): `GoogleAnalyticsService` (mock `IGa4Client`/`ISearchConsoleClient`), `DashboardAggregator` (mock DbContext via MockQueryable), RSS services (mock HTTP), scrapers (fixture HTML). Port V1 scenarios into `tests/PBA.Infrastructure.Tests`.
- Integration: `AnalyticsEndpoints` via `WebApplicationFactory<Program>` (+ Postgres/in-memory) in `tests/PBA.Api.Tests`.
- Frontend: store + service specs, component render specs (Jasmine/Karma).
- 80% coverage minimum on new code.

## 7. Security

- Service-account JSON stays gitignored; never logged.
- Scrape session cookies stored encrypted (`PlatformCredential`); never returned in API responses; never logged.
- RSS/scrape fetch targets come from fixed config (low SSRF surface) — still validate URLs against expected hosts.
- All analytics endpoints behind `X-Api-Key`. Mutating `/refresh` requires the write key.
- Tier 3 (scraping + credential handling) gets a mandatory `security-reviewer` pass before merge.

## 8. Out of scope / risks

- **LinkedIn engagement:** impossible; inventory only. Revisit only if Community Management API partnership is pursued (registered org required).
- **Twitter/X, Reddit, YouTube, Instagram:** not in this request. `DashboardAggregator`/`PlatformSummary` remain extensible for later.
- **Scraping fragility:** Medium/Substack UI changes will break scrapers; mitigated by graceful degradation, staleness badges, and the health endpoint. Not a hard dependency.
- **GA4/GSC limits:** GSC ~16-month window + long-tail truncation + 2–3 day lag; GA4 token quotas → mitigated by caching + scheduled refresh.

## 9. Open decisions

1. **Custom date range** in the period selector now, or presets-only for v1? (Recommend presets-only first.)
2. **Scrape HTML library:** AngleSharp vs HtmlAgilityPack. (Recommend AngleSharp — modern, CSS selectors.)
3. **Refresh cadence** for `MetricsRefreshJob`. (Recommend every 6h for website cache, daily for scrapers.)
