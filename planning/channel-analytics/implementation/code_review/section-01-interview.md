# Section-01 Review Triage & Decisions

No user interview required — none of the findings involve a security decision or a real product tradeoff. Decisions made autonomously (per the highest-risk nature of this section, the two test-coverage gaps are worth closing immediately).

## Auto-fixed

### #2 (MEDIUM) — Assert authorize-URL param ordering byte-for-byte
Ordering is a hard spec constraint ("query params, scope strings, ordering"). Added an ordered-key-sequence assertion to both provider `BuildAuthorization` tests so a future param reorder fails.
- `LinkedInOAuthProviderTests.BuildAuthorization_ProducesUnchangedUrlScopesAndState` — assert key order `[response_type, client_id, redirect_uri, scope, state]`.
- `TwitterOAuthProviderTests.BuildAuthorization_IncludesPkceS256ChallengeAndPersistsVerifier` — assert key order `[response_type, client_id, redirect_uri, scope, state, code_challenge, code_challenge_method]`.

### #3 (MEDIUM) — Coordinator Twitter refresh + rotated-token re-encrypt
Added `OAuthServiceTests.RefreshTokenAsync_Twitter_RoutesThroughProviderAndReEncryptsRotatedToken`: exercises keyed resolution to the Twitter provider through the coordinator, asserts success, and Verifies the coordinator re-encrypts BOTH the new access token and the rotated refresh token — the highest-traffic path, previously only covered for LinkedIn and never asserting rotation re-encrypt.

## Let go (with rationale)

### #1 (LOW) — refresh_token presence vs non-null
The new coordinator uses `tokens.RefreshToken is not null`; the original used `TryGetProperty` presence. The only divergence is the pathological `"refresh_token": null` JSON, which the original would have thrown/encrypted-null on. New behavior is strictly safer and this shape never occurs from LinkedIn/Twitter. No change.

### #4 (LOW) — `RefreshFailureReason` unused this section
KEPT. The section spec (summary + step 1 scope note) explicitly enumerates `RefreshFailureReason` as a section-01 deliverable, front-loaded because section-03 and the poller (section-05) depend on the contract. Removing it now would only churn the contract file and re-add it two sections later. This is a documented, intentional forward-declaration, not accidental speculation — the plan overrides pure YAGNI here.

### #5 (LOW) — `GetKeyedService` service-locator
KEPT. Plan explicitly directs keyed resolution to match the repo's keyed-DI convention (already used for connectors). No captive-dependency risk (coordinator + providers both Scoped, providers stateless). Acceptable; revisit if section-03 makes the map unwieldy.
