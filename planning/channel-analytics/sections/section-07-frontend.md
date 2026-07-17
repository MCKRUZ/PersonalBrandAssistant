# Section 07 — Frontend Analytics Shell + Per-Source Components

## Goal

Refactor the single Website-only analytics page into a **tabbed shell** (Overview | Website | YouTube | Instagram | TikTok) with per-source child components. Add cross-platform Overview, a generic per-channel component (KPIs + trend charts + recent-posts table + connect/reconnect affordance), a live YouTube deep-analytics panel, and the service/model additions that back them. Signals only, no NgRx.

This is the last section in the build order (plan §17 step 7) and consumes the read API delivered by **section-06-read-api**.

## Dependency (reference only — do not re-implement)

**section-06-read-api** exposes these endpoints under `/api/analytics` (already wired in `Program.cs`):

```
GET /api/analytics/overview?period=            -> OverviewDto
GET /api/analytics/channel/{platform}?period=  -> ChannelAnalyticsDto   (platform = youtube|instagram|tiktok)
GET /api/analytics/youtube/deep?period=        -> YouTubeDeepAnalyticsDto (LIVE call; may fail -> degrade gracefully)
```

The existing `/api/analytics/website` and `/api/analytics/health` are unchanged and stay in use.

Connect/reconnect uses the existing OAuth flow from **section-03**: navigate the browser to `/api/auth/{platform}/authorize?purpose=analytics` (a full-page redirect, not an XHR — the backend returns a redirect to the provider's consent screen). `{platform}` here is the lowercase route token (`youtube`, `instagram`, `tiktok`).

### Backend DTO shapes to mirror in TypeScript

From plan §9.1 (C# records — mirror as TS interfaces; JSON is camelCase via the app's default serializer):

```
MetricPoint(DateOnly Date, long Value)
TrendSeries(string Metric, IReadOnlyList<MetricPoint> Points)
KpiCard(string Key, string Label, long Value, double? DeltaPct, double? Rate)   // Rate = engagement rate fraction, null when N/A
RecentPost(string VideoId, string? Title, IReadOnlyDictionary<string,long> Metrics)
ChannelAnalyticsDto(Platform Platform, ConnectionStatus Status, DateOnly? AsOf,
                    IReadOnlyList<KpiCard> Kpis, IReadOnlyList<TrendSeries> Trends, IReadOnlyList<RecentPost> RecentPosts)
OverviewChannel(Platform Platform, ConnectionStatus Status, long? Followers, IReadOnlyList<MetricPoint> FollowerSparkline)
OverviewDto(long TotalAudience, IReadOnlyList<KpiCard> CombinedKpis, IReadOnlyList<OverviewChannel> Channels)

ConnectionStatus enum = Connected | ReconnectRequired | NotConnected
```

`YouTubeDeepAnalyticsDto` (plan §9.2) carries labeled series/breakdowns over the selected range: day series (views, estimated minutes watched, average view duration, subscribers gained/lost, likes, comments, shares) plus traffic-source and demographics breakdowns. Mirror it loosely — the component only needs to render whatever labeled series/breakdown arrays it returns. Keep the TS interface permissive (arrays of `{ label, points }` and `{ label, value }`) so it survives backend field tweaks.

> **Serialization note:** `DateOnly` serializes as an ISO date string (`"2026-07-01"`); `Platform` serializes per the API's existing enum convention — verify against a live/mocked `/api/analytics/channel/youtube` response whether it is the string name (`"YouTube"`) or the numeric value, and match the existing Website models' approach. Do NOT assume; check one real response shape from section-06's tests or a running instance before finalizing the enum type.

## Existing code this section builds on

- `src/PersonalBrandAssistant.Web/src/app/features/analytics/analytics.component.ts` — the **current single component** holding ALL Website markup, styles, signals (`period`, `loading`, `data`, `health`), and helpers (`num`, `duration`, `barWidth`, `posClass`, `trafficData`, `kpis`, etc.). The obsidian-theme CSS tokens (`--surface-card`, `--brand-primary`, `--font-mono`, etc.) and the KPI-grid / panel / data-table / skeleton patterns here are the visual vocabulary to reuse.
- `src/PersonalBrandAssistant.Web/src/app/features/analytics/services/analytics.service.ts` — `getWebsite(period)` + `getHealth()`. Add new methods here.
- `src/PersonalBrandAssistant.Web/src/app/features/analytics/models/analytics.model.ts` — Website interfaces + `AnalyticsPeriod = '7d' | '30d' | '90d'`. Reuse `AnalyticsPeriod`.
- `src/PersonalBrandAssistant.Web/src/app/app.routes.ts:15` — the lazy route `{ path: 'analytics', loadComponent: () => import('./features/analytics/analytics.component')... }`. **Keep this route.** The shell stays at `analytics.component.ts` so the route import is unchanged.

### Environment facts

- Angular `^19.2.0`, standalone components, signals.
- PrimeNG `^20.4.0`. **Tabs are the new `p-tabs` API** (`Tabs`, `TabList`, `Tab`, `TabPanels`, `TabPanel` — NOT the deprecated `TabView`). Verify the exact import path/module names against PrimeNG 20 docs before writing the shell template; if `p-tabs` differs in your installed build, use whatever the installed PrimeNG 20 ships. `ChartModule` (`p-chart`), `TableModule`, `SelectButtonModule`, `TooltipModule` are already used in this feature.
- Tests: `ng test --watch=false --browsers=ChromeHeadless` (run from `src/PersonalBrandAssistant.Web`). Jasmine/Karma + `HttpTestingController`, `afterEach(() => httpMock.verify())`.

## Target file structure (plan §12.1)

```
src/PersonalBrandAssistant.Web/src/app/features/analytics/
  analytics.component.ts                       # SHELL: p-tabs (Overview | Website | YouTube | Instagram | TikTok)
  overview/overview.component.ts               # cross-platform: total audience (approximate) + combined KPIs + per-platform sparklines
  website/website-analytics.component.ts       # existing Website markup MOVED here VERBATIM (behavior unchanged)
  channel/channel-analytics.component.ts       # generic per-platform view, input() platform
  models/channel-analytics.model.ts            # TS mirrors of the new DTOs + ConnectionStatus + deep-analytics
  services/analytics.service.ts                # add getOverview, getChannel, getYouTubeDeep
```

You may keep the YouTube deep panel inline in `channel-analytics.component` (gated by `platform === 'youtube'`) rather than a separate component — either is acceptable; the split is a readability choice. The spec below tests behavior, not the file split.

## Implementation steps

### Step 1 — Extract Website into its own component (regression-preserving)

Create `website/website-analytics.component.ts` and move the **entire current** `AnalyticsComponent` template, styles, signals, computed members, and helper methods into it **verbatim** (selector `app-website-analytics`). This component keeps calling `getWebsite(period)` + `getHealth()` and owns its own `period` SelectButton exactly as today. Nothing about its data shape or behavior changes — this is a pure move so the Website tab regression test passes.

### Step 2 — Analytics models

Create `models/channel-analytics.model.ts` with TS interfaces mirroring the §9.1 DTOs and `ConnectionStatus`. Stubs:

```ts
export type ConnectionStatus = 'Connected' | 'ReconnectRequired' | 'NotConnected';
export type ChannelPlatform = 'youtube' | 'instagram' | 'tiktok';

export interface MetricPoint { date: string; value: number; }
export interface TrendSeries { metric: string; points: MetricPoint[]; }
export interface KpiCard { key: string; label: string; value: number; deltaPct: number | null; rate: number | null; }
export interface RecentPost { videoId: string; title: string | null; metrics: Record<string, number>; }

export interface ChannelAnalytics {
  platform: string;               // match backend enum serialization — see serialization note
  status: ConnectionStatus;
  asOf: string | null;
  kpis: KpiCard[];
  trends: TrendSeries[];
  recentPosts: RecentPost[];
}

export interface OverviewChannel {
  platform: string;
  status: ConnectionStatus;
  followers: number | null;
  followerSparkline: MetricPoint[];
}

export interface AnalyticsOverview {
  totalAudience: number;          // labeled APPROXIMATE in the UI
  combinedKpis: KpiCard[];
  channels: OverviewChannel[];
}

export interface LabeledSeries { label: string; points: MetricPoint[]; }
export interface LabeledBreakdown { label: string; value: number; }
export interface YouTubeDeepAnalytics {
  series: LabeledSeries[];        // watch time, avg view duration, subs gained/lost, etc.
  trafficSources: LabeledBreakdown[];
  demographics: LabeledBreakdown[];
}
```

Keep field names aligned to the actual JSON keys; adjust after verifying one real response.

### Step 3 — Service methods

Extend `services/analytics.service.ts` (existing `getWebsite`/`getHealth` untouched):

```ts
getOverview(period: AnalyticsPeriod): Observable<AnalyticsOverview>            // GET /api/analytics/overview?period=
getChannel(platform: ChannelPlatform, period: AnalyticsPeriod): Observable<ChannelAnalytics>  // GET /api/analytics/channel/{platform}?period=
getYouTubeDeep(period: AnalyticsPeriod): Observable<YouTubeDeepAnalytics>     // GET /api/analytics/youtube/deep?period=
```

Use `HttpParams().set('period', period)` exactly like `getWebsite`. `getChannel` interpolates the platform token into the path: `${this.baseUrl}/channel/${platform}`.

### Step 4 — Shell component (`analytics.component.ts`)

Replace the current file's content with a thin shell:

- Selector `app-analytics` (unchanged — route import stays valid).
- PrimeNG `p-tabs` with five tabs: **Overview, Website, YouTube, Instagram, TikTok**.
- Each tab hosts its child component: `<app-overview>`, `<app-website-analytics>`, and three `<app-channel-analytics [platform]="...">`.
- **Lazy-load each tab's data on activation** (plan §12.2): a signal `activeTab` drives which child is instantiated; use PrimeNG tabs' active-value binding and `@if (activeTab() === 'youtube')` guards so a child only mounts (and only fires its HTTP call) when its tab is first opened. Website tab may mount eagerly to preserve current default behavior, but tests expect no channel HTTP calls before their tab is active.
- No data fetching in the shell itself — children own their loads.

### Step 5 — Overview component (`overview/overview.component.ts`)

- Selector `app-overview`. On init, call `getOverview(period)`.
- Signals: `period` (default `'30d'`), `loading`, `data: AnalyticsOverview | null`.
- Render **Total Audience** prominently, **explicitly labeled approximate** (plan §12.1 / §16 — YouTube subscriber counts are rounded). The word "approximate" (or "approx."/"≈") must be present in the rendered DOM near the total — the spec asserts this.
- Render `combinedKpis` as a KPI row (reuse the `.kpi-grid` / `.kpi-card` visual pattern).
- Render one **per-platform follower sparkline** per `channels[]` entry — a small `p-chart type="line"` built from `followerSparkline`. Show connection status per channel.

### Step 6 — Generic channel component (`channel/channel-analytics.component.ts`)

- Selector `app-channel-analytics`, input `platform` (`input<ChannelPlatform>()` signal input).
- Signals: `period`, `loading`, `data: ChannelAnalytics | null`, plus a `status` derived from `data()?.status`.
- On init (and on period change), call `getChannel(platform(), period())`.
- Render, when `status === 'Connected'` and data present:
  - **KPI row** from `kpis[]` (value + `deltaPct` badge + optional engagement `rate` shown as a percentage when non-null).
  - **Trend charts** from `trends[]` — one `p-chart type="line"` per `TrendSeries`.
  - **Recent-posts table** from `recentPosts[]` (reuse `.data-table` styling): columns = title + the known metric keys present in `metrics`.
- **Connect/reconnect affordance** (plan §12.1):
  - `status === 'NotConnected'` -> show a **"Connect {platform}"** button/link.
  - `status === 'ReconnectRequired'` -> show a **"Reconnect"** button/link.
  - Both link to `/api/auth/{platform}/authorize?purpose=analytics` as a full-page navigation (`window.location.href` or an anchor `href`, NOT an Angular router link — it must leave the SPA to hit the backend redirect). The rendered text/label must contain "Connect" or "Reconnect" respectively.
- **YouTube deep panel:** when `platform() === 'youtube'` and status Connected, additionally call `getYouTubeDeep(period)` and render a deep-analytics panel (watch time, average view duration, traffic sources, demographics). **Degrade gracefully:** on error, hide the panel (or show a subtle "deep analytics unavailable" note) and never break the rest of the tab. Wire `getYouTubeDeep().subscribe({ error: ... })` to a `deepFailed` signal.

### Step 7 — Verify build + tests

From `src/PersonalBrandAssistant.Web`: `ng test --watch=false --browsers=ChromeHeadless` and `ng build`.

## Tests (write FIRST — plan §14 / TDD Step 7)

Frontend = Jasmine/Karma + `HttpTestingController`, always `afterEach(() => httpMock.verify())`. Follow the existing spec patterns in `services/analytics.service.spec.ts` and `analytics.component.spec.ts`. Stubs:

### Service (`services/analytics.service.spec.ts` — extend)

```
# getOverview() GETs /api/analytics/overview?period=  (assert method GET + URL, flush stub OverviewDto)
# getChannel('youtube') GETs /api/analytics/channel/youtube?period=
# getYouTubeDeep() GETs /api/analytics/youtube/deep?period=
```

### Overview component (`overview/overview.component.spec.ts` — new)

```
# renders total audience LABELED APPROXIMATE + per-platform sparklines from mocked OverviewDto
#   (flush /api/analytics/overview?period=30d; assert DOM contains the total AND the word "approximate"/"approx"/"≈";
#    assert one chart / sparkline element per channel)
```

### Channel component (`channel/channel-analytics.component.spec.ts` — new)

```
# renders KPI row + trend charts + recent-posts table from a Connected ChannelAnalytics mock
# shows a "Connect" affordance when status = NotConnected (href -> /api/auth/{platform}/authorize?purpose=analytics)
# shows a "Reconnect" affordance when status = ReconnectRequired
# youtube tab renders deep-analytics panel from getYouTubeDeep(); panel hidden/degraded when that call errors
#   (flush /api/analytics/channel/youtube then error the /api/analytics/youtube/deep request; assert rest of tab still renders)
```

Note: when `[platform]="youtube"`, the component fires TWO requests (channel + deep). Match/flush both; in the error case, use `req.error(new ProgressEvent('error'))` on the deep request and assert the KPI/trend content still shows while the deep panel is absent.

### Shell + Website regression (`analytics.component.spec.ts` — update)

```
# website tab renders unchanged (regression) — the moved WebsiteAnalyticsComponent still loads
#   /api/analytics/website?period=30d + /api/analytics/health and shows the same content as before
# analytics shell switches source tabs and lazy-loads each source's data on activation
#   (assert: before activating the YouTube tab, NO /api/analytics/channel/youtube request is issued;
#    after activating it, exactly one is)
```

Preserve the two existing Website assertions (active-users render + health-down banner) against whichever component now owns that markup, so the regression is real.

## Acceptance checklist

- [ ] Website tab behavior byte-for-byte unchanged (existing two Website specs still green against the moved component).
- [ ] Shell shows 5 tabs; channel data loads only on tab activation (no eager channel HTTP calls).
- [ ] Overview shows Total Audience labeled **approximate** + per-platform sparklines.
- [ ] Generic channel component renders KPIs + trend charts + recent-posts table when Connected.
- [ ] Connect (`NotConnected`) and Reconnect (`ReconnectRequired`) affordances link to `/api/auth/{platform}/authorize?purpose=analytics` via full-page navigation.
- [ ] YouTube tab renders the live deep panel on success and degrades gracefully on error.
- [ ] Signals only, no NgRx; standalone components; lazy `/analytics` route unchanged.
- [ ] `ng test --watch=false --browsers=ChromeHeadless` and `ng build` both green; 80%+ coverage on new code.
