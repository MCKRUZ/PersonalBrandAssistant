# Section-03 Code Review — New Analytics OAuth Providers

**Reviewer verdict:** Core deliverable solid — no secrets committed, scopes hardcoded, `Publishing:` prefix honored, `OAuthRefreshResult` envelope migration clean (publishing connectors consume the coordinator's `Result<string>` and are unaffected; all `GetAuthorizationUrlAsync` call sites updated), `(Platform, Purpose)` find/create correct and tested, TikTok rotated refresh token surfaced + persisted, Instagram decrypts `EncryptedAccessToken` and leaves `RefreshToken` null. Network/5xx/429→Transient is ordered BEFORE body parsing in all three providers (so a transient error can't be misread as Revoked). Findings below.

## Findings

| # | Severity | Category | Finding |
|---|----------|----------|---------|
| 1 | HIGH | correctness / data-loss | This section makes creating an Analytics credential trivial for ANY platform (allow-list + `TryParsePurpose` accept `analytics` for LinkedIn/Twitter too). Publishing-side lookups (`GetActiveCredentialAsync` in 4 connectors; OAuth status/DELETE; `PlatformEndpoints`) still filter by `Platform` only. A LinkedIn/Twitter Analytics (read-only) credential could then be returned for a publishing op (silent scope failure) or removed by DELETE (wrong-credential data loss). |
| 2 | MEDIUM | latent trap | Coordinator `RefreshTokenAsync` (OAuthService.cs:81) short-circuits + DEACTIVATES any credential with null `EncryptedRefreshToken` before calling the provider. Instagram has no refresh token by design → routing it through the coordinator deactivates it. Doesn't bite today (poller calls `provider.RefreshAsync` directly), but latent. |
| 3 | MEDIUM | robustness | RefreshAsync success path can throw (plan line 123: "never throw out of RefreshAsync"). YouTube/Instagram deserialize+`GetProperty` unguarded on 200; TikTok guards `Deserialize` but not `GetProperty("expires_in")`. Malformed/missing-field 200 → JsonException/KeyNotFoundException escapes. |
| 4 | MEDIUM | test gap | The SAFETY direction (non-revoked error → Transient, so the poller never deactivates a valid credential) is untested per provider. Instagram's revoked test sends code=190 AND subcode=463 together (can't isolate the branch). |
| 5 | LOW-MED | robustness | Each provider recognizes exactly ONE revoked signal; any other genuine-revocation code → Transient → infinite poller retry. Plan hedged "or equivalent error code." |
| 6 | LOW | contract | `TryParsePurpose` uses `Enum.TryParse` → `?purpose=1`→Analytics, `?purpose=0`→Publishing sneak through. |
| 7 | LOW | robustness | `IsMetaRevoked` requires code/subcode `ValueKind==Number`; if Meta serializes code as a string the revoked signal is missed → Transient. |
