# Channel Analytics — Specification

## Goal

Extend the PBA analytics page from a single Website source (GA4 + Google Search Console) into a
multi-source **personal-brand command center** covering the user's **YouTube, Instagram, and TikTok**
channels. Pull every metric each platform's API exposes, and build a PBA-side **daily snapshot store +
scheduled poller** so the app accumulates history and renders **trend charts** the platform APIs cannot
provide directly.

The whole feature must be buildable and fully unit/integration tested against **mocked** API responses,
then "light up" per platform as OAuth tokens and (where needed) app approvals land — the same
gated-but-code-complete pattern used for the blog pipeline.

## Users & value

Single user (Matt). Value: one page to see audience size, growth, and content performance across every
owned channel, with trends over time — not just the current-state numbers each platform shows in isolation.

## Functional requirements

### FR1 — OAuth connect for YouTube, Instagram, TikTok (analytics purpose)
- Extend the existing OAuth subsystem to three new providers so the user can connect each channel with
  **analytics-read scopes** (distinct from any future publishing connection).
- Refactor `OAuthService`'s hardcoded `switch(Platform)` into an **`IOAuthProvider` map** (one class per
  provider via keyed DI); migrate LinkedIn + Twitter onto it with **behavior preserved exactly**.
- Store tokens in `PlatformCredential` with a new **`Purpose` discriminator** (`Publishing` | `Analytics`)
  and a composite unique index `(Platform, Purpose, IsActive)`. Reuse `TokenEncryptor` and the existing
  `RefreshTokenAsync`.
- Scopes per provider:
  - **YouTube**: `yt-analytics.readonly` (+ `youtube.readonly` for owner channel/video lists);
    `access_type=offline`, `prompt=consent`.
  - **Instagram** (Instagram-Login model, `graph.instagram.com`): `instagram_business_basic`,
    `instagram_business_manage_insights`.
  - **TikTok**: `user.info.basic`, `user.info.profile`, `user.info.stats`, `video.list`.
- Add the three platforms to the OAuth endpoint allow-list; connect/callback/status/disconnect all work.
- Reuse the existing ai-video-producer OAuth **apps** (client id/secret from secrets at runtime); the
  runbook covers adding PBA redirect URIs + analytics scopes to them.

### FR2 — Per-platform analytics services (read APIs)
- One `I{Platform}AnalyticsService` + client per platform, mirroring the GA4 facade/thin-client split.
- **YouTube**: Data API v3 (`channels.list`, `videos.list` — public, API key) for cumulative + per-video
  counts; Analytics API v2 (`reports.query`, OAuth) for owner deep metrics (views,
  estimatedMinutesWatched, averageViewDuration, subscribersGained/Lost, likes, comments, shares by `day`,
  plus traffic-source / geography / demographics dimensions).
- **Instagram**: Graph Insights — account metrics (`reach`, `views`, `accounts_engaged`,
  `total_interactions`, `likes`, `comments`, `saves`, `shares`, `follows_and_unfollows`,
  `profile_links_taps`, demographics, `follower_count`) + per-media metrics. Metric list must be
  **deprecation-tolerant** (skip unknown/removed metrics rather than failing the whole call; e.g.
  `impressions` is gone, replaced by `views`).
- **TikTok**: Display API `user.info.stats` (follower/following/likes/video counts) + `video.list`
  (per-video view/like/comment/share). Explicitly documented ceiling: no reach/demographics/retention via
  the creator API — TikTok trends derive entirely from our snapshot deltas.
- All services return `Result<T>`, log via `ILogger`, succeed on empty data, and fail gracefully on API
  errors. Each is fully testable with canned JSON (no live token needed).

### FR3 — Daily snapshot store + poller
- New entity **`ChannelMetricSnapshot`** capturing, per `(platform, date)`, account-level metrics and
  per-video metrics for the most recent **N** videos (N configurable, default 50). Include a `Provisional`
  flag for late-finalizing data (YouTube ~2-3d, IG ~48h lag) so trend lines can be corrected.
- New **`BackgroundService`** poller mirroring `DigestService`: hourly self-timed loop acting after a
  configured local time; per-run DI scope; **DB-uniqueness idempotency guard** (skip if a snapshot for
  `(platform, date)` already exists); try/catch-continue with structured logging.
- Before each platform call, ensure a valid token via `RefreshTokenAsync` (TikTok access token expires in
  24h; IG long-lived 60d proactive refresh; Google refresh). Persist any rotated refresh token.
- Handle failure modes: token revoked → mark connection "reconnect required" (don't silently retry);
  scope/permission removed → flag re-consent; account type changed → downgrade to public-only.
- Config via a `ChannelAnalytics` options section (`RunAtLocalTime`, `RecentVideoCount`, per-platform
  enable flags, YouTube API key).

### FR4 — Read API + queries
- New MediatR queries + endpoint group `MapChannelAnalyticsEndpoints` (`/api/analytics/...`) returning:
  - Per-platform current snapshot + trend series (from `ChannelMetricSnapshot` history) + recent-post list.
  - A cross-platform **Overview** aggregate (total audience across channels, combined engagement,
    per-platform sparkline series).
  - Per-platform connection **health/status** (connected / reconnect-required / not-connected), reusing the
    `/api/platforms` + OAuth `status` pattern.
- Follow the repo's static-class `Query`+`Handler` convention with `Result<T>` and `ToApiResult()`.

### FR5 — Frontend
- Analytics page becomes: an **Overview landing** (cross-platform totals, combined engagement,
  side-by-side per-platform sparklines) + a **tab per source** (Website, YouTube, Instagram, TikTok).
  Existing Website view becomes one tab, unchanged in behavior.
- Each per-platform tab: **KPI row** (followers/subscribers, views, engagement rate) + **trend line charts**
  from snapshot history + **top/recent-content table** with per-post metrics. PrimeNG, signals-based (no
  NgRx), matching the current analytics component patterns.
- Empty/skeleton/error states per source; a "connect / reconnect" affordance when a platform isn't
  authorized. Angular service methods + models mirroring the backend DTOs.

### FR6 — Console runbook (deliverable doc)
- Step-by-step guide for the user to action the external critical path in parallel:
  - **Google Cloud**: enable YouTube Data API v3 + YouTube Analytics API v2; add `yt-analytics.readonly`
    (+`youtube.readonly`); create/confirm an API key for public reads; **set the OAuth consent screen to
    "In Production"** (else refresh tokens die in 7 days); register PBA's Mac Mini redirect URI.
  - **Meta**: confirm IG account is Business/Creator; use "Instagram API with Instagram Login"; add
    `instagram_business_basic` + `instagram_business_manage_insights`; register PBA redirect URI; note
    Standard Access needs **no App Review** for own account.
  - **TikTok**: add `user.info.stats` + `video.list` scopes to the existing app; submit for approval;
    register PBA redirect URI.
  - Where each secret goes (DevSecrets / user-secrets / env), and how to verify a connection end-to-end.

## Non-functional requirements
- Immutable C# (records/init-only, `IReadOnlyList`), Result<T> for expected failures, FluentValidation at
  boundaries, options pattern. Small files. No em-dashes. No secrets in code.
- TDD, 80% coverage minimum on new code. Backend xUnit + `WebApplicationFactory<Program>`; frontend
  Jasmine/Karma + `HttpTestingController`. Mock only external HTTP + time.
- Single-host deployment (Mac Mini); in-memory OAuth state store is acceptable.
- Idempotent snapshot writes keyed `(platform, [video], date)`; ETags/backoff per research.

## Explicitly out of scope
- Publishing to these platforms (analytics only; publishing OAuth stays separate via the Purpose flag).
- TikTok Business/Marketing API demographics (advertiser-gated) and Research API (academic-only) — note as
  future options; do not build now.
- LinkedIn analytics (no viable API) and Medium/Substack (scrape-only) — unchanged from prior decision.
- Cross-platform historical backfill before go-live (history accrues from first poll forward).

## Key risks / unknowns to manage in the plan
- OAuth refactor must not regress LinkedIn/Twitter publishing — regression tests required.
- Instagram metric deprecations shift; keep metric lists data-driven and tolerant.
- Google consent-screen "Testing" 7-day refresh-token death is a silent failure mode — call out in runbook
  and add a health check that surfaces token expiry.
- TikTok Business API deeper fields were not field-verified; if pursued later, confirm `/business/get/` ref.
