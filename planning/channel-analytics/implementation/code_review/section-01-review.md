# Section-01 Code Review — OAuth Provider Refactor

**Reviewer verdict:** No Critical or High issues. The refactor meets the "zero behavior change on the wire" bar. Every path traced byte-for-byte against the deleted code:

- LinkedIn authorize URL (base, scope, param order response_type/client_id/redirect_uri/scope/state)
- Twitter authorize URL (PKCE S256, code_challenge/method appended last)
- LinkedIn exchange (PostAsync, client_secret in body, no Basic header)
- Twitter exchange (SendAsync, Basic header, code_verifier in body, no client_secret)
- LinkedIn refresh (client_secret in body, no header) / Twitter refresh (Basic header, client_id only)
- Dictionary insertion order preserved → FormUrlEncodedContent serialization unchanged
- State TTL (10 min), MaxPendingStates (1000), CleanExpiredStates, 32-byte hex state, credential upsert fields, IsActive semantics, no-refresh-token deactivation branch — all preserved
- `IOAuthService` public signature unchanged; no "untouched by design" file edited
- `NeedsRefresh` mirrors connectors (LinkedIn 5 min, Twitter 10 min, same comparison) — verified against LinkedInConnector.cs:28/134-135 and TwitterConnector.cs:26/236-237

## Findings

| # | Severity | Category | Finding |
|---|----------|----------|---------|
| 1 | LOW | behavior edge-case | refresh_token rotation changed from property-PRESENCE (`TryGetProperty`) to value-NON-NULL (`is not null`). New behavior is strictly safer; the `"refresh_token": null` JSON shape never occurs in practice. Note only. |
| 2 | MEDIUM | test gap | Authorize-URL ordering is a hard spec constraint, but all tests parse into an order-insensitive `NameValueCollection`. A param-reorder regression would pass. |
| 3 | MEDIUM | test gap | Coordinator refresh only tested for LinkedIn; no coordinator test for Twitter refresh routing, and no assertion that a rotated refresh_token is re-encrypted+persisted at the coordinator (the highest-traffic path). |
| 4 | LOW | YAGNI / dead code | `RefreshFailureReason` enum declared but unreferenced this section (wired in section-03). |
| 5 | LOW | convention | Coordinator uses `GetKeyedService` (service-locator) rather than injected map. Spec-directed; no captive-dependency risk. Acceptable. |

Full reviewer notes preserved in the interview transcript.
