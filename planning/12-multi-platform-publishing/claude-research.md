# Multi-Platform Publishing Research

## Codebase Architecture

### ContentPublisher Orchestrator
- `src/PBA.Infrastructure/Publishing/ContentPublisher.cs` implements `IContentPublisher`
- Single method: `PublishAsync(Guid contentId)`
- Hardcoded `if (content.PrimaryPlatform == Platform.Blog)` — only routes to `IBlogConnector`
- Creates `ContentPlatformPublish` record with status, URL, timestamp
- Fires `ContentTrigger.Publish` via state machine
- **Gap:** No factory/registry pattern. No error handling for unsupported platforms.

### BlogConnector (Reference Connector)
- `src/PBA.Infrastructure/Connectors/BlogConnector.cs` implements `IBlogConnector`
- Method: `Task<string> PublishAsync(Content content, CancellationToken ct)`
- Flow: validate → generate slug → read HTML template → markdown→HTML via Markdig → replace tokens → write file → git add/commit/push → return URL
- Config via `BlogConnectorOptions` (RepoPath, TemplatePath, Author, RemoteName, Branch, BaseUrl)
- Registered in `DependencyInjection.cs`: `services.AddScoped<IBlogConnector, BlogConnector>()`

### Connector Interfaces
- `IBlogConnector` in `PBA.Application/Common/Interfaces/`: `Task<string> PublishAsync(Content content, CancellationToken ct)`
- `IContentPublisher`: `Task PublishAsync(Guid contentId)`
- `IContentScheduler`: `string SchedulePublish(Guid contentId, DateTimeOffset publishAt)` + `void CancelScheduledPublish(string jobId)`
- **No generic `IPlatformConnector`** — each platform currently needs its own interface

### Content Domain Model
- `Content.cs`: Title, Body, ContentType, ContentStatus, PrimaryPlatform, VoiceScore, ViralityPrediction, Tags, ScheduledAt, PublishedAt, HangfireJobId, ParentContentId (cross-posts), CrossPosts list
- `ContentPlatformPublish.cs`: ContentId, Platform, PublishStatus (Pending/Published/Failed), PublishedUrl, PlatformPostId, ErrorMessage, Likes/Comments/Shares/Views, MetricsRefreshedAt

### State Machine (Stateless library)
- Idea → Draft → Review → Approved → Scheduled → Published → Archived
- Publishing triggers: `PublishNow` (Approved→Published), `Publish` (Scheduled→Published via Hangfire)
- Entry actions clear scheduling data on transitions back to Draft

### Hangfire Scheduling
- `HangfireContentScheduler`: `Schedule<IContentPublisher>(p => p.PublishAsync(contentId), publishAt)`
- `ScheduledPublishReconciler`: BackgroundService that catches missed schedules on startup

### Cross-Post Generation
- `GenerateCrossPost.Command(Guid ContentId, Platform TargetPlatform)`
- Uses `ISidecarClient` to AI-adapt content for target platform
- Creates child Content entity with `ParentContentId` = parent
- Platform constraints defined in `PlatformConstraints.cs`: Blog/Substack=50K, LinkedIn=3K, Twitter=280, Reddit=40K, YouTube=5K

### PublishContent Command
- Only handles Blog platform. Other platforms create ContentPlatformPublish record but return "no published URL"
- Fires `ContentTrigger.PublishNow` → state machine transition

### DI Registration (Infrastructure)
```
services.Configure<BlogConnectorOptions>(configuration.GetSection(BlogConnectorOptions.SectionName));
services.AddScoped<IBlogConnector, BlogConnector>();
services.AddScoped<IContentPublisher, ContentPublisher>();
services.AddScoped<IContentScheduler, HangfireContentScheduler>();
services.AddHostedService<ScheduledPublishReconciler>();
```

### Testing Setup
- xUnit test runner, Moq for mocking, EF Core InMemory for DB tests
- Pattern: `CreateContext()` with `UseInMemoryDatabase(Guid.NewGuid().ToString())`
- Test naming: `MethodName_Scenario_ExpectedResult`
- Existing tests: `GenerateCrossPostHandlerTests.cs` covers parent/child relationships, platform constraints, duplicate prevention

### Python Scripts (Reference Implementations)

**Medium (`medium/publish.py`):**
- Strip YAML frontmatter, remove "Executive read" sections
- Resolve relative image paths to absolute URLs
- Convert dark SVGs to light PNGs
- Add canonical notice pointing to blog
- API mode: POST to `/v1/users/{userId}/posts`

**Substack (`substack/publish.py`):**
- Strip title (Substack renders from metadata), remove references/author bio
- Upload images to Substack CDN
- Inject subscribe widget after executive summary bullets
- Add image captions from predefined mapping
- Cookie-based auth, supports draft/publish/schedule modes
- Internal API: `POST /api/v1/drafts`, `POST /api/v1/drafts/{id}/publish`

---

## Platform API Research

### Medium REST API v1

**Status:** Officially unsupported but functional. No new tokens issued; existing tokens work indefinitely.

**Auth:** Self-issued integration tokens at `https://medium.com/me/settings`. Header: `Authorization: Bearer {token}`

**Base URL:** `https://api.medium.com/v1`

**Key Endpoints:**
- `GET /v1/me` → user info (need userId for posting)
- `POST /v1/users/{authorId}/posts` → create post
  - Fields: title (required), contentFormat ("html"|"markdown", required), content (required), tags (max 3, max 25 chars each), canonicalUrl, publishStatus ("public"|"draft"|"unlisted"), notifyFollowers
  - Response: `{ data: { id, title, url, canonicalUrl, publishStatus, publishedAt } }`
- `POST /v1/images` → upload image (multipart/form-data, JPEG/PNG/GIF/TIFF). Often unnecessary — Medium auto-sideloads `<img src>` URLs.

**Rate Limits:** Not documented. Likely 429 responses on excessive use.

**Alternative:** Import tool at `https://medium.com/me/import` — paste URL, auto-backdates and adds canonical link.

**Connector Difficulty:** Low. Simple REST API, bearer token auth, accepts markdown directly.

### Substack (No Official API)

**Status:** NO official API. All approaches use reverse-engineered internal endpoints.

**Best Approach: Reverse-Engineered Internal API**
- Auth: Cookie-based session (`sid`, `substack.sid`, `substack.lli`). Cookies expire periodically.
- Content format: Tiptap-based JSON document structure
- `POST https://{publication}.substack.com/api/v1/drafts` → create draft (needs `byline_id`)
- `POST /api/v1/drafts/{draft_id}/publish` → publish with `{ send_email: true, audience: "everyone" }`
- Image upload to CDN via separate endpoint
- Cannot create new schedules via API (can only update existing)

**Alternative: Python Sidecar**
- JPres-Projects/Substack-API: Python REST API client with FastAPI server on port 8000
- Could run as Docker sidecar, call from .NET HttpClient
- More maintainable — Python community actively maintains these wrappers

**Connector Difficulty:** High. No official API, cookie auth expires, Tiptap JSON format is complex, endpoints can break.

### LinkedIn Community Management API v2

**Status:** Production-grade, versioned, Microsoft-backed. Use `Linkedin-Version: YYYYMM` header. `ugcPosts` deprecated; use `/rest/posts`.

**Auth:** OAuth 2.0 three-legged flow
- Authorization URL: `https://www.linkedin.com/oauth/v2/authorization`
- Token exchange: `POST https://www.linkedin.com/oauth/v2/accessToken`
- Access token: 60 days. Refresh token: 365 days. Auth code: 30 minutes.
- Scopes: `w_member_social` (post), `r_member_social` (read engagement), `w_organization_social` (company page), `openid`/`profile`

**Create Post:** `POST https://api.linkedin.com/rest/posts`
- Headers: `Authorization: Bearer {token}`, `X-Restli-Protocol-Version: 2.0.0`, `Linkedin-Version: 202604`
- Body: author (URN), commentary (max 3000 chars), visibility, distribution, lifecycleState
- Response: 201 with `x-restli-id` header

**Image Upload:** Two-step: `POST /rest/images?action=initializeUpload` → PUT binary to uploadUrl → attach URN to post

**Key Gotcha:** No scheduled/deferred publishing via API. `lifecycleState` must be `PUBLISHED`. Scheduling must be handled by PBA's Hangfire.

**Rate Limits:** ~100 API calls/day/member token.

**Connector Difficulty:** Medium. Well-documented API, but OAuth flow adds complexity. Company Page requires separate admin role.

### Twitter/X API v2

**Status:** Active but volatile. Free tier removed Feb 2026. Now pay-per-use (~$0.01/post create).

**Auth:** OAuth 2.0 with PKCE
- Authorization URL: `https://twitter.com/i/oauth2/authorize`
- Token exchange: `POST https://api.twitter.com/2/oauth2/token`
- Access token: 2 hours. Refresh token via `offline.access` scope.
- Scopes: `tweet.read`, `tweet.write`, `users.read`, `media.write`, `offline.access`

**Create Tweet:** `POST https://api.x.com/2/tweets`
- Body: text (max 280), media.media_ids, reply.in_reply_to_tweet_id, poll, quote_tweet_id
- Response: `{ data: { id, text } }`

**Thread Creation:** Chain replies — POST first tweet, use returned ID as `in_reply_to_tweet_id` for next.

**Media Upload:** Three-step chunked: INIT → APPEND (max 5MB chunks) → FINALIZE. For video, poll `processing_info` until complete.
- **Warning:** Some developers report 403 errors on v2 media endpoints with OAuth 2.0. v1.1 media upload with OAuth 1.0a remains more reliable. May need dual auth.

**Rate Limits:** 17 tweets/day on legacy free tier. Pay-per-use has 15-minute rolling windows, capped at 2M reads/month.

**Connector Difficulty:** Medium-High. OAuth PKCE is complex, short token lifetime needs aggressive refresh, dual auth may be needed for media, pricing changes frequently.

---

## Viability Summary

| Platform | API Quality | Auth Complexity | Build Difficulty | Risk Level |
|----------|------------|-----------------|------------------|------------|
| Medium | Frozen but works | Simple (bearer token) | Low | Medium (could be killed) |
| Substack | No official API | Complex (cookies expire) | High | High (breaks anytime) |
| LinkedIn | Production-grade | Medium (OAuth 2.0) | Medium | Low (Microsoft-backed) |
| Twitter/X | Functional, volatile | High (PKCE + dual auth) | Medium-High | Medium (pricing shifts) |

---

## Key Architecture Decisions Needed

1. **Generic IPlatformConnector interface** — replace platform-specific interfaces with a common abstraction
2. **Connector factory/registry** — replace hardcoded `if` in ContentPublisher with keyed DI or factory pattern
3. **Substack approach** — direct HTTP with cookie management vs Python sidecar container
4. **OAuth token storage** — database (encrypted) vs KeyVault vs separate credential store
5. **Multi-platform publish** — sequential or parallel? Error handling for partial failures?
6. **Content transformation** — each connector handles internally, or shared pipeline with platform-specific formatters?
7. **Metrics collection** — background job to poll platform APIs for engagement data?

---

## File Locations Reference

| Component | Path |
|-----------|------|
| IContentPublisher | `src/PBA.Application/Common/Interfaces/IContentPublisher.cs` |
| IBlogConnector | `src/PBA.Application/Common/Interfaces/IBlogConnector.cs` |
| IContentScheduler | `src/PBA.Application/Common/Interfaces/IContentScheduler.cs` |
| ContentPublisher | `src/PBA.Infrastructure/Publishing/ContentPublisher.cs` |
| BlogConnector | `src/PBA.Infrastructure/Connectors/BlogConnector.cs` |
| BlogConnectorOptions | `src/PBA.Infrastructure/Connectors/BlogConnectorOptions.cs` |
| HangfireContentScheduler | `src/PBA.Infrastructure/Publishing/HangfireContentScheduler.cs` |
| ScheduledPublishReconciler | `src/PBA.Infrastructure/Publishing/ScheduledPublishReconciler.cs` |
| ContentStateMachine | `src/PBA.Application/Features/ContentStudio/ContentStateMachine.cs` |
| GenerateCrossPost | `src/PBA.Application/Features/Content/Commands/GenerateCrossPost.cs` |
| PublishContent | `src/PBA.Application/Features/Content/Commands/PublishContent.cs` |
| PlatformConstraints | `src/PBA.Application/Features/Content/PlatformConstraints.cs` |
| Content entity | `src/PBA.Domain/Entities/Content.cs` |
| ContentPlatformPublish | `src/PBA.Domain/Entities/ContentPlatformPublish.cs` |
| Platform enum | `src/PBA.Domain/Enums/Platform.cs` |
| Infrastructure DI | `src/PBA.Infrastructure/DependencyInjection.cs` |
| ContentEndpoints | `src/PBA.Api/Endpoints/ContentEndpoints.cs` |
| Medium publish.py | matthewkruczek-ai/medium/publish.py |
| Substack publish.py | matthewkruczek-ai/substack/publish.py |
