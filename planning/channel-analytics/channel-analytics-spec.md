# Channel Analytics — Spec Seed

Fully flesh out the PBA analytics page with **YouTube, Instagram, and TikTok** channel analytics,
alongside the existing Website (GA4 + Google Search Console) analytics.

## Scope decisions (locked by the user — do not re-litigate)

- **Depth: "everything possible, all platforms."** Pull every metric each platform's API exposes,
  accepting Meta/TikTok app-review delays.
- **Trends: yes.** Build a PBA-side **daily snapshot store + scheduled poller** so we accumulate
  history and render trend charts. The platform APIs will not give deep history — we accumulate our own.
- **Console runbook required.** The plan must include a step-by-step console setup guide for the user
  (Meta Business, TikTok Developer, Google Cloud). This is the critical path and runs in parallel with
  the build.

## Existing code map (verified by exploration — reuse, do not rebuild)

### Analytics feature (backend)
- `src/PBA.Application/Features/Analytics/` — `Dtos/WebsiteAnalyticsDtos.cs`,
  `Queries/GetWebsiteAnalytics.cs`, `Queries/GetAnalyticsHealth.cs`.
- Interfaces: `src/PBA.Application/Common/Interfaces/` — `IGoogleAnalyticsService.cs`,
  `IGa4Client.cs`, `ISearchConsoleClient.cs`.
- Endpoints: `src/PBA.Api/Endpoints/AnalyticsEndpoints.cs` (group `/api/analytics`,
  routes `GET /website` + `GET /health`), registered in `src/PBA.Api/Program.cs` line 72
  (`app.MapAnalyticsEndpoints()`). MediatR queries return `Result<T>`; endpoints use `result.ToApiResult()`.

### Analytics services (infrastructure)
- `src/PBA.Infrastructure/Services/Analytics/` — `GoogleAnalyticsService.cs`, `Ga4Client.cs`,
  `SearchConsoleClient.cs`.
- Options: `src/PBA.Infrastructure/Configuration/GoogleAnalyticsOptions.cs` (SectionName `GoogleAnalytics`).
- DI in `src/PBA.Infrastructure/DependencyInjection.cs` lines 120-124.
- appsettings `src/PBA.Api/appsettings.json` lines 42-46.
- GA4/GSC use a static **service-account** json (`secrets/google-analytics-sa.json`) — different from the
  per-user OAuth the social channels need.

### Existing OAuth + encrypted token subsystem (built for publishing — REUSE for analytics tokens)
- Entity `src/PBA.Domain/Entities/PlatformCredential.cs` (EncryptedAccessToken/RefreshToken, expiries,
  Scopes, IsActive, unique index on Platform filtered `IsActive=true`).
- EF config `src/PBA.Infrastructure/Data/Configurations/PlatformCredentialConfiguration.cs`.
- Encryption: `src/PBA.Infrastructure/Security/TokenEncryptor.cs` (`ITokenEncryptor`, AES-GCM 256,
  key from `EncryptionOptions`).
- OAuth flow: `src/PBA.Infrastructure/Security/OAuthService.cs` (`IOAuthService`) +
  `src/PBA.Api/Endpoints/OAuthEndpoints.cs` (group `/api/auth`, routes `/{platform}/authorize|callback|status`,
  `DELETE /{platform}`) — **currently gated to LinkedIn + Twitter only** via `OAuthPlatforms` hashset
  (OAuthEndpoints.cs line 10).
- Platform status: `src/PBA.Api/Endpoints/PlatformEndpoints.cs` (`/api/platforms`).

### Platform enum
- `src/PBA.Domain/Enums/Platform.cs` = Blog, Substack, LinkedIn, Twitter, Reddit, YouTube=5, Medium.
  **YouTube already in enum** (no connector). **Instagram + TikTok not in enum yet — must add.**

### Frontend
- `src/PersonalBrandAssistant.Web/src/app/features/analytics/` — `analytics.component.ts`
  (single standalone component, inline template/styles, **signals-based, NO NgRx**, PrimeNG
  Table/Chart/SelectButton/Tooltip), `models/analytics.model.ts`, `services/analytics.service.ts`
  (baseUrl `/api/analytics`, `getWebsite(period)` + `getHealth()`).
- Lazy route in `app.routes.ts` line 15. Nav in `shell/sidebar/sidebar.component.ts`.
- Page is **single-source** (hardcoded Website) — no source tabbing exists; must add a source switcher
  (recommend PrimeNG tabs) + per-platform section components + trend line charts.

### Tests
- Backend: `tests/PBA.Api.Tests/Endpoints/AnalyticsEndpointsTests.cs`,
  `tests/PBA.Infrastructure.Tests/Services/Analytics/*`.
- Frontend: `analytics.component.spec.ts` + `analytics.service.spec.ts`.
- NOTE: a legacy test tree `tests/PersonalBrandAssistant.*.Tests/` has an old `DashboardAggregator`
  multi-source design with **no matching src** — treat as dead/superseded, confirm and ignore.

## Feasibility / API notes (verified via API knowledge + cross-project nexus)

- User's `ai-video-producer` project already has OAuth apps + vaulted tokens
  (`TIKTOK_CLIENT_KEY/SECRET`, `IG_ACCESS_TOKEN`, `youtube_client_secret.json` in `~/.certs/`) but scoped
  for **publishing** (video.publish etc.), **not analytics** — analytics scopes differ and need
  re-consent/review.
- **YouTube:** Data API v3 (public: subscribers, total views, per-video views/likes/comments — API key only)
  + Analytics API v2 (owner: watch time, traffic sources, subscriber growth, demographics — OAuth
  `yt-analytics.readonly`). Richest + easiest.
- **Instagram:** Graph API Insights (follower count, reach, impressions, profile views, per-media
  likes/comments/saves/reach). Needs `instagram_manage_insights`; app review for production.
- **TikTok:** Display API `user.info.stats` (follower/following/likes/video counts) + `video.list`
  (per-video view/like/comment/share). Deep analytics-tab metrics **not exposed** by the public API —
  be honest about this ceiling in the plan.

## Architecture guidance for the plan

- Per platform: `I{Platform}AnalyticsService` interface (Application) + client/service + `{Platform}Options`
  (Infrastructure) + `Features/ChannelAnalytics/{Dtos,Queries}` + endpoint group
  `MapChannelAnalyticsEndpoints` + Program.cs wiring + DI registration.
- Auth: draw per-user OAuth tokens from the existing `PlatformCredential` + `TokenEncryptor` store
  (NOT the GA4 service-account file). Extend `OAuthService`/`OAuthEndpoints` to the 3 platforms with correct
  provider config + scopes.
- Snapshot store: new `ChannelMetricSnapshot` entity + EF config + migration; daily scheduled poller
  reusing the existing radar/digest daily scheduling mechanism (research how `DigestService` scheduling works).
- Code must be fully buildable + testable against **mocked** API responses now, lighting up per-platform as
  tokens/approvals land (same gated pattern as the blog pipeline).

## Stack / standards

.NET 10, C#, MediatR/CQRS, `Result<T>` (PBA.Domain.Common), FluentValidation, immutable records;
Angular 19 standalone + signals + PrimeNG. TDD, 80% coverage minimum. No em-dashes; follow global
coding-style rules. All LLM calls (if any) route through `ISidecarClient`, never direct.
