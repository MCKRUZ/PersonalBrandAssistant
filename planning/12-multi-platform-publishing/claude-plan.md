# Multi-Platform Publishing — Implementation Plan

## 1. Context and Goals

PBA (Personal Brand Assistant) is a .NET 10 + Angular 19 application that manages content creation and publishing for a personal brand. It currently has a working Content Studio with a full state machine (Idea → Draft → Review → Approved → Scheduled → Published → Archived) and a single publishing connector for a static blog (git-based HTML publishing to matthewkruczek.ai).

The goal is to extend publishing to four additional platforms — Medium, Substack, LinkedIn, and Twitter/X — so that content created in PBA can be published to multiple channels from a single interface. Each platform has different APIs, authentication models, and content format requirements.

Existing Python scripts in the `matthewkruczek-ai` repository handle Medium and Substack publishing today. This plan replaces those with native C# connectors inside PBA's infrastructure layer.

### Prerequisites
- Add `Medium` to the `Platform` enum (currently missing — only Blog, Substack, LinkedIn, Twitter, Reddit, YouTube exist)

### What This Plan Covers
- A unified `IPlatformConnector` interface with keyed DI resolution by `Platform` enum
- A shared content transformation pipeline with per-platform formatters
- Four new connectors: Medium, Substack, LinkedIn, Twitter
- OAuth 2.0 flow for LinkedIn and Twitter (full callback endpoint, token exchange, storage)
- Encrypted credential storage in PostgreSQL
- Retry queue for failed publishes (Hangfire background jobs)
- API endpoint updates for multi-platform publishing
- Frontend changes: platform target selection, publish confirmation modal, connection management

### What This Plan Does Not Cover
- Reddit (account banned), YouTube (requires video pipeline)
- Automated scheduling AI or analytics aggregation
- Medium series management (future enhancement)
- Social media comment/reply management

---

## 2. Architecture: IPlatformConnector and Keyed DI

### Current State
The existing architecture uses a platform-specific interface `IBlogConnector` with a single implementation `BlogConnector`. The `ContentPublisher` orchestrator has a hardcoded `if (content.PrimaryPlatform == Platform.Blog)` check that only routes to `BlogConnector`. Adding platforms means adding more `if` branches — this doesn't scale.

### Target State
Replace `IBlogConnector` with a generic `IPlatformConnector` interface. All platforms — including the existing Blog — implement this interface. The `ContentPublisher` resolves connectors via keyed DI using `Platform` enum values, eliminating conditional routing.

### IPlatformConnector Interface

```csharp
public interface IPlatformConnector
{
    Platform Platform { get; }
    Task<PlatformPublishResult> PublishAsync(PlatformPublishRequest request, CancellationToken ct);
    Task<bool> ValidateCredentialsAsync(CancellationToken ct);
    PlatformCapabilities GetCapabilities();
}
```

```csharp
public record PlatformPublishRequest(
    Content Content,
    string TransformedContent,  // already processed by the transformation pipeline
    IReadOnlyList<string> Tags,
    string? CanonicalUrl,
    PublishMode Mode  // Draft, Publish, Schedule
);

public record PlatformPublishResult(
    bool Success,
    string? PublishedUrl,
    string? PlatformPostId,
    string? ErrorMessage
);

public record PlatformCapabilities(
    int MaxCharacters,
    bool SupportsMarkdown,
    bool SupportsHtml,
    bool SupportsImages,
    bool SupportsScheduling,
    bool SupportsThreads,
    IReadOnlyList<string> SupportedMediaTypes
);
```

### Registration Pattern

Use .NET 8+ keyed services to register connectors by `Platform` enum:

```csharp
services.AddKeyedScoped<IPlatformConnector, BlogConnector>(Platform.Blog);
services.AddKeyedScoped<IPlatformConnector, MediumConnector>(Platform.Medium);
services.AddKeyedScoped<IPlatformConnector, SubstackConnector>(Platform.Substack);
services.AddKeyedScoped<IPlatformConnector, LinkedInConnector>(Platform.LinkedIn);
services.AddKeyedScoped<IPlatformConnector, TwitterConnector>(Platform.Twitter);
```

The `ContentPublisher` injects `IServiceProvider` and resolves connectors dynamically:

```csharp
var connector = serviceProvider.GetKeyedService<IPlatformConnector>(platform);
```

### BlogConnector Migration
The existing `BlogConnector` must be adapted to implement `IPlatformConnector` instead of `IBlogConnector`. Its current `PublishAsync(Content, CancellationToken)` signature changes to accept `PlatformPublishRequest` and return `PlatformPublishResult`. The `IBlogConnector` interface is then removed.

---

## 3. Content Transformation Pipeline

### Problem
Each platform requires different content formatting: Medium accepts markdown with canonical URLs; Substack needs Tiptap JSON; LinkedIn needs plain text under 3000 chars; Twitter needs plain text under 280 chars (or thread segments). The Python scripts embed this logic inline. A shared pipeline with pluggable formatters keeps transformation logic testable and the connectors focused on API communication.

### Architecture

```
Raw Markdown (Content.Body)
    │
    ▼
IContentTransformer.TransformAsync(content, platform)
    │
    ├─ SharedPreprocessor: strip YAML frontmatter, normalize image paths
    │
    ├─ IPlatformFormatter (resolved by Platform enum)
    │   ├─ BlogFormatter: markdown → HTML via Markdig, apply template
    │   ├─ MediumFormatter: inject canonical URL, resolve images to absolute URLs
    │   ├─ SubstackFormatter: markdown → Tiptap JSON, inject subscribe widget
    │   ├─ LinkedInFormatter: strip markdown to plain text, truncate to 3000 chars
    │   └─ TwitterFormatter: strip markdown, split into thread segments at 280 chars
    │
    └─ Returns: string (formatted content ready for the platform API)
```

### Interface

```csharp
public interface IContentTransformer
{
    Task<string> TransformAsync(Content content, Platform platform, CancellationToken ct);
}

public interface IPlatformFormatter
{
    Platform Platform { get; }
    Task<string> FormatAsync(PreprocessedContent content, CancellationToken ct);
}

public record PreprocessedContent(
    string Title,
    string Body,           // frontmatter stripped, images resolved
    string? CanonicalUrl,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ImageReference> Images
);
```

The `ContentTransformer` runs shared preprocessing first, then delegates to the platform-specific formatter. Formatters are registered via keyed DI identical to the connector pattern.

### Key Transformations by Platform

**Medium:** Keep markdown (Medium API accepts it natively). Add canonical URL footer pointing to matthewkruczek.ai. Resolve relative image paths to absolute URLs. Convert SVG references to PNG equivalents (SVGs don't render on Medium).

**Substack:** Convert markdown to Substack's Tiptap JSON document structure. This is the most complex transformation — each markdown element (paragraphs, headings, lists, images, code blocks) maps to a Tiptap node type. Insert a subscribe widget node after the executive summary section. Upload images to Substack's CDN and replace URLs. Strip references section and author bio.

**LinkedIn:** Convert markdown to plain text. Preserve line breaks and bullet structure but strip all formatting (bold, italic, links become bare text). Truncate to 3000 characters with an ellipsis and "Read more" link back to the blog post.

**Twitter:** Convert markdown to plain text. If content exceeds 280 characters, split into a thread: break at sentence boundaries near the 280-char limit, number segments (1/N format or use reply chaining). Include a link to the full article in the first or last tweet.

---

## 4. Domain Model Changes

### New Entity: PlatformCredential

Stores OAuth tokens and API credentials per platform. All token values are encrypted at rest using AES-256 with a key from configuration (`Encryption:Key`).

```csharp
public class PlatformCredential
{
    public Guid Id { get; init; }
    public Platform Platform { get; set; }
    public string EncryptedAccessToken { get; set; }
    public string? EncryptedRefreshToken { get; set; }
    public DateTimeOffset? AccessTokenExpiresAt { get; set; }
    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }
    public string? Scopes { get; set; }
    public bool IsActive { get; set; }
    public string? EncryptedCookies { get; set; }       // Substack: encrypted session cookies
    public string? EncryptedIntegrationToken { get; set; } // Medium: integration token
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

### Content Entity Update

Add a `TargetPlatforms` property to track which platforms a piece of content should be published to. This is separate from `PrimaryPlatform` which identifies the canonical version.

```csharp
// On Content entity
public List<Platform> TargetPlatforms { get; set; } = [];
```

Store as a JSON column in PostgreSQL (EF Core value converter). Populated when the user creates or edits content, confirmed at publish time.

### ContentPlatformPublish Update

Add retry tracking fields:

```csharp
// Additional fields on ContentPlatformPublish
public int RetryCount { get; set; }
public DateTimeOffset? NextRetryAt { get; set; }
```

### EF Configuration

Add `PlatformCredential` to `ApplicationDbContext` as a new `DbSet`. Add value converter for `Content.TargetPlatforms` (JSON serialization). Add migration for new table and columns.

---

## 5. Platform Connectors

### 5.1 MediumConnector

**API:** Medium REST API v1 — officially unsupported but functional. Existing integration tokens continue to work.

**Authentication:** Bearer token from `PlatformCredential.EncryptedIntegrationToken`. No OAuth flow needed — user enters token manually in the platform connections UI.

**Publishing Flow:**
1. Decrypt integration token from `PlatformCredential`
2. `GET /v1/me` to obtain the authenticated user's `id` (cache this after first successful call)
3. `POST /v1/users/{userId}/posts` with body:
   - `title`: from content
   - `contentFormat`: `"markdown"` (Medium accepts markdown natively)
   - `content`: output from MediumFormatter (includes canonical URL footer)
   - `tags`: from content, max 3, max 25 chars each (truncate/select)
   - `canonicalUrl`: link to the published blog post on matthewkruczek.ai
   - `publishStatus`: from publish options (`"public"`, `"draft"`, or `"unlisted"`)
4. Extract `url` and `id` from response for `ContentPlatformPublish` record

**Error Handling:** Medium API returns structured error responses. Map HTTP 401 to "token expired or invalid" and prompt reconfiguration. Map 429 to "rate limited" and schedule retry.

**HttpClient:** Registered as a typed client via `IHttpClientFactory` with base address `https://api.medium.com/v1` and default `Accept: application/json` header.

### 5.2 SubstackConnector

**API:** Reverse-engineered internal API. No official documentation. Endpoints based on analysis of the existing Python `substack/publish.py` script and community reverse-engineering efforts.

**Authentication:** Cookie-based session. Substack uses three cookies: `substack.sid`, `sid`, and `substack.lli`. These are obtained by logging in with email/password and stored encrypted in `PlatformCredential.EncryptedCookies`. When cookies expire, the connector re-authenticates automatically.

**Login Flow:**
1. User enters email and password in the platform connections UI
2. `POST https://{publication}.substack.com/api/v1/login` with email and password
3. Extract session cookies from response
4. Store encrypted cookies in `PlatformCredential` — do NOT persist the email/password
5. When cookies expire, prompt the user to re-login manually (no auto-re-login with stored credentials)

**Publishing Flow:**
1. Load and decrypt cookies. If expired, re-login.
2. Get user info to obtain `byline_id`
3. Create draft: `POST /api/v1/drafts` with Tiptap JSON body (from SubstackFormatter), title, subtitle
4. Upload images: for each image in content, upload to Substack CDN via their image upload endpoint, replace URLs in the draft
5. Add tags: `PUT /api/v1/post/{post_id}/tags`
6. Publish: based on mode:
   - Draft: stop after step 3
   - Publish: `POST /api/v1/drafts/{draft_id}/prepublish` → `POST /api/v1/drafts/{draft_id}/publish` with `{ send_email: true, audience: "everyone" }`
   - Schedule: update draft with scheduled time (limited — Substack's internal API has limited scheduling support)

**Tiptap JSON Conversion:** This is the most complex part. The `SubstackFormatter` must convert markdown AST nodes to Tiptap document nodes. Key mappings:
- Paragraph → `{ type: "paragraph", content: [{ type: "text", text: "..." }] }`
- Heading → `{ type: "heading", attrs: { level: N } }`
- Bullet list → `{ type: "bulletList", content: [{ type: "listItem" }] }`
- Image → `{ type: "captionedImage", attrs: { src: "cdn-url" } }`
- Code block → `{ type: "codeBlock" }`
- Bold/italic → marks array on text nodes

**Resilience:** This connector is inherently fragile and must be behind a feature flag from day one (not deferred to Phase 6). Wrap all API calls with specific error handling that distinguishes "auth expired" from "API changed" from "rate limited." Log all Substack API request/response payloads at Debug level for diagnosing when endpoints break.

**HttpClient:** Named client via `IHttpClientFactory` with cookie container support. Base address: `https://{publication}.substack.com`.

### 5.3 LinkedInConnector

**API:** LinkedIn Community Management API v2 — production-grade, versioned, Microsoft-backed.

**Authentication:** OAuth 2.0 three-legged flow. User already has a LinkedIn developer app configured.

**OAuth Flow:**
1. User clicks "Connect LinkedIn" in the platform connections UI
2. Frontend calls `GET /api/auth/linkedin/authorize`
3. Backend constructs authorization URL with `client_id`, `redirect_uri`, `scope` (openid, profile, w_member_social), `state` (CSRF token)
4. Backend redirects user to LinkedIn authorization page
5. User grants permissions, LinkedIn redirects to `GET /api/auth/linkedin/callback?code=...&state=...`
6. Backend validates `state`, exchanges `code` for tokens via `POST https://www.linkedin.com/oauth/v2/accessToken`
7. Backend stores encrypted access token (60-day lifetime) and refresh token (365-day lifetime) in `PlatformCredential`
8. Backend calls `GET /userinfo` to get the user's LinkedIn person URN, stores it

**Token Refresh:**
Before each API call, check if access token expires within 5 minutes. If so, refresh via `POST https://www.linkedin.com/oauth/v2/accessToken` with `grant_type=refresh_token`. Update stored tokens.

**Publishing Flow:**
1. Validate/refresh token
2. If content has images: upload via two-step process:
   - `POST https://api.linkedin.com/rest/images?action=initializeUpload` with owner URN → get `uploadUrl` and image URN
   - `PUT {uploadUrl}` with raw image bytes
3. Create post: `POST https://api.linkedin.com/rest/posts` with:
   - `author`: person URN
   - `commentary`: formatted text from LinkedInFormatter (max 3000 chars)
   - `visibility`: `"PUBLIC"`
   - `distribution`: `{ feedDistribution: "MAIN_FEED" }`
   - `lifecycleState`: `"PUBLISHED"` (LinkedIn has no draft/schedule via API)
   - If sharing a blog link: include `content` object with article URL and description
4. Extract post URN from `x-restli-id` response header

**Required Headers:** `Authorization: Bearer {token}`, `X-Restli-Protocol-Version: 2.0.0`, `Linkedin-Version: 202604` (update monthly as needed)

**HttpClient:** Typed client via `IHttpClientFactory` with base address `https://api.linkedin.com`. Polly retry policy for transient failures.

### 5.4 TwitterConnector

**API:** Twitter/X API v2. Pay-per-use pricing (~$0.01/post create).

**Authentication:** OAuth 2.0 with PKCE. Requires new developer app setup.

**OAuth Flow:**
1. User clicks "Connect Twitter" in platform connections UI
2. Frontend calls `GET /api/auth/twitter/authorize`
3. Backend generates PKCE code challenge (SHA-256), stores code verifier in session/temp storage
4. Backend constructs authorization URL with `client_id`, `redirect_uri`, scopes (tweet.read, tweet.write, users.read, media.write, offline.access), `state`, `code_challenge`, `code_challenge_method=S256`
5. User grants permissions on twitter.com, redirects to `GET /api/auth/twitter/callback?code=...&state=...`
6. Backend exchanges code + code_verifier for tokens via `POST https://api.twitter.com/2/oauth2/token`
7. Store encrypted access token (2-hour lifetime) and refresh token

**Token Refresh:**
Twitter access tokens expire after 2 hours — aggressive refresh is required. Before each API call, check if token expires within 10 minutes. Refresh via `POST https://api.twitter.com/2/oauth2/token` with `grant_type=refresh_token`.

**Publishing Flow — Single Tweet:**
1. Validate/refresh token
2. If content has media: upload via three-step chunked process:
   - INIT: `POST https://api.x.com/2/media/upload/initialize` with media_type and total_bytes
   - APPEND: `POST https://api.x.com/2/media/upload/{media_id}/append` with binary chunks (max 5MB each)
   - FINALIZE: `POST https://api.x.com/2/media/upload/{media_id}/finalize`
   - For video/GIF: poll `processing_info` status until `succeeded`
3. Create tweet: `POST https://api.x.com/2/tweets` with text and optional `media.media_ids`
4. Extract tweet `id` from response

**Publishing Flow — Thread:**
1. Split content into segments (from TwitterFormatter)
2. Post first tweet, capture `id`
3. For each subsequent segment: `POST /2/tweets` with `reply.in_reply_to_tweet_id` set to previous tweet's `id`
4. Track all tweet IDs in `ContentPlatformPublish` (store first tweet ID as `PlatformPostId`)

**Media Upload Caveat:** Some developers report 403 errors on v2 media endpoints with OAuth 2.0. Build the connector using v2 media upload only. If 403 errors occur in practice, a v1.1 fallback using OAuth 1.0a can be added later — but do not build it preemptively (YAGNI). Document the risk.

**HttpClient:** Typed client via `IHttpClientFactory` with base address `https://api.x.com`.

---

## 6. OAuth Infrastructure

### OAuth Service

A central `IOAuthService` manages the OAuth flow for all platforms that use it (LinkedIn, Twitter). This avoids duplicating authorization URL construction, token exchange, and state validation across connectors.

```csharp
public interface IOAuthService
{
    Task<string> GetAuthorizationUrlAsync(Platform platform, CancellationToken ct);
    Task<PlatformCredential> ExchangeCodeAsync(Platform platform, string code, string state, CancellationToken ct);
    Task<string> RefreshTokenAsync(PlatformCredential credential, CancellationToken ct);
}
```

Each platform's OAuth parameters (authorization URL, token URL, scopes, client credentials) come from its platform-specific options class.

### CSRF Protection

The `state` parameter in OAuth flows must be validated to prevent CSRF attacks. Generate a cryptographically random string, store it in a short-lived cache (IDistributedCache or in-memory with 10-minute TTL), and validate on callback. Reject callbacks with missing or invalid state.

### PKCE (Twitter Only)

Twitter requires PKCE with S256 challenge method. The `IOAuthService` generates a random code verifier, computes the SHA-256 code challenge, stores the verifier keyed by state value, and uses it during token exchange.

### Token Encryption

Use AES-256-GCM for encrypting tokens at rest. The encryption key comes from configuration (`Encryption:Key` in User Secrets / KeyVault). Create an `ITokenEncryptor` service that handles encrypt/decrypt operations. Every connector calls this service before storing or using tokens.

---

## 7. ContentPublisher Refactor

### Current Flow
```
PublishAsync(contentId)
  → load content
  → if Platform == Blog → blogConnector.PublishAsync()
  → create ContentPlatformPublish record
  → fire state machine trigger
```

### New Flow
```
PublishAsync(contentId, targetPlatforms?)
  → load content
  → determine target platforms (from parameter, or content.TargetPlatforms, or just PrimaryPlatform)
  → idempotency check: skip any platform that already has a Published ContentPlatformPublish record
  → identify primary platform (content.PrimaryPlatform)
  → transform content for primary platform
  → publish to primary platform via keyed connector
  → if primary fails → abort, return failure
  → if primary succeeds → fire state machine trigger (Approved → Published)
  → for each secondary platform (parallel):
      → transform content
      → publish via keyed connector
      → create ContentPlatformPublish record (Success or Failed)
      → if failed → schedule retry via BackgroundJob.Schedule
  → return aggregate result (primary + per-platform status)
```

**Design note:** Content is considered "published" once the primary platform succeeds. Secondary platform failures are recorded as Failed ContentPlatformPublish records and retried automatically. The content's state machine status is Published regardless of secondary outcomes.

### Multi-Platform Publish Command

Update `PublishContent.Command` to accept an optional list of target platforms:

```csharp
public record Command(Guid ContentId, IReadOnlyList<Platform>? TargetPlatforms = null) : IRequest<Result<PublishResult>>;

public record PublishResult(
    bool PrimarySuccess,
    string? PrimaryUrl,
    IReadOnlyList<PlatformPublishOutcome> SecondaryOutcomes
);

public record PlatformPublishOutcome(Platform Platform, bool Success, string? Url, string? Error);
```

If `TargetPlatforms` is null, use `content.TargetPlatforms`. If that's also empty, use only `content.PrimaryPlatform`.

**Hangfire Compatibility:** Keep the existing `IContentPublisher.PublishAsync(Guid contentId)` signature as the primary method (used by `HangfireContentScheduler` and `ScheduledPublishReconciler`). Add a new overload `PublishAsync(Guid contentId, IReadOnlyList<Platform> targetPlatforms, CancellationToken ct)` for the MediatR handler. The Guid-only method calls the full method with `targetPlatforms: null` and `CancellationToken.None`.

### Parallel Secondary Publishing

Secondary platforms publish in parallel using `Task.WhenAll`. Each publish is independent — a failure on one platform does not affect others. Results are collected into a list of per-platform outcomes.

---

## 8. Retry Queue

### Design

Failed secondary-platform publishes should be retried automatically. Use Hangfire to schedule retry jobs with exponential backoff.

**Retry Policy:**
- Max 3 retries per platform per publish attempt
- Backoff: 5 minutes, 30 minutes, 2 hours
- After 3 failures: mark as permanently failed, surface in UI for manual retry

**Retry Mechanism:**
When a secondary publish fails, `ContentPublisher` creates a `ContentPlatformPublish` record with `Status = Failed` and `RetryCount = 0`, then schedules a specific retry via `BackgroundJob.Schedule<IPublishRetryHandler>(x => x.RetryAsync(publishRecordId), TimeSpan.FromMinutes(5))`. This follows the same pattern as `HangfireContentScheduler` — no polling, just delayed execution.

The `IPublishRetryHandler.RetryAsync` method:
1. Loads the `ContentPlatformPublish` record
2. Idempotency check: if already `Published`, skip
3. Resolves the connector for the platform, transforms content, attempts publish
4. On success: update record to `Status = Published` with URL and platform post ID
5. On failure: increment `RetryCount`. If < 3, schedule next retry at next backoff interval (5 min, 30 min, 2 hours). If >= 3, mark as permanently failed (surface in UI for manual retry).

---

## 9. API Endpoints

### OAuth Endpoints (new)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/auth/{platform}/authorize` | Initiate OAuth flow, redirect to platform |
| GET | `/api/auth/{platform}/callback` | Handle OAuth callback, exchange code for tokens |
| GET | `/api/auth/{platform}/status` | Check connection status (connected/expired/not configured) |
| DELETE | `/api/auth/{platform}` | Disconnect platform (revoke + delete tokens) |

### Platform Management Endpoints (new)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/platforms` | List all platforms with connection status and capabilities |
| POST | `/api/platforms/{platform}/credentials` | Store manual credentials (Medium token, Substack cookies) |

### Updated Content Endpoints

| Method | Path | Change |
|--------|------|--------|
| POST | `/api/content/{id}/publish` | Accept optional `targetPlatforms` array in body |
| PUT | `/api/content/{id}/schedule` | Accept optional `targetPlatforms` array in body |
| POST | `/api/content/{id}/retry/{platform}` | New — manually retry a failed platform publish |
| GET | `/api/content/{id}/publish-status` | New — get per-platform publish status for a content item |

### Updated Content DTOs

The `CreateContent` and `UpdateContent` commands need a `TargetPlatforms` field so users can set target platforms during content creation/editing.

---

## 10. Frontend Changes

### Content Editor Updates

**Platform Target Selector:** Add a multi-select checkbox group to the content editor showing available platforms (Blog, Medium, Substack, LinkedIn, Twitter). Each checkbox is only enabled if the platform is connected (has valid credentials). The selection populates `content.targetPlatforms` and is saved with the content.

**Character Count Indicators:** Show per-platform character counts below the editor. For Twitter (280), LinkedIn (3000), etc. Highlight when content exceeds a platform's limit. For Blog/Medium/Substack, show word count instead (no meaningful character limit).

### Publish Confirmation Modal

When the user clicks Publish or Schedule, show a confirmation modal that:
1. Lists each target platform with a preview of the transformed content
2. Shows connection status (green checkmark = connected, red X = not connected, yellow = token expiring)
3. Allows the user to toggle platforms on/off before confirming
4. Shows the primary platform prominently (must succeed for publish to proceed)
5. Has Publish / Schedule / Cancel buttons

### Platform Connections Page

A new settings page (`/settings/platforms`) where the user manages platform connections:
- **LinkedIn:** "Connect" button triggers OAuth flow, shows "Connected as {name}" when active
- **Twitter:** "Connect" button triggers OAuth flow, shows connection status
- **Medium:** Text input for integration token, "Save" button, shows "Connected" when token validates
- **Substack:** Email/password form, "Login" button, shows "Connected" when cookies are valid

Each platform card shows: connection status, last publish date, token expiry (if applicable), and a "Disconnect" button.

### Content List Enhancements

Add per-platform publish status badges to content cards in the list view. A content item published to Blog + Medium but failed on Twitter shows: Blog (green), Medium (green), Twitter (red with "Retry" action).

---

## 11. Configuration and Secrets

### Options Classes

Each platform gets its own options class in the Infrastructure layer:

```csharp
public class MediumOptions { public bool Enabled { get; init; } public string DefaultPublishStatus { get; init; } = "draft"; }
public class SubstackOptions { public bool Enabled { get; init; } public string PublicationSlug { get; init; } public string DefaultAudience { get; init; } = "everyone"; }
public class LinkedInOptions { public bool Enabled { get; init; } public string ClientId { get; init; } public string ClientSecret { get; init; } public string RedirectUri { get; init; } }
public class TwitterOptions { public bool Enabled { get; init; } public string ClientId { get; init; } public string ClientSecret { get; init; } public string RedirectUri { get; init; } public string? ApiKey { get; init; } public string? ApiSecret { get; init; } }
public class EncryptionOptions { public string Key { get; init; } }
```

### appsettings.json

All platform settings live under a `Publishing` section. Secrets (tokens, client secrets) go in User Secrets for development and environment variables / KeyVault for Docker deployment. Only non-secret configuration (Enabled flags, publication slugs, redirect URIs) goes in appsettings.json.

**OAuth Redirect URIs:** The `RedirectUri` values must exactly match what's registered with LinkedIn and Twitter. In Docker on Mac Mini behind Tailscale, the external URL differs from localhost. Configure redirect URIs per environment: `https://localhost:{port}/api/auth/{platform}/callback` for local dev, `https://{tailscale-hostname}/api/auth/{platform}/callback` for Docker. Both LinkedIn and Twitter require HTTPS for redirect URIs in production.

### DI Registration

A single `AddPublishingDependencies()` extension method in Infrastructure registers:
- Options for each platform
- HttpClient factories for each platform
- IPlatformConnector implementations (keyed by Platform)
- IPlatformFormatter implementations (keyed by Platform)
- IContentTransformer
- IOAuthService
- ITokenEncryptor
- PublishRetryProcessor (Hangfire recurring job)

---

## 12. Implementation Order

The implementation should proceed in this order, where each step builds on the previous:

### Phase 1: Foundation
1. **Add `Medium` to Platform enum** and define `IPlatformConnector` interface + supporting types (PlatformPublishRequest, PlatformPublishResult, PlatformCapabilities, PublishResult) in Application layer
2. **PlatformCredential entity + migration** — new table (own `Guid Id`, no BaseEntity), encryption infrastructure, Content.TargetPlatforms JSON column, ContentPlatformPublish retry fields
3. **IContentTransformer + IPlatformFormatter interfaces** in Application layer
4. **ContentTransformer implementation** with shared preprocessor
5. **BlogConnector migration** — adapt to implement IPlatformConnector, remove IBlogConnector, remove internal Markdig conversion (use TransformedContent from BlogFormatter instead)
6. **ContentPublisher refactor** — keyed DI resolution, multi-platform publish flow, primary + best-effort logic, idempotency check, keep Guid-only overload for Hangfire compatibility
7. **Update existing tests** — migrate BlogConnector tests and ContentPublisher tests to new IPlatformConnector interface
8. **DI registration updates** — keyed services, options, HttpClient factories

### Phase 2: OAuth + Credential Storage
8. **ITokenEncryptor** — AES-256 encryption/decryption service
9. **IOAuthService** — authorization URL generation, code exchange, PKCE support, token refresh
10. **OAuth endpoints** — `/api/auth/{platform}/authorize`, `/callback`, `/status`, disconnect
11. **Platform management endpoints** — list platforms, store manual credentials

### Phase 3: Connectors
12. **MediumConnector + MediumFormatter** — simplest connector, good validation of the architecture
13. **LinkedInConnector + LinkedInFormatter** — OAuth flow already built, tests the token refresh pipeline
14. **TwitterConnector + TwitterFormatter** — complex auth (PKCE + short-lived tokens + media fallback), thread support
15. **SubstackConnector + SubstackFormatter** — most complex (cookie auth, Tiptap JSON, image CDN upload)

### Phase 4: Retry + API Updates
16. **Retry handler** — IPublishRetryHandler with BackgroundJob.Schedule (no polling), exponential backoff
17. **Content endpoint updates** — multi-platform publish/schedule, retry endpoint, publish status endpoint
18. **Content DTO updates** — TargetPlatforms on create/update

### Phase 5: Frontend
19. **Platform connections settings page** — OAuth connect/disconnect, token entry, connection status
20. **Content editor platform targets** — checkbox selector, character count indicators
21. **Publish confirmation modal** — per-platform preview and toggle
22. **Content list status badges** — per-platform publish status on content cards

### Phase 6: Polish
23. **Integration testing** — end-to-end flows for each connector with mock APIs
24. **Error handling and logging** — structured logging for all connector operations
25. **Feature flags** — per-platform enable/disable via configuration
