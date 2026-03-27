<!-- PROJECT_CONFIG
runtime: dotnet-angular
test_command: dotnet test && cd src/PersonalBrandAssistant.Web && ng test --watch=false --browsers=ChromeHeadless
END_PROJECT_CONFIG -->

<!-- SECTION_MANIFEST
section-01-data-model
section-02-chat-authoring-backend
section-03-substack-prep-service
section-04-blog-html-generator
section-05-github-publish-service
section-06-rss-detection
section-07-staggered-scheduling
section-08-notification-system
section-09-chat-authoring-frontend
section-10-substack-prep-ui
section-11-blog-publish-ui
section-12-blog-dashboard
section-13-pipeline-integration
END_MANIFEST -->

# Implementation Sections Index

## Dependency Graph

| Section | Depends On | Blocks | Parallelizable |
|---------|------------|--------|----------------|
| section-01-data-model | - | all | Yes (foundation) |
| section-02-chat-authoring-backend | 01 | 09 | Yes |
| section-03-substack-prep-service | 01 | 10 | Yes |
| section-04-blog-html-generator | 01 | 05, 11 | Yes |
| section-05-github-publish-service | 01, 04 | 07, 11 | No |
| section-06-rss-detection | 01 | 07 | Yes |
| section-07-staggered-scheduling | 01, 05, 06 | 12, 13 | No |
| section-08-notification-system | 01 | 07, 12, 13 | Yes |
| section-09-chat-authoring-frontend | 02 | 13 | No |
| section-10-substack-prep-ui | 03 | 13 | No |
| section-11-blog-publish-ui | 04, 05 | 13 | No |
| section-12-blog-dashboard | 07, 08 | 13 | No |
| section-13-pipeline-integration | 07, 08, 09, 10, 11, 12 | - | No (final) |

## Execution Order

1. **Batch 1**: section-01-data-model (no dependencies — foundation)
2. **Batch 2**: section-02-chat-authoring-backend, section-03-substack-prep-service, section-04-blog-html-generator, section-06-rss-detection, section-08-notification-system (parallel after 01)
3. **Batch 3**: section-05-github-publish-service, section-07-staggered-scheduling (after 04+06+08)
4. **Batch 4**: section-09-chat-authoring-frontend, section-10-substack-prep-ui, section-11-blog-publish-ui, section-12-blog-dashboard (parallel, frontend after respective backends)
5. **Batch 5**: section-13-pipeline-integration (final — wires everything together)

## Section Summaries

### section-01-data-model
EF Core migration: new entities (ChatConversation, SubstackDetection, UserNotification, BlogPublishRequest), new Content columns (SubstackPostUrl, BlogPostUrl, BlogDeployCommitSha, BlogDelayOverride, BlogSkipped), indexes (unique RssGuid, unique SubstackUrl, filtered notification idempotency, BlogPost dashboard query). Configuration options classes.

### section-02-chat-authoring-backend
IBlogChatService implementation: Claude API proxy with blog-writer system prompt, conversation windowing (summary + last N messages), SSE streaming, persistence (save after stream completes), structured finalization with JSON schema validation and retry. API endpoints: POST chat, GET history, POST finalize.

### section-03-substack-prep-service
ISubstackPrepService: transforms finalized content into Substack-optimized fields (title, subtitle, body markdown, SEO description, tags, section, preview text). Formatting rules for Substack-safe markdown. API endpoints: GET substack-prep, POST substack-published (manual mark with idempotency).

### section-04-blog-html-generator
IBlogHtmlGenerator: renders content into matthewkruczek.ai HTML template. Markdown-to-HTML via Markdig (raw HTML disabled). Canonical URL injection (placeholder initially, real URL on regeneration). Slug generation with content ID hash suffix for uniqueness. Open Graph tags, meta description.

### section-05-github-publish-service
IGitHubPublishService: commits blog HTML via GitHub Contents API. Create vs update handling (fetch sha for existing files). Fine-grained PAT auth with header redaction. Commit message format, author identity. Deploy verification with exponential backoff (30s→60s→120s→240s). API endpoints: GET blog-prep, POST blog-publish, GET blog-status.

### section-06-rss-detection
Enhanced SubstackService: 15-minute BackgroundService poller with conditional GET (ETag, If-Modified-Since). Sliding window (14-day re-scan, dedupe on guid). SHA-256 content hashing for edit detection. ISubstackContentMatcher: exact title match (High), fuzzy match within 48h (Medium), no match logging. SubstackDetection record creation, domain event firing.

### section-07-staggered-scheduling
PublishDelayRule system: global default (7 days) with per-post override. BlogDelayOverride (null=default) + BlogSkipped (explicit skip). Scheduling flow: Substack confirmed → compute blog ScheduledAt on ContentPlatformStatus → wait for user confirmation. Platform ordering enforcement (block blog publish before Substack live). Modified ScheduledPublishProcessor: notification instead of auto-publish for PersonalBlog.

### section-08-notification-system
INotificationService: UserNotification entity with types (SubstackDetected, BlogReady). Idempotent creation (unique constraint on ContentId+Type for Pending). Status transitions (Pending→Acknowledged→Acted). API endpoints: GET notifications, POST acknowledge, POST act.

### section-09-chat-authoring-frontend
Angular blog-chat.component.ts: message list with sanitized markdown rendering (DOMPurify), input field, SSE stream consumption, typing indicator, "Finalize Draft" button. NgRx signal store for chat state. Service for chat API communication.

### section-10-substack-prep-ui
Angular substack-prep.component.ts: per-field display with copy-to-clipboard buttons, copy confirmation indicators, "Mark as Published" button with optional URL input. Status badge (Draft/Ready/Published). Integrated into content detail view as a tab.

### section-11-blog-publish-ui
Angular blog-prep.component.ts: HTML preview panel, "Publish to Blog" button (disabled until SubstackPostUrl present), deploy status display (commit SHA, URL, verification state), retry button on failure. Integrated into content detail view as a tab.

### section-12-blog-dashboard
Angular blog-dashboard page: table/card list of all blog posts with Substack + Blog status badges. Timeline visualization per post. Stats header. Filtering by status and date range. Actions: schedule, override delay, skip blog, publish. NgRx signal store + HTTP service. Route registration.

### section-13-pipeline-integration
Wire everything together: modified pipeline wizard (BlogPost routes to chat step instead of outline+draft). Content detail view tabs (Substack Prep, Blog Prep). Updated workflow panel with blog-specific states. Navigation to blog dashboard. End-to-end integration tests.
