# Section-04 Code Review — Channel Analytics Services

**Reviewer verdict:** Faithful, clean implementation. No Critical/High. Metric-bag keys fully compliant and all `long`-typed (invariant holds); fractional deep-path correctly isolated as `double`; try/catch/log/Result.Fail shape matches `GoogleAnalyticsService`; DI keyed registrations + HttpClient BaseAddresses correct; no captive-dependency issue.

## Findings

| # | Severity | Category | Finding |
|---|----------|----------|---------|
| 1 | MEDIUM | test gap | YouTube uploads-playlist paging loop (`DiscoverRecentVideoIdsAsync`) never exercised across >1 page — every test returns a single page with null token. `pageToken` advancement unproven (TikTok got a real 2-page test; YouTube didn't). |
| 2 | MEDIUM | latent bug | `YouTubeDeepAnalyticsMapper` hardcodes `day` as the only label; the plan says the client also serves traffic-source/geography/demographics variants (no `day` column → dimension column becomes a bogus all-zero series). Section-06 consumes this mapper for exactly those variants. |
| 3 | MEDIUM | test gap | IG "deprecation tolerance" (plan-critical) not actually exercised — the facade test mocks the client to already return a partial bag, so it only proves the facade is a pass-through. The real map-by-returned-name drop lives untested in `InstagramGraphClient`. |
| 4 | MED-LOW | untested seam | `InstagramGraphClient` parses account insights from `total_value.value` but per-media from `values[0].value`. If media insights also return `total_value`, media bags come back empty (silent data loss). |
| 5 | LOW | robustness | No max-page/non-advancing-cursor guard on either paging loop — a misbehaving API (has_more=true forever / non-null token, N never reached) loops unbounded on a daily poller. |
| 6 | LOW | test gap | No test asserts facades pass the exact canonical metric lists to IG/TikTok clients; YT per-video test omits the exact-key-count check the account test has. |
| 7 | LOW | security | IG passes `access_token` in the query string (proxy/log capture risk); TikTok correctly uses the `Authorization` header. |

## Untested-seam note
`YouTubeApiClient` (SDK), `InstagramGraphClient` / `TikTokDisplayClient` (HTTP) are untested by design (facades mock the seams). They need a real-credential smoke test before prod. The response-shape assumptions (#4, IG URL version prefix) are the highest-risk unverified parts.
