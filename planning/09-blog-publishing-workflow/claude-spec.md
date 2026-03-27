# Complete Specification: Blog Publishing Workflow

## Overview

A two-stage blog publishing pipeline within PBA that enables authoring blog posts via an embedded Claude chat interface (using blog-writer skill logic), preparing dual-format output (Substack + personal blog), and orchestrating staggered publishing with Substack first and matthewkruczek.ai following after a configurable delay.

## Architecture Context

### Existing Infrastructure
- **Content entity** with full state machine: Draft -> Review -> Approved -> Scheduled -> Publishing -> Published (+ Failed, Archived)
- **RepurposingService** with parent-child content relationships, tree depth tracking, idempotency
- **PublishingPipeline** with platform adapters (Twitter/X, LinkedIn, Instagram, YouTube, Reddit)
- **SubstackService** (RSS read-only) polling `https://matthewkruczek.substack.com/feed`
- **PlatformType enum** includes `Substack` and `PersonalBlog` (no adapters yet)
- **ContentType enum** includes `BlogPost`
- **Background services**: ScheduledPublishProcessor (30s poll), RetryFailedProcessor, RepurposeOnPublishProcessor
- **Content pipeline UI**: Topic -> Outline -> Draft -> Review wizard
- **ContentCalendarService**: iCalendar RRULE scheduling, slot materialization
- **Testing**: xUnit + Moq, CustomWebApplicationFactory, in-memory SQLite

### Target Site: matthewkruczek.ai
- Static HTML site deployed via GitHub Pages
- Blog posts are HTML files in the repository
- Deployment triggered by push to main branch

## Workflow

### Phase 1: Content Authoring
1. User navigates to PBA content page, initiates a new blog post
2. PBA presents a **chat interface** where the user interacts with Claude
3. The chat uses the `matt-kruczek-blog-writer` skill logic for voice, humanization, and content patterns
4. User iterates on the draft through conversation (feedback, revisions, refinements)
5. When satisfied, user finalizes the content

### Phase 2: Dual-Format Preparation
6. PBA generates **Substack-formatted output**: title, subtitle, body (markdown/rich text), SEO description, tags, section, preview text - each field individually copyable
7. PBA generates **blog HTML output**: content wrapped in matthewkruczek.ai HTML template with canonical URL pointing to Substack
8. Both versions are stored and accessible from the content detail view

### Phase 3: Substack Manual Publish
9. User copies each prepared field into Substack's editor and publishes
10. User can mark the content as "Published to Substack" in PBA (manual confirmation), or...
11. PBA's RSS poller detects the new publication automatically (15-minute polling interval)

### Phase 4: Blog Scheduling
12. On Substack detection (RSS or manual), PBA sends a notification to the user
13. User confirms -> PBA schedules the blog version for `Substack publish date + 7 days` (default)
14. User can override the delay per-post, or skip the blog version entirely
15. The scheduled blog deploy appears in both the content pipeline and the blog publishing dashboard

### Phase 5: Blog Deployment
16. When the scheduled date arrives, PBA stages the blog HTML (ready to deploy)
17. PBA notifies user that the blog version is ready
18. User clicks "Publish" in PBA -> triggers git commit to matthewkruczek-ai repo + GitHub Pages deploy
19. PBA tracks the deployment status (commit SHA, deploy success)

## Components to Build

### 1. Chat-Based Content Authoring Interface
- **Angular component**: Embedded chat UI within the content creation flow
- **Backend**: API endpoint that proxies to Claude API with blog-writer skill system prompt
- **Conversation persistence**: Store chat history per content item for reference
- **Finalization**: "Finalize" action that extracts the final draft from the conversation

### 2. Substack Publishing Adapter
- **Not a true ISocialPlatform adapter** (no write API exists)
- **SubstackPrepService**: Formats finalized content into Substack-ready fields
  - Title, subtitle, body (Substack-optimized markdown), SEO description, tags, section name, preview text
- **UI**: Content detail view with per-field copy-to-clipboard buttons
- **Manual publish tracking**: User can mark as "published to Substack" manually
- **RSS auto-detection**: Enhanced SubstackService that matches published RSS entries to PBA content items

### 3. PersonalBlog Publishing Adapter
- **BlogHtmlGenerator**: Renders blog HTML from content using matthewkruczek.ai template conventions
  - Includes canonical URL to Substack post
  - Follows existing blog post structure/styling
- **GitHubPublishService**: Commits generated HTML to the matthewkruczek-ai repo via GitHub REST API
  - Commit message: `content: add blog post "Title Here"`
  - Author identity: `Personal Brand Assistant <pba@matthewkruczek.ai>`
  - Stores commit SHA for tracking
- **Deploy verification**: HTTP GET the new blog URL after deploy, verify 200 status
- **Semi-automated**: PBA prepares everything, user clicks "Publish" to trigger

### 4. Staggered Publish Scheduling
- **PublishDelayRule model**: Configurable delay between platform publishes
  - Default: Substack -> PersonalBlog = 7 days
  - Global default with per-post override
  - Option to skip the blog version entirely
- **Enhanced ScheduledPublishProcessor**: Understands cross-platform temporal dependencies
  - Computes blog `ScheduledAt` = Substack `PublishedAt` + delay
  - Respects platform ordering (Substack must be Published before blog can be Scheduled)
- **Notification system**: Alerts user when Substack is detected live and blog scheduling is pending confirmation

### 5. RSS Publication Detection
- **Enhanced SubstackService**:
  - 15-minute polling interval via BackgroundService
  - HTTP conditional GET (ETag + If-Modified-Since) for efficiency
  - Deduplication on `guid` (permalink)
  - Content matching: matches RSS entries to PBA content by title + date proximity
  - SHA-256 content hash for edit detection
  - Stores last poll timestamp, only processes newer items
- **ContentPublicationMatcher**: Links detected RSS entries to existing PBA content items
  - Primary: exact title match
  - Secondary: fuzzy title match + date within 24h window
  - Fallback: manual linking in UI

### 6. Blog Publishing Dashboard (Angular)
- **Dedicated view** showing all blog posts in the two-stage pipeline
- **Pipeline visualization**: Substack status (Draft/Ready/Published) -> Blog status (Waiting/Scheduled/Ready/Published)
- **Timeline view**: Visual timeline showing Substack publish date, delay period, scheduled blog date
- **Actions**: Override delay, skip blog, trigger publish, re-generate HTML
- **Filtering**: By status, date range, tags

### 7. Content Pipeline Integration
- **New pipeline stage**: After "Approved", show "Publish Prep" with Substack + Blog format previews
- **Status tracking**: Extended content detail view showing cross-platform publish status
- **Workflow panel**: Updated to show the dual-platform publishing states

## Data Model Changes

### New/Modified Entities
- `Content` entity: Add `SubstackPublishedAt`, `SubstackPostUrl`, `BlogPublishedAt`, `BlogPostUrl`, `BlogDeployCommitSha`
- `PublishDelayRule`: Global and per-content configurable delays between platforms
- `SubstackDetection`: RSS detection log (guid, matched content ID, detected at, confidence score)
- `BlogPublishRequest`: Staged blog publish with HTML content, target path, status (Staged/Publishing/Published/Failed)
- `ChatConversation`: Chat history for content authoring sessions (content ID, messages, timestamps)

### Configuration
- `SubstackOptions`: Enhanced with `PollingIntervalMinutes` (default 15), `MatchingConfidenceThreshold`
- `BlogPublishOptions`: Repo URL, branch, content path pattern, author name/email, deploy verification URL pattern
- `PublishDelayOptions`: Default delay (7 days), per-platform defaults

## API Endpoints

### Content Authoring Chat
- `POST /api/content/{id}/chat` - Send message to Claude for content iteration
- `GET /api/content/{id}/chat/history` - Get conversation history
- `POST /api/content/{id}/chat/finalize` - Extract final draft from conversation

### Substack Prep
- `GET /api/content/{id}/substack-prep` - Get Substack-formatted fields
- `POST /api/content/{id}/substack-published` - Manually mark as published to Substack

### Blog Publish
- `GET /api/content/{id}/blog-prep` - Get blog HTML preview
- `POST /api/content/{id}/blog-publish` - Trigger git commit + deploy
- `GET /api/content/{id}/blog-status` - Check deploy status

### Blog Dashboard
- `GET /api/blog-pipeline` - List all blog posts with two-stage status
- `POST /api/blog-pipeline/{id}/schedule` - Confirm blog schedule after Substack detection
- `PUT /api/blog-pipeline/{id}/delay` - Override delay for specific post
- `POST /api/blog-pipeline/{id}/skip-blog` - Skip blog version

### Notifications
- `GET /api/notifications` - Get pending notifications (Substack detected, blog ready, etc.)
- `POST /api/notifications/{id}/acknowledge` - Acknowledge notification

## Constraints

- Substack has no write API - publishing remains manual with PBA assistance
- matthewkruczek.ai is static HTML + GitHub Pages - publishing = git commit + push
- RSS polling interval (15 min) determines maximum detection lag
- Blog-writer skill logic must be adapted for API consumption (currently designed for CLI)
- Chat interface requires Claude API integration (streaming for good UX)
- GitHub API rate limits apply to blog publishing (5000 req/hr authenticated)

## Out of Scope
- Substack API integration (doesn't exist)
- Changes to matthewkruczek.ai site structure/templates
- Social media promotion of blog posts (separate feature)
- Analytics for blog posts (existing analytics dashboard)
- Paid/subscriber-only Substack content handling
