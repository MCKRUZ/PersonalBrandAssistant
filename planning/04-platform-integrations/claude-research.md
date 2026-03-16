# Phase 04 Platform Integrations — Research

## Codebase Patterns

### Architecture
- Clean Architecture: Domain → Application → Infrastructure → Api
- Services organized by feature under Infrastructure/Services
- DI registrations centralized in `DependencyInjection.cs`
- Scoped services for DB-touching work, Singletons for stateless factories

### Key Integration Points
- **IPublishingPipeline** (`PublishingPipelineStub` returns failure) — this is where real platform publishing plugs in
- **ScheduledPublishProcessor** (every 30s) finds `Scheduled` content and calls `IPublishingPipeline.PublishAsync()`
- **RetryFailedProcessor** (every 60s) retries failed content with exponential backoff (1m, 5m, 15m), max 3 retries
- **Content.TargetPlatforms** is `PlatformType[]` — content targets multiple platforms

### Platform Entity Already Exists
```csharp
Platform: Type, DisplayName, IsConnected, EncryptedAccessToken, EncryptedRefreshToken,
          TokenExpiresAt, RateLimitState (JSONB), LastSyncAt, Settings (JSONB), Version
```
- EF Core config with unique index on Type, JSONB for value objects, `xmin` concurrency

### Encrypted Storage
- `IEncryptionService` wraps ASP.NET Data Protection (DPAPI)
- Keys persisted to file system (dev) or Azure Key Vault (prod)
- Platform entity already has `byte[]` fields for encrypted tokens

### Existing Patterns to Follow
- **ChatClientFactory** pattern: sealed, ConcurrentDictionary caching, config-driven, disposable
- **TokenTrackingDecorator**: decorator pattern for cross-cutting concerns
- **AgentExecutionContext**: AsyncLocal for per-flow context tracking
- **Result<T>** for all operations with ErrorCode enum
- **BackgroundService** with PeriodicTimer for processors
- **Minimal API** endpoint groups with tags

### Testing
- xUnit + Moq + WebApplicationFactory + Testcontainers PostgreSQL
- CustomWebApplicationFactory removes hosted services, provides authenticated client
- AsyncQueryableHelpers for EF Core async mock support
- MockChatClient/MockChatClientFactory pattern for external service mocks

---

## Platform API Research (March 2026)

### Twitter/X API v2

**Tiers:** Free ($0, 1,500 tweets/mo write-only), Basic ($200/mo, 50K tweets/mo + read), Pro ($5,000/mo)

**Auth:** OAuth 2.0 PKCE → access token (2h) + refresh token. Scopes: `tweet.read`, `tweet.write`, `users.read`, `offline.access`

**Posting:** `POST /2/tweets` with `{"text":"..."}`. Threads via `reply.in_reply_to_tweet_id` chaining. Media: chunked upload INIT/APPEND/FINALIZE.

**Rate limits:** Free tier media upload: 17 init/finalize calls per 24h. Monitor `x-rate-limit-remaining` header.

**Gotchas:** Free tier is write-only (no engagement tracking). 2h token expiry requires aggressive refresh. v1.1 is sunset.

**.NET libs:** LinqToTwitter, TweetinviAPI, or direct HttpClient

### LinkedIn Marketing API

**API:** Community Management API (Posts API). ugcPosts and Share API are deprecated.

**Auth:** 3-legged OAuth 2.0. Tokens: 60-day lifetime. Required headers: `X-Restli-Protocol-Version: 2.0.0`, `Linkedin-Version: YYYYMM`

**Posting:** `POST /rest/posts`. Supports text, images, video, documents, articles, multi-image, polls. Media uploaded via separate Images/Videos API to get URNs.

**Scopes:** `w_member_social` (post as member), `w_organization_social` (post as org)

**Rate limits:** Unpublished daily limits per endpoint. 429 with `Retry-After` header.

**Gotchas:** API versions get sunset regularly. Article posts need manual thumbnail/title (no URL scraping). Organic carousels not supported. URNs must be URL-encoded in params.

**.NET libs:** None — use HttpClient directly

### Instagram Graph API (Meta)

**Requirements:** Instagram Business/Creator account linked to Facebook Page. Meta App Review required.

**Auth:** Facebook OAuth → short-lived token (1h) → long-lived token (60 days, refreshable). Refresh via `GET /refresh_access_token`. Must refresh before expiry — expired tokens cannot be recovered.

**Posting:** Two-step: create container (`POST /{ig-user-id}/media`) → publish (`POST /{ig-user-id}/media_publish`). Supports feed images/video, reels, stories, carousels.

**Media:** Must be publicly accessible URLs (except resumable video upload). JPEG only for images. Videos need processing poll before publish.

**Rate limits:** 100 API-published posts per 24h rolling. Check via `GET /{ig-user-id}/content_publishing_limit`.

**Gotchas:** Facebook Page linkage mandatory. 60-day token silent expiry. No text-only posts. Alt text only for images (not reels/stories).

**.NET libs:** None — use HttpClient directly

### YouTube Data API v3

**CRITICAL: No official community posts API.** Focus on video metadata and engagement.

**Auth:** Google OAuth 2.0. Refresh tokens don't expire (unless revoked). Scopes: `youtube`, `youtube.upload`, `youtube.readonly`.

**Operations:** Video upload (1,600 units), metadata update (50 units), search (100 units). Default quota: 10,000 units/day = max 6 uploads/day.

**Gotchas:** Quota increases require Google compliance audit (weeks). Community posts have no API — any workaround violates ToS.

**.NET libs:** `Google.Apis.YouTube.v3` (official, well-maintained)

---

## Summary Table

| Platform | Auth | Token Life | Publish Rate | .NET SDK |
|----------|------|-----------|-------------|----------|
| X/Twitter | OAuth 2.0 PKCE | 2h access + refresh | 1,500/mo (Free) | LinqToTwitter / HttpClient |
| LinkedIn | OAuth 2.0 3-leg | 60 days | Unpublished daily | HttpClient |
| Instagram | Meta OAuth | 60 days (refresh) | 100/24h | HttpClient |
| YouTube | Google OAuth 2.0 | Refresh never expires | 10K units/day | Google.Apis.YouTube.v3 |

## Recommendations

1. **X/Twitter:** Start Free tier. Upgrade to Basic ($200/mo) only for engagement analytics.
2. **LinkedIn:** Apply for Community Management API early — approval takes time. Use Posts API only.
3. **Instagram:** Build robust 50-day token auto-refresh. Facebook Page linkage is a hard prereq.
4. **YouTube:** Drop community posts from scope. Focus video metadata updates. Use official SDK.
5. **Cross-cutting:** Build thin HttpClient wrappers per platform behind ISocialPlatform. Use decorator pattern for rate limiting and retry (like TokenTrackingDecorator).
