<!-- PROJECT_CONFIG
runtime: dotnet
test_command: dotnet test
END_PROJECT_CONFIG -->

<!-- SECTION_MANIFEST
section-01-oauth-provider-refactor
section-02-domain-persistence
section-03-oauth-providers
section-04-analytics-services
section-05-snapshot-poller
section-06-read-api
section-07-frontend
section-08-console-runbook
END_MANIFEST -->

# Channel Analytics — Implementation Sections Index

Backend tests: `dotnet test`. Frontend section (07) additionally uses `ng test --watch=false
--browsers=ChromeHeadless` (noted in that section). Build order follows plan §17: the OAuth refactor
(highest regression risk) lands and is verified green first, in isolation.

## Dependency Graph

| Section | Depends On | Blocks | Parallelizable |
|---------|------------|--------|----------------|
| section-01-oauth-provider-refactor | - | 03 | Yes |
| section-02-domain-persistence | - | 03, 04, 05, 06 | Yes |
| section-03-oauth-providers | 01, 02 | 05 | Yes |
| section-04-analytics-services | 02 | 05, 06 | Yes |
| section-05-snapshot-poller | 02, 03, 04 | - | No |
| section-06-read-api | 02, 04 | 07 | No |
| section-07-frontend | 06 | - | No |
| section-08-console-runbook | - | - | Yes |

## Execution Order

1. **section-01-oauth-provider-refactor**, **section-02-domain-persistence**, **section-08-console-runbook**
   (no dependencies — parallel). Section 01 must verify LinkedIn/Twitter regression green before anything
   builds on it.
2. **section-03-oauth-providers** (after 01 + 02), **section-04-analytics-services** (after 02) — parallel.
3. **section-05-snapshot-poller** (after 03 + 04), **section-06-read-api** (after 04) — parallel.
4. **section-07-frontend** (after 06).

## Section Summaries

### section-01-oauth-provider-refactor
Introduce `IOAuthProvider` (BuildAuthorization returning url + persisted state additions;
`ExchangeCodeAsync`; `RefreshAsync(PlatformCredential)` returning `Result<OAuthTokenResult>` with
`RefreshFailureReason`; `NeedsRefresh`). Migrate LinkedIn + Twitter verbatim into
`LinkedInOAuthProvider`/`TwitterOAuthProvider` (authorize, scopes, exchange, refresh, Twitter PKCE). Convert
`OAuthService` into a coordinator resolving keyed providers, keeping `StateStore`, `PlatformCredential`
upsert, `TokenEncryptor`, and its public signatures. **Gate: LinkedIn/Twitter authorize + exchange +
refresh regression tests green, zero behavior change.** (Plan §7; TDD Step 1.)

### section-02-domain-persistence
`Platform` enum additions (Instagram, TikTok — append, no renumber), `CredentialPurpose`, `SnapshotScope`;
`ChannelMetricSnapshot` entity (cumulative integer metric bag, non-null `VideoId` sentinel);
`PlatformCredential.Purpose` + `PlatformCredentialConfiguration` composite filtered unique index
`(Platform, Purpose)`; `ChannelMetricSnapshotConfiguration` (jsonb converter, unique index, range index);
`DbSet`s on `ApplicationDbContext` + `IAppDbContext`; EF migration `AddChannelAnalytics` + idempotent
`scripts/migrate-add-channel-analytics.sql` (drops the old index by name). (Plan §4, §5; TDD Step 2.)

### section-03-oauth-providers
`YouTubeOAuthProvider`, `InstagramOAuthProvider`, `TikTokOAuthProvider` with hardcoded analytics scopes and
their own refresh (Instagram extends the long-lived access token — no refresh token), plus provider-specific
`NeedsRefresh` lead times and `RefreshFailureReason` mapping. New `*OAuthOptions` classes (`Publishing:*`
sections), DI `Configure`, add the three platforms to the `OAuthPlatforms` allow-list, and the
`?purpose=analytics` param threaded into state so the callback stores `Purpose=Analytics`. (Plan §7.1/§7.3/§7.4;
TDD Step 3.)

### section-04-analytics-services
Keyed `IChannelAnalyticsService` (one per platform) + thin clients returning `ChannelPollResult`. YouTube
(Data v3 via uploads playlist — not search; batched videos.list; plus a live Analytics v2 deep path).
Instagram (Graph Insights, deprecation-tolerant metric list). TikTok (Display API, cursor pagination to N).
All fully tested against canned JSON with injected HTTP/SDK clients. (Plan §6; TDD Step 4.)

### section-05-snapshot-poller
`ChannelMetricPollingService : BackgroundService` (hourly self-timed loop, host-local date, per-run scope,
idempotency guard, provider-driven refresh with revoked-only deactivation, video-rows-first/account-row-last
transactional upsert, N-cap), `ChannelAnalyticsOptions`, and `AddHostedService` DI registration. (Plan §8;
TDD Step 5.)

### section-06-read-api
MediatR queries `GetChannelAnalytics` (KPIs + delta trends + engagement rate + status), `GetAnalyticsOverview`
(aggregate audience + sparklines), `GetYouTubeDeepAnalytics` (live). `ChannelAnalyticsEndpoints` group +
`Program.cs` wiring. Snapshot-backed except the one live YouTube path. (Plan §9, §10; TDD Step 6.)

### section-07-frontend
Analytics shell with PrimeNG tabs (Overview | Website | YouTube | Instagram | TikTok); `overview.component`
(total audience labeled approximate + per-platform sparklines); generic `channel-analytics.component` (KPI
row + trend charts + recent-posts table + connect/reconnect affordance); extract existing Website markup into
its own component unchanged; YouTube tab's live deep-analytics panel; `analytics.service` methods + models.
Signals only, no NgRx. Frontend tests via Karma headless. (Plan §12; TDD Step 7.)

### section-08-console-runbook
`planning/channel-analytics/runbook.md`: step-by-step Google Cloud (enable YouTube Data v3 + Analytics v2,
scopes, API key, consent screen "In Production", redirect URI), Meta (IG Business/Creator, Instagram-Login
scopes, redirect URI, no App Review for own account), TikTok (add user.info.stats + video.list, redirect URI,
review), where each secret goes, and end-to-end verification. Documentation only, no tests. (Plan §13.)
