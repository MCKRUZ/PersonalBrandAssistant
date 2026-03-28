# Section 10 Code Review Interview

## Auto-fixes Applied

| Finding | Fix |
|---------|-----|
| LOW-01: Mutable dictionary | Changed to `ImmutableDictionary<string, string>.Empty` |
| LOW-03: Missing CancellationToken | Added `CancellationToken ct` to `ListPlatforms` and passed to `ToListAsync(ct)` |
| MED-01: GetStatus incomplete | Added `TokenExpiresAt`, `LastSyncAt`, `GrantedScopes` to response |
| MED-02: ListPlatforms incomplete | Added `LastSyncAt` to projection |
| MED-03: postId no validation | Added non-empty + max length 256 check |

## User Decisions

| Finding | Decision |
|---------|----------|
| HIGH-01: Test-post no safeguards | **Fix**: Added `TestPostRequest` record with `Confirm` flag and optional `Message`. Endpoint requires `Confirm=true` to publish. |

## Let Go

| Finding | Reason |
|---------|--------|
| HIGH-02: Tests verify mocks, not endpoints | Current tests validate contracts and logic. Full WebApplicationFactory integration tests are significant effort, deferred to E2E coverage phase |
| MED-04: FluentValidation on OAuthCallbackRequest | OAuthManager validates state/code internally. Over-engineering for a 3-field record |
| MED-05: REST convention (disconnect) | Subjective; current route is clear and self-documenting |
| LOW-02: Anonymous types | Response records are over-engineering at this stage |
| LOW-04: Missing negative-path tests | Related to HIGH-02 decision |
| LOW-05: MediaEndpoints tests | Already exists from prior session, not part of this section's scope |

## Tests: 11 passing after fixes
