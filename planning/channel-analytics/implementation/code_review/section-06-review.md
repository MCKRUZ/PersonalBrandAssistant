# Section-06 Code Review — Read API

**Reviewer verdict:** Well-structured, faithfully mirrors `GetWebsiteAnalytics`. Delta/engagement math fundamentally correct. One HIGH design gap, several MEDIUM/LOW.

## Confirmed correct
- Delta baseline: cumulative `[100,105,111] → [+5,+6]`, first row seeds baseline, no off-by-one.
- Engagement precedence: reach-when-present else `AudienceValue(followers‖subscribers)`; `total_interactions` preferred over the individual sum (no double-count).
- `BuildKpis` latest/prior guarded (Count≥2); single-snapshot safe.
- NotConnected → empty DTO (not error); TotalAudience sums Connected only.
- Period → host-local `DateOnly` window matches the poller's clock; default 30d; LinkedIn/unknown → 400.
- `GetYouTubeDeepAnalytics` never throws (all 4 reports + Decrypt in one try/catch; missing/inactive cred → Fail first).
- M4 isolation: poller stripped by `ImplementationType` FullName match; `TestFactory_DoesNotStart...` asserts it. Valid.
- `ToApiResult` maps General Fail → Problem (500).

## Findings

| # | Severity | Category | Finding |
|---|----------|----------|---------|
| H1 | HIGH | correctness | The live deep path decrypts and uses the STORED YouTube access token with NO refresh. Google tokens expire in ~1h; only the daily poller refreshes. So the token is stale ~23h/day → the YouTube deep tab fails almost all day, every day. Not an edge case — the common case. Must ensure a fresh token before the client call. (Layering: the Application handler can't inject the Infrastructure `IOAuthProvider`, so this needs an Application `IAnalyticsTokenProvider` abstraction.) |
| M1 | MEDIUM | correctness | `ResolveStatusAsync` uses `FirstOrDefault(Platform && Purpose==Analytics)` with no active-preferring order. Section-02's index allows an inactive + active analytics cred to coexist (filter is `WHERE IsActive`); FirstOrDefault may return the inactive one → wrong `ReconnectRequired` + drops the channel from TotalAudience. The deep handler filters `&& IsActive`, so the two paths can disagree. |
| M2 | MEDIUM | design | `BuildKpis`/`BuildTrends` emit a card/series for every key in the bag — but those ARE the canonical account keys (poller writes only canonical keys), so this matches the plan. Engagement card carries `Value=0` sentinel (frontend special-cases by Key). Acceptable. |
| M3 | MEDIUM | spec | `CombinedKpis` is just `total_audience` (plan says plural). Only the audience sum is cross-platform-meaningful (summing YouTube vs IG "views" is dubious). |
| M4 | MEDIUM | spec | KPI `DeltaPct` is day-over-day (prior snapshot), not period-over-period — matches the plan's literal "prior snapshot in-range." |
| M5 | MEDIUM | edge | `DeltaSeries` is index-based; a poller gap (skipped day) makes one point span >1 calendar day. Matches "consecutive snapshots"; hourly retry keeps gaps rare. |
| L1 | LOW | naming | `FollowerSparkline` is a delta series (per plan), not cumulative levels. |
| L2 | LOW | design | Overview shows last-known Followers for ReconnectRequired channels but excludes them from the total (defensible). |
| L3 | LOW | validation | `Enum.TryParse<Platform>("5")` → YouTube → 200. Harmless (resolves to a real analytics platform). |

## Test gaps
Negative deltas (subscribers lost), engagement null branches (zero/absent denominator, uncomputable interactions), the `total_interactions`+individual-keys no-double-count case, and M1 (multiple creds → status).
