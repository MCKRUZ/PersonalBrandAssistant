# Section 08: Platform Adapters

## Overview

This section implements the four concrete platform adapters (Twitter/X, LinkedIn, Instagram, YouTube) and their shared abstract base class `PlatformAdapterBase`. Each adapter implements the `ISocialPlatform` interface (defined in section-02) and encapsulates all platform-specific HTTP API interaction logic. The base class centralizes cross-cutting concerns: token loading/decryption, rate limit checking, 401 retry with token refresh, and error handling.

All adapters use typed `HttpClient` instances registered via `IHttpClientFactory` (wired in section-12). Each concrete adapter overrides platform-specific methods for publishing, media upload, engagement retrieval, and profile fetching.

**Deviations from plan:**
- Polly-based transient fault resilience deferred to section-12 DI configuration (HttpClient policies)
- 429 handling simplified — `HandleHttpError` maps 429 to `ErrorCode.InternalError`; Polly retry will handle actual retries
- All adapters include post ID format validation (regex per platform) to prevent URL injection
- Instagram `PostUrl` returns placeholder `https://www.instagram.com/` since Graph API returns numeric IDs, not shortcodes
- LinkedIn publish returns `Result.Failure` if `x-restli-id` header missing (no GUID fabrication)
- Twitter thread publishing returns `Result.Failure` on partial failure instead of silent success
- YouTube quota cost (1600 per upload) recorded directly via `IRateLimiter.RecordRequestAsync`
- All `PlatformSpecific` dictionaries use `.AsReadOnly()` for immutability
- Protected fields on base class changed to properties (`MediaStorage`, `Logger`)

## Dependencies

- **Section 02 (Interfaces & Models):** `ISocialPlatform`, `PlatformContent`, `PublishResult`, `ContentValidation`, `EngagementStats`, `MediaFile`, `RateLimitDecision`
- **Section 04 (Media Storage):** `IMediaStorage` for resolving file paths and signed URLs from `MediaFile.FileId`
- **Section 05 (Rate Limiter):** `IRateLimiter` for pre-call checks and post-call recording
- **Section 06 (OAuth Manager):** `IOAuthManager` for token refresh on 401 responses

## File Paths

### Production Code

| File | Description |
|------|-------------|
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/PlatformAdapterBase.cs` | Abstract base with common concerns |
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/TwitterPlatformAdapter.cs` | Twitter/X API v2 adapter |
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/LinkedInPlatformAdapter.cs` | LinkedIn REST API adapter |
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/InstagramPlatformAdapter.cs` | Meta Graph API adapter |
| `src/PersonalBrandAssistant.Infrastructure/Services/PlatformServices/Adapters/YouTubePlatformAdapter.cs` | YouTube Data API v3 adapter |

### Test Files

| File | Description |
|------|-------------|
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/TwitterPlatformAdapterTests.cs` | Twitter adapter tests |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/LinkedInPlatformAdapterTests.cs` | LinkedIn adapter tests |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/InstagramPlatformAdapterTests.cs` | Instagram adapter tests |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/YouTubePlatformAdapterTests.cs` | YouTube adapter tests |

## Tests (Write First)

All adapter tests use Moq for dependencies and a `MockHttpMessageHandler` to intercept HTTP calls. The base class behavior is tested through the concrete adapters (no separate test file for the abstract class).

### PlatformAdapterBase (tested via concrete adapters)

```csharp
// File: tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/TwitterPlatformAdapterTests.cs
// (Base class behavior tested through TwitterPlatformAdapter as representative)

// Test: PublishAsync loads and decrypts OAuth tokens before API call
//   Arrange: Mock IApplicationDbContext returning Platform with EncryptedAccessToken,
//            Mock IEncryptionService.Decrypt returning "test-token"
//   Act: Call PublishAsync with valid PlatformContent
//   Assert: IEncryptionService.Decrypt called with Platform.EncryptedAccessToken,
//           HTTP request includes Authorization: Bearer test-token

// Test: PublishAsync checks rate limit before making request
//   Arrange: Mock IRateLimiter.CanMakeRequestAsync returning Allowed=true
//   Act: Call PublishAsync
//   Assert: IRateLimiter.CanMakeRequestAsync called with (PlatformType.TwitterX, "publish", ct)
//           before any HTTP request is made

// Test: PublishAsync records request after successful API call
//   Arrange: Mock HTTP response 200
//   Act: Call PublishAsync
//   Assert: IRateLimiter.RecordRequestAsync called with remaining/resetAt from response headers

// Test: PublishAsync retries once on 401 after token refresh
//   Arrange: Mock HTTP to return 401 on first call, 200 on second,
//            Mock IOAuthManager.RefreshTokenAsync returning success
//   Act: Call PublishAsync
//   Assert: IOAuthManager.RefreshTokenAsync called once,
//           HTTP request made twice, final result is success

// Test: PublishAsync returns failure with RetryAt on 429
//   Arrange: Mock HTTP to return 429 with Retry-After header
//   Act: Call PublishAsync
//   Assert: Result is failure, IRateLimiter.RecordRequestAsync called with remaining=0,
//           error contains retry information

// Test: PublishAsync handles transient errors (5xx) via Polly retry
//   Arrange: Mock HTTP to return 503 on first call, 200 on second
//   Act: Call PublishAsync
//   Assert: Result is success (Polly retried transparently)
```

### TwitterPlatformAdapter

```csharp
// File: tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/TwitterPlatformAdapterTests.cs

// Test: PublishAsync posts single tweet with correct JSON body
//   Arrange: PlatformContent with text "Hello world", no media
//   Act: Call PublishAsync
//   Assert: HTTP POST to /tweets with body {"text": "Hello world"},
//           result contains PlatformPostId and PostUrl

// Test: PublishAsync chains thread tweets via reply.in_reply_to_tweet_id
//   Arrange: PlatformContent with Metadata["thread"] containing JSON array of tweet texts
//   Act: Call PublishAsync
//   Assert: First POST to /tweets without reply field,
//           subsequent POSTs include {"reply":{"in_reply_to_tweet_id":"<prev_id>"}},
//           result contains first tweet's PostUrl

// Test: PublishAsync uploads media via chunked upload (INIT/APPEND/FINALIZE)
//   Arrange: PlatformContent with one MediaFile, mock IMediaStorage.GetStreamAsync
//   Act: Call PublishAsync
//   Assert: POST /media/upload with command=INIT,
//           POST /media/upload with command=APPEND,
//           POST /media/upload with command=FINALIZE,
//           final POST /tweets includes media_ids

// Test: GetEngagementAsync returns failure on 403 (free tier)
//   Arrange: Mock HTTP to return 403
//   Act: Call GetEngagementAsync("tweet-123")
//   Assert: Result is failure with appropriate error message about free tier

// Test: GetProfileAsync returns user profile data
//   Arrange: Mock HTTP to return user JSON from /users/me
//   Act: Call GetProfileAsync
//   Assert: Result contains PlatformProfile with username and display name
```

### LinkedInPlatformAdapter

```csharp
// File: tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/LinkedInPlatformAdapterTests.cs

// Test: PublishAsync includes required headers (X-Restli-Protocol-Version, Linkedin-Version)
//   Arrange: Valid PlatformContent
//   Act: Call PublishAsync
//   Assert: HTTP request has X-Restli-Protocol-Version: 2.0.0,
//           HTTP request has Linkedin-Version header matching configured API version

// Test: PublishAsync uploads media via Images API and references URN in post
//   Arrange: PlatformContent with MediaFile, mock IMediaStorage.GetStreamAsync
//   Act: Call PublishAsync
//   Assert: POST to register upload returns uploadUrl + image URN,
//           PUT to uploadUrl with file bytes,
//           POST /posts body includes image URN reference

// Test: GetProfileAsync returns LinkedIn profile
//   Arrange: Mock HTTP to return profile JSON from /userinfo
//   Act: Call GetProfileAsync
//   Assert: Result contains PlatformProfile with name and profile URL
```

### InstagramPlatformAdapter

```csharp
// File: tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/InstagramPlatformAdapterTests.cs

// Test: PublishAsync creates container then publishes (two-step)
//   Arrange: PlatformContent with one image MediaFile
//   Act: Call PublishAsync
//   Assert: POST /{ig-user-id}/media with image_url (signed URL),
//           POST /{ig-user-id}/media_publish with creation_id,
//           result contains PlatformPostId

// Test: PublishAsync uses signed URL for media container
//   Arrange: PlatformContent with MediaFile(FileId="abc")
//   Act: Call PublishAsync
//   Assert: IMediaStorage.GetSignedUrlAsync called with "abc",
//           container creation request includes the signed URL

// Test: PublishAsync polls container status before publishing video
//   Arrange: PlatformContent with video MediaFile,
//            mock container status to return IN_PROGRESS then FINISHED
//   Act: Call PublishAsync
//   Assert: GET /{container-id}?fields=status_code polled,
//           publish only called after FINISHED status

// Test: PublishAsync creates carousel with child containers
//   Arrange: PlatformContent with 3 MediaFiles, Metadata["carousel"]="true"
//   Act: Call PublishAsync
//   Assert: 3 child container POSTs with is_carousel_item=true,
//           1 parent container POST referencing child IDs,
//           1 publish POST with parent container ID

// Test: ValidateContentAsync rejects text-only posts
//   Arrange: PlatformContent with text but no media
//   Act: Call ValidateContentAsync
//   Assert: Result contains ContentValidation with IsValid=false,
//           Errors includes "media required"
```

### YouTubePlatformAdapter

```csharp
// File: tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/YouTubePlatformAdapterTests.cs

// Test: PublishAsync uploads video via resumable upload (streaming)
//   Arrange: PlatformContent with video MediaFile, mock IMediaStorage.GetStreamAsync
//   Act: Call PublishAsync
//   Assert: Video inserted via YouTube Data API with streaming upload,
//           result contains PlatformPostId (video ID) and PostUrl

// Test: PublishAsync tracks quota usage (1600 per upload)
//   Arrange: Successful upload
//   Act: Call PublishAsync
//   Assert: IRateLimiter.RecordRequestAsync called with quota cost of 1600

// Test: PublishAsync updates metadata with snippet
//   Arrange: PlatformContent with Title and text (description), Metadata["tags"]
//   Act: Call PublishAsync
//   Assert: Video snippet includes title, description, and tags

// Test: GetEngagementAsync returns video statistics
//   Arrange: Mock YouTube API to return statistics for video ID
//   Act: Call GetEngagementAsync("video-123")
//   Assert: Result contains EngagementStats with views, likes, comments

// Test: RefreshToken handles invalid_grant as disconnect
//   Arrange: Mock IOAuthManager.RefreshTokenAsync to return failure with invalid_grant
//   Act: Call PublishAsync (triggers 401 -> refresh)
//   Assert: Platform.IsConnected set to false, result is failure
```

## Implementation Details

### PlatformAdapterBase

Abstract base class that all four adapters inherit from. Located at `src/PersonalBrandAssistant.Infrastructure/Services/Platform/Adapters/PlatformAdapterBase.cs`.

**Constructor dependencies:**
- `IApplicationDbContext` -- load Platform entity with tokens
- `IEncryptionService` -- decrypt access/refresh tokens
- `IRateLimiter` -- pre-call check and post-call recording
- `IOAuthManager` -- refresh tokens on 401
- `IMediaStorage` -- resolve file paths/URLs from FileId
- `ILogger<T>` -- structured logging with platform context

**Key responsibilities (template method pattern):**

1. **Token loading:** Load Platform entity by `Type`, decrypt `EncryptedAccessToken` via `IEncryptionService.Decrypt`. If no token or platform not connected, return `Result.Failure` immediately.

2. **Rate limit check:** Call `IRateLimiter.CanMakeRequestAsync(Type, endpoint, ct)`. If `Allowed=false`, return failure with `RetryAt` and `Reason` from the decision. Do not make the HTTP call.

3. **Execute platform-specific call:** Delegate to abstract method (e.g., `ExecutePublishAsync`, `ExecuteGetEngagementAsync`). The concrete adapter performs the actual HTTP request.

4. **Record rate limit usage:** After a successful call, call `IRateLimiter.RecordRequestAsync` with remaining/resetAt parsed from response headers.

5. **401 handling:** If the platform API returns 401, call `IOAuthManager.RefreshTokenAsync(Type, ct)`. If refresh succeeds, reload the token and retry the platform-specific call exactly once. If refresh fails (e.g., `invalid_grant` for YouTube), mark platform as disconnected and return failure.

6. **429 handling:** If the platform API returns 429, parse `Retry-After` header, update rate limit state via `RecordRequestAsync` with `remaining=0`, and return failure with retry information.

7. **Structured logging:** All operations log with `{Platform}` property for filtering. Log token load, rate limit decisions, API call outcomes, retry attempts.

**Abstract methods each adapter must implement:**

```csharp
/// <summary>Each adapter implements the actual HTTP publish call.</summary>
protected abstract Task<Result<PublishResult>> ExecutePublishAsync(
    string accessToken, PlatformContent content, CancellationToken ct);

protected abstract Task<Result<Unit>> ExecuteDeletePostAsync(
    string accessToken, string platformPostId, CancellationToken ct);

protected abstract Task<Result<EngagementStats>> ExecuteGetEngagementAsync(
    string accessToken, string platformPostId, CancellationToken ct);

protected abstract Task<Result<PlatformProfile>> ExecuteGetProfileAsync(
    string accessToken, CancellationToken ct);

/// <summary>Parse rate limit headers from platform-specific response.</summary>
protected abstract (int? remaining, DateTimeOffset? resetAt) ParseRateLimitHeaders(
    HttpResponseMessage response);
```

The public `ISocialPlatform` methods (`PublishAsync`, `DeletePostAsync`, `GetEngagementAsync`, `GetProfileAsync`) are implemented in the base class using the template: load token -> check rate limit -> call abstract method -> record rate limit -> handle errors.

`ValidateContentAsync` does not require tokens or rate limiting, so it is implemented directly in each concrete adapter without going through the base template.

### TwitterPlatformAdapter

Located at `src/PersonalBrandAssistant.Infrastructure/Services/Platform/Adapters/TwitterPlatformAdapter.cs`.

**Typed HttpClient:** Base URL `https://api.x.com/2` (configurable via `PlatformIntegrationOptions.Twitter.BaseUrl`).

**Key behaviors:**

- **Single tweet:** `POST /tweets` with JSON body `{"text": "..."}`. Parse response for `data.id` and construct URL as `https://x.com/i/status/{id}`.

- **Thread publishing:** When `PlatformContent.Metadata["thread"]` contains a JSON array of strings, post each as a tweet chained via `{"reply":{"in_reply_to_tweet_id":"<previous_id>"}}`. Return the first tweet's ID and URL as the `PublishResult`.

- **Media upload:** Uses Twitter's chunked upload endpoint (v1.1 media upload, still required):
  1. `POST /media/upload` with `command=INIT`, `total_bytes`, `media_type`
  2. `POST /media/upload` with `command=APPEND`, `media_id`, `segment_index`, chunked file data
  3. `POST /media/upload` with `command=FINALIZE`, `media_id`
  4. Poll `GET /media/upload?command=STATUS&media_id=...` for processing status if needed
  5. Include `media_ids` in the tweet JSON body

- **Rate limit headers:** Parse `x-rate-limit-remaining` and `x-rate-limit-reset` (epoch seconds) from response.

- **Free tier detection:** `GetEngagementAsync` calls `GET /tweets/{id}?tweet.fields=public_metrics`. If 403 returned, return failure indicating write-only (free tier) access.

- **Authorization:** `Authorization: Bearer {access_token}` header on all requests.

- **`PlatformType` property:** Returns `PlatformType.TwitterX`.

### LinkedInPlatformAdapter

Located at `src/PersonalBrandAssistant.Infrastructure/Services/Platform/Adapters/LinkedInPlatformAdapter.cs`.

**Typed HttpClient:** Base URL `https://api.linkedin.com/rest` (configurable). Default request headers: `X-Restli-Protocol-Version: 2.0.0`, `Linkedin-Version: {ApiVersion from options}`.

**Key behaviors:**

- **Post creation:** `POST /posts` with LinkedIn Posts API schema. JSON body includes `author` (URN from profile), `commentary`, `visibility` (PUBLIC), and optional `content` for media attachments.

- **Media upload:** Two-step via Images/Videos API:
  1. `POST /images?action=initializeUpload` (or `/videos`) with owner URN -> returns `uploadUrl` and asset URN
  2. `PUT {uploadUrl}` with raw file bytes
  3. Reference the asset URN in the post body under `content.media`

- **Rate limit headers:** LinkedIn uses `Retry-After` header on 429 responses. No per-request remaining count; record `remaining=0` with `resetAt` parsed from `Retry-After`.

- **Profile:** `GET /userinfo` to get sub, name, picture. Map to `PlatformProfile`.

- **Authorization:** `Authorization: Bearer {access_token}` header.

- **`PlatformType` property:** Returns `PlatformType.LinkedIn`.

### InstagramPlatformAdapter

Located at `src/PersonalBrandAssistant.Infrastructure/Services/Platform/Adapters/InstagramPlatformAdapter.cs`.

**Typed HttpClient:** Base URL `https://graph.facebook.com/v21.0` (configurable, version pinned).

**Key behaviors:**

- **Two-step publish (single image):**
  1. `POST /{ig-user-id}/media` with `image_url` (signed URL from `IMediaStorage.GetSignedUrlAsync`) and `caption`
  2. `POST /{ig-user-id}/media_publish` with `creation_id` from step 1
  The IG user ID is stored in `Platform.Settings` or retrieved via `GET /me?fields=instagram_business_account` on first use.

- **Video publishing:** Same two-step but with `video_url` and `media_type=VIDEO`. After container creation, poll `GET /{container-id}?fields=status_code` until `status_code=FINISHED` (or `ERROR`). If status is not immediately `FINISHED`, return `PublishResult` with status indicating `Processing` -- the `PublishCompletionPoller` (section-11) handles the rest.

- **Carousel:** Create child containers with `is_carousel_item=true` for each media item (max 10), then create parent container referencing all child IDs, then publish the parent.

- **Content validation:** `ValidateContentAsync` rejects `PlatformContent` with no media (Instagram requires media). Returns `ContentValidation` with `IsValid=false` and error "Instagram requires at least one media attachment".

- **Rate limit:** No per-request headers. Query `GET /{ig-user-id}/content_publishing_limit` and cache result for 5 minutes (use `IMemoryCache` or a simple timestamp field). Report remaining quota to `IRateLimiter.RecordRequestAsync`.

- **Authorization:** Access token passed as query parameter `?access_token={token}` (Meta Graph API convention).

- **`PlatformType` property:** Returns `PlatformType.Instagram`.

### YouTubePlatformAdapter

Located at `src/PersonalBrandAssistant.Infrastructure/Services/Platform/Adapters/YouTubePlatformAdapter.cs`.

**Important:** This adapter uses the `Google.Apis.YouTube.v3` NuGet package instead of raw `HttpClient` for the core upload/metadata operations. The typed `HttpClient` is still used for any raw API calls not covered by the SDK. The `Google.Apis.Auth` package provides credential handling.

**Key behaviors:**

- **Video upload:** Use `YouTubeService.Videos.Insert` with resumable upload. Stream the video file from `IMediaStorage.GetStreamAsync(fileId)` -- do not buffer the entire file in memory. Set snippet (title from `PlatformContent.Title`, description from `PlatformContent.Text`, tags from `PlatformContent.Metadata["tags"]`), status (privacy from metadata or default to `public`).

- **Metadata update:** `YouTubeService.Videos.Update` with snippet changes. Costs 50 quota units.

- **Thumbnail:** `YouTubeService.Thumbnails.Set` if thumbnail media is present. Costs 50 quota units.

- **Quota tracking:** After each API call, record quota cost via `IRateLimiter.RecordRequestAsync`. Costs: upload = 1600, update = 50, thumbnail = 50, list = 1. The rate limiter (section-05) tracks daily total against the 10,000 unit limit.

- **Engagement stats:** `YouTubeService.Videos.List` with `part=statistics` for views, likes, comments, shares.

- **`invalid_grant` handling:** When `IOAuthManager.RefreshTokenAsync` fails with an `invalid_grant` response, set `Platform.IsConnected = false` and return failure. The user must reconnect via OAuth.

- **`PlatformType` property:** Returns `PlatformType.YouTube`.

- **Credential setup:** Create `UserCredential` or `GoogleCredential` from the decrypted access token. The base class handles token refresh via `IOAuthManager` before passing the token down.

### PlatformProfile Record

The adapters return profile data as a shared record. Define alongside other models:

```csharp
/// <summary>Profile information returned by GetProfileAsync.</summary>
public record PlatformProfile(string PlatformUserId, string DisplayName, string? ProfileUrl, string? AvatarUrl);
```

This record should be placed in `src/PersonalBrandAssistant.Application/Common/Models/PlatformProfile.cs`. It is part of the `ISocialPlatform` contract (section-02 defines the interface; this model is used by the return type).

### NuGet Dependencies

The following packages are needed in `PersonalBrandAssistant.Infrastructure.csproj`:

- `Microsoft.Extensions.Http.Polly` -- Polly integration with `IHttpClientFactory` for typed clients
- `Google.Apis.YouTube.v3` -- official YouTube Data API SDK for video upload/metadata
- `Google.Apis.Auth` -- Google OAuth credential handling for YouTube

For tests in `PersonalBrandAssistant.Infrastructure.Tests.csproj`:

- `Moq` (already present)
- `RichardSzalay.MockHttp` or a hand-rolled `MockHttpMessageHandler` -- for intercepting typed `HttpClient` requests in tests

### MockHttpMessageHandler for Tests

Tests need to intercept HTTP calls made by the typed `HttpClient`. Create a simple mock handler:

```csharp
/// <summary>
/// Test helper for intercepting HttpClient requests.
/// Queue responses and assert requests were made with expected parameters.
/// </summary>
// File: tests/PersonalBrandAssistant.Infrastructure.Tests/Services/Platform/MockHttpMessageHandler.cs
```

The handler should support queuing responses, capturing sent requests for assertion, and matching by URL pattern. Keep it simple -- a `Queue<HttpResponseMessage>` with the ability to inspect `SentRequests` after the test.

### Key Design Decisions

1. **Template method pattern in base class:** The base class implements `ISocialPlatform` methods as `sealed` (or non-virtual) calling protected abstract methods. This ensures all adapters consistently check rate limits and handle auth errors without code duplication.

2. **FileId not FilePath:** `MediaFile.FileId` is resolved to actual paths/URLs via `IMediaStorage` inside each adapter. This prevents infrastructure path leakage across layer boundaries.

3. **YouTube SDK vs HttpClient:** YouTube uses the official SDK because its resumable upload protocol is complex and the SDK handles chunked streaming correctly. The other three platforms use raw `HttpClient` since their APIs are straightforward REST.

4. **Token passed as parameter:** The base class decrypts the token and passes it as a `string accessToken` parameter to the abstract methods. Concrete adapters never touch `IEncryptionService` directly.

5. **Rate limit recording from response headers:** Each adapter implements `ParseRateLimitHeaders` to extract platform-specific rate limit information. The base class calls this after every successful response and feeds the data to `IRateLimiter.RecordRequestAsync`.