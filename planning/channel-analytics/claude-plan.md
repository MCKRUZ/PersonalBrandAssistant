# Channel Analytics — Implementation Plan

## 1. What we are building and why

The PBA analytics page today shows a single source: the Website (Google Analytics 4 + Search Console),
served by a static service-account credential. This plan extends it into a multi-source personal-brand
analytics dashboard covering the user's **YouTube, Instagram, and TikTok** channels, with an **Overview**
landing that aggregates across all sources.

Two things make this more than "call three more APIs":

1. **Per-user OAuth, not a service account.** Each social channel needs the channel owner's OAuth token.
   The repo already has an OAuth + encrypted-token subsystem (built for publishing); we extend it for
   analytics-scoped tokens, kept distinct from publishing tokens.
2. **The APIs don't provide history.** YouTube Analytics gives limited time-series; Instagram and TikTok
   mostly give current snapshots (TikTok gives *only* cumulative counts). To show trends, PBA must poll
   each channel **once daily** and persist its own **snapshots**, then compute trends from snapshot deltas.

The entire feature is built and tested against **mocked** API responses so it is green before any real
token exists, then activates per platform as tokens and approvals land (the gated-but-code-complete
pattern already used for the blog publishing pipeline).

## 2. Existing patterns this plan mirrors (do not reinvent)

- **Analytics feature shape:** `Features/Analytics/Queries/GetWebsiteAnalytics.cs` — a static wrapper class
  holding a `Query` record (`: IRequest<Result<TDto>>`) and a nested `Handler` with primary-constructor DI;
  MediatR auto-registered. Endpoint group `AnalyticsEndpoints.MapAnalyticsEndpoints(this IEndpointRouteBuilder)`
  using `app.MapGroup(...)`, `ISender.Send`, and `result.ToApiResult()`; wired in `Program.cs`.
- **Facade/thin-client split:** `IGoogleAnalyticsService` (orchestration, DTO mapping, `Result<T>`,
  try/catch + logging) over `IGa4Client` / `ISearchConsoleClient` (thin SDK wrappers). Each new platform
  mirrors this split so the client is trivially mockable.
- **Daily job:** `Services/Radar/DigestService.cs` — a `BackgroundService` with an hourly self-timed loop
  that acts after a configured local time, opens a DI scope per run, and guards idempotency with a DB
  uniqueness check. Registered via `AddHostedService<T>()` in `DependencyInjection.cs`.
- **OAuth + tokens:** `OAuthService` (switch-based today), `TokenEncryptor` (AES-GCM 256), `PlatformCredential`
  (encrypted tokens, expiry, scopes, `IsActive`), `OAuthEndpoints` (`/api/auth/{platform}/...` with an
  allow-list), `RefreshTokenAsync` (already exists, used by connectors).
- **EF:** POCO entity in `PBA.Domain.Entities`; optional `IEntityTypeConfiguration` auto-applied via
  `ApplyConfigurationsFromAssembly`; `DbSet` on `ApplicationDbContext` (+ `IAppDbContext`); migrations via
  `dotnet ef ... --project src/PBA.Infrastructure --startup-project src/PBA.Api --output-dir Data/Migrations`;
  ProductVersion 10.0.7; also ship an idempotent SQL script per the `scripts/migrate-*.sql` convention.
- **Frontend:** `features/analytics/analytics.component.ts` — standalone, signals-based (no NgRx), PrimeNG
  (`Table`, `Chart`, `SelectButton`, `Tooltip`); `analytics.service.ts` HTTP wrapper; lazy route in
  `app.routes.ts`.
- **Dead code:** ignore `tests/PersonalBrandAssistant.*.Tests/` (orphaned, cannot compile).

## 3. Architecture overview

```
Angular analytics page
  Overview tab ──────────────► GET /api/analytics/overview
  Website tab  ──────────────► GET /api/analytics/website        (unchanged)
  YouTube/IG/TikTok tabs ────► GET /api/analytics/channel/{platform}
  connect/reconnect  ────────► GET /api/auth/{platform}/authorize (existing OAuth flow)

Backend
  ChannelAnalyticsEndpoints ─► MediatR queries ─► read ChannelMetricSnapshot history (DB)
                                                 └─► compute trends + current + overview aggregate
  ChannelMetricPollingService (daily BackgroundService)
     └─ per connected (platform, Analytics) credential:
          ensure token (RefreshTokenAsync) ─► I{Platform}AnalyticsService ─► platform API
          ─► write ChannelMetricSnapshot rows (idempotent per (platform, date))
  OAuth: IOAuthProvider map (LinkedIn, Twitter, YouTube, Instagram, TikTok)
     └─ tokens ─► PlatformCredential (Purpose = Analytics) via TokenEncryptor
```

Two reads paths, deliberately separated:
- **Live-ish read** for the current dashboard comes from the **latest stored snapshot** (not a live API
  call on page load) — fast, resilient, and consistent with the daily-poll model. (YouTube Analytics
  time-series can additionally be queried live for a chosen range if desired, but the default is
  snapshot-backed.)
- **Trend series** come purely from stored snapshot history.

This keeps page loads independent of external API availability and rate limits.

## 4. Domain layer changes (`src/PBA.Domain`)

### 4.1 `Enums/Platform.cs`
Add `Instagram` and `TikTok` members (YouTube already exists as `=5`). Append new members to preserve
existing numeric values (do not renumber — persisted `PlatformCredential.Platform` values depend on them).

### 4.2 `Enums/CredentialPurpose.cs` (new)
```
enum CredentialPurpose { Publishing = 0, Analytics = 1 }
```

### 4.3 `Entities/PlatformCredential.cs`
Add:
```
CredentialPurpose Purpose { get; init; } = CredentialPurpose.Publishing;   // back-compat default
```
Existing rows default to `Publishing` (their current meaning). See 5.1 for the index change.

### 4.4 `Entities/ChannelMetricSnapshot.cs` (new)
**Snapshots store only CUMULATIVE, as-of-capture counts** (subscribers/followers, total views, total likes,
video counts; per-video cumulative views/likes/comments/shares). A cumulative count captured at an instant is
**always final** — there is no "provisional / correct-it-later" state. **All trends (including
subscribersGained/Lost and growth rates) are derived at read time as deltas between consecutive daily
snapshots**, one consistent mechanism across all three platforms that depends on no platform's finalization
lag. (YouTube's deep per-day analytics — watch time, traffic sources, demographics — is NOT stored here; it
is queried live from the YouTube Analytics API for the selected range on the YouTube tab, since that API
keeps its own history. See 6.2 / 9.2.)

One row per `(Platform, SnapshotDate, Scope, VideoId)`. Fields (init-only POCO, `Guid Id`):
```
Guid Id
Platform Platform
DateOnly SnapshotDate            // host-LOCAL capture date (same clock as ChannelAnalytics:RunAtLocalTime)
SnapshotScope Scope             // Account | Video   (new small enum)
string VideoId                  // NON-NULLABLE; sentinel "" for Account scope (so the unique index + upsert work)
string? VideoTitle              // denormalized for display (Video scope)
IReadOnlyDictionary<string,long> Metrics   // metric name -> value, stored as jsonb
DateTimeOffset CapturedAt
```
**Invariant:** every value in `Metrics` is an integer count (or whole seconds) — no fractions. Ratios
(engagement rate) and fractional analytics are computed at read time or fetched live, never stored here.

Rationale for a **metric bag (jsonb)** rather than fixed columns: the three platforms expose different,
evolving metric sets (Instagram deprecates/renames metrics; TikTok is a small fixed set; YouTube is large).
A typed column-per-metric schema would require a migration every time a platform changes. The bag keeps the
schema stable; DTO mapping picks the known keys per platform. Canonical metric keys are defined per platform
in the service layer (see 6). Trends are a range-fetch of ~90 rows + in-memory delta extraction, so the
`(Platform, SnapshotDate)` btree index suffices — no GIN index on the jsonb needed.

`Enums/SnapshotScope.cs` (new): `{ Account = 0, Video = 1 }`.

## 5. Infrastructure — persistence (`src/PBA.Infrastructure/Data`)

### 5.1 `Configurations/PlatformCredentialConfiguration.cs` (new)
`PlatformCredential` currently maps by convention with a unique filtered index on `(Platform)` where
`IsActive` (created in migration `20260527132050_AddMultiPlatformPublishing`). Replace that index with a
**composite filtered unique index** `(Platform, Purpose)` where `IsActive = true`, so one active
Publishing *and* one active Analytics credential can coexist per platform. Configure `Purpose` conversion
(enum→int).

### 5.2 `Configurations/ChannelMetricSnapshotConfiguration.cs` (new)
- `HasKey(Id)`.
- `Metrics` → `HasColumnType("jsonb")` with a value converter (`Dictionary<string,long>` ↔ json) and a
  value comparer.
- **Unique index** `(Platform, SnapshotDate, Scope, VideoId)`. Because `VideoId` is non-nullable (sentinel
  `""` for Account scope), this genuinely enforces one Account row per platform-day and one row per video-day
  in PostgreSQL, and enables a real `ON CONFLICT` upsert. (A nullable `VideoId` would NOT — PostgreSQL treats
  NULLs as distinct, so account rows could duplicate and `ON CONFLICT` would degrade to plain INSERT.)
- Index `(Platform, SnapshotDate)` for range/trend queries.
- `VideoId` (required), `VideoTitle` max lengths.

### 5.3 `ApplicationDbContext` + `IAppDbContext`
Add `DbSet<ChannelMetricSnapshot> ChannelMetricSnapshots => Set<...>();` to both. (Publishing credential
DbSet already present.)

### 5.4 Migration
`dotnet ef migrations add AddChannelAnalytics --project src/PBA.Infrastructure --startup-project src/PBA.Api
--output-dir Data/Migrations`. Produces: the `Purpose` column + reworked unique index, and the
`ChannelMetricSnapshots` table. Also hand-author `scripts/migrate-add-channel-analytics.sql` (idempotent,
matching existing `scripts/migrate-*.sql` style) for the Mac Mini prod apply. The script must **drop the old
`PlatformCredential` unique index by name** (the one created in `20260527132050_AddMultiPlatformPublishing`)
before creating the new composite `(Platform, Purpose)` filtered index, so the apply doesn't leave both.

## 6. Infrastructure — analytics services (`src/PBA.Infrastructure/Services/Analytics`)

Each platform gets a facade + thin client, mirroring the GA4 split. Interfaces live in
`PBA.Application/Common/Interfaces`.

### 6.1 Common contracts (`PBA.Application`)
```
interface IChannelAnalyticsService {
    Platform Platform { get; }
    // Pull the current account snapshot + recent-video snapshots for today's poll.
    Task<Result<ChannelPollResult>> PollAsync(PlatformCredential credential, int recentVideoCount, CancellationToken ct);
}

// value objects (records) returned by PollAsync
record ChannelPollResult(AccountMetrics Account, IReadOnlyList<VideoMetrics> RecentVideos);
record AccountMetrics(IReadOnlyDictionary<string,long> Metrics, bool Provisional);
record VideoMetrics(string VideoId, string? Title, IReadOnlyDictionary<string,long> Metrics, bool Provisional);
```
Registered with **keyed DI** by platform so the poller can resolve the right service:
`services.AddKeyedScoped<IChannelAnalyticsService, YouTubeAnalyticsService>(Platform.YouTube)` (and IG,
TikTok). This matches the repo's keyed-DI convention for extensible registrations.

### 6.2 YouTube (`YouTubeAnalyticsService` + `YouTubeApiClient`)
- Thin client wraps Data API v3: `channels.list?part=statistics,contentDetails`, then the uploads playlist
  via `playlistItems.list` (1 unit, newest-first) to discover recent video IDs — **NOT `search.list`**
  (100 units, unreliable ordering) — then `videos.list?part=statistics,snippet` batched ≤50 ids. Uses
  `Google.Apis.YouTube.v3` (add NuGet ref), authorized with the stored OAuth access token; public reads may
  use the configured API key. (No ETag/`If-None-Match` — negligible value for a once-daily poll.)
- **Snapshot (cumulative) account keys:** `subscribers`, `views`, `videos`. **Per-video keys:** `views`,
  `likes`, `comments`. Recent videos limited to N.
- **Deep day-analytics (NOT snapshotted — queried live on the YouTube tab):** a separate call path over
  Analytics API v2 `reports.query` (`ids=channel==MINE`, `dimensions=day`, metrics `views`,
  `estimatedMinutesWatched`, `averageViewDuration`, `subscribersGained`, `subscribersLost`, `likes`,
  `comments`, `shares`; plus traffic-source / geography / demographics dimensions) for the user-selected
  range. Uses `Google.Apis.YouTubeAnalytics.v2`. Exposed via a dedicated query/DTO (see 9.2), not stored in
  `ChannelMetricSnapshot`. `subscribersGained/Lost` trend on other tabs is derived from our cumulative
  `subscribers` deltas.

### 6.3 Instagram (`InstagramAnalyticsService` + `InstagramGraphClient`)
- HTTP client against `graph.instagram.com` (Instagram-Login model). Account insights
  (`/{ig-user-id}/insights?metric_type=total_value&period=day`) + `follower_count` + per-media insights.
- Canonical account keys: `followers`, `reach`, `views`, `accounts_engaged`, `total_interactions`, `likes`,
  `comments`, `saves`, `shares`, `profile_links_taps`.
- Per-media keys: `reach`, `views`, `likes`, `comments`, `saves`, `shares`.
- **Deprecation tolerance:** the requested metric list is data-driven (a constant array); the client requests
  metrics and silently drops any the API rejects as unknown/deprecated (log at debug), never failing the whole
  poll. `impressions` is intentionally absent (removed; `views` replaces it).

### 6.4 TikTok (`TikTokAnalyticsService` + `TikTokDisplayClient`)
- HTTP client against `open.tiktokapis.com` v2: `/v2/user/info/` (fields incl. `follower_count`,
  `following_count`, `likes_count`, `video_count`) and `/v2/video/list/` (fields incl. `view_count`,
  `like_count`, `comment_count`, `share_count`, `title`, `id`). `/v2/video/list/` caps `max_count ≤ 20/page`,
  so the client **loops on the returned `cursor`/`has_more`** to accumulate up to N videos.
- Canonical account keys: `followers`, `following`, `likes`, `videos`.
- Per-video keys: `views`, `likes`, `comments`, `shares`.
- Documented ceiling in code comments + runbook: no reach/demographics/retention available; trends are
  snapshot deltas only.

All three services: return `Result<ChannelPollResult>`, catch API exceptions → `Result.Fail` with a typed
reason, and are constructed to accept an injected `HttpClient`/SDK client so tests feed canned responses.

## 7. Infrastructure — OAuth refactor (`src/PBA.Infrastructure/Security`)

### 7.1 New abstraction
```
interface IOAuthProvider {
    Platform Platform { get; }
    // Provider builds its own authorize URL AND any state it needs persisted (e.g. Twitter PKCE code_verifier).
    // The coordinator (OAuthService) persists State into the StateStore; provider-specific logic never leaks out.
    AuthorizationRequest BuildAuthorization(string state);          // record: (string Url, OAuthStateAdditions Additions)
    Task<OAuthTokenResult> ExchangeCodeAsync(string code, OAuthStateEntry state, CancellationToken ct);
    // Takes the full credential (NOT a bare refresh-token string) so providers with no separate refresh token
    // — e.g. Instagram, which extends the long-lived access token — can implement their own refresh.
    Task<Result<OAuthTokenResult>> RefreshAsync(PlatformCredential credential, CancellationToken ct);
    // Provider-specific proactive refresh window (Google ~expiry-1h; TikTok ~expiry/24h; Instagram ~50 days).
    bool NeedsRefresh(PlatformCredential credential, DateTimeOffset now);
}
```
One implementation per provider in `Security/OAuthProviders/`:
`LinkedInOAuthProvider`, `TwitterOAuthProvider` (migrated verbatim from the current switch arms — authorize,
scopes, token-exchange, **and refresh** behavior preserved), plus new `YouTubeOAuthProvider`,
`InstagramOAuthProvider`, `TikTokOAuthProvider`. Registered with keyed DI by `Platform`.

`RefreshAsync` returns `Result<OAuthTokenResult>` carrying a **`RefreshFailureReason { Revoked, Transient }`**
on failure (from the revoked-vs-transient signals in research B4: Google `invalid_grant`, Meta subcode
190/458/463, TikTok `access_token_invalid` = Revoked; network/5xx/429 = Transient). The poller deactivates a
credential only on `Revoked` (see 8.1).

### 7.2 `OAuthService` becomes a thin coordinator
Retains `GetAuthorizationUrlAsync` / `ExchangeCodeAsync` / `RefreshTokenAsync` signatures (so
`OAuthEndpoints` and the connectors — `LinkedInConnector:137`, `TwitterConnector:239` — are untouched), but
the body resolves the keyed `IOAuthProvider` and delegates. It keeps: the in-memory `StateStore` (persisting
the provider's `OAuthStateAdditions`, including any PKCE `code_verifier`), `PlatformCredential` upsert, and
`TokenEncryptor` calls. `ExchangeCodeAsync` gains a `CredentialPurpose` (default `Publishing`; the analytics
connect flow passes `Analytics`) so tokens land under the right purpose.

**The current per-platform refresh internals move into each provider's `RefreshAsync`** — Twitter's Basic-auth
refresh, LinkedIn's refresh, etc. are decomposed out of `OAuthService` into `TwitterOAuthProvider` /
`LinkedInOAuthProvider`. `OAuthService.RefreshTokenAsync(credential)` now decrypts the refresh token (if any)
and delegates to the provider; this path is the highest-traffic production path and is regression-tested
(see 14).

### 7.3 Provider config (options)
New options classes under `Configuration/`, section names nested under `Publishing:` for consistency with
existing providers (or a new `OAuth:` root — **decision: keep `Publishing:` prefix** to avoid touching the
existing binding convention, even though these are analytics scopes; the section is about the *app*, not the
purpose):
```
YouTubeOAuthOptions   SectionName = "Publishing:YouTube"     { Enabled, ClientId, ClientSecret, RedirectUri, ApiKey }
InstagramOAuthOptions SectionName = "Publishing:Instagram"   { Enabled, ClientId, ClientSecret, RedirectUri }
TikTokOAuthOptions    SectionName = "Publishing:TikTok"      { Enabled, ClientId, ClientSecret, RedirectUri }
```
Scopes are hardcoded per provider (as LinkedIn/Twitter do today), per spec FR1. Client id/secret come from
user-secrets / env (reusing ai-video-producer app credentials); never committed.

### 7.4 `OAuthEndpoints`
Add `Platform.YouTube`, `Instagram`, `TikTok` to the `OAuthPlatforms` allow-list. Add an optional
`?purpose=analytics` query param on `/{platform}/authorize` (default `publishing`) threaded into the state
entry so the callback stores the credential with the right `Purpose`. No new routes needed.

## 8. Infrastructure — daily poller (`src/PBA.Infrastructure/Services/Analytics`)

### 8.1 `ChannelMetricPollingService : BackgroundService`
Follow the `DigestService` shape (hourly self-timed loop, per-run DI scope, try/catch-continue,
`OperationCanceledException` excluded). Differs deliberately in using `IOptionsMonitor<ChannelAnalyticsOptions>`
for hot-reload of `RunAtLocalTime`/`RecentVideoCount`.
- ctor: `IServiceScopeFactory`, `IOptionsMonitor<ChannelAnalyticsOptions>`, `ILogger`.
- Hourly self-timed loop; act after `RunAtLocalTime`. "Today" = **host-local date** (same clock as
  `RunAtLocalTime`), matching `SnapshotDate`.
- Per run: open scope; resolve `ApplicationDbContext`, `IOAuthService`, keyed `IChannelAnalyticsService`s and
  keyed `IOAuthProvider`s.
- For each active `PlatformCredential` with `Purpose = Analytics` and an enabled platform:
  1. **Idempotency guard:** skip platform if an Account snapshot already exists for `(platform, today-local)`.
     (Snapshots are cumulative + final, so once today's row exists there is nothing to re-poll.)
  2. **Ensure a valid token:** if `provider.NeedsRefresh(credential, now)`, call
     `OAuthService.RefreshTokenAsync(credential)`; persist any rotated refresh token. On refresh failure,
     inspect `RefreshFailureReason`: **`Revoked` → set `IsActive=false`** (reconnect required) and skip;
     **`Transient` → leave active**, log, and skip this run (retry tomorrow).
  3. Call `service.PollAsync(credential, RecentVideoCount, ct)`.
  4. **On success, write inside a single transaction: Video rows first, then the Account row last** as a true
     completion sentinel (so a mid-write crash never leaves the guard thinking the platform is done). Writes
     are idempotent upserts (`ON CONFLICT` on the unique index).
  5. On failure, log and continue (never throw out of the loop).
- Registered `AddHostedService<ChannelMetricPollingService>()` in `DependencyInjection.cs`.

### 8.2 `ChannelAnalyticsOptions` (`Configuration/`)
```
SectionName = "ChannelAnalytics"
{
  RunAtLocalTime : string  = "05:00"
  RecentVideoCount : int   = 50
  YouTubeEnabled / InstagramEnabled / TikTokEnabled : bool = false   // per-platform gates
}
```

## 9. Application — read queries (`src/PBA.Application/Features/ChannelAnalytics`)

### 9.1 DTOs (`Dtos/ChannelAnalyticsDtos.cs`)
```
record MetricPoint(DateOnly Date, long Value);
record TrendSeries(string Metric, IReadOnlyList<MetricPoint> Points);   // deltas derived from cumulative snapshots
record KpiCard(string Key, string Label, long Value, double? DeltaPct, double? Rate);  // Rate = engagement rate (fraction), null when N/A
record RecentPost(string VideoId, string? Title, IReadOnlyDictionary<string,long> Metrics);
record ChannelAnalyticsDto(
    Platform Platform,
    ConnectionStatus Status,               // Connected | ReconnectRequired | NotConnected
    DateOnly? AsOf,
    IReadOnlyList<KpiCard> Kpis,
    IReadOnlyList<TrendSeries> Trends,
    IReadOnlyList<RecentPost> RecentPosts);
record OverviewChannel(Platform Platform, ConnectionStatus Status, long? Followers, IReadOnlyList<MetricPoint> FollowerSparkline);
record OverviewDto(
    long TotalAudience,                    // sum of followers/subscribers across connected channels
    IReadOnlyList<KpiCard> CombinedKpis,
    IReadOnlyList<OverviewChannel> Channels);
```
`ConnectionStatus` enum (Application-level).

### 9.2 Queries (static-class convention)
- `GetChannelAnalytics.Query(Platform Platform, AnalyticsPeriod Period)` → `Result<ChannelAnalyticsDto>`.
  Handler reads snapshot history for the platform over the period, builds KPIs (latest cumulative + `DeltaPct`
  vs prior), **trend series as deltas between consecutive daily snapshots**, and recent posts (latest Video
  snapshots). Computes engagement `Rate` per platform: **interactions ÷ reach** where reach exists (Instagram),
  else **interactions ÷ followers** (YouTube, TikTok), where interactions = likes+comments+shares(+saves).
  Resolves connection status from `PlatformCredential (Purpose=Analytics)`.
- `GetAnalyticsOverview.Query(AnalyticsPeriod Period)` → `Result<OverviewDto>`. Fans out across platforms,
  aggregates total audience + combined KPIs + per-platform follower sparklines (delta series).
- `GetYouTubeDeepAnalytics.Query(AnalyticsPeriod Period)` → `Result<YouTubeDeepAnalyticsDto>`. **Live** call to
  the YouTube Analytics API (via `IChannelAnalyticsService`/YouTube client) for watch time, average view
  duration, traffic sources, and demographics over the range — shown on the YouTube tab only. This is the one
  read that hits an external API on demand (wrapped in `Result` so the tab degrades gracefully if it fails);
  everything else is snapshot-backed. DTO carries labeled series/breakdowns.
- Reuse the existing `AnalyticsPeriod` (`7d|30d|90d`) resolution used by the Website endpoint.

Snapshot-backed handlers depend only on `IAppDbContext` + a status helper — no live API calls. The single
live path (`GetYouTubeDeepAnalytics`) additionally resolves the keyed YouTube service + its credential.

## 10. API — endpoints (`src/PBA.Api/Endpoints/ChannelAnalyticsEndpoints.cs`)

```
MapChannelAnalyticsEndpoints(this IEndpointRouteBuilder app)
  group "/api/analytics"
  GET  /overview?period=            -> GetAnalyticsOverview.Query      -> OverviewDto
  GET  /channel/{platform}?period=  -> GetChannelAnalytics.Query       -> ChannelAnalyticsDto
  GET  /youtube/deep?period=        -> GetYouTubeDeepAnalytics.Query    -> YouTubeDeepAnalyticsDto  (live)
```
`{platform}` parsed to the `Platform` enum (YouTube|Instagram|TikTok); invalid → BadRequest. Register
`app.MapChannelAnalyticsEndpoints();` in `Program.cs` beside `MapAnalyticsEndpoints()`. The existing
`/api/analytics/website` + `/health` are unchanged. Connection status for the "connect" affordance is
already available via the existing `/api/platforms` + `/api/auth/{platform}/status`; the analytics DTO also
carries `Status` for convenience.

## 11. Configuration & secrets

- `appsettings.json`: add `ChannelAnalytics` section (defaults, per-platform gates false) and
  `Publishing:{YouTube,Instagram,TikTok}` sections shipping `{ "Enabled": false }` only.
- Secrets (user-secrets dev / env prod, per repo security rules): `Publishing:YouTube:ClientId/ClientSecret/
  ApiKey`, `Publishing:Instagram:ClientId/ClientSecret`, `Publishing:TikTok:ClientId/ClientSecret`, and each
  `RedirectUri` (PBA Mac Mini Tailscale callback). `Encryption:Key` already exists (reused).
- DI registrations added to `DependencyInjection.cs`: three `Configure<...OAuthOptions>()`, three
  `AddKeyedScoped<IChannelAnalyticsService,...>()`, five `IOAuthProvider` keyed registrations, the
  `Configure<ChannelAnalyticsOptions>()`, and `AddHostedService<ChannelMetricPollingService>()`.

## 12. Frontend (`src/PersonalBrandAssistant.Web/src/app/features/analytics`)

### 12.1 Structure
Refactor the single component into a shell + per-source children:
```
analytics/
  analytics.component.ts            # shell: PrimeNG tabs (Overview | Website | YouTube | Instagram | TikTok)
  overview/overview.component.ts    # cross-platform: total audience, combined KPIs, per-platform sparklines
  website/website-analytics.component.ts   # existing Website markup extracted here (behavior unchanged)
  channel/channel-analytics.component.ts   # generic per-platform view, @Input() platform
  models/channel-analytics.model.ts        # TS mirrors of the new DTOs + ConnectionStatus
  services/analytics.service.ts            # add getOverview(period), getChannel(platform, period)
```
The generic `channel-analytics.component` renders KPI row + PrimeNG line charts (trend series) + a recent-
posts table, plus a "Connect {platform}" / "Reconnect" affordance driven by `ConnectionStatus` (links to
`/api/auth/{platform}/authorize?purpose=analytics`). Website stays its own component (different data shape).
The **YouTube** tab additionally renders a live deep-analytics panel (watch time, average view duration,
traffic sources, demographics) from `GET /api/analytics/youtube/deep`, degrading gracefully if that live call
fails. The **Overview** must label `TotalAudience` as *approximate* (YouTube subscriber counts are rounded).

### 12.2 State
Signals only (match existing): `period`, `loading`, `data`, `status` per tab; `computed()` for KPI/trend/
chart view-models. Data loads on tab activation. No NgRx.

### 12.3 Routing/nav
The lazy `/analytics` route stays; the shell owns the tabs. Sidebar entry unchanged.

## 13. Console runbook (deliverable: `planning/channel-analytics/runbook.md`)

A standalone doc (not code) the user actions in parallel. Sections:
- **Google Cloud (YouTube):** enable YouTube Data API v3 + YouTube Analytics API v2; create/confirm API key;
  on the OAuth client, add PBA's Mac Mini redirect URI; add scopes `yt-analytics.readonly`,
  `youtube.readonly`; **move the OAuth consent screen to "In Production"** (critical — Testing kills refresh
  tokens after 7 days); where to put ClientId/Secret/ApiKey.
- **Meta (Instagram):** confirm IG account is Business/Creator; use "Instagram API with Instagram Login"; add
  `instagram_business_basic` + `instagram_business_manage_insights`; add PBA redirect URI; note Standard
  Access needs no App Review for the owner's account; where secrets go.
- **TikTok:** in the existing app, add products/scopes `user.info.stats` + `video.list`; add PBA redirect
  URI; submit for review; sandbox works for the developer's own account meanwhile.
- **Verify:** connect each channel via the PBA UI, confirm a first snapshot appears after the poll (or via a
  manual trigger), and confirm status flips to Connected.

## 14. Testing strategy (TDD, 80%+)

Backend (xUnit):
- **OAuth refactor regression (highest priority):** provider tests asserting LinkedIn/Twitter authorize URLs,
  scopes, token-exchange, **AND refresh** behavior are byte-for-byte unchanged (refresh is the highest-traffic
  production path — `LinkedInConnector:137`, `TwitterConnector:239` — so it must be covered, not just
  authorize/exchange). Twitter PKCE `code_verifier` generation/persistence tested across the provider/coordinator
  boundary.
- **Each `IChannelAnalyticsService`:** feed canned platform JSON to a stub `HttpClient`/SDK client; assert
  the returned `ChannelPollResult` metric bags; assert deprecation tolerance (IG unknown metric dropped),
  ETag handling (YouTube), and TikTok field mapping.
- **Poller:** `ChannelMetricPollingService` with mocked services + in-memory DB — asserts idempotency
  (second run same day writes nothing), provider-specific refresh trigger (`NeedsRefresh`), **reconnect-required
  ONLY on `Revoked` (transient failure leaves credential active)**, N-video capping, and the
  video-rows-first/account-row-last transactional write (simulated mid-write failure leaves no account row).
- **Test host isolation:** the endpoint `WebApplicationFactory` must **strip `ChannelMetricPollingService`**
  (and rely on per-platform gates defaulting false) so the poller doesn't start and hit external APIs during
  integration tests. (DigestService needs the same treatment — follow whatever the existing factory does.)
- **Queries:** handler tests over seeded snapshots — KPI deltas, trend series assembly, overview aggregation,
  connection-status resolution.
- **Endpoints:** `WebApplicationFactory<Program>` — routes, period parsing, platform parsing, `ToApiResult`
  mapping.
- **EF config:** unique-index behavior for `(Platform, Purpose, IsActive)` and snapshot uniqueness.

Frontend (Jasmine/Karma + `HttpTestingController`): service methods hit correct URLs; overview + channel
components render KPIs/charts/tables from mocked responses; connect/reconnect affordance shows per status;
Website tab unchanged.

## 15. Rollout / gating (irreversible steps flagged)

1. Merge code (all green against mocks) — safe, no external effect.
2. Apply migration on Mac Mini (`scripts/migrate-add-channel-analytics.sql`) — additive, low risk.
3. User completes the runbook (external, parallel).
4. Connect each channel via UI (writes real tokens).
5. Flip per-platform `ChannelAnalytics:{Platform}Enabled=true` so the poller includes it. **First real poll
   spends nothing meaningful (no LLM); safe.** Trends accrue from day one forward.

## 16. Risks and mitigations

- **OAuth refactor regresses publishing** → dedicated regression tests + preserve `OAuthService` public
  signatures; providers migrated verbatim first, new providers added second.
- **Instagram metric churn** → data-driven, deprecation-tolerant metric requests; never fail a poll on one
  bad metric.
- **Google "Testing" 7-day refresh death** → runbook step + a health signal that surfaces token expiry
  (`ConnectionStatus = ReconnectRequired`).
- **TikTok data ceiling** → set expectations in UI copy + runbook; lean on snapshot deltas for all TikTok
  trends.
- **Snapshot table growth** → per-video capped at N; jsonb keeps schema stable; full-history retention
  revisited only if size becomes an issue (documented, not pre-optimized).
- **Multi-instance OAuth state** → not a concern (single Mac Mini host); documented assumption.
- **`TotalAudience` is approximate** → YouTube `subscriberCount` is rounded to 3 significant figures; the
  Overview UI copy must label the aggregate as approximate, not exact.

## 17. Build sequence (for /deep-implement — distinct from the deploy order in §15)

Order chosen so the highest-risk change (the OAuth refactor, which can regress *existing working publishing*)
lands and is verified in isolation before anything depends on it:

1. **OAuth refactor + regression.** Introduce `IOAuthProvider` + keyed DI; migrate LinkedIn/Twitter verbatim
   (authorize, exchange, **refresh**, PKCE); `OAuthService` becomes a coordinator. **Gate: full LinkedIn/Twitter
   regression suite green, no behavior change** — before any new provider or analytics code exists.
2. **Domain + persistence.** `Platform` additions, `CredentialPurpose`, `SnapshotScope`, `ChannelMetricSnapshot`,
   the `PlatformCredentialConfiguration` index rework, EF config, migration + idempotent SQL script.
3. **New OAuth providers.** YouTube/Instagram/TikTok providers + options + allow-list + `?purpose=analytics`.
4. **Per-platform analytics services.** The three `IChannelAnalyticsService`s + clients, each fully tested
   against canned JSON.
5. **Poller.** `ChannelMetricPollingService` + `ChannelAnalyticsOptions` + DI.
6. **Read queries + endpoints.** `GetChannelAnalytics`, `GetAnalyticsOverview`, `GetYouTubeDeepAnalytics`,
   `ChannelAnalyticsEndpoints`.
7. **Frontend.** Shell tabs + Overview + generic channel component + Website extraction + service/models.
8. **Runbook** (`runbook.md`) — can be authored in parallel any time; it gates only real-data go-live, not code.
