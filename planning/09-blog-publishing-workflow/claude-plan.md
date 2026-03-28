# Implementation Plan: Blog Publishing Workflow

## 1. Overview

This plan adds a two-stage blog publishing pipeline to PBA: author blog posts via an embedded Claude chat interface, prepare dual-format output (Substack + personal blog), publish to Substack manually, detect the publication via RSS, then deploy to matthewkruczek.ai after a configurable delay.

The system extends PBA's existing content pipeline, publishing infrastructure, and background services rather than building parallel systems.

### End-to-End Flow

1. User creates a blog post in PBA via a chat interface powered by Claude (blog-writer skill logic)
2. On finalization, PBA generates both a Substack-formatted version (all fields ready to copy) and blog HTML (matthewkruczek.ai template with a placeholder canonical URL — the real Substack URL is injected once detected)
3. User copies Substack fields into Substack's editor and publishes manually
4. PBA's RSS poller detects the new Substack publication (within ~15 minutes)
5. PBA notifies the user; user confirms to schedule the blog deploy for Substack publish date + 7 days (configurable)
6. On the scheduled date, PBA stages the blog HTML; user clicks "Publish" to trigger a git commit to the matthewkruczek-ai repo, which auto-deploys via GitHub Pages
7. A blog publishing dashboard provides at-a-glance status for all posts across both platforms

---

## 2. Chat-Based Content Authoring

### Why a Chat Interface

The user's established workflow is to invoke the `matt-kruczek-blog-writer` skill in Claude Code, iterate through conversation, and refine until satisfied. PBA needs to replicate this conversational authoring experience within its own UI so the full lifecycle (write, prepare, publish, track) stays in one place.

### Architecture

The chat interface is an Angular component embedded in the content creation flow. It communicates with a backend endpoint that proxies to the Claude API with the blog-writer skill's system prompt and voice guidelines baked in.

**Backend: Chat Proxy Service**

A new `IBlogChatService` handles the Claude API interaction:
- Accepts user messages and content context (topic, outline, previous draft iterations)
- Prepends the blog-writer system prompt (voice rules, humanizer principles, no em-dashes)
- Streams Claude's response back to the frontend via Server-Sent Events (SSE)
- Persists conversation history per content item for reference and resume

The system prompt should incorporate the core logic from the `matt-kruczek-blog-writer` skill: Matt's authentic voice, enterprise AI thought leadership angle, humanizer rules, and content patterns. This is not invoking the CLI skill directly — it's using the same writing principles via the Claude API.

```csharp
public interface IBlogChatService
{
    IAsyncEnumerable<string> SendMessageAsync(Guid contentId, string userMessage, CancellationToken ct);
    Task<ChatConversation> GetConversationAsync(Guid contentId, CancellationToken ct);
    Task<string> ExtractFinalDraftAsync(Guid contentId, CancellationToken ct);
}
```

**Conversation Persistence**

Store chat history in a `ChatConversation` entity linked to the content item:

```csharp
public record ChatMessage(string Role, string Content, DateTimeOffset Timestamp);

public class ChatConversation
{
    public Guid Id { get; init; }
    public Guid ContentId { get; init; }
    public List<ChatMessage> Messages { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastMessageAt { get; set; }
}
```

Messages are stored as a JSON column. To prevent unbounded token growth in long authoring sessions, the service implements **conversation windowing**: it keeps the last N messages in full, plus a periodically-updated summary of earlier messages. Both raw and summarized forms are persisted. When sending to Claude, the service constructs: `[system prompt] + [conversation summary] + [last N messages] + [new user message]`.

**SSE persistence**: Assistant messages are persisted only after the stream completes successfully. During streaming, a temporary "in-progress" marker prevents duplicate sends. If the stream is interrupted, the partial response is discarded and the user can retry.

**Finalization**: The "Finalize" action sends Claude a structured extraction prompt requesting a strict JSON output contract: `{ title, subtitle, body_markdown, seo_description, tags[] }`. The response is validated against this schema before saving to the content entity. On validation failure, the service retries with a corrective prompt (up to 2 retries).

**Frontend: Chat Component**

An Angular standalone component (`blog-chat.component.ts`) with:
- Message list (user + assistant messages, rendered with markdown support via a sanitized renderer — use DOMPurify or Angular's built-in sanitizer to prevent stored XSS from markdown rendering)
- Input field with send button
- Streaming response display (SSE consumption via `EventSource` or Angular's `HttpClient` with `observe: 'events'`)
- "Finalize Draft" button that extracts the final version and transitions to the next pipeline stage
- Loading/typing indicator during Claude response

The chat component is embedded within the content pipeline wizard as a new step between "Topic" and "Review." For blog posts specifically, it replaces the outline+draft generation steps with the conversational flow.

### API Endpoints

```
POST /api/content/{id}/chat          → Send message, returns SSE stream
GET  /api/content/{id}/chat/history  → Get conversation history
POST /api/content/{id}/chat/finalize → Extract final draft, save to content Body
```

### Configuration

```json
"BlogChatOptions": {
    "Model": "claude-sonnet-4-6-20250514",
    "MaxTokens": 8192,
    "SystemPromptPath": "prompts/blog-writer-system.md"
}
```

The system prompt file contains the blog-writer voice guidelines, humanizer rules, and Matt Kruczek's content patterns. This file can be updated without redeploying.

---

## 3. Substack Publishing Adapter

### Why Not a Traditional ISocialPlatform Adapter

Substack has no public write API. The existing adapter pattern (`ISocialPlatform.PublishAsync()`) doesn't apply. Instead, PBA prepares all content fields for manual copy-paste and tracks publication status through RSS detection or manual confirmation.

### SubstackPrepService

A new service that transforms finalized content into Substack-optimized fields:

```csharp
public interface ISubstackPrepService
{
    Task<SubstackPreparedContent> PrepareAsync(Guid contentId, CancellationToken ct);
}

public record SubstackPreparedContent(
    string Title,
    string Subtitle,
    string Body,           // Substack-optimized markdown
    string SeoDescription,
    string[] Tags,
    string SectionName,
    string PreviewText,
    string CanonicalUrl    // Will be null until blog is published
);
```

**Formatting rules:**
- Body: Clean markdown optimized for Substack's editor (no raw HTML, proper heading hierarchy, image references as markdown)
- Subtitle: Extracted from the first paragraph or generated as a summary
- Preview text: First 200 chars of body, trimmed at word boundary
- Tags: Extracted from content metadata tags
- Section name: Derived from content series/category if applicable

### UI: Substack Prep View

A component within the content detail page showing each Substack field with:
- Field label and formatted content preview
- "Copy" button per field (copies to clipboard)
- Visual indicator when a field has been copied (checkmark)
- "Mark as Published" button for manual confirmation
- Status badge: Draft | Ready to Copy | Published

### Manual vs. RSS-Based Publication Tracking

Two paths to confirm Substack publication:
1. **Manual**: User clicks "Mark as Published to Substack" in PBA after pasting and publishing (with optional Substack URL)
2. **Automatic**: RSS poller detects the new post (see Section 5)

Both paths trigger the same downstream action: create notification for blog scheduling confirmation.

**Race condition handling**: If the user manually marks as published (providing the Substack URL), that takes precedence. A unique index on `SubstackPostUrl` prevents duplicate detection records. If RSS later finds the same post, it recognizes the content is already linked and skips. Conversely, if RSS detects first and the user later manually confirms, the manual action is a no-op (content already has `SubstackPostUrl`). Both paths use upsert semantics.

### API Endpoints

```
GET  /api/content/{id}/substack-prep       → Get prepared Substack fields
POST /api/content/{id}/substack-published   → Manually mark as published (with optional Substack URL)
```

---

## 4. PersonalBlog Publishing Adapter

### Blog HTML Generation

A `IBlogHtmlGenerator` service renders content into matthewkruczek.ai's HTML blog post format:

```csharp
public interface IBlogHtmlGenerator
{
    Task<BlogHtmlResult> GenerateAsync(Guid contentId, CancellationToken ct);
}

public record BlogHtmlResult(
    string Html,
    string FilePath,       // e.g., "blog/2026-03-27-agent-first-part-5.html"
    string CanonicalUrl    // Substack URL for SEO
);
```

The generator:
- Loads the blog post HTML template from configuration (or a template file in PBA)
- Converts the content body from markdown to HTML
- Injects title, date, author, meta description, Open Graph tags, canonical URL
- Follows matthewkruczek.ai's existing blog post structure and CSS classes
- File path follows the pattern: `blog/YYYY-MM-DD-slug.html`

The canonical URL points to the Substack version (since it publishes first), which is good SEO practice for duplicate content. **Important**: at initial generation time, the Substack URL may not be known yet. The generator creates the HTML with a placeholder canonical URL. When the Substack URL is confirmed (via RSS detection or manual entry), the blog HTML is **regenerated** with the real URL. The "Publish to Blog" button is disabled until `SubstackPostUrl` is present, ensuring the canonical URL is always correct at deploy time.

**File path uniqueness**: The slug is derived from the title, lowercased, with special characters removed. To prevent collisions on same-day posts with similar titles, a short hash suffix (first 6 chars of the content ID) is appended: `blog/2026-03-27-agent-first-part-5-a1b2c3.html`.

### GitHub Publish Service

A `IGitHubPublishService` commits the generated HTML to the matthewkruczek-ai repo:

```csharp
public interface IGitHubPublishService
{
    Task<GitCommitResult> CommitBlogPostAsync(BlogPublishRequest request, CancellationToken ct);
    Task<bool> VerifyDeploymentAsync(string blogPostUrl, CancellationToken ct);
}

public record GitCommitResult(string CommitSha, string CommitUrl, bool Success, string? Error);
```

**Implementation approach:**
- Use GitHub REST API (Contents API) to create/update files without cloning the repo
- `PUT /repos/{owner}/{repo}/contents/{path}` with base64-encoded content
- Commit message: `content: add blog post "Title Here"`
- Committer: `Personal Brand Assistant <pba@matthewkruczek.ai>`
- Always commits to `main` branch (content is already reviewed in PBA's pipeline)
- Store the commit SHA in PBA for tracking

**Post-deploy verification:**
- After commit, GitHub Pages rebuilds and deploys automatically
- PBA uses exponential backoff for verification: first check at 30 seconds, then 60s, 120s, 240s (max ~8 minutes total wait)
- Each check does an HTTP GET to the expected blog URL
- Verifies 200 status code (note: CDN caches may return stale 404s, so retry is important)
- Updates content status to Published (on 200) or Failed (after all retries exhausted)
- On failure, user sees a "Retry Verification" button in the UI

**GitHub Contents API handling**: The `PUT /repos/{owner}/{repo}/contents/{path}` endpoint requires the current file `sha` when updating an existing file. The service should first check if the file exists (GET the path), and if so, include the `sha` in the PUT. For new posts this is a create; for corrections/re-deploys it's an update.

### Semi-Automated Flow

The blog publish is **not fully automatic** — PBA prepares everything, but the user clicks "Publish" to trigger:
1. PBA shows the generated blog HTML preview
2. User reviews and clicks "Publish to Blog"
3. PBA commits to GitHub, waits for deploy, verifies
4. Status updates to Published with commit SHA and blog URL

### Configuration

```json
"BlogPublishOptions": {
    "RepoOwner": "MCKRUZ",
    "RepoName": "matthewkruczek-ai",
    "Branch": "main",
    "ContentPath": "blog/",
    "FilePattern": "YYYY-MM-DD-{slug}-{hash}.html",
    "TemplatePath": "templates/blog-post.html",
    "AuthorName": "Personal Brand Assistant",
    "AuthorEmail": "pba@matthewkruczek.ai",
    "DeployVerificationUrlPattern": "https://matthewkruczek.ai/blog/{filename}",
    "DeployVerificationInitialDelaySeconds": 30,
    "DeployVerificationMaxRetries": 4
}
```

### API Endpoints

```
GET  /api/content/{id}/blog-prep     → Get blog HTML preview
POST /api/content/{id}/blog-publish  → Trigger git commit + deploy
GET  /api/content/{id}/blog-status   → Check deploy status (commit SHA, URL, verification)
```

---

## 5. RSS Publication Detection

### Enhanced SubstackService

The existing `SubstackService` reads RSS for analytics. Enhance it to also detect new publications and match them to PBA content items.

**Polling strategy:**
- 15-minute interval via `BackgroundService` (appropriate for weekly publishing cadence)
- HTTP conditional GET with `If-None-Match` (ETag) and `If-Modified-Since` headers
- If server returns 304 Not Modified, skip processing
- Fall back to content-based change detection if conditional headers aren't supported

**Feed processing:**
- Parse RSS 2.0 XML, extract `title`, `link`, `guid`, `pubDate`, `content:encoded`
- Deduplicate on `guid` (the post permalink URL) — use a unique index on `SubstackDetection.RssGuid`
- Use a **sliding window** approach: always re-scan items from the last 14 days and deduplicate, rather than relying on `pubDate > lastPoll` (which can miss backdated posts or fail after poll gaps)
- Store SHA-256 hash of `content:encoded` for edit detection on subsequent polls

### Content Matching

A `ISubstackContentMatcher` service links detected RSS entries to existing PBA content items:

```csharp
public interface ISubstackContentMatcher
{
    Task<ContentMatchResult> MatchAsync(SubstackRssEntry entry, CancellationToken ct);
}

public record ContentMatchResult(
    Guid? ContentId,
    MatchConfidence Confidence,  // High, Medium, Low, None
    string MatchReason
);
```

**Matching strategy (in priority order):**
1. **Exact title match** against content items with `ContentType = BlogPost` and `Status = Approved` or with Substack in `TargetPlatforms` → High confidence
2. **Fuzzy title match** (Levenshtein distance < 20% of title length) + `pubDate` within 48 hours of content creation → Medium confidence
3. **No match** → Log for manual review, surface in dashboard

High-confidence matches proceed automatically. Medium-confidence matches require user confirmation. Low/no matches are surfaced in the dashboard for manual linking.

### Detection Event Flow

When a match is found:
1. Update the content item: set `SubstackPublishedAt`, `SubstackPostUrl`
2. Create a `SubstackDetection` record for audit trail
3. Fire a domain event: `SubstackPublicationDetectedEvent`
4. Create a user notification: "Your Substack post '{title}' was detected. Schedule blog deploy?"

### Data Model

```csharp
public class SubstackDetection
{
    public Guid Id { get; init; }
    public Guid? ContentId { get; init; }
    public string RssGuid { get; init; }
    public string Title { get; init; }
    public string SubstackUrl { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
    public DateTimeOffset DetectedAt { get; init; }
    public MatchConfidence Confidence { get; init; }
    public string ContentHash { get; init; }
}
```

### Configuration

Extend existing `SubstackOptions`:

```json
"SubstackOptions": {
    "FeedUrl": "https://matthewkruczek.substack.com/feed",
    "PollingIntervalMinutes": 15,
    "MatchConfidenceThreshold": "Medium",
    "EnableConditionalGet": true
}
```

---

## 6. Staggered Publish Scheduling

### Publish Delay Rules

A configurable system for defining temporal relationships between platform publishes:

```csharp
public record PublishDelayRule(
    PlatformType SourcePlatform,
    PlatformType TargetPlatform,
    TimeSpan DefaultDelay,
    bool RequiresConfirmation
);
```

Default rule: `Substack -> PersonalBlog = 7 days, RequiresConfirmation = true`

The delay is stored as a global default in configuration, overridable per content item via two properties:
- `BlogDelayOverride` (`TimeSpan?`): null means "use global default"; a value overrides the delay for this post
- `BlogSkipped` (`bool`): explicitly marks a post to skip blog publishing entirely

These are kept separate to avoid semantic ambiguity — null TimeSpan always means "use default," never "skip."

### Scheduling Flow

When Substack publication is confirmed (RSS detection or manual):

1. Check if the content has blog in its target platforms
2. If `BlogSkipped` is true → skip blog version entirely
3. Calculate `blogScheduledAt = substackPublishedAt + (BlogDelayOverride ?? defaultDelay)`
4. If `RequiresConfirmation` is true → create notification, wait for user to confirm
5. On confirmation → transition content's blog platform status to Scheduled with `blogScheduledAt`
6. The existing `ScheduledPublishProcessor` (30-second poll) picks it up when due

### Platform Ordering Enforcement

The system must prevent publishing the blog version before Substack is live:
- `PublishingPipeline` checks: if content targets both Substack and PersonalBlog, and Substack status is not Published, block PersonalBlog publishing with a clear error
- The UI disables the "Publish to Blog" button until Substack is confirmed published
- This is enforced at both the API level (validation) and UI level (disabled state)

### Enhanced Content Entity

Add fields to `Content` to track cross-platform publishing state:

```csharp
// New properties on Content entity
// Metadata fields (no generic equivalent — blog-workflow-specific)
public string? SubstackPostUrl { get; set; }
public string? BlogPostUrl { get; set; }
public string? BlogDeployCommitSha { get; set; }
public TimeSpan? BlogDelayOverride { get; set; }  // null = use default
public bool BlogSkipped { get; set; }
```

**Source of truth**: The existing `ContentPlatformStatus` table remains the authoritative source for all platform publish states (including `PublishedAt`, `ScheduledAt`, status transitions). The fields above store only **metadata with no generic equivalent** — URLs, commit SHAs, delay config. Never store publish timestamps or status on the Content entity when `ContentPlatformStatus` already tracks them. This prevents dual-source-of-truth bugs.

For scheduling, `BlogScheduledAt` is stored on the `ContentPlatformStatus` record for the PersonalBlog platform via its existing `ScheduledAt` field — not as a separate Content column.

### Configuration

```json
"PublishDelayOptions": {
    "DefaultSubstackToBlogDelay": "7.00:00:00",
    "RequiresConfirmation": true
}
```

---

## 7. Notification System

### Why Notifications

The workflow has two human-in-the-loop points that need notifications:
1. Substack publication detected → prompt user to confirm blog scheduling
2. Blog publish date reached → prompt user to trigger deployment

### Implementation

PBA likely already has some form of notification or event broadcasting (the `IPipelineEventBroadcaster` exists). The notification system for blog publishing should:

- Store notifications as entities with type, message, content reference, status (Pending/Acknowledged/Acted)
- Surface in the Angular UI via a notification indicator (badge, panel, or toast)
- Include action buttons inline (e.g., "Schedule Blog" directly from the notification)

```csharp
public class UserNotification
{
    public Guid Id { get; init; }
    public string Type { get; init; }        // "SubstackDetected", "BlogReady"
    public string Message { get; init; }
    public Guid? ContentId { get; init; }
    public NotificationStatus Status { get; set; }  // Pending, Acknowledged, Acted
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
}
```

If PBA already has a notification system, integrate with it rather than building a new one. If not, this is a lightweight addition scoped to the blog workflow.

**Idempotency**: Enforce a unique constraint on `(ContentId, Type)` for pending notifications. This prevents the `ScheduledPublishProcessor` from creating duplicate "Blog ready" notifications on every poll tick. Notification creation should be an upsert: if a pending notification already exists for the same content + type, skip.

### API Endpoints

```
GET  /api/notifications                  → Get pending notifications
POST /api/notifications/{id}/acknowledge → Acknowledge
POST /api/notifications/{id}/act         → Take action (e.g., confirm blog schedule)
```

---

## 8. Blog Publishing Dashboard (Angular)

### Purpose

A dedicated view showing all blog posts across the two-stage publishing pipeline. This is in addition to the pipeline integration — the dashboard provides the "at a glance" view while the pipeline integration provides the "in the flow" experience.

### Layout

**Header**: "Blog Publishing" title with stats (X posts in pipeline, Y scheduled, Z published)

**Main content**: Table/card list of blog posts with columns:
- Title (linked to content detail)
- Substack Status: badge showing Draft | Ready | Published (with date)
- Blog Status: badge showing Waiting | Scheduled (with date) | Ready | Published (with URL) | Skipped
- Delay: "7 days" or custom override
- Actions: Schedule / Override Delay / Skip Blog / Publish

**Filtering**: By status (all, pending Substack, scheduled blog, published), date range

**Timeline visualization**: For each post, a horizontal timeline showing:
- Substack publish date (dot, green if published)
- Delay period (line connecting dots)
- Blog publish date (dot, green if published, yellow if scheduled, gray if pending)

### Component Structure

```
blog-publishing/
  blog-dashboard.component.ts          # Main dashboard page
  blog-pipeline-card.component.ts      # Individual post card with timeline
  blog-timeline.component.ts           # Visual timeline for a single post
  blog-dashboard.store.ts              # NgRx signal store for dashboard state
  blog-dashboard.service.ts            # HTTP service for dashboard API
```

### API Endpoint

```
GET /api/blog-pipeline?status=all&from=2026-01-01&to=2026-12-31
```

Returns all blog-type content with their Substack and blog platform statuses, scheduled dates, and delay configurations. This is a read-optimized query joining content with platform status and detection records.

---

## 9. Content Pipeline Integration

### Modified Pipeline Wizard

The existing content pipeline wizard (Topic -> Outline -> Draft -> Review) needs modification for blog posts:

**For `ContentType.BlogPost`:**
1. **Topic** — same as today
2. **Chat Authoring** — new step replacing Outline + Draft; embedded chat interface with Claude
3. **Finalize** — extract final draft, generate both format versions
4. **Publish Prep** — new step showing Substack fields (copy buttons) + blog HTML preview
5. **Track** — cross-platform status tracking (Substack published? Blog scheduled? Blog deployed?)

The wizard should detect `ContentType.BlogPost` and route to the blog-specific steps. Other content types continue using the existing Outline -> Draft flow.

### Content Detail View Enhancement

The existing content detail view gets a new section for blog posts:
- **Substack Prep** tab: formatted fields with copy buttons
- **Blog Prep** tab: HTML preview with "Publish to Blog" button
- **Publishing Status** panel: dual-platform status with timeline

### Workflow Panel Update

The `content-workflow-panel.component.ts` should show blog-specific states when viewing a blog post:
- "Authoring" (chat in progress)
- "Ready for Substack" (finalized, Substack fields prepared)
- "Awaiting Substack Publication" (user needs to paste and publish)
- "Substack Live" (RSS detected or manually confirmed)
- "Blog Scheduled" (waiting for deploy date)
- "Blog Ready" (deploy date reached, awaiting user trigger)
- "Published" (both platforms live)

---

## 10. Data Model Changes Summary

### New Entities

| Entity | Purpose |
|--------|---------|
| `ChatConversation` | Stores chat history for blog authoring sessions |
| `SubstackDetection` | Audit log of RSS detections with matching metadata |
| `UserNotification` | Notification queue for human-in-the-loop actions |
| `BlogPublishRequest` | Staged blog publish with HTML, target path, status |

### Modified Entities

| Entity | Changes |
|--------|---------|
| `Content` | Add `SubstackPostUrl`, `BlogPostUrl`, `BlogDeployCommitSha`, `BlogDelayOverride` (TimeSpan?), `BlogSkipped` (bool). Publish timestamps and scheduling use existing `ContentPlatformStatus` — no duplicate timestamp columns. |

### New Enums

| Enum | Values |
|------|--------|
| `MatchConfidence` | High, Medium, Low, None |
| `NotificationStatus` | Pending, Acknowledged, Acted |
| `BlogPublishStatus` | Staged, Publishing, Published, Failed |

### EF Core Migration

A single migration adds the new entities and content columns. The `ChatConversation.Messages` property uses a JSON column (`ToJson()` in EF Core). Key indexes:
- `SubstackDetection.RssGuid`: unique index for dedup
- `SubstackDetection.SubstackUrl`: unique index to prevent duplicate detection via RSS + manual
- `UserNotification(ContentId, Type)`: unique filtered index where `Status = Pending` for notification idempotency
- `Content(ContentType, Status)`: filtered index on `BlogPost` for dashboard query performance

---

## 11. Background Services

### New: SubstackPublicationPoller

A `BackgroundService` that polls the Substack RSS feed every 15 minutes:
- Uses the enhanced `SubstackService` with conditional GET
- Calls `ISubstackContentMatcher` for each new entry
- Creates notifications for confirmed matches
- Logs unmatched entries for dashboard review

This replaces or extends the existing Substack analytics polling. The analytics functionality continues to work, but the poller now also performs publication detection.

### Modified: ScheduledPublishProcessor

The existing 30-second poller already handles scheduled content publishing. For blog posts, it needs to:
- Recognize that PersonalBlog content requires the semi-auto flow (don't auto-publish)
- Instead of calling `PublishingPipeline.PublishAsync()`, create a notification: "Blog post ready for deployment"
- The actual publish happens when the user clicks "Publish" in the UI

This is a small modification: check if the platform is `PersonalBlog` and if so, create a notification instead of auto-publishing.

### New: BlogDeployVerifier

A lightweight service that, after a blog publish is triggered:
- Uses exponential backoff: 30s → 60s → 120s → 240s (4 attempts over ~8 minutes)
- Each attempt HTTP GETs the expected blog URL
- Updates status to Published (on 200) or continues retrying
- After all retries exhausted: marks as Failed, user can manually retry via UI button
- Accounts for CDN/cache delays that may serve stale 404s during GitHub Pages rebuild

---

## 12. Service Registration & Dependencies

### New Service Interfaces

| Interface | Implementation | Registration |
|-----------|---------------|--------------|
| `IBlogChatService` | `BlogChatService` | Scoped |
| `ISubstackPrepService` | `SubstackPrepService` | Scoped |
| `IBlogHtmlGenerator` | `BlogHtmlGenerator` | Scoped |
| `IGitHubPublishService` | `GitHubPublishService` | Scoped |
| `ISubstackContentMatcher` | `SubstackContentMatcher` | Scoped |
| `INotificationService` | `NotificationService` | Scoped |

### External Dependencies

| Dependency | Purpose |
|------------|---------|
| Anthropic SDK | Claude API for chat authoring |
| Markdig (or similar) | Markdown to HTML conversion for blog generation. Configure with raw HTML disabled to prevent XSS in generated blog posts. |
| GitHub REST API | Blog post commits (via existing `HttpClient`) |

The Anthropic SDK may already be in use for the sidecar AI integration. The blog chat service should use the same SDK configuration and API key management.

---

## 13. Security Considerations

- **Claude API key**: Uses existing PBA API key management (User Secrets dev, Azure Key Vault prod)
- **GitHub Personal Access Token**: Needed for committing to matthewkruczek-ai repo. Store in User Secrets / Key Vault. **Use a fine-grained PAT** scoped to the single `matthewkruczek-ai` repo with Contents read/write only — never use a classic PAT with broad `repo` scope. Ensure HTTP client logging redacts authorization headers.
- **Chat content**: Conversation history may contain draft content. No special sensitivity, but stored in the same encrypted database as other content
- **Rate limiting**: Claude API has rate limits; add retry with backoff on 429. GitHub API: 5000 req/hr authenticated, not a concern for blog publishing frequency
- **Input validation**: User messages to chat endpoint must be validated (max length, no injection). Claude API handles sanitization, but validate at the boundary.

---

## 14. Testing Strategy

### Unit Tests

- `SubstackPrepService`: Verify field formatting, subtitle extraction, preview text truncation
- `SubstackContentMatcher`: Test exact match, fuzzy match, no match, confidence scoring
- `BlogHtmlGenerator`: Verify template rendering, canonical URL injection, slug generation
- `PublishDelayRule`: Verify delay calculation, null override (skip), custom override
- Content entity: New state transitions, new property validations

### Integration Tests

- `BlogChatService`: Mock Claude API, verify conversation persistence, finalization extraction
- `GitHubPublishService`: Mock GitHub API, verify commit creation, error handling
- `SubstackPublicationPoller`: Mock RSS feed responses, verify detection and matching flow
- Blog pipeline endpoints: Full HTTP tests via `WebApplicationFactory`

### E2E Considerations

- Content creation chat flow: create blog post -> send messages -> finalize -> verify both format versions generated
- Substack detection: simulate RSS feed with new entry -> verify notification created
- Blog publish flow: trigger publish -> verify GitHub API called -> verify status update

### Test Patterns

Follow existing patterns: xUnit + Moq, `CustomWebApplicationFactory` with in-memory SQLite, AAA pattern. The `CustomWebApplicationFactory` should register mock `HttpMessageHandler` for Claude API and GitHub API calls. Add `SubstackPublicationPoller` to the list of background services removed during tests.
