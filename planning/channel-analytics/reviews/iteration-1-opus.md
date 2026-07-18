# Opus Review

**Model:** claude-opus (deep-plan opus-plan-reviewer)
**Generated:** 2026-07-17

---

## CRITICAL

### C1. The `Provisional` / late-correction story is incoherent with the idempotency guard
Sections 4.4, 8.1 (step 1), and 16 describe a `Provisional` flag for late-finalizing data so "trend lines
can be corrected," but the poller's idempotency guard skips the entire platform if an Account snapshot
already exists for `(platform, today)`. Nothing ever re-fetches or overwrites a provisional row — the
correction path does not exist. Either make correction real (guard: final row → skip; provisional row →
refresh; write = update) or cut `Provisional` (YAGNI). Also a conceptual muddle: the Account bag mixes
*cumulative* counts (subscribers, total views — meaningful as-of-capture) with YouTube Analytics *per-day
deltas* (`subscribersGained`, keyed to a calendar day that finalizes 2-3 days later). Different correction
semantics conflated under one `SnapshotDate`. Define what `SnapshotDate` means.

### C2. The unique index does not enforce Account-row uniqueness in PostgreSQL
`UNIQUE (Platform, SnapshotDate, Scope, VideoId)` with null `VideoId` for Account scope: PostgreSQL treats
NULL as distinct, so account rows can duplicate freely and the "idempotent upsert" (`ON CONFLICT`) silently
degrades to plain INSERT. Fix: non-nullable `VideoId` with a sentinel (`""` for Account), or `NULLS NOT
DISTINCT` (PG15+, confirm EF/Npgsql emits it). Sentinel is simpler and makes upsert work.

### C3. Instagram token refresh does not fit the `RefreshAsync(refreshToken, …)` contract
The Instagram-Login model has NO separate refresh token; you extend the long-lived access token via
`refresh_access_token`. The existing `RefreshTokenAsync` returns `Fail("No refresh token available")` +
sets `IsActive=false` when no refresh token — so IG polling marks the credential dead on day one. Widen the
contract to `RefreshAsync(PlatformCredential, …)` and let each provider handle its own refresh.

## HIGH

### H1. OAuth refresh-path regression is not guarded — only authorize/exchange are
Publishing actively calls `RefreshTokenAsync` in production (LinkedInConnector:137, TwitterConnector:239).
Regression tests (14) only cover authorize/exchange. Add per-provider refresh-path regression tests; state
in 7.2 where the current per-platform refresh internals move (into each provider's `RefreshAsync`).

### H2. Twitter PKCE verifier lifecycle is ambiguous across the OAuthService/provider boundary
Who generates/stores the S256 `code_verifier` — coordinator or provider? If the coordinator does it
generically, Twitter-specific logic leaks back into OAuthService, defeating the refactor. Have the provider
produce `(url, stateAdditions)` and the coordinator persist.

### H3. Refresh failure blanket-sets `IsActive=false`; no error taxonomy
Any refresh failure (transient blip, TikTok 429) wrongly forces manual reconnect. Define a
`RefreshFailureReason`/`PollFailureReason` enum using the revoked-vs-transient signals in research B4;
deactivate only on genuine revocation.

### H4. No implementation ordering — the riskiest piece isn't sequenced first
Section 15 is deploy order, not build order. The OAuth refactor is highest-risk and a hard dependency; it
should land + verify green (LinkedIn/Twitter regression) in isolation first. Add a build sequence:
(1) OAuth refactor + regression; (2) entity + migration; (3) services; (4) poller; (5) queries/endpoints;
(6) frontend.

### H5. Partial-write atomicity — the guard sentinel can leave orphaned polls
If a run crashes after the Account row but before video rows, the next day's guard skips and video snapshots
are lost. Wrap per-platform write in a transaction, or write video rows first and the Account row last as a
true completion sentinel.

## MEDIUM

- **M1.** YouTube recent-video discovery must use `channels.list → uploads playlist → playlistItems.list`
  (1 unit, chronological), NOT `search.list` (100 units, unreliable ordering).
- **M2.** Engagement-rate KPI can't be a `long`; add `double? Rate` and define the per-platform formula.
- **M3.** `IReadOnlyDictionary<string,long>` can't hold fractional metrics (averageViewDuration, ratios,
  revenue). State the "integer counts/seconds only" invariant or widen the type.
- **M4.** The poller BackgroundService starts during `WebApplicationFactory` tests. Ensure gates default
  false AND no credential exists, or strip hosted services in the test factory. Note it.
- **M5.** TikTok `/v2/video/list/` caps `max_count ≤ 20/page`; add the cursor-pagination loop to reach N=50.
- **M6.** `SnapshotDate` UTC vs `RunAtLocalTime` local — pick one clock for both guard key and SnapshotDate.
- **M7.** Provider-specific refresh lead time (Google 1h, TikTok 24h, IG proactive ~50d) — one threshold
  can't serve all.
- **M8.** `IOptionsMonitor` vs DigestService's `IOptions` contradicts "mirror exactly" — pick one, drop
  "exactly."

## LOW / YAGNI

- **L1.** ETag/`If-None-Match` buys ~nothing for a once-daily poll (resource changes daily). Cut it.
- **L2.** The migration SQL must drop the specific old index by name (from
  `20260527132050_AddMultiPlatformPublishing`) so the idempotent apply doesn't leave both.
- **L3.** `TotalAudience` is approximate (YouTube subscriberCount rounded to 3 sig figs) — note in UI copy.

## Genuinely fine (do not second-guess)
jsonb metric bag over typed columns; the Purpose composite-index migration (relaxes uniqueness → safe);
collapsing to one keyed `IChannelAnalyticsService`; snapshot-backed reads decoupled from live API; the
gated-but-code-complete rollout; the `IOAuthProvider` refactor itself (user-locked, justified).

C1-C3 ship silent data bugs — block on those. H1-H2 most likely to regress existing publishing.
