# TDD Plan: Blog Publishing Workflow

Testing framework: xUnit + Moq, `CustomWebApplicationFactory` with in-memory SQLite, AAA pattern. Follow existing project conventions.

---

## 2. Chat-Based Content Authoring

### BlogChatService Tests

```csharp
// Test: SendMessageAsync prepends system prompt to first message
// Test: SendMessageAsync includes conversation summary + last N messages (not full history)
// Test: SendMessageAsync persists assistant message only after stream completes
// Test: SendMessageAsync discards partial response on stream interruption
// Test: SendMessageAsync creates new ChatConversation on first message for content
// Test: SendMessageAsync appends to existing ChatConversation on subsequent messages
// Test: GetConversationAsync returns null for content with no conversation
// Test: GetConversationAsync returns full conversation with all messages
// Test: ExtractFinalDraftAsync sends structured JSON extraction prompt
// Test: ExtractFinalDraftAsync validates response against schema (title, subtitle, body_markdown, seo_description, tags)
// Test: ExtractFinalDraftAsync retries with corrective prompt on validation failure
// Test: ExtractFinalDraftAsync fails after max retries (2) with invalid responses
// Test: ExtractFinalDraftAsync saves validated content to Content.Body and metadata
```

### Chat Conversation Windowing Tests

```csharp
// Test: Windowing keeps last N messages in full when conversation exceeds threshold
// Test: Windowing generates summary of older messages
// Test: Windowing persists both raw and summarized forms
// Test: Claude request includes [system] + [summary] + [recent messages] + [new message]
```

### Chat API Endpoint Tests

```csharp
// Test: POST /api/content/{id}/chat returns SSE stream with correct content-type
// Test: POST /api/content/{id}/chat returns 404 for non-existent content
// Test: POST /api/content/{id}/chat validates message max length
// Test: GET /api/content/{id}/chat/history returns conversation messages
// Test: GET /api/content/{id}/chat/history returns empty array for no conversation
// Test: POST /api/content/{id}/chat/finalize saves draft and returns finalized content
// Test: POST /api/content/{id}/chat/finalize returns 400 if no conversation exists
```

---

## 3. Substack Publishing Adapter

### SubstackPrepService Tests

```csharp
// Test: PrepareAsync generates all Substack fields from finalized content
// Test: PrepareAsync extracts subtitle from first paragraph
// Test: PrepareAsync truncates preview text at 200 chars on word boundary
// Test: PrepareAsync produces clean markdown (no raw HTML in body)
// Test: PrepareAsync extracts tags from content metadata
// Test: PrepareAsync derives section name from content series/category
// Test: PrepareAsync returns null canonical URL when blog not yet published
// Test: PrepareAsync returns 404 for non-existent content
// Test: PrepareAsync handles content with no metadata gracefully
```

### Substack Publish Tracking Tests

```csharp
// Test: POST /api/content/{id}/substack-published sets SubstackPostUrl on content
// Test: POST /api/content/{id}/substack-published creates SubstackDetection record
// Test: POST /api/content/{id}/substack-published triggers notification for blog scheduling
// Test: POST /api/content/{id}/substack-published is idempotent (no-op if already published)
// Test: POST /api/content/{id}/substack-published with URL takes precedence over RSS detection
// Test: GET /api/content/{id}/substack-prep returns formatted fields with correct structure
```

---

## 4. PersonalBlog Publishing Adapter

### BlogHtmlGenerator Tests

```csharp
// Test: GenerateAsync renders content into HTML template
// Test: GenerateAsync injects title, date, author, meta description
// Test: GenerateAsync injects Open Graph tags
// Test: GenerateAsync sets canonical URL to Substack URL when available
// Test: GenerateAsync uses placeholder canonical URL when Substack URL not yet known
// Test: GenerateAsync converts markdown body to HTML with raw HTML disabled (XSS prevention)
// Test: GenerateAsync produces correct file path: blog/YYYY-MM-DD-slug-hash.html
// Test: GenerateAsync appends content ID hash suffix for uniqueness
// Test: GenerateAsync handles special characters in title for slug generation
// Test: GenerateAsync regenerates with updated canonical URL when called again after Substack URL set
```

### GitHubPublishService Tests

```csharp
// Test: CommitBlogPostAsync creates file via GitHub Contents API with correct path and base64 content
// Test: CommitBlogPostAsync uses correct commit message format
// Test: CommitBlogPostAsync uses configured author name and email
// Test: CommitBlogPostAsync commits to configured branch
// Test: CommitBlogPostAsync stores commit SHA in result
// Test: CommitBlogPostAsync handles file already exists (fetches sha, sends update)
// Test: CommitBlogPostAsync returns error on GitHub API failure (401, 403, 422)
// Test: CommitBlogPostAsync redacts authorization header in logs
// Test: VerifyDeploymentAsync returns true on HTTP 200
// Test: VerifyDeploymentAsync retries with exponential backoff (30s, 60s, 120s, 240s)
// Test: VerifyDeploymentAsync returns false after all retries exhausted
```

### Blog Publish Flow Tests

```csharp
// Test: POST /api/content/{id}/blog-publish is blocked when SubstackPostUrl is null
// Test: POST /api/content/{id}/blog-publish regenerates HTML with real canonical URL before committing
// Test: POST /api/content/{id}/blog-publish commits to GitHub and starts verification
// Test: POST /api/content/{id}/blog-publish updates ContentPlatformStatus to Published on success
// Test: POST /api/content/{id}/blog-publish updates ContentPlatformStatus to Failed on verification failure
// Test: POST /api/content/{id}/blog-publish stores BlogDeployCommitSha and BlogPostUrl
// Test: GET /api/content/{id}/blog-prep returns HTML preview
// Test: GET /api/content/{id}/blog-status returns current deploy status
```

---

## 5. RSS Publication Detection

### SubstackContentMatcher Tests

```csharp
// Test: MatchAsync returns High confidence on exact title match with BlogPost content
// Test: MatchAsync returns Medium confidence on fuzzy title match within 48h window
// Test: MatchAsync returns None when no matching content found
// Test: MatchAsync only matches content with ContentType.BlogPost
// Test: MatchAsync only matches content with Substack in TargetPlatforms or Status = Approved
// Test: MatchAsync skips content that already has SubstackPostUrl set
// Test: MatchAsync handles empty title gracefully
// Test: Fuzzy match: "Agent-First Enterprise Part 5" matches "Agent-First Enterprise: Part 5"
// Test: Fuzzy match: "Weekly Notes #12" does NOT match "Weekly Notes #13" (too different)
```

### SubstackPublicationPoller Tests

```csharp
// Test: Poller parses RSS 2.0 XML correctly (title, link, guid, pubDate, content:encoded)
// Test: Poller deduplicates on guid (skips already-seen entries)
// Test: Poller uses sliding window (processes items from last 14 days, not just since last poll)
// Test: Poller sends conditional GET headers (If-None-Match, If-Modified-Since)
// Test: Poller skips processing on 304 Not Modified
// Test: Poller falls back to full processing when conditional headers not supported
// Test: Poller stores SHA-256 content hash for edit detection
// Test: Poller detects content edits via changed hash on same guid
// Test: Poller creates SubstackDetection record on new match
// Test: Poller creates UserNotification on high-confidence match
// Test: Poller logs unmatched entries without creating notification
// Test: Poller handles malformed RSS XML gracefully
// Test: Poller handles network errors without crashing the background service
```

---

## 6. Staggered Publish Scheduling

### PublishDelayRule Tests

```csharp
// Test: Calculate blog scheduled date = Substack published + default delay (7 days)
// Test: Calculate blog scheduled date with custom BlogDelayOverride
// Test: BlogDelayOverride null uses global default delay
// Test: BlogSkipped = true skips blog scheduling entirely
// Test: BlogSkipped and BlogDelayOverride are independent (override set but skipped = no schedule)
```

### Scheduling Flow Tests

```csharp
// Test: Substack publication triggers blog scheduling notification when RequiresConfirmation = true
// Test: User confirmation creates ContentPlatformStatus for PersonalBlog with ScheduledAt
// Test: Platform ordering: PersonalBlog publish blocked when Substack not yet Published
// Test: Platform ordering: PersonalBlog publish allowed when Substack is Published
// Test: ScheduledPublishProcessor creates notification (not auto-publish) for PersonalBlog platform
// Test: ScheduledPublishProcessor notification is idempotent (no duplicates on repeated polls)
```

### Blog Pipeline Dashboard API Tests

```csharp
// Test: GET /api/blog-pipeline returns all BlogPost content with platform statuses
// Test: GET /api/blog-pipeline filters by status correctly
// Test: GET /api/blog-pipeline filters by date range
// Test: POST /api/blog-pipeline/{id}/schedule sets ContentPlatformStatus.ScheduledAt
// Test: PUT /api/blog-pipeline/{id}/delay updates BlogDelayOverride
// Test: POST /api/blog-pipeline/{id}/skip-blog sets BlogSkipped = true
```

---

## 7. Notification System

### NotificationService Tests

```csharp
// Test: CreateNotification stores notification with Pending status
// Test: CreateNotification is idempotent (unique constraint on ContentId + Type for Pending)
// Test: CreateNotification returns existing notification if duplicate
// Test: AcknowledgeNotification sets status to Acknowledged with timestamp
// Test: ActOnNotification sets status to Acted
// Test: GetPendingNotifications returns only Pending status notifications
// Test: GetPendingNotifications filters by content ID when specified
```

### Notification API Endpoint Tests

```csharp
// Test: GET /api/notifications returns pending notifications
// Test: POST /api/notifications/{id}/acknowledge updates status
// Test: POST /api/notifications/{id}/act triggers appropriate action (e.g., schedule blog)
// Test: POST /api/notifications/{id}/act returns 404 for non-existent notification
```

---

## 8. Blog Publishing Dashboard (Angular)

### Component Tests

```typescript
// Test: blog-dashboard renders list of blog posts with correct status badges
// Test: blog-dashboard shows stats header (X in pipeline, Y scheduled, Z published)
// Test: blog-pipeline-card shows Substack status badge with correct color
// Test: blog-pipeline-card shows Blog status badge with correct color
// Test: blog-pipeline-card shows delay value (default or override)
// Test: blog-timeline renders horizontal timeline with correct dot positions
// Test: blog-timeline colors: green (published), yellow (scheduled), gray (pending)
// Test: blog-dashboard filters by status
// Test: blog-dashboard filters by date range
// Test: Actions: Schedule button calls correct API
// Test: Actions: Skip Blog button calls correct API and updates UI
// Test: Actions: Publish button disabled when Substack not published
```

---

## 9. Content Pipeline Integration

### Modified Pipeline Wizard Tests

```typescript
// Test: Pipeline wizard routes to chat step for ContentType.BlogPost
// Test: Pipeline wizard routes to outline+draft steps for other content types
// Test: Finalize step calls ExtractFinalDraft and generates both format versions
// Test: Publish Prep step shows Substack fields with copy buttons
// Test: Publish Prep step shows blog HTML preview
// Test: Copy button copies correct field to clipboard
// Test: Track step shows dual-platform status
// Test: "Publish to Blog" button disabled when SubstackPostUrl is null
```

### Workflow Panel Tests

```typescript
// Test: Workflow panel shows "Authoring" during active chat
// Test: Workflow panel shows "Ready for Substack" after finalization
// Test: Workflow panel shows "Awaiting Substack Publication" before detection
// Test: Workflow panel shows "Substack Live" after RSS detection
// Test: Workflow panel shows "Blog Scheduled" with date after scheduling
// Test: Workflow panel shows "Blog Ready" when scheduled date reached
// Test: Workflow panel shows "Published" when both platforms live
```

---

## 10. Data Model / Migration Tests

```csharp
// Test: Migration adds all new entities (ChatConversation, SubstackDetection, UserNotification, BlogPublishRequest)
// Test: Migration adds new Content columns (SubstackPostUrl, BlogPostUrl, BlogDeployCommitSha, BlogDelayOverride, BlogSkipped)
// Test: ChatConversation.Messages stores and retrieves JSON correctly
// Test: SubstackDetection.RssGuid unique index prevents duplicate inserts
// Test: SubstackDetection.SubstackUrl unique index prevents duplicate detection
// Test: UserNotification unique filtered index on (ContentId, Type) where Status = Pending
// Test: Content filtered index on (ContentType, Status) for BlogPost queries
```

---

## 11. Background Services

### SubstackPublicationPoller Integration Tests

```csharp
// Test: Poller runs on configured interval (15 minutes)
// Test: Poller processes feed and creates detections + notifications end-to-end
// Test: Poller handles RSS feed unavailable without crashing
// Test: Poller respects conditional GET (doesn't reprocess on 304)
```

### ScheduledPublishProcessor Modification Tests

```csharp
// Test: Processor creates notification instead of auto-publishing for PersonalBlog platform
// Test: Processor continues auto-publishing for non-PersonalBlog platforms (no regression)
// Test: Processor notification creation is idempotent
```

### BlogDeployVerifier Tests

```csharp
// Test: Verifier checks URL with exponential backoff timing (30s, 60s, 120s, 240s)
// Test: Verifier marks Published on first successful 200
// Test: Verifier marks Failed after all retries exhausted
// Test: Verifier handles network errors during verification
```
