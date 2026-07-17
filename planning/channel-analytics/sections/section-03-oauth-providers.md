# section-03-oauth-providers

## Goal

Add analytics-scoped OAuth support for three new social platforms — **YouTube, Instagram, and TikTok** — on top of the `IOAuthProvider` abstraction introduced in section-01. This section delivers:

1. Three new `IOAuthProvider` implementations with hardcoded analytics scopes and their own refresh behavior.
2. Three new `*OAuthOptions` config classes bound to `Publishing:*` sections.
3. DI registrations (`Configure<...>` + keyed `IOAuthProvider` registrations).
4. The three platforms added to the `OAuthEndpoints` allow-list.
5. A `?purpose=analytics` query param on `/{platform}/authorize` threaded through OAuth state so the callback stores the credential with `Purpose = Analytics`.

Everything is built and tested against **mocked HTTP responses** — no real token is required for this section to go green.

## Dependencies (already delivered — do not re-implement)

**From section-01-oauth-provider-refactor** (in `src/PBA.Infrastructure/Security/`):
- `IOAuthProvider` interface:
  ```
  interface IOAuthProvider {
      Platform Platform { get; }
      AuthorizationRequest BuildAuthorization(string state);   // record: (string Url, OAuthStateAdditions Additions)
      Task<OAuthTokenResult> ExchangeCodeAsync(string code, OAuthStateEntry state, CancellationToken ct);
      Task<Result<OAuthTokenResult>> RefreshAsync(PlatformCredential credential, CancellationToken ct);
      bool NeedsRefresh(PlatformCredential credential, DateTimeOffset now);
  }
  ```
- Supporting types: `AuthorizationRequest`, `OAuthStateAdditions`, `OAuthStateEntry`, `OAuthTokenResult`, and `RefreshFailureReason { Revoked, Transient }` (carried on the `Result<OAuthTokenResult>` failure).
- Existing providers `LinkedInOAuthProvider` / `TwitterOAuthProvider` under `Security/OAuthProviders/` — mirror their structure, ctor injection (options + `ITokenEncryptor` + `IHttpClientFactory` + logger), and HTTP usage.
- `OAuthService` is now a thin coordinator that resolves keyed `IOAuthProvider` by `Platform`, persists `OAuthStateAdditions` into its `StateStore`, and upserts `PlatformCredential` via `TokenEncryptor`.
- Providers are registered with **keyed DI by `Platform`** (`AddKeyedScoped<IOAuthProvider, ...>(Platform.X)`).

**From section-02-domain-persistence** (in `src/PBA.Domain/`):
- `Platform` enum now includes `Instagram` and `TikTok`; `YouTube = 5` already existed.
- `CredentialPurpose { Publishing = 0, Analytics = 1 }` enum exists.
- `PlatformCredential.Purpose` (init-only, defaults `Publishing`) exists, with the composite filtered unique index `(Platform, Purpose)` where `IsActive`.

If either dependency is not present, stop — this section cannot be implemented in isolation without them.

## Background context an implementer needs

**Why analytics tokens are distinct from publishing tokens.** Analytics needs the same channel owner's token but at *different scopes* (read-only analytics scopes), so it lives as a separate credential with `Purpose = Analytics`. One active `Publishing` and one active `Analytics` credential can coexist per platform (the section-02 composite index enables this). The `?purpose=analytics` flow is what makes the callback stamp the new credential correctly.

**Config convention.** Existing providers bind options from `Publishing:*` sections (e.g. `TwitterOptions.SectionName = "Publishing:Twitter"`). **Decision from the plan: keep the `Publishing:` prefix** for the three new options classes even though these are analytics scopes — the section name is about the *app registration*, not the token purpose. Do not invent an `OAuth:` root.

**Scopes are hardcoded per provider**, exactly as LinkedIn/Twitter do today. Options carry only `Enabled`, `ClientId`, `ClientSecret`, `RedirectUri` (and `ApiKey` for YouTube). Secrets come from user-secrets (dev) / env (prod); never commit them. `appsettings.json` ships only `{ "Enabled": false }` for each.

**Refresh differs sharply by platform** (from the OAuth research):
- **YouTube (Google):** standard OAuth refresh-token grant. Proactive refresh window ~expiry − 1h. Revoked signal: Google returns `invalid_grant`.
- **TikTok:** refresh-token grant that **rotates the refresh token** — the response returns a new refresh token that must be persisted. Proactive window ~expiry / 24h. Revoked signal: `access_token_invalid` (or equivalent error code).
- **Instagram (Instagram-Login model):** **there is NO separate refresh token.** Refresh means calling the "refresh a long-lived access token" endpoint (`ig_refresh_token` grant on `graph.instagram.com`) to extend the existing long-lived access token (~60-day lifetime). Proactive window ~50 days. This is exactly why `RefreshAsync` takes the full `PlatformCredential` (not a bare refresh-token string) — Instagram must not fail on a missing refresh token. Revoked signal: Meta error subcodes 190 / 458 / 463.
- **Transient** (all providers): network error / 5xx / 429 -> `RefreshFailureReason.Transient`. The poller (section-05) deactivates a credential only on `Revoked`, never on `Transient`.

## Tests FIRST (TDD Step 3)

Write these as xUnit stubs before implementing. Naming: `Method_Scenario_ExpectedResult`. Feed canned token/refresh JSON to a stubbed `HttpClient` (via a fake `HttpMessageHandler`); mock `TimeProvider` for `NeedsRefresh` windows. The implementer writes the assertions.

Per provider — instantiate `{Provider}` for each of YouTube, Instagram, TikTok:
```
# {Provider}_BuildAuthorization_IncludesCorrectScopesAndRedirectUri
# {Provider}_ExchangeCode_MapsTokenResponseToOAuthTokenResult          (canned token JSON)
# YouTubeProvider_RefreshAsync_UsesRefreshToken
# TikTokProvider_RefreshAsync_PersistsRotatedRefreshToken
# InstagramProvider_RefreshAsync_ExtendsLongLivedAccessToken_NoRefreshToken   (must NOT fail on missing refresh token)
# {Provider}_NeedsRefresh_ReturnsTrue_WithinProviderLeadTime            (Google ~1h, TikTok ~24h, IG ~50d)
# {Provider}_RefreshAsync_MapsRevokedSignal_ToRefreshFailureReasonRevoked
# {Provider}_RefreshAsync_MapsNetworkError_ToRefreshFailureReasonTransient
```

Endpoint / allow-list (`WebApplicationFactory<Program>`):
```
# OAuthEndpoints_AllowsYouTubeInstagramTikTok_Authorize
# OAuthEndpoints_Authorize_WithPurposeAnalytics_StoresCredentialAsAnalytics
```

Notes for the endpoint tests:
- `OAuthEndpoints_AllowsYouTubeInstagramTikTok_Authorize` — asserts `/api/auth/youtube/authorize` (and instagram, tiktok) no longer returns a "does not support OAuth" error; it hits the coordinator and issues a redirect.
- `OAuthEndpoints_Authorize_WithPurposeAnalytics_StoresCredentialAsAnalytics` — end-to-end assertion that `?purpose=analytics` on authorize is threaded through state so that when the callback runs `ExchangeCodeAsync`, the resulting `PlatformCredential` is persisted with `Purpose = Analytics`. The default (no param) still yields `Publishing`.

**Revoked-vs-transient mapping** is the highest-value correctness test here — get the per-provider signal -> `RefreshFailureReason` mapping right (Google `invalid_grant`, Meta 190/458/463, TikTok `access_token_invalid` -> `Revoked`; network/5xx/429 -> `Transient`), because section-05's deactivation logic depends entirely on it.

## Implementation

### 1. Options classes — `src/PBA.Infrastructure/Configuration/`

Mirror the existing `TwitterOptions` shape exactly (init-only, `required` on secrets, `SectionName` const). Three new files:

```
YouTubeOAuthOptions.cs    SectionName = "Publishing:YouTube"
  { bool Enabled; required string ClientId; required string ClientSecret; required string RedirectUri; string? ApiKey; }

InstagramOAuthOptions.cs  SectionName = "Publishing:Instagram"
  { bool Enabled; required string ClientId; required string ClientSecret; required string RedirectUri; }

TikTokOAuthOptions.cs     SectionName = "Publishing:TikTok"
  { bool Enabled; required string ClientId; required string ClientSecret; required string RedirectUri; }
```

`appsettings.json` (in `src/PBA.Api/`): add `Publishing:YouTube`, `Publishing:Instagram`, `Publishing:TikTok` sections each shipping `{ "Enabled": false }` only. Real ClientId/ClientSecret/ApiKey/RedirectUri go to user-secrets (dev) / env (prod).

### 2. Providers — `src/PBA.Infrastructure/Security/OAuthProviders/`

Three new files, each implementing `IOAuthProvider`, following the structure of the migrated `LinkedInOAuthProvider` / `TwitterOAuthProvider` (ctor-injected options, injected `HttpClient` / `IHttpClientFactory`, `ITokenEncryptor`, `TimeProvider`).

**`YouTubeOAuthProvider.cs`** — `Platform => Platform.YouTube`
- `BuildAuthorization`: Google authorize URL with hardcoded analytics scopes `yt-analytics.readonly` and `youtube.readonly`, `access_type=offline`, `prompt=consent` (needed to guarantee a refresh token), `RedirectUri` from options, `state`. No extra state additions.
- `ExchangeCodeAsync`: standard Google token exchange (`https://oauth2.googleapis.com/token`) -> map to `OAuthTokenResult`.
- `RefreshAsync`: refresh-token grant. Decrypt the credential's refresh token (inject `ITokenEncryptor` as the other providers do). Google `invalid_grant` -> `Result.Fail` with `RefreshFailureReason.Revoked`; network/5xx/429 -> `Transient`.
- `NeedsRefresh`: `true` when `now >= AccessTokenExpiresAt - 1h`.

**`InstagramOAuthProvider.cs`** — `Platform => Platform.Instagram`
- `BuildAuthorization`: Instagram-Login authorize URL (`https://www.instagram.com/oauth/authorize`) with hardcoded scopes `instagram_business_basic` + `instagram_business_manage_insights`, `RedirectUri`, `state`.
- `ExchangeCodeAsync`: short-lived token exchange -> **immediately exchange for a long-lived token** (`graph.instagram.com/access_token?grant_type=ig_exchange_token`). Map the long-lived token + ~60-day expiry to `OAuthTokenResult`. **No refresh token** — leave it null/absent.
- `RefreshAsync`: call the long-lived-token refresh endpoint (`graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token&access_token={current}`). Must **not** fail when the credential has no refresh token — it uses the current access token. Returns a new access token + extended expiry. Meta error subcodes 190/458/463 -> `Revoked`; network/5xx/429 -> `Transient`.
- `NeedsRefresh`: `true` when `now >= AccessTokenExpiresAt - 50 days`.

**`TikTokOAuthProvider.cs`** — `Platform => Platform.TikTok`
- `BuildAuthorization`: TikTok authorize URL (`https://www.tiktok.com/v2/auth/authorize/`) with hardcoded scopes `user.info.stats` + `video.list`, `client_key` from options, `RedirectUri`, `state`.
- `ExchangeCodeAsync`: token exchange at `https://open.tiktokapis.com/v2/oauth/token/` -> map to `OAuthTokenResult` (access token, refresh token, expiry).
- `RefreshAsync`: refresh-token grant at the same token endpoint. TikTok **rotates the refresh token** — capture the new refresh token from the response into the returned `OAuthTokenResult` so section-05's poller can persist it. `access_token_invalid` -> `Revoked`; network/5xx/429 -> `Transient`.
- `NeedsRefresh`: `true` when `now >= AccessTokenExpiresAt - (AccessTokenLifetime / 24)` ~= expiry − 1h for a 24h token.

All three: catch API/HTTP exceptions and return `Result.Fail` with the appropriate `RefreshFailureReason` — never throw out of `RefreshAsync`.

### 3. DI registration — `src/PBA.Infrastructure/DependencyInjection.cs`

Add alongside the section-01 provider registrations:
```csharp
services.Configure<YouTubeOAuthOptions>(config.GetSection(YouTubeOAuthOptions.SectionName));
services.Configure<InstagramOAuthOptions>(config.GetSection(InstagramOAuthOptions.SectionName));
services.Configure<TikTokOAuthOptions>(config.GetSection(TikTokOAuthOptions.SectionName));

services.AddKeyedScoped<IOAuthProvider, YouTubeOAuthProvider>(Platform.YouTube);
services.AddKeyedScoped<IOAuthProvider, InstagramOAuthProvider>(Platform.Instagram);
services.AddKeyedScoped<IOAuthProvider, TikTokOAuthProvider>(Platform.TikTok);
```
Register any named/typed `HttpClient`s the providers need following the repo's existing `AddHttpClient` convention.

### 4. Endpoint changes — `src/PBA.Api/Endpoints/OAuthEndpoints.cs`

Current state (the thing you are modifying):
```csharp
private static readonly HashSet<Platform> OAuthPlatforms = [Platform.LinkedIn, Platform.Twitter];
```

**Change 1 — allow-list:** add the three platforms:
```csharp
private static readonly HashSet<Platform> OAuthPlatforms =
    [Platform.LinkedIn, Platform.Twitter, Platform.YouTube, Platform.Instagram, Platform.TikTok];
```

**Change 2 — `?purpose=analytics` on authorize.** Thread an optional `purpose` query param (default `publishing`) into the OAuth state so the callback knows which `CredentialPurpose` to stamp:
- Parse `string? purpose`; map to `CredentialPurpose` (`"analytics"` -> `Analytics`, anything else / absent -> `Publishing`). Invalid values -> `BadRequest`.
- Pass the purpose into the authorize flow so it is persisted in the `OAuthStateEntry` (via section-01's `OAuthService` / `StateStore`). The callback (`/{platform}/callback`) then reads the purpose off the state entry and calls the coordinator's `ExchangeCodeAsync` with that `CredentialPurpose`, so the resulting `PlatformCredential.Purpose` is correct.

Whether the purpose is carried by adding a parameter to the coordinator method or by storing it in the state entry depends on section-01's coordinator surface — use whichever section-01 exposes; the plan's intent (§7.2/§7.4) is purpose captured at authorize time, flowing to the callback via persisted state; no new routes are added. Default behavior (no `purpose` param) must remain byte-for-byte `Publishing`.

> **Coordination note:** section-01 deliberately did NOT add the `CredentialPurpose` parameter to `IOAuthService.ExchangeCodeAsync` (it predates section-02's enum). This section adds that thread-through — extend the coordinator's exchange path to accept/carry the purpose from state now that `CredentialPurpose` exists. Keep the change minimal and preserve the default-`Publishing` behavior for LinkedIn/Twitter.

## Files to create / modify

Create:
- `src/PBA.Infrastructure/Configuration/YouTubeOAuthOptions.cs`
- `src/PBA.Infrastructure/Configuration/InstagramOAuthOptions.cs`
- `src/PBA.Infrastructure/Configuration/TikTokOAuthOptions.cs`
- `src/PBA.Infrastructure/Security/OAuthProviders/YouTubeOAuthProvider.cs`
- `src/PBA.Infrastructure/Security/OAuthProviders/InstagramOAuthProvider.cs`
- `src/PBA.Infrastructure/Security/OAuthProviders/TikTokOAuthProvider.cs`
- Test files mirroring where section-01's provider tests live (`YouTubeOAuthProviderTests.cs`, etc.) + OAuth endpoint test additions.

Modify:
- `src/PBA.Infrastructure/DependencyInjection.cs`
- `src/PBA.Api/Endpoints/OAuthEndpoints.cs`
- `src/PBA.Api/appsettings.json`

## Verification

Run `dotnet test`. Gate for this section:
- All three providers' authorize/exchange/refresh/`NeedsRefresh`/failure-mapping tests green against canned JSON.
- Endpoint tests green: the three platforms are authorizable, and `?purpose=analytics` produces an `Analytics` credential while the default stays `Publishing`.
- **No regression** in the section-01 LinkedIn/Twitter provider + endpoint tests.

## What this section explicitly does NOT do

- No `ChannelMetricSnapshot` reads/writes, no analytics API data-fetch clients (section-04).
- No poller (section-05 consumes these providers' `NeedsRefresh` / `RefreshAsync` / `RefreshFailureReason`).
- No frontend connect/reconnect UI (section-07 links to `/api/auth/{platform}/authorize?purpose=analytics`).
- No Google Cloud / Meta / TikTok console setup (section-08 runbook). These providers go green against mocks with no real credentials.

---

## Implementation Outcome (as built)

Implemented as planned. Build clean; all mock-based tests green (no real credentials needed). 57 Security tests + 15 API OAuth tests pass; full API (93) + full non-Docker Infrastructure (410) suites green — no regressions.

### Files created
- Options: `src/PBA.Infrastructure/Configuration/{YouTube,Instagram,TikTok}OAuthOptions.cs`
- Providers: `src/PBA.Infrastructure/Security/OAuthProviders/{YouTube,Instagram,TikTok}OAuthProvider.cs`
- Tests: `tests/PBA.Infrastructure.Tests/Security/OAuthProviders/{YouTube,Instagram,TikTok}OAuthProviderTests.cs`

### Files modified
- `OAuthContracts.cs` (added `OAuthRefreshResult`), `IOAuthProvider.cs`, `OAuthStateEntry.cs`, `OAuthService.cs`
- `LinkedInOAuthProvider.cs` / `TwitterOAuthProvider.cs` (RefreshAsync return type)
- `DependencyInjection.cs`, `IOAuthService.cs`, `OAuthEndpoints.cs`, `appsettings.json`
- Tests: `OAuthServiceTests.cs`, `LinkedIn/TwitterOAuthProviderTests.cs`, `TestWebApplicationFactory.cs`, `OAuthEndpointsTests.cs`

### Contract evolution (deferred here by section-01's scope note — NOT scope creep)
1. **`IOAuthProvider.RefreshAsync` now returns `Task<OAuthRefreshResult>`** (was `Task<Result<OAuthTokenResult>>`). `OAuthRefreshResult` carries `RefreshFailureReason` (Revoked/Transient) on failure — the domain `Result<T>` can't, and section-05's poller keys deactivation on it. LinkedIn/Twitter providers, the coordinator, and their tests were updated; LinkedIn/Twitter map any non-success to Transient (no distinct signal wired). Publishing connectors are unaffected (they consume the coordinator's `Result<string>`).
2. **`CredentialPurpose` threads authorize→state→exchange:** `IOAuthService.GetAuthorizationUrlAsync` gained a `purpose` param; `OAuthStateEntry` gained `Purpose`; the coordinator's `ExchangeCodeAsync` finds/creates by `(Platform, Purpose)` (an Analytics flow can't overwrite a Publishing credential). Default (no `?purpose`) stays Publishing.

### Review fixes applied (see `implementation/code_review/section-03-interview.md`)
- **HIGH:** `?purpose=analytics` is now constrained to `{YouTube, Instagram, TikTok}` (400 otherwise). Closes a data-loss hazard: a LinkedIn/Twitter Analytics credential could otherwise shadow the Publishing one in the connectors' `Platform`-only lookups.
- **MEDIUM:** all three providers' RefreshAsync success path is now guarded (malformed/missing-field 200 → Transient, never throws).
- **MEDIUM:** added per-provider SAFETY-direction tests (non-revoked error → Transient) so the poller never deactivates a valid credential; split Instagram's revoked test to isolate code=190.
- **LOW:** `TryParsePurpose` accepts only the literal `publishing`/`analytics` (numeric strings rejected); `IsMetaRevoked` tolerates string-encoded codes.

### Known constraints for section-05 (the poller)
- **MUST call `provider.RefreshAsync` directly**, never `IOAuthService.RefreshTokenAsync` — the coordinator's "null refresh token → deactivate" branch would wrongly deactivate Instagram (which has no refresh token by design).
- **Backstop recommended:** each provider recognizes only the plan-specified revoked signal; a genuine revocation surfacing an unlisted code maps to Transient → infinite retry. The poller should bound consecutive Transient failures (alert/deactivate after N over a long window).

### Docker
This section needs no DB — all tests are mock-based and ran fully in this session. (The section-02 real-DB tests remain Docker-gated.)
