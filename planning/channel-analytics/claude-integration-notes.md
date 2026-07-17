# Integration Notes — Opus Review (iteration 1)

Verdict: the review was high quality. **Integrating all CRITICAL, all HIGH, all MEDIUM, and all LOW
findings** — none were rejected. The biggest change is an architecture simplification (C1) that also
resolves M3 and part of C3. Summary of what changed in `claude-plan.md` and why.

## CRITICAL — all integrated

### C1 → snapshots are cumulative-only + final; trends are deltas; YouTube deep metrics go live
The provisional/correction machinery was half-built and conflated cumulative counts with per-day analytics
deltas. Resolution (cleaner architecture, unifies all three platforms):
- **`ChannelMetricSnapshot` stores only CUMULATIVE, as-of-capture counts** (subscribers/followers, total
  views, total likes, video counts; per-video cumulative view/like/comment/share). A cumulative count as of
  a capture instant is **always final** → `Provisional` flag **removed entirely** (also kills M3's
  fractional-metric worry: all stored values are integer counts).
- **All trends — including `subscribersGained/Lost` and growth rates — are computed from deltas between
  consecutive daily cumulative snapshots.** One consistent mechanism across YouTube/IG/TikTok; no dependence
  on any platform's day-dimension finalization lag.
- **YouTube deep day-analytics** (watch time, average view duration, traffic sources, demographics) is
  **not** stored as daily snapshots. It's queried **live from the YouTube Analytics API for the selected
  range** on the YouTube tab (that API keeps its own history, so we don't accumulate it). This still
  delivers "everything possible" for YouTube without the provisional problem.
- **`SnapshotDate` = host-local capture date** (same clock as `RunAtLocalTime`), resolving M6.

### C2 → sentinel VideoId, real uniqueness + upsert
`VideoId` is **non-nullable**; Account-scope rows use sentinel `""`. Unique index
`(Platform, SnapshotDate, Scope, VideoId)` now genuinely enforces one account row/day and enables a real
`ON CONFLICT` upsert.

### C3 → `RefreshAsync(PlatformCredential, ct)` + provider-owned refresh
`IOAuthProvider.RefreshAsync` takes the **`PlatformCredential`**, not a bare refresh-token string. Instagram's
provider extends the long-lived access token (no refresh token); YouTube/TikTok use their refresh tokens.
Each provider owns its refresh mechanics and its **refresh lead-time** (resolves M7): Google ~expiry-1h,
TikTok ~expiry (24h), Instagram proactive at ~50 days.

## HIGH — all integrated

- **H1:** Added per-provider **refresh-path regression tests** for LinkedIn/Twitter (the highest-traffic
  production path); 7.2 now states the current per-platform refresh internals decompose into each provider's
  `RefreshAsync`.
- **H2:** `IOAuthProvider.BuildAuthorization(...)` now returns `(string Url, OAuthStateAdditions State)`; the
  provider generates any PKCE `code_verifier`, the **coordinator persists** it in the StateStore. No
  provider-specific logic leaks back into `OAuthService`.
- **H3:** Added `RefreshFailureReason { Revoked, Transient }`; the poller **deactivates the credential only
  on `Revoked`** (genuine revocation signals from research B4), leaving transient failures active for the
  next run.
- **H4:** Added **Section 17 — Build sequence** (distinct from deploy order): (1) OAuth refactor +
  LinkedIn/Twitter regression, verified in isolation; (2) entity + migration; (3) per-platform services;
  (4) poller; (5) queries/endpoints; (6) frontend. This drives the section split.
- **H5:** Per-platform poll write is wrapped in a **single transaction**, and the **Account row is written
  last** as a true completion sentinel (video rows first).

## MEDIUM — all integrated
- **M1:** YouTube recent videos via `channels.list → uploads playlist → playlistItems.list` (1 unit,
  chronological). No `search.list`.
- **M2:** `KpiCard` gains `double? Rate`; engagement-rate formula defined per platform (interactions ÷
  reach where reach exists, else interactions ÷ followers).
- **M3:** Resolved by C1 (integer-only bag); invariant stated explicitly.
- **M4:** Testing section notes the poller hosted service must be **stripped in the test `WebApplicationFactory`**
  (and gates default false).
- **M5:** TikTok `video.list` **cursor-pagination loop** to reach N (20/page cap).
- **M6:** Resolved by C1 (host-local date).
- **M7:** Resolved by C3 (provider-specific lead time).
- **M8:** Keep `IOptionsMonitor` (hot-reload of `RunAtLocalTime`/`RecentVideoCount` is useful); dropped the
  "mirror DigestService exactly" wording.

## LOW — all integrated
- **L1:** **Cut ETags** — negligible value for a once-daily poll.
- **L2:** The hand-authored `scripts/migrate-add-channel-analytics.sql` **drops the old index by name**
  (from `20260527132050_AddMultiPlatformPublishing`) before creating the composite one.
- **L3:** Overview UI copy notes `TotalAudience` is approximate (YouTube subscriberCount rounded to 3 sig
  figs).

## Kept as-is (reviewer confirmed fine)
jsonb metric bag; Purpose composite-index migration safety; single keyed `IChannelAnalyticsService`;
snapshot-backed reads; gated-but-code-complete rollout; the `IOAuthProvider` refactor.
