# Section-03 Review Triage & Decisions

Security-sensitive section. No user interview required — the HIGH finding has one clearly-correct minimal fix; the rest are robustness improvements or documented deferrals with no product tradeoff. Decisions autonomous.

## Auto-fixed

### #1 (HIGH) — Constrain `?purpose=analytics` to the analytics platforms
Added an `AnalyticsPlatforms = {YouTube, Instagram, TikTok}` guard in the authorize endpoint: `purpose=analytics` for any other platform → `400`. This fully closes the hazard — you can no longer create a LinkedIn/Twitter Analytics credential, so the publishing connectors' `Platform`-only lookups always resolve the Publishing credential, and the three analytics platforms have no publishing lookups. LinkedIn analytics is impossible (project memory) and Twitter analytics is out of scope, so this constraint costs nothing and is future-honest: if analytics is ever added for a publishing platform, THAT change must also make the publishing queries purpose-aware. Added `Authorize_AnalyticsPurposeForPublishingPlatform_Returns400`.

### #3 (MEDIUM) — Guard the RefreshAsync success path (never throw)
Wrapped the 200-branch parse+map in all three new providers in a try/catch (JsonException / KeyNotFoundException / InvalidOperationException) → `Transient`. Honors the plan's explicit "never throw out of RefreshAsync" and makes the three providers consistent.

### #4 (MEDIUM) — Safety-direction tests
Added per-provider "non-revoked error → Transient" tests: YouTube 400 without `invalid_grant`; TikTok 400 with a different error code; Instagram benign 400 (non-190). Split Instagram's revoked test to assert code=190 alone (isolates the branch). Added a malformed-200-body → Transient test.

### #6 (LOW) — TryParsePurpose accepts only named values
Replaced `Enum.TryParse` with an explicit `publishing`/`analytics` (case-insensitive) match; empty→Publishing; anything else (incl. numeric strings) → `400`.

### #7 (LOW) — IsMetaRevoked tolerates string-encoded codes
`IsMetaRevoked` now reads code/error_subcode whether the JSON value is a Number or a numeric String.

## Let go (documented, deferred to section-05)

### #2 (MEDIUM) — Coordinator deactivates no-refresh-token credentials
KEPT. The coordinator's "null refresh token → deactivate" branch is section-01's preserved publishing-path behavior; no caller routes Instagram/YouTube/TikTok through `IOAuthService.RefreshTokenAsync` today (they have no publishing connectors). Restructuring it now would risk section-01's verbatim guarantee for a caller that doesn't exist. **Section-05 constraint (documented in the section doc): the poller MUST call `provider.RefreshAsync` directly, never `IOAuthService.RefreshTokenAsync`, so Instagram's no-refresh-token path works.**

### #5 (LOW-MED) — Single revoked signal per provider
KEPT for section-03. The providers map exactly the revoked signals the plan specified (YouTube `invalid_grant`, Meta 190/458/463, TikTok `access_token_invalid`); expanding to every possible revocation code needs the OAuth research/runbook (section-08) and authoritative code lists, not guessing. Residual risk: a genuine revocation surfacing an unlisted code maps to Transient → the poller retries indefinitely. **Section-05 recommendation (documented): give the poller a backstop — e.g. after N consecutive Transient failures over a long window, alert and/or deactivate — so a mis-mapped signal can't loop forever.**
