# Channel Analytics — Usage Guide

Multi-platform channel analytics (YouTube / Instagram / TikTok) alongside the existing Website
analytics. Cumulative snapshots are polled daily; the API derives deltas/KPIs; the Angular shell
renders per-platform tabs. Built across 8 sections on branch `v2-rebuild`.

## Quick Start

### 1. Configure a platform (see the console runbook)
Full external setup — OAuth apps, scopes, redirect URIs, review — is in
`planning/channel-analytics/runbook.md`. Minimum to light up a platform:

```bash
# dev secrets (example: YouTube)
dotnet user-secrets set "Publishing:YouTube:ClientId"     "<id>"     --project src/PBA.Api
dotnet user-secrets set "Publishing:YouTube:ClientSecret" "<secret>" --project src/PBA.Api
dotnet user-secrets set "Publishing:YouTube:ApiKey"       "<key>"    --project src/PBA.Api
dotnet user-secrets set "Publishing:YouTube:RedirectUri"  "https://matthews-mac-mini.tail2800e3.ts.net/api/auth/youtube/callback" --project src/PBA.Api
```

Flip the per-platform gate (default `false`):
```
ChannelAnalytics:YouTubeEnabled = true     # or InstagramEnabled / TikTokEnabled
# prod (Docker env): ChannelAnalytics__YouTubeEnabled=true
```

### 2. Run
```bash
dotnet build && dotnet test                       # backend
cd src/PersonalBrandAssistant.Web && ng serve      # frontend
```

### 3. Connect + verify
Open the Analytics page → the platform's tab → **Connect** → grant scopes on the provider consent
screen → status flips to **Connected** → first `ChannelMetricSnapshot` lands on the next daily poll →
KPIs render, trend charts fill in over subsequent days.

## API Reference

All under `/api/analytics` (read) and `/api/auth` (OAuth). Enums serialize as string names.

| Method / Route | Returns | Notes |
|---|---|---|
| `GET /api/analytics/overview?period=` | `OverviewDto` | Total audience (approx.) + combined KPIs + per-platform sparklines |
| `GET /api/analytics/channel/{platform}?period=` | `ChannelAnalyticsDto` | `{platform}` = `youtube`\|`instagram`\|`tiktok`; KPIs + trends + recent posts + status |
| `GET /api/analytics/youtube/deep?period=` | `YouTubeDeepAnalyticsDto` | **Live** Analytics v2 call; may fail → degrade gracefully |
| `GET /api/analytics/website?period=` | `WebsiteAnalytics` | Unchanged (GA4 + Search Console) |
| `GET /api/analytics/health` | `AnalyticsHealth` | GA4 / Search Console availability |
| `GET /api/auth/{platform}/authorize?purpose=analytics` | 302 → provider consent | Full-page redirect (not XHR) |
| `GET /api/auth/{platform}/callback` | 302 → `/settings/platforms` | Exchanges code, stores encrypted token |

`period` ∈ `7d` | `30d` | `90d`. `ConnectionStatus` ∈ `Connected` | `ReconnectRequired` | `NotConnected`.

### Frontend (Angular, `features/analytics/`)
- `analytics.component.ts` — `p-tabs` shell (Overview | Website | YouTube | Instagram | TikTok);
  lazy-mounts each tab's child on first activation (no eager channel HTTP).
- `overview/`, `channel/`, `website/` — per-view components; `shared/` — `<app-period-selector>`,
  shared card styles, palette. `AnalyticsService` — `getOverview` / `getChannel` / `getYouTubeDeep`.

## Data model & polling
- `ChannelMetricSnapshot` stores **cumulative** counts per platform per day (integer-only bag).
- The daily poller writes one snapshot per connected+enabled platform; the read API computes deltas,
  KPIs, and trend series from consecutive snapshots (a single snapshot has no delta yet).
- TikTok ceiling: only cumulative counts are available from the API — reach/demographics/retention
  come from PBA's own snapshot deltas, not TikTok.

## Tests
- Backend: `dotnet test` (sections 01–06).
- Frontend: `cd src/PersonalBrandAssistant.Web && ng test --watch=false --browsers=ChromeHeadless`
  — **605 specs green**; `ng build` clean.

## Section map
| # | Section | Commit |
|---|---|---|
| 01 | OAuth provider refactor | `05fdc13` |
| 02 | Domain + persistence | `3cc1d65` |
| 03 | YouTube/Instagram/TikTok OAuth | `89ad5b9` |
| 04 | Data-fetch services | `f591cd2` |
| 05 | Snapshot poller | `4dfa6e9` |
| 06 | Read API | `38db25d` |
| 07 | Frontend shell + components | `0ad3da6` |
| 08 | Console runbook | `808e54d` |
