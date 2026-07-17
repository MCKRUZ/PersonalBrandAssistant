# Channel Analytics — Research

Two research passes: (A) existing codebase mechanisms to mirror, (B) verified 2026 platform-API
capabilities. All backend paths are relative to repo root. Stack: .NET 10, EF Core `10.0.7`,
PostgreSQL + pgvector, MediatR/CQRS with `Result<T>`, Angular 19 + signals + PrimeNG.

---

# PART A — Codebase mechanisms (patterns to mirror exactly)

## A1. Daily scheduling / background jobs

**No Quartz/Hangfire-recurring, no cron.** Recurring work = `BackgroundService` subclasses registered
with `AddHostedService<T>()`. (Hangfire exists only for one-shot delayed publish jobs.)

**Canonical daily template — `src/PBA.Infrastructure/Services/Radar/DigestService.cs`:**
- Extends `BackgroundService`; ctor injects `IServiceScopeFactory`, `IOptions<DigestOptions>`, `ILogger`.
- `ExecuteAsync`: `while (!stoppingToken.IsCancellationRequested)` loop that wakes **every hour**
  (`Task.Delay(TimeSpan.FromHours(1), stoppingToken)`) and *acts only after* a configured local time
  (`TimeOnly.ParseExact(options.RunAtLocalTime,"HH:mm")` compared to `TimeOnly.FromDateTime(now)`).
  Self-timed loop, NOT a precise sleep-until.
- **Scope per run:** `using var scope = scopeFactory.CreateScope();` then
  `scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()`. Scoped services resolved from the
  scope, never injected into the singleton ctor.
- **Idempotency guard = DB uniqueness, not a lock:**
  `if (await db.Digests.AnyAsync(d => d.Date == date && d.Kind == kind, ct)) return;` → at most one row
  per (calendar day, kind). The poller must do the same: skip if a `ChannelMetricSnapshot` already exists
  for `(date, platform/channel)`.
- **Failure guard:** try/catch inside the loop logs and continues; `OperationCanceledException` excluded.

**Interval variant** (if we ever want pure interval instead of time-of-day):
`src/PBA.Infrastructure/Services/SourcePollingService.cs` uses `IOptionsMonitor<T>` for hot-reloadable
interval. Others: `Radar/HighScoreAlertService.cs`, `Publishing/ScheduledPublishReconciler.cs`
(run-once-on-startup with `CreateAsyncScope()`).

**DI registration** — `src/PBA.Infrastructure/DependencyInjection.cs` inside `AddInfrastructureDependencies`:
`AddHostedService<SourcePollingService>()` (l.72), `...Radar.DigestService` (l.97), etc. Add
`AddHostedService<ChannelMetricPollingService>()` here.

**Config** — `DigestOptions` (`src/PBA.Infrastructure/Configuration/DigestOptions.cs`,
`SectionName="Digest"`), bound `services.Configure<T>(configuration.GetSection(T.SectionName))`.
appsettings: `"Digest": { "RunAtLocalTime": "07:00", ... }`. Add a `ChannelAnalytics` section +
`ChannelAnalyticsOptions { public const string SectionName = "ChannelAnalytics"; ... }`.

## A2. OAuth subsystem internals

Files: `src/PBA.Infrastructure/Security/OAuthService.cs`,
`src/PBA.Api/Endpoints/OAuthEndpoints.cs`, `src/PBA.Infrastructure/Security/TokenEncryptor.cs`,
interface `src/PBA.Application/Common/Interfaces/IOAuthService.cs`.

**Architecture: hardcoded `switch (Platform)` — NO `IOAuthProvider` abstraction.** `OAuthService` is one
class branching on the enum. Two dispatch points (`GetAuthorizationUrlAsync`, `ExchangeCodeAsync`) are
`platform switch` expressions that throw `NotSupportedException` for unlisted platforms. Authorize-URL /
scopes / token-exchange are hardcoded in private `Build{Provider}AuthUrl` / `Exchange{Provider}CodeAsync`
methods (e.g. LinkedIn hardcodes scope + endpoint; Twitter hardcodes PKCE `S256`, scopes, Basic-auth).

- CSRF/PKCE state = in-memory `static ConcurrentDictionary<string,OAuthStateEntry> StateStore`, 10-min TTL,
  `MaxPendingStates=1000`. **Single-instance assumption** (fine for PBA's single Mac Mini host).
- **Per-provider options** nested under `Publishing:` — `LinkedInOptions.SectionName="Publishing:LinkedIn"`,
  `TwitterOptions.SectionName="Publishing:Twitter"`, fields `Enabled`, `ClientId`, `ClientSecret`,
  `RedirectUri` (all `required` except Enabled → must come from user-secrets/env). Registered
  `DependencyInjection.cs:137-138`; `AddScoped<IOAuthService, OAuthService>()` l.144.
- **Persistence:** `ExchangeCodeAsync` upserts one `PlatformCredential` per platform, encrypting via
  `ITokenEncryptor`: sets `EncryptedAccessToken`, `EncryptedRefreshToken`,
  `AccessTokenExpiresAt = UtcNow.AddSeconds(ExpiresIn)`, `Scopes`, `IsActive=true`.
- **`TokenEncryptor`** = AES-GCM 256, key `EncryptionOptions.Key` (section `"Encryption"`, base64, 32 bytes),
  nonce(12)+ciphertext+tag(16) packed base64.
- **Token refresh EXISTS:** `OAuthService.RefreshTokenAsync(PlatformCredential, ct) : Task<Result<string>>`
  (part of `IOAuthService`). Returns `Fail("No refresh token available")` + sets `IsActive=false` when no
  refresh token; else decrypts and re-hits the token endpoint. Already called by `LinkedInConnector.cs:137`
  and `TwitterConnector.cs:239`. **The poller will reuse this for pre-call token refresh.**
- **Endpoint group** `MapOAuthEndpoints` on `/api/auth` with hardcoded allow-list
  `static readonly HashSet<Platform> OAuthPlatforms = [Platform.LinkedIn, Platform.Twitter]`. Routes:
  `GET /{platform}/authorize`, `GET /{platform}/callback` (→ redirect `/settings/platforms?connected=...`;
  `SecurityException`→`Forbid`), `GET /{platform}/status`, `DELETE /{platform}`.

**To add YouTube/Instagram/TikTok, touch (mirror LinkedIn):** (1) new options classes with SectionName +
ClientId/Secret/RedirectUri; (2) `services.Configure<>()`; (3) new `Build{X}AuthUrl` + `Exchange{X}CodeAsync`
+ new `IOptions<>` ctor params; (4) enum arms in both switches; (5) add to `OAuthPlatforms` allow-list;
(6) add `Platform` enum members. **Design note:** existing pattern is switch-based; a small refactor to an
`IOAuthProvider` map (keyed DI per platform) would be cleaner given we're adding 3 providers at once —
decide in the plan (leans toward the map since the switch will get long).

## A3. EF entity + migration pattern (reference: `PlatformCredential`)

- **Entity:** plain POCO in `PBA.Domain.Entities`, `Guid Id { get; init; } = Guid.NewGuid();`,
  `DateTimeOffset CreatedAt/UpdatedAt` defaults, no base class.
- **Config optional (convention by default).** `PlatformCredential` has no config class. Configs in
  `src/PBA.Infrastructure/Data/Configurations/` are auto-applied by
  `modelBuilder.ApplyConfigurationsFromAssembly(...)`. Add a `ChannelMetricSnapshotConfiguration` **for the
  unique index** backing the idempotency guard (jsonb/precision as needed). Pattern from
  `ContentConfiguration.cs`: `HasKey`, `Property().IsRequired().HasMaxLength()`,
  `Property().HasColumnType("jsonb")`, `HasIndex(...).IsUnique()`.
- **DbSet:** add `public DbSet<ChannelMetricSnapshot> ChannelMetricSnapshots => Set<...>();` to
  `src/PBA.Infrastructure/Data/ApplicationDbContext.cs`, and to `IAppDbContext`
  (`src/PBA.Application/Common/Interfaces/IAppDbContext.cs`) if handlers use the interface.
- **Design-time factory:** `src/PBA.Infrastructure/Data/DesignTimeDbContextFactory.cs` hardcodes
  `Host=localhost;Database=pba_design_time` + `.UseVector()` (so `dotnet ef` needs no running app).
- **Migration command:**
  ```
  dotnet ef migrations add AddChannelMetricSnapshot \
    --project src/PBA.Infrastructure --startup-project src/PBA.Api --output-dir Data/Migrations
  dotnet ef database update --project src/PBA.Infrastructure --startup-project src/PBA.Api
  ```
  Migrations in `src/PBA.Infrastructure/Data/Migrations/`. Latest = `20260617144341_AddIsMicrosoftSource`.
  ProductVersion `10.0.7`. (Also generate an idempotent SQL script for prod, matching the existing
  `scripts/migrate-*.sql` convention.)
- **NOT EF:** `tools/PBA.Migration/DataMigrator.cs` is a separate *data*-migration console tool. Don't confuse.

## A4. Endpoint + MediatR feature wiring (reference: `GetWebsiteAnalytics` / `AnalyticsEndpoints`)

- **Query shape:** static wrapper class → `public record Query(...) : IRequest<Result<TDto>>` + nested
  `public sealed class Handler(deps...) : IRequestHandler<Query, Result<TDto>>`. Deps via primary ctor.
  MediatR auto-registered by `AddApplicationDependencies()` scanning the Application assembly — no manual
  registration. Return `Result<TDto>.Success(...)` / `Result<TDto>.Fail(...)`.
- **Endpoint group:** `static class` + `MapXxx(this IEndpointRouteBuilder app)`, `app.MapGroup("/api/x")
  .WithTags("X")`, lambda takes route/query params + `ISender sender` + `CancellationToken ct`, then
  `var result = await sender.Send(new Query(...), ct); return result.ToApiResult();`.
- **`ToApiResult()`** — `src/PBA.Api/Extensions/ResultExtensions.cs` maps Result→IResult
  (Validation→BadRequest, NotFound→NotFound, PermissionRequired/GovernanceBlocked→Forbid, Conflict→Conflict).
- **Wire-up:** add `app.MapChannelAnalyticsEndpoints();` to the flat list in `Program.cs:66-75`. DI at
  `Program.cs:14-15` (`AddApplicationDependencies()` + `AddInfrastructureDependencies(config)`).

## A5. Legacy test tree — CONFIRMED DEAD, ignore

`tests/PersonalBrandAssistant.*.Tests/` (DashboardAggregator, AnalyticsDashboard*, etc.) is orphaned: no
`src/` references those types; its csproj references a non-existent `src/PersonalBrandAssistant.Domain`;
no root `.sln` binds it — it can't even compile. **Live tests = the `PBA.*` set**
(`tests/PBA.Api.Tests`, `PBA.Application.Tests`, `PBA.Infrastructure.Tests`, `PBA.Migration.Tests`). Do not
mirror or extend the legacy tree.

---

# PART B — Platform APIs (verified against official docs, July 2026; every claim source-cited in the
research transcript)

## B1. YouTube — Data API v3 (public, API key) + Analytics API v2 (owner, OAuth)

**Public stats (API key only, no OAuth), 1 quota unit each:**
- `channels.list?part=statistics&id={CHANNEL_ID}` → `viewCount`, `subscriberCount`, `videoCount`,
  `hiddenSubscriberCount`. NOTE `subscriberCount` is rounded to 3 sig figs (2019 policy) — not exact.
- `videos.list?part=statistics,snippet&id={UP TO 50 IDS}` → per-video `viewCount`, `likeCount`,
  `commentCount`, `favoriteCount`. `dislikeCount` empty since 2021. Batch 50 IDs/call.

**Owner deep metrics — Analytics API v2 (OAuth), separate API, does NOT consume Data API quota:**
- `GET https://youtubeanalytics.googleapis.com/v2/reports?ids=channel==MINE&startDate&endDate&metrics&dimensions`.
- Core metrics: `views`, `estimatedMinutesWatched`, `averageViewDuration`, `comments`, `likes`, `dislikes`,
  `shares`, `subscribersGained`, `subscribersLost`, `engagedViews`, `estimatedRevenue`, `viewerPercentage`.
- Core dimensions: `day`, `month`, `video`, `country`, `ageGroup`, `gender`, `sharingService`,
  `uploaderType`. Non-core: `insightTrafficSourceType`, `deviceType`, `subscribedStatus`, etc.
- Canonical daily query: `dimensions=day&metrics=views,estimatedMinutesWatched,averageViewDuration,
  subscribersGained,subscribersLost,likes,comments,shares` over a range.
- **Data lag ~2-3 days** (response only includes fully-finalized days) → store snapshots with a
  `provisional` flag and allow late correction, or trend lines show phantom dips.

**OAuth scopes:** `yt-analytics.readonly` (the one we need), `yt-analytics-monetary.readonly` (revenue),
`youtube.readonly` (owner channel/video lists). **Quota:** 10,000 units/day; daily poll ≈ 11 units even for a
500-video channel. Use ETags/`If-None-Match` → HTTP 304 on unchanged resources.

## B2. Instagram — Graph API Insights

**MAJOR: two paths; the newer one removes the Facebook-Page requirement.**
- **Instagram API with Instagram Login** (newer, `graph.instagram.com`): scopes
  `instagram_business_basic` + `instagram_business_manage_insights`. Direct Business/Creator login, **no FB
  Page needed.** ← use this.
- Classic Facebook-Login (`graph.facebook.com`): `instagram_basic` + `instagram_manage_insights` +
  `pages_read_engagement`.
- Account must be **professional (Business or Creator)** — personal accounts have no insights.

**App Review: NOT required for your own account.** "My app is only for a business I own or manage" =
**Standard Access, no App Review.** Review + Business Verification are only for Tech Providers serving
multiple businesses (Advanced Access). This corrects the earlier assumption — IG is much lower-friction than
originally framed.

**DEPRECATIONS (the big 2024-2025 changes):**
- Account-level `impressions` **removed Apr 21, 2025**; media `impressions` deprecated for media created
  after Jul 2, 2024. Replaced by universal **`views`** metric (`total_value` type, breakdowns
  `follower_type`, `media_product_type`).
- Also deprecated: media `plays`, `clips_replays_count`, non-Reels `video_views`; account `profile_views`,
  `website_clicks`, `phone_call_clicks`, `text_message_clicks`, `email_contacts` (time-series). (Per-metric
  dates beyond `impressions` are vendor-reported, consistent across 3 sources.)
- **Design implication:** wrap metric lists so an unknown/deprecated metric is skipped, not fatal. Watch the
  Instagram Platform Changelog.

**Currently available:**
- Account (`GET /{ig-user-id}/insights`, `metric_type=total_value`, `period=day`): `reach`, `views`,
  `accounts_engaged`, `total_interactions`, `likes`, `comments`, `saves`, `shares`, `replies`,
  `follows_and_unfollows`, `profile_links_taps`; lifetime demographics `engaged_audience_demographics`,
  `reached_audience_demographics`, `follower_demographics` (breakdowns age/city/country/gender);
  `follower_count` / `online_followers`. Caveats: `follower_count`/`online_followers` unavailable <100
  followers; demographics need ≥100 engagements, top 45 only; **data lags up to 48h**; missing data =
  empty set (not `0`).
- Per-media (`GET /{ig-media-id}/insights`): `comments`, `likes`, `saved`, `shares`, `reach`, `views`,
  `follows`, `profile_visits`, `total_interactions`; Reels `ig_reels_avg_watch_time`,
  `ig_reels_video_view_total_time`.

**Rate limits:** Platform limit `200 × users/hour` (app-wide) — non-issue for a daily single-account poll.

## B3. TikTok — Display API (shallow) + Business API (gated)

**Display API — three endpoints, cumulative counts only:**
- `GET /v2/user/info/` — scope-gated fields: `user.info.basic` (open_id, display_name, avatar),
  `user.info.profile` (bio, username, is_verified), **`user.info.stats` → `follower_count`,
  `following_count`, `likes_count`, `video_count`** ← the account stats. NOTE: stats moved OUT of
  `user.info.basic` into the dedicated `user.info.stats` scope (a real migration) — must request it and
  re-authorize.
- `POST /v2/video/list/` (scope `video.list`) / `POST /v2/video/query/` → per-video `id`, `create_time`,
  `title`, `share_url`, `duration`, **`view_count`, `like_count`, `comment_count`, `share_count`**.
  Paginated by cursor, `max_count` ≤ 20/page.
- Scopes must be added to the app and **approved** in the TikTok portal; unaudited apps only work for the
  developer's own test accounts.

**Business API deeper but gated:** audience demographics, video performance over time, profile views, reach
exist via the TikTok Business/Marketing API (`business.tiktok.com`, `/business/get/`), but behind Business
API access approval oriented at advertisers. (High confidence access is richer; exact field list NOT
verbatim-verified this pass — confirm the `/business/get/` reference before building against it.) Research
API = academic-only (US/EU non-profit), not usable.

**THE CEILING (be explicit with the user):** TikTok's in-app analytics tab shows profile views, reach,
unique viewers, audience demographics, traffic sources, watch time/retention, new-vs-returning — **none of
which any self-service creator API exposes.** Via Display API you get only cumulative totals + per-video
counts. **All TikTok trends must come from OUR daily snapshot deltas**, not from TikTok.

## B4. Daily-polling robustness (all three)

**Token refresh:**
- **Google/YouTube:** access token ~1h; long-lived **refresh token** via `access_type=offline`
  (`prompt=consent`). Breaks if revoked, unused 6 months, or >100 live tokens. **CRITICAL:** if the OAuth
  consent screen is in **"Testing"** status, refresh tokens **die after 7 days** — must be "In Production."
- **Instagram:** short token 1h → exchange for **long-lived 60-day** token (`ig_exchange_token`); refresh
  (extend 60 days) via `refresh_access_token` when token is >24h old and unexpired. Poller should refresh
  proactively (~>50 days old).
- **TikTok:** access token **`expires_in` = 24h**; refresh via `grant_type=refresh_token`;
  **refresh_token ~365 days**; refresh MAY rotate the refresh_token → **persist the new one every refresh.**

**Best practices:** YouTube ETags/`If-None-Match` (304s), batch videos 50/call, Analytics query only the
range since last poll. Meta: read `X-App-Usage`/`X-Business-Use-Case-Usage` headers, back off near 100%.
TikTok: low volume, exponential backoff on 429. General: idempotent snapshot writes keyed on
`(account, metric, date)`; add jitter; off-peak schedule.

**Failure modes to handle:** token revoked (Google `invalid_grant`, Meta subcode 190/458/463, TikTok
`access_token_invalid`) → flag "reconnect required," don't silently retry. Scope removed (e.g. TikTok
`user.info.stats`) → fields null → prompt re-consent. Account type changed Business→Personal → insights stop
→ downgrade to public-only. Google consent screen in Testing → 7-day silent death. Metric deprecation
(Instagram) → skip unknown metric, don't fail whole request. Data lag (YouTube 2-3d, IG 48h) → provisional
snapshots with late correction.

---

# Testing setup (verified from live `PBA.*` test tree)

- **Backend:** xUnit. `WebApplicationFactory<Program>` + in-memory/real DB for endpoint/pipeline tests
  (`tests/PBA.Api.Tests/Endpoints/AnalyticsEndpointsTests.cs`). Service tests mock the thin API clients
  (`tests/PBA.Infrastructure.Tests/Services/Analytics/*`). Naming `Method_Scenario_ExpectedResult`. Mock
  only external HTTP clients + time (`TimeProvider`); prefer real Result<T>/DTOs. 80% coverage min.
- **Frontend:** Jasmine/Karma, `HttpTestingController` + `afterEach(() => httpMock.verify())`; signals-based
  component tests (`analytics.component.spec.ts`, `analytics.service.spec.ts`). `data-testid` for any E2E.
- **Poller/API-client tests** mock the platform HTTP responses (canned JSON per B1-B3) so the whole feature
  is green before any real token exists — the gated-but-code-complete pattern from the blog pipeline.
