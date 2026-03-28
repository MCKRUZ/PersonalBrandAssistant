# Phase 04 Platform Integrations — TDD Plan

Testing framework: xUnit + Moq + WebApplicationFactory + Testcontainers PostgreSQL (existing project conventions). Uses `AsyncQueryableHelpers` for EF Core async DbSet mocking. `CustomWebApplicationFactory` removes hosted services and provides authenticated client.

---

## 1. Domain Entities & Enums

```csharp
// Test: ContentPlatformStatus initializes with default values (Pending status, 0 retries)
// Test: ContentPlatformStatus.IdempotencyKey is set and immutable after construction
// Test: OAuthState.ExpiresAt is set relative to CreatedAt
// Test: PlatformPublishStatus enum has all expected values (Pending, Published, Failed, RateLimited, Skipped, Processing)
```

## 2. Application Interfaces & Models

```csharp
// Test: PlatformContent record equality works with same values
// Test: MediaFile uses FileId (not file path)
// Test: RateLimitDecision.Allowed=false includes RetryAt
// Test: OAuthTokens stores GrantedScopes array
// Test: PlatformIntegrationOptions binds from configuration section
// Test: MediaStorageOptions binds BasePath and SigningKey
```

## 3. EF Core Configuration

```csharp
// Test: ContentPlatformStatus persists and retrieves from DB (Testcontainers)
// Test: ContentPlatformStatus composite index on (ContentId, Platform) enforced
// Test: ContentPlatformStatus unique index on IdempotencyKey prevents duplicates
// Test: ContentPlatformStatus xmin concurrency token throws DbUpdateConcurrencyException on stale write
// Test: OAuthState persists and retrieves with State unique index
// Test: OAuthState ExpiresAt index allows efficient cleanup queries
// Test: Platform entity GrantedScopes field persists string array
```

## 4. Media Storage

```csharp
// Test: SaveAsync creates file in date-organized path ({basePath}/{yyyy}/{MM}/{guid}.{ext})
// Test: SaveAsync returns unique fileId
// Test: SaveAsync validates MIME type against magic bytes
// Test: SaveAsync rejects files exceeding size limit
// Test: GetStreamAsync returns readable stream for existing file
// Test: GetStreamAsync returns failure for non-existent fileId
// Test: GetPathAsync returns correct filesystem path
// Test: DeleteAsync removes file and returns true
// Test: DeleteAsync returns false for non-existent file
// Test: GetSignedUrlAsync generates URL with HMAC token and expiry
// Test: GetSignedUrlAsync URL expires after specified duration
// Test: MediaEndpoints serves file when HMAC token is valid and not expired
// Test: MediaEndpoints returns 403 when HMAC token is invalid
// Test: MediaEndpoints returns 403 when URL has expired
// Test: MediaEndpoints returns 404 for non-existent fileId
```

## 5. Rate Limiter

```csharp
// Test: CanMakeRequestAsync returns Allowed=true when no rate limit state exists
// Test: CanMakeRequestAsync returns Allowed=true when remaining > 0
// Test: CanMakeRequestAsync returns Allowed=false with RetryAt when remaining = 0
// Test: CanMakeRequestAsync returns Allowed=false with Reason for YouTube daily quota exceeded
// Test: RecordRequestAsync updates Platform entity RateLimitState JSONB
// Test: RecordRequestAsync creates endpoint entry if not exists
// Test: RecordRequestAsync updates existing endpoint entry
// Test: GetStatusAsync returns aggregate status across endpoints
// Test: YouTube quota resets at midnight PT (TimeZoneInfo America/Los_Angeles)
// Test: Instagram publishing limit cached with TTL (not re-queried on every call)
```

## 6. OAuth Manager

```csharp
// Test: GenerateAuthUrlAsync creates OAuthState entry in DB with TTL
// Test: GenerateAuthUrlAsync returns cryptographically random state parameter
// Test: GenerateAuthUrlAsync includes PKCE code_verifier in OAuthState for Twitter
// Test: GenerateAuthUrlAsync returns correct platform-specific OAuth URL
// Test: ExchangeCodeAsync validates state against DB and rejects mismatch
// Test: ExchangeCodeAsync deletes OAuthState entry after successful exchange
// Test: ExchangeCodeAsync rejects expired state entries
// Test: ExchangeCodeAsync stores encrypted tokens in Platform entity
// Test: ExchangeCodeAsync stores granted scopes in Platform entity
// Test: ExchangeCodeAsync uses code_verifier from OAuthState for Twitter PKCE
// Test: RefreshTokenAsync refreshes and updates encrypted tokens
// Test: RefreshTokenAsync handles invalid_grant for YouTube (marks disconnected)
// Test: RevokeTokenAsync calls platform revocation endpoint and clears tokens
// Test: RevokeTokenAsync sets IsConnected=false on Platform entity
// Test: Twitter auth URL includes PKCE challenge and required scopes
// Test: LinkedIn auth URL includes correct scopes and API version
// Test: Instagram auth URL includes Facebook OAuth parameters
// Test: YouTube auth URL includes Google OAuth parameters and scopes
```

## 7. Content Formatters

```csharp
// Twitter
// Test: FormatAndValidate truncates text to 280 chars with ellipsis
// Test: FormatAndValidate splits long content into thread (multiple PlatformContent entries)
// Test: Thread splits at sentence boundaries, not mid-word
// Test: Thread adds numbering (1/N format)
// Test: FormatAndValidate appends hashtags within char limit
// Test: FormatAndValidate returns failure for empty text

// LinkedIn
// Test: FormatAndValidate allows up to 3000 chars
// Test: FormatAndValidate returns failure for text exceeding 3000 chars
// Test: FormatAndValidate preserves inline hashtags

// Instagram
// Test: FormatAndValidate returns failure when no media attached (text-only not allowed)
// Test: FormatAndValidate limits caption to 2200 chars
// Test: FormatAndValidate limits hashtags to 30
// Test: FormatAndValidate limits carousel to 10 items

// YouTube
// Test: FormatAndValidate requires Title
// Test: FormatAndValidate limits title to 100 chars
// Test: FormatAndValidate limits description to 5000 chars
// Test: FormatAndValidate separates tags from description
```

## 8. Platform Adapters

```csharp
// PlatformAdapterBase
// Test: PublishAsync loads and decrypts OAuth tokens before API call
// Test: PublishAsync checks rate limit before making request
// Test: PublishAsync records request after successful API call
// Test: PublishAsync retries once on 401 after token refresh
// Test: PublishAsync returns failure with RetryAt on 429
// Test: PublishAsync handles transient errors (5xx) via Polly retry

// TwitterPlatformAdapter
// Test: PublishAsync posts single tweet with correct JSON body
// Test: PublishAsync chains thread tweets via reply.in_reply_to_tweet_id
// Test: PublishAsync uploads media via chunked upload (INIT/APPEND/FINALIZE)
// Test: GetEngagementAsync returns failure on 403 (free tier)
// Test: GetProfileAsync returns user profile data

// LinkedInPlatformAdapter
// Test: PublishAsync includes required headers (X-Restli-Protocol-Version, Linkedin-Version)
// Test: PublishAsync uploads media via Images API and references URN in post
// Test: GetProfileAsync returns LinkedIn profile

// InstagramPlatformAdapter
// Test: PublishAsync creates container then publishes (two-step)
// Test: PublishAsync uses signed URL for media container
// Test: PublishAsync polls container status before publishing video
// Test: PublishAsync creates carousel with child containers
// Test: ValidateContentAsync rejects text-only posts

// YouTubePlatformAdapter
// Test: PublishAsync uploads video via resumable upload (streaming)
// Test: PublishAsync tracks quota usage (1600 per upload)
// Test: PublishAsync updates metadata with snippet
// Test: GetEngagementAsync returns video statistics
// Test: RefreshToken handles invalid_grant as disconnect
```

## 9. Publishing Pipeline

```csharp
// Test: PublishAsync loads content with target platforms
// Test: PublishAsync skips platform if ContentPlatformStatus is already Published (idempotency)
// Test: PublishAsync skips platform if ContentPlatformStatus is Processing
// Test: PublishAsync acquires lease via optimistic concurrency (sets Pending)
// Test: PublishAsync throws DbUpdateConcurrencyException on concurrent lease attempt
// Test: PublishAsync sets IdempotencyKey as SHA256(ContentId:Platform:ContentVersion)
// Test: PublishAsync calls FormatAndValidate and skips platform on failure (sets Skipped)
// Test: PublishAsync checks rate limit and defers on RateLimited (sets NextRetryAt)
// Test: PublishAsync publishes to each platform independently
// Test: PublishAsync records PlatformPostId and PostUrl on success
// Test: PublishAsync sets Processing for async uploads (IG video, YT)
// Test: PublishAsync sets overall status to Published when all succeed
// Test: PublishAsync sets overall status to PartiallyPublished when some succeed
// Test: PublishAsync sets overall status to Failed when all fail
// Test: PublishAsync notifies user on partial failure via INotificationService
```

## 10. API Endpoints

```csharp
// Test: GET /api/platforms returns all platforms with connection status
// Test: GET /api/platforms/{type}/auth-url returns URL and state
// Test: POST /api/platforms/{type}/callback exchanges code for tokens with state validation
// Test: POST /api/platforms/{type}/callback rejects invalid state (returns 400)
// Test: DELETE /api/platforms/{type}/disconnect revokes tokens and sets IsConnected=false
// Test: GET /api/platforms/{type}/status returns token validity, rate limits, scopes
// Test: POST /api/platforms/{type}/test-post publishes test post
// Test: GET /api/platforms/{type}/engagement/{postId} returns engagement stats
// Test: All endpoints require API key auth (return 401 without key)
// Test: GET /api/media/{fileId} serves file with valid HMAC token
// Test: GET /api/media/{fileId} returns 403 with invalid/expired token
```

## 11. Background Processors

```csharp
// TokenRefreshProcessor
// Test: Refreshes Twitter tokens when expiry < 30min
// Test: Refreshes LinkedIn/Instagram tokens when expiry < 10 days
// Test: Skips YouTube (no scheduled refresh)
// Test: Marks platform disconnected on refresh failure
// Test: Notifies user on refresh failure
// Test: Instagram logs warning at 14 days, error at 3 days before expiry
// Test: Cleans up expired OAuthState entries
// Test: Only queries platforms with tokens expiring within threshold (efficient query)

// PlatformHealthMonitor
// Test: Calls GetProfileAsync for each connected platform
// Test: Updates LastSyncAt on success
// Test: Warns when granted scopes don't include required scopes
// Test: Attempts token refresh on auth failure
// Test: Logs API errors without disconnecting platform

// PublishCompletionPoller
// Test: Polls Processing entries for Instagram container status
// Test: Updates to Published when IG container status is FINISHED
// Test: Polls YouTube video processing status
// Test: Marks Failed after 30 minutes of polling
```

## 12. DI Configuration

```csharp
// Test: All ISocialPlatform implementations resolve from DI
// Test: All IPlatformContentFormatter implementations resolve from DI
// Test: IOAuthManager resolves as scoped
// Test: IRateLimiter resolves as scoped
// Test: IMediaStorage resolves as singleton
// Test: IPublishingPipeline resolves as PublishingPipeline (not stub)
// Test: Typed HttpClients configured for Twitter, LinkedIn, Instagram
// Test: Background services registered (TokenRefreshProcessor, PlatformHealthMonitor, PublishCompletionPoller)
// Test: PlatformIntegrationOptions binds from configuration
// Test: MediaStorageOptions binds from configuration
```
