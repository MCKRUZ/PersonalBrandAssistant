# Section-06 Review Triage & Decisions

No user interview required — H1/M1 are clear correctness fixes; the rest match the plan or are documented. Decisions autonomous.

## Auto-fixed

### H1 (HIGH) — Stale access token on the live deep path
The live YouTube deep path used the STORED access token with no refresh; Google tokens expire ~1h after the daily poll, so the tab would fail ~23h/day. Introduced an Application-layer `IAnalyticsTokenProvider` (impl `AnalyticsTokenProvider` in Infrastructure) that ensures a fresh token on demand — resolves the keyed `IOAuthProvider`, runs the same NeedsRefresh/RefreshAsync/revoked-only-deactivation/re-persist dance as the poller, and returns the decrypted token (or Fail on missing/inactive/revoked/transient). Required because the Application handler can't inject the Infrastructure `IOAuthProvider` (Application doesn't reference Infrastructure). The deep handler now depends on `IYouTubeApiClient` + `IAnalyticsTokenProvider` (no DbContext/encryptor). Rewrote the deep-handler tests accordingly (fresh-token / token-unavailable-skips-client / success-maps).

### M1 (MEDIUM) — Nondeterministic status with a stale inactive credential
`ResolveStatusAsync` now `OrderByDescending(c => c.IsActive)` before FirstOrDefault, so a reconnect that leaves a stale inactive row beside the new active one reports Connected (not ReconnectRequired) and keeps the channel in TotalAudience. Added `ResolvesStatus_PrefersActiveOverStaleInactive`.

### Test gaps closed
Added: negative delta (subscribers lost), engagement `total_interactions` precedence (no double-count), engagement null when no interactions, engagement null when denominator zero.

## Documented (match the plan / minor — no code change)

- **M2** — KPI/trend per key: those keys ARE the canonical account keys (the poller writes only canonical keys), so this matches the plan's "for each canonical account metric key." Engagement card's `Value=0` sentinel with the number in `Rate` is a minor frontend special-case.
- **M3** — `CombinedKpis = [total_audience]`: only the audience sum is cross-platform-meaningful (summing YouTube vs IG "views" is semantically dubious; TikTok has no account "views"). Per-platform detail lives in `Channels`.
- **M4** — KPI `DeltaPct` day-over-day: matches the plan's literal "prior snapshot in-range." Period-over-period, if wanted, is a frontend (section-07) concern.
- **M5** — index-based `DeltaSeries`: matches "consecutive snapshots"; the poller's hourly retry keeps gaps rare, and a rare merged delta is an acceptable visual artifact.
- **L1** — `FollowerSparkline` is a delta series (daily growth) per the plan's explicit "delta series over the window."
- **L2** — a ReconnectRequired channel shows last-known Followers but is excluded from TotalAudience (shows the count while flagging it's disconnected).
- **L3** — `Enum.TryParse("5") → YouTube → 200`: harmless (resolves to a real analytics platform; the `AnalyticsPlatforms` set still gates non-analytics platforms).
