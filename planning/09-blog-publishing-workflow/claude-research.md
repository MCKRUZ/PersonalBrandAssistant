# Research: Blog Publishing Workflow

## Part 1: Codebase Analysis

### Platform Adapters

**ISocialPlatform Interface**: Core interface with `PublishAsync()`, `DeletePostAsync()`, `GetEngagementAsync()`, `GetProfileAsync()`, `ValidateContentAsync()`, `CheckPublishStatusAsync()`. All return `Result<T>`.

**PlatformAdapterBase**: Base class handling OAuth token loading, rate limiting via `IRateLimiter`, media storage, and encryption. Abstract methods for platform-specific implementation.

**Implemented adapters**: Twitter/X, LinkedIn, Instagram, YouTube, Reddit. No Substack or PersonalBlog adapters exist.

### Publishing Pipeline

**PublishingPipeline** flow:
1. `PublishAsync(contentId)` fetches content, iterates `TargetPlatforms`
2. Per-platform: loads/creates `ContentPlatformStatus`, formats via `IPlatformContentFormatter`, checks rate limits, publishes via adapter
3. Idempotency: SHA256(`contentId:platform:version`) prevents duplicates
4. Error handling: per-platform failures tracked separately, partial failure supported
5. Status transitions: all succeed -> Published, all fail -> Failed, mixed -> stays Publishing

### Repurposing Service

- `Content.ParentContentId` tracks parent, `Content.TreeDepth` tracks recursion depth
- Configurable max depth via `ContentEngineOptions.MaxTreeDepth`
- Idempotency: checks for existing child with same source platform + type + target before creating
- Tree traversal via iterative BFS (one DB query per level)

### Substack Service (Read-Only)

- Config: `appsettings.json` with `FeedUrl: https://matthewkruczek.substack.com/feed`
- Interface: `ISubstackService.GetRecentPostsAsync(limit, cancellationToken)`
- RSS-based polling, extracts: title, body, publish date, URL
- Used for analytics only (`GET /analytics/substack`)

### Content State Machine

```
Draft -> Review -> Approved -> Scheduled -> Publishing -> Published
                                         -> Failed -> Draft (retry)
Any state -> Archived -> Draft
```

Transitions enforced by `Content.ValidTransitions` dictionary. `TransitionTo()` throws on invalid transition. Emits `ContentStateChangedEvent`.

### Content Entity Properties

Core: Id, ContentType, Title, Body, Status, Metadata, TargetPlatforms, ParentContentId, TreeDepth, ScheduledAt, PublishedAt, PublishingStartedAt, RetryCount, NextRetryAt, RepurposeSourcePlatform, Version, ImageFileId, ImageRequired.

ContentMetadata: AiGenerationContext, PlatformSpecificData (dict), TokensUsed.

### Content Pipeline (AI-Driven)

1. CreateFromTopic -> draft with AiGenerationContext metadata
2. GenerateOutline -> sidecar AI outline
3. GenerateDraft -> platform-specific system prompts (LinkedIn: professional, Twitter: punchy, etc.)
4. GeneratePlatformDraft -> repurpose parent content for specific platform
5. Review & feedback loop

### Scheduling Infrastructure

- `ContentCalendarService`: iCalendar RRULE-based recurring series, slot materialization on demand
- `ContentSeries`: recurring schedule with RRULE, timezone, platforms, content type, theme tags
- `CalendarSlot`: status (Open -> Filled -> Completed/Skipped)
- `IContentScheduler`: transitions content to Scheduled with `ScheduledAt` time

### Background Services

| Service | Function | Interval |
|---------|----------|----------|
| ScheduledPublishProcessor | Polls for due scheduled content | 30 seconds |
| RetryFailedProcessor | Retries failed publishes with backoff | Periodic |
| PublishCompletionPoller | Checks in-flight publish status | Periodic |
| RepurposeOnPublishProcessor | Auto-repurposes on ContentPublishedEvent | Event-driven |
| PlatformHealthMonitor | Verifies OAuth scopes per platform | Periodic |
| EngagementScheduler | Human-like engagement activities | Scheduled |
| CalendarSlotProcessor, DailyContentProcessor, WorkflowRehydrator, InboxPoller, TokenRefreshProcessor | Supporting services | Various |

### Testing Setup

- **Framework**: xUnit + Moq, `CustomWebApplicationFactory`, in-memory SQLite
- **Domain tests**: state machine transitions, factory methods, metadata
- **Application tests**: CRUD handlers, validators, platform interfaces
- **Infrastructure tests**: ContentPipeline, PublishingPipeline, ContentCalendarService, API endpoints
- **Test factory** removes background services during tests, uses test API key

---

## Part 2: Web Research

### Topic 1: Substack RSS Feed Polling

**Feed Structure**: RSS 2.0 with `content:encoded` extension. Fields: `title`, `description`, `link`, `guid` (permalink), `pubDate` (RFC 822), `content:encoded` (full HTML for free posts), `dc:creator`.

**Recommended polling interval**: 15 minutes for weekly-ish publishing frequency.

**Efficient polling**: Use HTTP conditional GET (`If-None-Match` + `If-Modified-Since`). Fall back to content-based change detection if headers absent.

**Deduplication**: Primary on `guid` (permalink). Secondary: title matching, date filtering, SHA-256 content hash for edit detection.

**Known quirks**:
- Paywalled content truncated in `content:encoded`
- No draft/scheduled post visibility in RSS
- No edit timestamps - only content hashing detects edits
- No public API - RSS is only reliable programmatic access
- Feed returns ~20-25 most recent posts, no pagination

**Recommendations for PBA**:
1. Poll every 15 minutes via `BackgroundService`
2. Implement conditional GET (ETag + If-Modified-Since)
3. Deduplicate on `guid`, store content hash for edit detection
4. Parse with XML parser targeting standard RSS fields + `content:encoded`
5. Store last poll timestamp, only process newer items

### Topic 2: Static Site Git-Based Publishing

**Approaches**: Direct commit to `main` (fastest, for already-reviewed content) vs. feature branch + PR (adds review gate).

**Recommendation for PBA**: Direct commit to `main` since content is already approved in PBA's pipeline.

**Deployment triggers**: GitHub Actions `on: push` with `paths: ['content/blog/**']` filter. Path filtering prevents unnecessary rebuilds.

**Blog post file structure**: Markdown with YAML frontmatter (title, date, author, description, tags, slug, image).

**Git best practices**:
- Semantic commit messages: `content: add blog post "Title Here"`
- Dedicated `content/blog/` path (PBA writes, humans edit templates)
- Atomic commits (one post per commit)
- Dedicated bot identity: `Personal Brand Assistant <pba@matthewkruczek.ai>`

**Implementation recommendations**:
1. Use GitHub REST API (or libgit2sharp) to create commits without cloning
2. Path convention: `content/blog/YYYY-MM-DD-slug.md`
3. Store commit SHA in PBA database for tracking
4. Post-deploy verification: HTTP GET the new blog URL, verify 200

### Topic 3: Cross-Platform Staggered Scheduling

**Industry finding**: No mainstream tool (Buffer, Hootsuite, CoSchedule, Planable) has native "publish to A, then auto-publish to B after X days." Staggering is always manual time-setting per platform.

**This is a custom workflow pattern PBA must implement.**

**Recommended architecture: Hybrid Calendar + Polling**:
1. When parent content is published, compute all child `scheduledAt` timestamps
2. Store in database with `status: Pending`
3. Background service polls every 1-5 minutes for due items
4. Execute platform publish, update status

**Data model concept**:
```
ContentItem (parent)
  ├── publishedAt: timestamp
  ├── childPublishRules:
  │     ├── { platform: "PersonalBlog", delayDays: 7 }
  │     └── { platform: "LinkedIn", delayDays: 0, delayHours: 2 }
  └── ChildContentItems[] (each with computed scheduledAt)
```

**Best practices from industry**:
- Buffer queue model: FIFO per platform with pre-set time slots
- CoSchedule campaigns: group related posts, each with own schedule but visually linked
- Planable approval chains: auto-publish when chain completes (maps to PBA's pipeline)

**Recommendations for PBA**:
1. Calendar-based scheduling with polling executor
2. Compute staggered times at content approval/creation
3. Configurable delay rules per content type and platform pair
4. Background service polls every 1 minute for due items
5. Platform-specific optimal time windows (snap to best posting time)
6. Retry with exponential backoff for failed publishes
