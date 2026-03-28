# Phase 04 Implementation Plan — Platform Integrations

## Context

This phase adds social media platform integrations to the Personal Brand Assistant. The system already has a content workflow engine (Phase 02) with a `PublishingPipelineStub` that returns failure, and an AI agent orchestration layer (Phase 03). This phase replaces the stub with real platform adapters for Twitter/X, LinkedIn, Instagram, and YouTube.

The codebase follows Clean Architecture (.NET 10, EF Core, PostgreSQL) with existing patterns for DI registration, Result<T> error handling, background processors, encrypted storage, and Minimal API endpoints.

## What We're Building

Four platform adapters behind a common `ISocialPlatform` interface, plus:
- OAuth management with encrypted token storage, server-side state validation, and auto-refresh
- Database-backed rate limiting with rich decision responses
- Persistent media storage with HMAC-signed URLs
- Per-platform content formatting
- Multi-platform publishing pipeline with idempotency and partial failure tracking
- API endpoints for platform connection management
- Background processors for token refresh and health monitoring

## Architecture

### ISocialPlatform Abstraction

Each platform implements `ISocialPlatform`:

```csharp
public interface ISocialPlatform
{
    PlatformType Type { get; }
    Task<Result<PublishResult>> PublishAsync(PlatformContent content, CancellationToken ct);
    Task<Result<Unit>> DeletePostAsync(string platformPostId, CancellationToken ct);
    Task<Result<EngagementStats>> GetEngagementAsync(string platformPostId, CancellationToken ct);
    Task<Result<PlatformProfile>> GetProfileAsync(CancellationToken ct);
    Task<Result<ContentValidation>> ValidateContentAsync(PlatformContent content, CancellationToken ct);
}
```

```csharp
public record PublishResult(string PlatformPostId, string PostUrl, DateTimeOffset PublishedAt);
public record PlatformContent(string Text, string? Title, ContentType ContentType, IReadOnlyList<MediaFile> Media, Dictionary<string, string> Metadata);
public record MediaFile(string FileId, string MimeType, string? AltText);
public record ContentValidation(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
```

Note: `MediaFile` uses `FileId` (not `FilePath`) to avoid leaking infrastructure paths. Adapters resolve paths/URLs via `IMediaStorage`.

### Platform Adapter Base

Abstract base class providing common functionality:
- Loads and decrypts OAuth tokens from Platform entity
- Checks rate limits before API calls via IRateLimiter
- Records API call against rate limit quota
- Handles 401 (token expired → refresh → retry once)
- Handles 429 (rate limited → update rate limit state, return failure with RetryAt)
- Transient fault handling via Polly policies (5xx, timeouts, socket errors)
- Structured logging with platform context

Each concrete adapter overrides platform-specific HTTP call logic.

### OAuth Management

`IOAuthManager` handles the OAuth lifecycle per platform:

```csharp
public interface IOAuthManager
{
    Task<Result<OAuthAuthorizationUrl>> GenerateAuthUrlAsync(PlatformType platform, CancellationToken ct);
    Task<Result<OAuthTokens>> ExchangeCodeAsync(PlatformType platform, string code, string state, string? codeVerifier, CancellationToken ct);
    Task<Result<OAuthTokens>> RefreshTokenAsync(PlatformType platform, CancellationToken ct);
    Task<Result<Unit>> RevokeTokenAsync(PlatformType platform, CancellationToken ct);
}
```

```csharp
public record OAuthAuthorizationUrl(string Url, string State);
public record OAuthTokens(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt, string[]? GrantedScopes);
```

**Server-side state validation:** The `GenerateAuthUrlAsync` method generates a cryptographically random `state` parameter internally, stores it in a new `OAuthState` DB table with TTL (10 minutes), and returns both the URL and state. On callback, `ExchangeCodeAsync` validates the `state` against the stored value and rejects mismatches (CSRF protection).

**PKCE code_verifier storage:** For Twitter OAuth 2.0 PKCE, the `code_verifier` is stored in the `OAuthState` table alongside the `state` parameter. Retrieved and deleted during code exchange.

**OAuthState entity:**

```csharp
public class OAuthState
{
    public Guid Id { get; set; }
    public string State { get; set; }
    public PlatformType Platform { get; set; }
    public string? CodeVerifier { get; set; }  // PKCE only
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
```

Cleanup: expired entries purged by TokenRefreshProcessor on each cycle.

**Flow:** Angular app calls `GET /api/platforms/{type}/auth-url` → backend generates state + stores in DB → returns URL + state → Angular redirects user to platform → user approves → platform redirects to Angular callback route → Angular extracts auth code + state → sends `POST /api/platforms/{type}/callback` with code + state → backend validates state against DB → exchanges for tokens → encrypts and stores in Platform entity → deletes OAuthState entry.

**Granted scopes tracking:** On token exchange, store the granted scopes in a new `GrantedScopes` string[] field on the Platform entity. Health monitor validates required scopes are present.

**Per-platform OAuth details:**

| Platform | Flow | Token Lifetime | Refresh Strategy |
|----------|------|---------------|-----------------|
| Twitter/X | OAuth 2.0 PKCE | 2h access + refresh | Refresh every 90min via background job |
| LinkedIn | OAuth 2.0 3-legged | 60 days | Refresh at 50 days via background job |
| Instagram | Meta OAuth → long-lived | 60 days (refreshable) | Refresh at 50 days, MUST refresh before expiry |
| YouTube | Google OAuth 2.0 | Refresh never expires* | Refresh on 401; handle invalid_grant as disconnect |

*YouTube refresh tokens can be revoked or expire due to inactivity. Handle `invalid_grant` response by marking platform as disconnected and notifying user to reconnect.

**Required scopes per platform:**

| Platform | Required Scopes |
|----------|----------------|
| Twitter/X | `tweet.read`, `tweet.write`, `users.read`, `offline.access` |
| LinkedIn | `w_member_social`, `r_liteprofile` |
| Instagram | `instagram_basic`, `instagram_content_publish`, `pages_show_list` |
| YouTube | `youtube`, `youtube.upload` |

### Rate Limiting

Database-backed via Platform entity's `RateLimitState` JSONB field (already exists).

```csharp
public interface IRateLimiter
{
    Task<RateLimitDecision> CanMakeRequestAsync(PlatformType platform, string endpoint, CancellationToken ct);
    Task RecordRequestAsync(PlatformType platform, string endpoint, int remaining, DateTimeOffset? resetAt, CancellationToken ct);
    Task<RateLimitStatus> GetStatusAsync(PlatformType platform, CancellationToken ct);
}
```

```csharp
public record RateLimitDecision(bool Allowed, DateTimeOffset? RetryAt, string? Reason);
public record RateLimitStatus(int? RemainingCalls, DateTimeOffset? ResetAt, bool IsLimited);
```

`CanMakeRequestAsync` returns a rich `RateLimitDecision` with `RetryAt` and `Reason` so the pipeline can set `NextRetryAt` on ContentPlatformStatus and provide meaningful feedback.

Rate limit state updated from API response headers on every call:
- Twitter: `x-rate-limit-remaining`, `x-rate-limit-reset`
- LinkedIn: `Retry-After` header on 429
- Instagram: Query `content_publishing_limit` endpoint (cache result with 5min TTL to avoid extra API calls)
- YouTube: Quota tracked locally (10,000 units/day, reset midnight PT via `TimeZoneInfo("America/Los_Angeles")`)

The `RateLimitState` value object on Platform entity is extended to support per-endpoint tracking:

```csharp
public class PlatformRateLimitState
{
    public Dictionary<string, EndpointRateLimit> Endpoints { get; set; } = new();
    public int? DailyQuotaUsed { get; set; }  // YouTube
    public int? DailyQuotaLimit { get; set; }  // YouTube
    public DateTimeOffset? QuotaResetAt { get; set; }  // YouTube
}

public class EndpointRateLimit
{
    public int? RemainingCalls { get; set; }
    public DateTimeOffset? ResetAt { get; set; }
}
```

### Media Storage

```csharp
public interface IMediaStorage
{
    Task<string> SaveAsync(Stream content, string fileName, string mimeType, CancellationToken ct);
    Task<Stream> GetStreamAsync(string fileId, CancellationToken ct);
    Task<string> GetPathAsync(string fileId, CancellationToken ct);
    Task<bool> DeleteAsync(string fileId, CancellationToken ct);
    Task<string> GetSignedUrlAsync(string fileId, TimeSpan expiry, CancellationToken ct);
}
```

Persistent storage on configurable path (`MediaStorage:BasePath`, default `./media`). Files organized by date: `{basePath}/{yyyy}/{MM}/{guid}.{ext}`.

**Signed URLs (replacing static file serving):** For Instagram (requires public URLs for image posts), `GetSignedUrlAsync` generates an HMAC-signed URL with expiry: `/api/media/{fileId}?token={hmac}&expires={timestamp}`. A dedicated media endpoint validates the HMAC + expiry and streams the file. This avoids exposing the media directory directly via `UseStaticFiles`.

Configuration adds `MediaStorage:SigningKey` (generated on first run if missing, stored in User Secrets).

**Validation:** `SaveAsync` validates MIME type against magic bytes (not just extension), enforces per-platform size limits, and rejects unknown types.

### Content Formatting

Each platform has a formatter implementing `IPlatformContentFormatter`:

```csharp
public interface IPlatformContentFormatter
{
    PlatformType Platform { get; }
    Result<PlatformContent> FormatAndValidate(Content content);
}
```

Combined format + validate into a single method returning `Result<PlatformContent>` to avoid divergence between validation and formatting logic. Validation errors are returned as `Result.Failure` with descriptive error codes.

**Platform-specific formatting rules:**

| Platform | Char Limit | Hashtags | Media | Special |
|----------|-----------|----------|-------|---------|
| Twitter/X | 280 chars | Inline or appended | Images, video, GIFs | Thread splitting for long content |
| LinkedIn | 3,000 chars | Inline | Images, video, documents | Article support |
| Instagram | 2,200 chars caption | Appended (up to 30) | Required (no text-only) | Carousel up to 10 items |
| YouTube | 5,000 chars description | Tags separate field | Thumbnail | Title (100 chars max) |

Twitter thread formatter: splits long content at sentence boundaries, respects 280 char limit per tweet, adds numbering (1/N format), chains via `in_reply_to_tweet_id`.

### Publishing Pipeline

Replace `PublishingPipelineStub` with `PublishingPipeline`:

1. Load Content by ID with TargetPlatforms
2. For each target platform:
   a. **Idempotency check:** Look up existing `ContentPlatformStatus`. If already `Published`, skip. If `Processing`, skip (async upload in progress).
   b. **Acquire lease:** Set status to `Pending` with optimistic concurrency (xmin) to prevent duplicate processing.
   c. Resolve `ISocialPlatform` adapter
   d. Resolve `IPlatformContentFormatter`
   e. FormatAndValidate content → skip platform if invalid (set status to `Skipped`), log warning
   f. Check rate limits → defer if rate-limited (set `NextRetryAt` from `RateLimitDecision.RetryAt`)
   g. Upload media if needed
   h. Publish via adapter
   i. Record per-platform result (set `PlatformPostId`, `PostUrl`, `PublishedAt`)
   j. For async uploads (IG video, YT upload): set status to `Processing`
3. Track per-platform publish status using `ContentPlatformStatus` entity:

```csharp
public class ContentPlatformStatus
{
    public Guid Id { get; set; }
    public Guid ContentId { get; set; }
    public PlatformType Platform { get; set; }
    public PlatformPublishStatus Status { get; set; }
    public string? PlatformPostId { get; set; }
    public string? PostUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public string? IdempotencyKey { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public uint Version { get; set; }  // xmin concurrency token
}
```

`IdempotencyKey` is computed as `SHA256({ContentId}:{Platform}:{ContentVersion})` and checked before publishing to prevent duplicate posts on retry.

4. Overall content status: `Published` if ALL platforms succeeded, `PartiallyPublished` if SOME succeeded, `Failed` only if ALL failed
5. Notify user of partial failures via existing `INotificationService`

**PlatformPublishStatus enum:** `Pending`, `Published`, `Failed`, `RateLimited`, `Skipped`, `Processing`

### Platform-Specific Adapters

All adapters use typed `HttpClient` instances registered via `IHttpClientFactory` with Polly policies for transient fault handling (retry with exponential backoff for 5xx/408/timeouts, circuit breaker).

#### TwitterPlatformAdapter
- Typed HttpClient with `https://api.x.com/2` base URL (configurable)
- Posts: `POST /tweets` with JSON body
- Threads: chain tweets via `reply.in_reply_to_tweet_id`
- Media: chunked upload (INIT → APPEND → FINALIZE → poll status)
- Free tier detection: if engagement endpoint returns 403, mark as write-only
- Headers: `Authorization: Bearer {access_token}`

#### LinkedInPlatformAdapter
- Typed HttpClient with `https://api.linkedin.com/rest` base URL
- Required headers: `X-Restli-Protocol-Version: 2.0.0`, `Linkedin-Version: {YYYYMM}`
- Posts: `POST /posts` with Posts API schema
- Media: upload via Images/Videos API → get URN → reference in post
- Mentions: use Rest.li mention entities in post schema (not markdown-like syntax)

#### InstagramPlatformAdapter
- Typed HttpClient with Meta Graph API
- Two-step publish: create container → publish container
- Media containers require signed URL (via `IMediaStorage.GetSignedUrlAsync`) or resumable upload for video
- Poll `status_code` on container before publishing (video processing) → set `Processing` status
- Carousels: create child containers with `is_carousel_item=true`, then parent container

#### YouTubePlatformAdapter
- Uses `Google.Apis.YouTube.v3` NuGet package (official SDK)
- Video upload via resumable upload protocol (streaming, not buffering entire file)
- Metadata update: `Videos.Update` with snippet (title, description, tags)
- Thumbnail upload: `Thumbnails.Set`
- Track quota usage locally (1,600 per upload, 50 per update)
- Handle `invalid_grant` on refresh as disconnect signal
- No community post support

### Background Processors

**TokenRefreshProcessor** (BackgroundService with PeriodicTimer, every 5 minutes):
- Query Platform entities where `IsConnected = true` AND token expiry is within threshold (indexed query, not full table scan)
- For Twitter: refresh if `TokenExpiresAt - now < 30min`
- For LinkedIn/Instagram: refresh if `TokenExpiresAt - now < 10 days`
- For YouTube: skip (refresh on 401 only); handle `invalid_grant` as disconnect
- On refresh failure: mark platform as disconnected, notify user
- Instagram: CRITICAL — tokens that expire cannot be recovered. Log warning at 14 days, error at 3 days.
- Cleanup: purge expired `OAuthState` entries older than 1 hour

**PlatformHealthMonitor** (BackgroundService, every 15 minutes):
- For each connected platform, call `GetProfileAsync()` to validate connection
- Validate granted scopes against required scopes; warn if missing
- Update `LastSyncAt` on Platform entity
- If validation fails: check if token issue (try refresh) or API issue (log, don't disconnect)

**PublishCompletionPoller** (BackgroundService, every 30 seconds):
- Query `ContentPlatformStatus` entries with `Status = Processing`
- For Instagram: poll container `status_code` → if `FINISHED`, call publish endpoint → update to `Published`
- For YouTube: check video processing status → if complete, update to `Published` with final URL
- Max poll duration: 30 minutes per entry, then mark as `Failed`

### API Endpoints

```
MapGroup("/api/platforms").WithTags("Platforms")

GET    /                          → List all platforms with status
GET    /{type}/auth-url           → Generate OAuth URL (returns { url, state })
POST   /{type}/callback           → Exchange code for tokens (body: { code, codeVerifier?, state })
DELETE /{type}/disconnect         → Revoke tokens, set IsConnected=false
GET    /{type}/status             → Token validity, rate limits, last sync, granted scopes
POST   /{type}/test-post          → Publish test post to verify connection
GET    /{type}/engagement/{postId}→ Get engagement stats for a published post
```

Media endpoint (separate group):
```
MapGroup("/api/media").WithTags("Media")

GET    /{fileId}                  → Serve media file (validates HMAC token + expiry from query params)
```

All platform endpoints require API key auth (existing `ApiKeyMiddleware`). Media endpoint validates HMAC signature instead.

### Configuration

```json
{
  "PlatformIntegrations": {
    "Twitter": {
      "CallbackUrl": "http://localhost:4200/platforms/twitter/callback",
      "BaseUrl": "https://api.x.com/2"
    },
    "LinkedIn": {
      "CallbackUrl": "http://localhost:4200/platforms/linkedin/callback",
      "ApiVersion": "202603",
      "BaseUrl": "https://api.linkedin.com/rest"
    },
    "Instagram": {
      "CallbackUrl": "http://localhost:4200/platforms/instagram/callback"
    },
    "YouTube": {
      "CallbackUrl": "http://localhost:4200/platforms/youtube/callback",
      "DailyQuotaLimit": 10000
    }
  },
  "MediaStorage": {
    "BasePath": "./media"
  }
}
```

All secrets (ClientId, ClientSecret, AppId, AppSecret, MediaStorage:SigningKey) via User Secrets (dev) / Azure Key Vault (prod). Only callback URLs and non-secret config in appsettings.json.

### DI Registration

In `DependencyInjection.cs`:

```
// Configuration
services.Configure<PlatformIntegrationOptions>(config.GetSection("PlatformIntegrations"));
services.Configure<MediaStorageOptions>(config.GetSection("MediaStorage"));

// Singletons
services.AddSingleton<IMediaStorage, LocalMediaStorage>();

// Typed HttpClients with Polly policies
services.AddHttpClient<TwitterPlatformAdapter>(c => c.BaseAddress = new Uri(twitterConfig.BaseUrl))
    .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(Math.Pow(2, i))));
services.AddHttpClient<LinkedInPlatformAdapter>(c => { c.BaseAddress = ...; c.DefaultRequestHeaders.Add(...); })
    .AddTransientHttpErrorPolicy(...);
services.AddHttpClient<InstagramPlatformAdapter>(...)
    .AddTransientHttpErrorPolicy(...);

// Scoped
services.AddScoped<IOAuthManager, OAuthManager>();
services.AddScoped<IRateLimiter, DatabaseRateLimiter>();
services.AddScoped<IPublishingPipeline, PublishingPipeline>();  // replaces stub

// Platform adapters (all scoped, resolved via IEnumerable<ISocialPlatform>)
services.AddScoped<ISocialPlatform, TwitterPlatformAdapter>();
services.AddScoped<ISocialPlatform, LinkedInPlatformAdapter>();
services.AddScoped<ISocialPlatform, InstagramPlatformAdapter>();
services.AddScoped<ISocialPlatform, YouTubePlatformAdapter>();

// Content formatters (all scoped)
services.AddScoped<IPlatformContentFormatter, TwitterContentFormatter>();
services.AddScoped<IPlatformContentFormatter, LinkedInContentFormatter>();
services.AddScoped<IPlatformContentFormatter, InstagramContentFormatter>();
services.AddScoped<IPlatformContentFormatter, YouTubeContentFormatter>();

// Background services
services.AddHostedService<TokenRefreshProcessor>();
services.AddHostedService<PlatformHealthMonitor>();
services.AddHostedService<PublishCompletionPoller>();
```

### New Domain Entities

**ContentPlatformStatus** — tracks per-platform publish status for multi-platform content:

Fields: `Id`, `ContentId` (FK), `Platform` (PlatformType), `Status` (PlatformPublishStatus enum), `PlatformPostId`, `PostUrl`, `ErrorMessage`, `IdempotencyKey`, `RetryCount`, `NextRetryAt`, `PublishedAt`, `Version` (xmin)

EF Core config: composite index on `(ContentId, Platform)`, unique index on `IdempotencyKey`, FK to Content, xmin concurrency token.

**OAuthState** — temporary storage for OAuth state parameter and PKCE code_verifier:

Fields: `Id`, `State`, `Platform` (PlatformType), `CodeVerifier` (nullable), `CreatedAt`, `ExpiresAt`

EF Core config: unique index on `State`, index on `ExpiresAt` for cleanup queries.

**PlatformPublishStatus enum:** `Pending`, `Published`, `Failed`, `RateLimited`, `Skipped`, `Processing`

**Platform entity updates:**
- Add `GrantedScopes` (string[]?) field for tracking OAuth scopes

### File Structure

```
src/PersonalBrandAssistant.Application/
  Common/
    Interfaces/
      IMediaStorage.cs
      IOAuthManager.cs
      IPlatformContentFormatter.cs
      IRateLimiter.cs
      ISocialPlatform.cs
    Models/
      PlatformIntegrationOptions.cs
      MediaStorageOptions.cs
      PlatformContent.cs
      PublishResult.cs
      OAuthTokens.cs
      ContentValidation.cs
      EngagementStats.cs
      RateLimitDecision.cs
      RateLimitStatus.cs

src/PersonalBrandAssistant.Domain/
  Entities/
    ContentPlatformStatus.cs
    OAuthState.cs
  Enums/
    PlatformPublishStatus.cs

src/PersonalBrandAssistant.Infrastructure/
  Services/
    Platform/
      OAuthManager.cs
      DatabaseRateLimiter.cs
      LocalMediaStorage.cs
      PublishingPipeline.cs
      Adapters/
        PlatformAdapterBase.cs
        TwitterPlatformAdapter.cs
        LinkedInPlatformAdapter.cs
        InstagramPlatformAdapter.cs
        YouTubePlatformAdapter.cs
      Formatters/
        TwitterContentFormatter.cs
        LinkedInContentFormatter.cs
        InstagramContentFormatter.cs
        YouTubeContentFormatter.cs
  BackgroundJobs/
    TokenRefreshProcessor.cs
    PlatformHealthMonitor.cs
    PublishCompletionPoller.cs
  Data/
    Configurations/
      ContentPlatformStatusConfiguration.cs
      OAuthStateConfiguration.cs

src/PersonalBrandAssistant.Api/
  Endpoints/
    PlatformEndpoints.cs
    MediaEndpoints.cs

tests/PersonalBrandAssistant.Infrastructure.Tests/
  Services/
    Platform/
      OAuthManagerTests.cs
      DatabaseRateLimiterTests.cs
      LocalMediaStorageTests.cs
      PublishingPipelineTests.cs
      TwitterPlatformAdapterTests.cs
      LinkedInPlatformAdapterTests.cs
      InstagramPlatformAdapterTests.cs
      YouTubePlatformAdapterTests.cs
      TwitterContentFormatterTests.cs
      LinkedInContentFormatterTests.cs
      InstagramContentFormatterTests.cs
      YouTubeContentFormatterTests.cs
  BackgroundJobs/
    TokenRefreshProcessorTests.cs
    PublishCompletionPollerTests.cs
  Api/
    PlatformEndpointsTests.cs
    MediaEndpointsTests.cs
```

## Implementation Order

Sections should be implemented in dependency order:

1. **Domain entities & enums** — ContentPlatformStatus, OAuthState, PlatformPublishStatus, Platform entity updates
2. **Application interfaces & models** — ISocialPlatform, IOAuthManager, IRateLimiter, IMediaStorage, IPlatformContentFormatter, DTOs
3. **EF Core configuration** — ContentPlatformStatusConfiguration, OAuthStateConfiguration, Platform entity updates
4. **Media storage** — LocalMediaStorage with signed URLs, MediaEndpoints
5. **Rate limiter** — DatabaseRateLimiter with RateLimitDecision response
6. **OAuth manager** — Per-platform OAuth flows with server-side state validation, PKCE support, scope tracking
7. **Content formatters** — Per-platform content transformation and validation (combined FormatAndValidate)
8. **Platform adapters** — Twitter, LinkedIn, Instagram, YouTube implementations with typed HttpClients
9. **Publishing pipeline** — Replace stub, idempotency, optimistic concurrency, partial failure with PartiallyPublished
10. **API endpoints** — Platform management, OAuth callbacks with state validation, status with scopes
11. **Background processors** — TokenRefreshProcessor, PlatformHealthMonitor, PublishCompletionPoller
12. **DI configuration** — Wire everything, typed HttpClients + Polly, appsettings, remove stub
