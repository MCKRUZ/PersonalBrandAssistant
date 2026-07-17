# section-01-oauth-provider-refactor

## Objective

Introduce an `IOAuthProvider` abstraction and refactor the existing `OAuthService` (currently a big switch statement) into a thin coordinator that resolves keyed providers. Migrate the LinkedIn and Twitter OAuth logic **verbatim** into `LinkedInOAuthProvider` and `TwitterOAuthProvider`.

**This is the highest-regression-risk section of the entire feature.** LinkedIn/Twitter OAuth (especially token refresh) is the highest-traffic production path — it is what keeps publishing working. The goal is a pure internal restructuring with **zero behavior change**. No new platforms, no analytics code, no domain changes land here. Those come in later sections.

**Gate to pass before this section is considered done:** the full LinkedIn/Twitter authorize + exchange + refresh regression suite is green, and behavior is byte-for-byte unchanged.

This section has **no dependencies** on other sections. It **blocks** section-03 (new OAuth providers), which will plug `YouTube`/`Instagram`/`TikTok` providers into the abstraction this section creates.

## Background: what exists today

All OAuth logic currently lives in one file, `src/PBA.Infrastructure/Security/OAuthService.cs`, as a `sealed class` implementing `IOAuthService`. It uses primary-constructor DI:

```csharp
public sealed class OAuthService(
    IHttpClientFactory httpClientFactory,
    ITokenEncryptor encryptor,
    IAppDbContext db,
    IOptions<LinkedInOptions> linkedInOptions,
    IOptions<TwitterOptions> twitterOptions,
    ILogger<OAuthService> logger) : IOAuthService
```

The public interface (`src/PBA.Application/Common/Interfaces/IOAuthService.cs`) — **must remain unchanged** so `OAuthEndpoints`, `LinkedInConnector`, and `TwitterConnector` stay untouched:

```csharp
public interface IOAuthService
{
    Task<string> GetAuthorizationUrlAsync(Platform platform, CancellationToken ct);
    Task<PlatformCredential> ExchangeCodeAsync(Platform platform, string code, string state, CancellationToken ct);
    Task<Result<string>> RefreshTokenAsync(PlatformCredential credential, CancellationToken ct);
}
```

Internally, `OAuthService` today does five things that this refactor splits between the coordinator and the providers:

1. **State management (STAYS in coordinator):** a `static ConcurrentDictionary<string, OAuthStateEntry> StateStore`, a 10-minute TTL, a `MaxPendingStates = 1000` cap, `CleanExpiredStates()`, and a random 32-byte hex `state` generator. `OAuthStateEntry` is a private record `(Platform Platform, string? CodeVerifier = null)` with `CreatedAt`/`IsExpired`.
2. **Authorize URL building (MOVES to providers):** `BuildLinkedInAuthUrl(state)` and `BuildTwitterAuthUrl(state)`. Twitter generates a PKCE `code_verifier` (base64url of 64 random bytes) + S256 `code_challenge`, and stores the verifier in the state entry.
3. **Code exchange (MOVES to providers):** `ExchangeLinkedInCodeAsync` and `ExchangeTwitterCodeAsync` — Twitter uses HTTP Basic auth (`client_id:client_secret`) plus `code_verifier`; LinkedIn uses `client_secret` in the form body. Both return a private `OAuthTokenResponse` record.
4. **Credential upsert (STAYS in coordinator):** find-or-create `PlatformCredential` by platform, `TokenEncryptor.Encrypt` the tokens, set expiries/scopes/`IsActive`, `SaveChangesAsync`.
5. **Token refresh (MOVES to providers):** `RefreshTokenAsync` currently switches on platform for the token endpoint + client id/secret, adds Basic auth for Twitter, POSTs a `refresh_token` grant, and re-encrypts the rotated tokens.

The exact current source of all five is in `src/PBA.Infrastructure/Security/OAuthService.cs` (311 lines). **Copy the LinkedIn/Twitter-specific bodies verbatim — do not rewrite them.** The hard constraint is that the produced authorize URLs (query params, scope strings, ordering), the exchange requests (auth headers, form fields), and the refresh requests are identical to today.

Key literals that must be preserved exactly:
- LinkedIn scope: `openid profile w_member_social`; authorize base `https://www.linkedin.com/oauth/v2/authorization`; token endpoint `https://www.linkedin.com/oauth/v2/accessToken`.
- Twitter scope: `tweet.read tweet.write users.read media.write offline.access`; authorize base `https://twitter.com/i/oauth2/authorize`; token endpoint `https://api.twitter.com/2/oauth2/token`; Basic-auth on both exchange and refresh; `code_challenge_method=S256`.

Options classes (unchanged, reused as-is):
- `LinkedInOptions` (`SectionName = "Publishing:LinkedIn"`): `Enabled, ClientId, ClientSecret, RedirectUri`.
- `TwitterOptions` (`SectionName = "Publishing:Twitter"`): `Enabled, ClientId, ClientSecret, RedirectUri, ApiKey?, ApiSecret?`.

`TokenEncryptor` (AES-GCM 256, `src/PBA.Infrastructure/Security/TokenEncryptor.cs`) and `PlatformCredential` (encrypted tokens, `AccessTokenExpiresAt`, `RefreshTokenExpiresAt`, `Scopes`, `IsActive`, `UpdatedAt`) are used as-is.

## Tests first (write these before implementing)

Test project: `tests/PBA.Infrastructure.Tests` (the active project — **ignore the orphaned `tests/PersonalBrandAssistant.*.Tests/`**). An existing `tests/PBA.Infrastructure.Tests/Security/OAuthServiceTests.cs` already covers the current behavior with a mocked `HttpMessageHandler`, mocked `ITokenEncryptor` (returns `encrypted:{s}` / strips the prefix), in-memory `ApplicationDbContext`, and real `LinkedInOptions`/`TwitterOptions` fixtures. **Reuse that harness's setup pattern.** Test naming is `Method_Scenario_ExpectedResult` (xUnit).

New/updated provider tests go under `tests/PBA.Infrastructure.Tests/Security/OAuthProviders/`. Coordinator tests stay in `Security/OAuthServiceTests.cs`.

Regression tests (guard existing publishing — these must stay green through the whole refactor):

```
LinkedInOAuthProvider_BuildAuthorization_ProducesUnchangedUrlScopesAndState
TwitterOAuthProvider_BuildAuthorization_IncludesPkceS256ChallengeAndPersistsVerifier
TwitterOAuthProvider_ExchangeCode_UsesBasicAuthAndCodeVerifier_Unchanged
LinkedInOAuthProvider_ExchangeCode_ReturnsTokenResult_Unchanged
LinkedInOAuthProvider_RefreshAsync_RefreshesToken_BehaviorUnchanged     # highest-traffic path
TwitterOAuthProvider_RefreshAsync_UsesBasicAuth_BehaviorUnchanged
OAuthService_GetAuthorizationUrl_ResolvesKeyedProviderAndPersistsStateAdditions
OAuthService_ExchangeCode_UnknownPlatform_ReturnsNotSupported
OAuthService_ExchangeCode_DefaultsPurposeToPublishing
```

Boundary tests (prove no provider-specific logic leaks into the coordinator):

```
OAuthService_PersistsTwitterCodeVerifier_FromProviderStateAdditions
OAuthProviderMap_ResolvesOneProviderPerPlatform_ViaKeyedDI
```

Testing guidance:
- **Assert on the wire, not on internals.** For authorize URLs, parse the query string and assert `response_type`, `client_id`, `redirect_uri`, `scope`, `state` (and for Twitter, `code_challenge`, `code_challenge_method=S256`). For exchange/refresh, capture the outgoing `HttpRequestMessage` via the mocked `HttpMessageHandler` and assert the endpoint, the `Authorization: Basic ...` header (Twitter), and the form fields.
- The cleanest way to prove "verbatim" is to keep the pre-refactor assertions from the existing `OAuthServiceTests` and re-point them: the coordinator tests still call `oauthService.GetAuthorizationUrlAsync(...)` / `ExchangeCodeAsync(...)` / `RefreshTokenAsync(...)` and must produce identical output. Add the new provider-level tests that call the providers directly.
- `TwitterOAuthProvider_RefreshAsync_UsesBasicAuth_BehaviorUnchanged` must assert the refresh POST to `https://api.twitter.com/2/oauth2/token` carries the Basic auth header and a `grant_type=refresh_token` body without `client_secret` in the form (Twitter puts it in the header); LinkedIn puts `client_secret` in the body and no Basic header.
- `OAuthProviderMap_ResolvesOneProviderPerPlatform_ViaKeyedDI`: build a `ServiceProvider` from the DI registration and assert `GetKeyedService<IOAuthProvider>(Platform.LinkedIn)` and `Platform.Twitter` each resolve to the expected concrete type, and that an unregistered platform resolves to `null`.

Do not over-specify assertion bodies here — the implementer writes them against the existing harness.

## Implementation

### 1. New abstraction — `IOAuthProvider`

Create `src/PBA.Infrastructure/Security/IOAuthProvider.cs`. **Decision: keep it in Infrastructure/Security** alongside the providers, since providers are Infrastructure concerns and nothing in Application resolves them directly.

```csharp
interface IOAuthProvider {
    Platform Platform { get; }

    // Provider builds its own authorize URL AND any state it needs persisted (e.g. Twitter PKCE code_verifier).
    // The coordinator persists Additions into the StateStore; provider-specific logic never leaks out.
    AuthorizationRequest BuildAuthorization(string state);

    Task<OAuthTokenResult> ExchangeCodeAsync(string code, OAuthStateEntry state, CancellationToken ct);

    // Takes the full credential (NOT a bare refresh-token string) so future providers with no separate
    // refresh token can implement their own refresh. LinkedIn/Twitter both use a refresh_token today.
    Task<Result<OAuthTokenResult>> RefreshAsync(PlatformCredential credential, CancellationToken ct);

    // Provider-specific proactive refresh window. For LinkedIn/Twitter, preserve today's effective behavior.
    bool NeedsRefresh(PlatformCredential credential, DateTimeOffset now);
}
```

Supporting types (new records, in `Security/OAuthProviders/` or a shared `Security/OAuthContracts.cs`):

```csharp
record AuthorizationRequest(string Url, OAuthStateAdditions Additions);

// Provider tells the coordinator what extra state to persist. Today only Twitter uses CodeVerifier.
record OAuthStateAdditions(string? CodeVerifier = null);

// Replaces the private OAuthTokenResponse record. Same shape.
record OAuthTokenResult(
    string AccessToken,
    string? RefreshToken,
    int ExpiresIn,
    int? RefreshTokenExpiresIn,
    string Scopes);

// RefreshAsync failure classification. Section-03 adds full revoked-vs-transient signal mapping per
// provider; for LinkedIn/Twitter in THIS section, any non-success refresh maps to Transient by default
// EXCEPT the "no refresh token" case, which stays a plain Fail as today (see step 4 below).
enum RefreshFailureReason { Revoked, Transient }
```

`OAuthStateEntry` currently is a `private sealed record` inside `OAuthService`. Promote it to a shared type the providers can receive on `ExchangeCodeAsync` (e.g. `Security/OAuthStateEntry.cs`), keeping its fields `(Platform Platform, string? CodeVerifier = null)` plus `CreatedAt`/`IsExpired`. The coordinator still owns creating/storing/expiring these entries.

> **Scope note on `RefreshFailureReason` / `NeedsRefresh`:** the `IOAuthProvider` contract declares both because section-03 and the poller (section-05) depend on them. In THIS section, implement them for LinkedIn/Twitter with **behavior equivalent to today** — do not invent new refresh-timing semantics. `NeedsRefresh` should mirror the effective refresh window used today. The connectors (`LinkedInConnector`, `TwitterConnector`) currently decide when to refresh via their own `TokenRefreshWindow` and call `RefreshTokenAsync`; leave that path exactly as-is. `NeedsRefresh` is added to the interface for later consumers, but wiring the poller to it is section-05's job, not this one.

### 2. Migrate providers verbatim

Create under `src/PBA.Infrastructure/Security/OAuthProviders/`:

- `LinkedInOAuthProvider.cs` — implements `IOAuthProvider`, `Platform => Platform.LinkedIn`. Move `BuildLinkedInAuthUrl` → `BuildAuthorization` (returns the URL + empty `OAuthStateAdditions`), `ExchangeLinkedInCodeAsync` → `ExchangeCodeAsync`, and the LinkedIn arm of `RefreshTokenAsync` → `RefreshAsync`. Ctor takes `IHttpClientFactory`, `IOptions<LinkedInOptions>`, `ITokenEncryptor` (see step 2 decision), and its own `ILogger<LinkedInOAuthProvider>`. **The URL/scope/header/body construction must be copied exactly** from the current `OAuthService`.

- `TwitterOAuthProvider.cs` — implements `IOAuthProvider`, `Platform => Platform.Twitter`. Move `BuildTwitterAuthUrl` → `BuildAuthorization`: generate the PKCE `code_verifier` + S256 `code_challenge` inside the provider, put the verifier in the returned `OAuthStateAdditions.CodeVerifier`, and include `code_challenge`/`code_challenge_method` in the URL. Move `ExchangeTwitterCodeAsync` → `ExchangeCodeAsync` (reads `state.CodeVerifier`, uses Basic auth). Move the Twitter arm of `RefreshTokenAsync` → `RefreshAsync` (Basic auth, no `client_secret` in body). Keep the `Base64UrlEncode` helper local to the provider.

Both providers keep their platform's hardcoded scope string and endpoints. Neither provider touches the DB or the `StateStore` — those are coordinator responsibilities. **Chosen approach for refresh decryption: inject `ITokenEncryptor` into each provider** so `RefreshAsync(PlatformCredential, ct)` can decrypt `credential.EncryptedRefreshToken` itself and return the re-usable plaintext tokens in `OAuthTokenResult`; the coordinator then re-encrypts and persists. This keeps the `RefreshAsync(PlatformCredential)` signature honest (the whole point of passing the credential, not a bare string, is future providers like Instagram that have no refresh token).

### 3. `OAuthService` becomes a thin coordinator

Rewrite `src/PBA.Infrastructure/Security/OAuthService.cs` to:

- Keep the same public `IOAuthService` signatures.
- Resolve the keyed `IOAuthProvider` for the platform (via `IServiceProvider.GetKeyedService<IOAuthProvider>(platform)` — **prefer keyed resolution** to match the repo's keyed-DI convention already used for connectors).
- `GetAuthorizationUrlAsync`: run `CleanExpiredStates()` + the `MaxPendingStates` guard (unchanged), generate the 32-byte hex `state`, call `provider.BuildAuthorization(state)`, persist an `OAuthStateEntry` into `StateStore` carrying the platform and `Additions.CodeVerifier`, return the URL. Unknown/unregistered platform → same `NotSupportedException($"OAuth is not supported for {platform}")` behavior as today (resolve returns null → throw).
- `ExchangeCodeAsync`: pop + expiry-check the state entry (unchanged), call `provider.ExchangeCodeAsync(code, stateEntry, ct)`, then run the **unchanged** find-or-create + encrypt + persist logic. Unknown platform → `NotSupportedException`.
- `RefreshTokenAsync(credential, ct)`: preserve the current "no refresh token → set `IsActive=false`, `SaveChanges`, `Result.Fail`" branch **in the coordinator** (this is shared, not provider-specific). Otherwise resolve the provider, call `provider.RefreshAsync(credential, ct)`, and on the returned `OAuthTokenResult` re-encrypt + persist the new access token (and rotated refresh token if present) and return `Result<string>.Success(newAccessToken)`. On provider failure, preserve today's behavior: log, set `IsActive=false`, `SaveChanges`, `Result<string>.Fail(...)`.

> **Purpose parameter (forward-looking, minimal here):** the plan calls for `ExchangeCodeAsync` to eventually accept a `CredentialPurpose` (default `Publishing`). `CredentialPurpose` does not exist yet — it lands in section-02. **Do NOT add the purpose parameter in this section** (it would force a domain dependency this section must not have). The regression test `OAuthService_ExchangeCode_DefaultsPurposeToPublishing` asserts the *current* behavior: exchanged credentials are stored exactly as today (which is the future "Publishing" default). Section-03 threads the real `?purpose=analytics` param once section-02 has introduced the enum. Keep the public `IOAuthService.ExchangeCodeAsync` signature unchanged here.

### 4. DI registration

In `src/PBA.Infrastructure/DependencyInjection.cs`, near the existing `services.AddScoped<IOAuthService, OAuthService>();` (line 144), add keyed provider registrations:

```csharp
services.AddKeyedScoped<IOAuthProvider, LinkedInOAuthProvider>(Platform.LinkedIn);
services.AddKeyedScoped<IOAuthProvider, TwitterOAuthProvider>(Platform.Twitter);
```

Keep `services.AddScoped<IOAuthService, OAuthService>();` and the existing `Configure<LinkedInOptions>` / `Configure<TwitterOptions>` (lines 137-138) unchanged. Providers need `IHttpClientFactory` (already registered), their options, `ITokenEncryptor` (registered line 143), and a logger — all resolvable.

### 5. Untouched by design (verify, don't edit)

- `src/PBA.Application/Common/Interfaces/IOAuthService.cs` — signatures unchanged.
- `src/PBA.Api/Endpoints/OAuthEndpoints.cs` — unchanged (the `OAuthPlatforms` allow-list and `?purpose=` param are section-03's job).
- `src/PBA.Infrastructure/Connectors/LinkedInConnector.cs` (refresh call at line 137) and `TwitterConnector.cs` (line 239) — unchanged; they still call `oauthService.RefreshTokenAsync(credential, ct)` and get identical results.
- `Platform` enum, `PlatformCredential`, `TokenEncryptor` — unchanged.

## Verification / Definition of Done

1. `dotnet build` clean.
2. `dotnet test` green — specifically the full regression + boundary list above, plus **every pre-existing test** in `tests/PBA.Infrastructure.Tests/Security/OAuthServiceTests.cs` and `tests/PBA.Api.Tests/Endpoints/OAuthEndpointsTests.cs` still passes (these encode the current LinkedIn/Twitter behavior — if any breaks, the migration was not verbatim).
3. No change to any file listed in "Untouched by design."
4. Diff review confirms: authorize URLs, exchange requests, and refresh requests are identical on the wire to pre-refactor. This is a pure restructuring — **zero behavior change**.

Only after this gate is green should section-03 (new providers) build on the abstraction.
