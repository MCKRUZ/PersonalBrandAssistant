# Multi-Platform Publishing Connectors

## Goal

Build native C# publishing connectors for Medium, Substack, LinkedIn, and Twitter/X inside PBA's existing ContentPublisher infrastructure. Replace the standalone Python scripts in matthewkruczek-ai with integrated, first-class platform connectors that route through the Content Studio state machine.

## Current State

### What exists in PBA (v2-rebuild branch)

- **BlogConnector** (`src/PBA.Infrastructure/Connectors/BlogConnector.cs`) — working git-based publisher that renders markdown to HTML, commits to the matthewkruczek-ai repo, and pushes. Returns the published URL.
- **ContentPublisher** (`src/PBA.Infrastructure/Publishing/ContentPublisher.cs`) — orchestrator that creates `ContentPlatformPublish` records and fires state machine triggers. Currently only routes to BlogConnector.
- **ContentPlatformPublish** entity — tracks per-platform publish status (Pending/Published/Failed) with metrics (Likes, Comments, Shares, Views, MetricsRefreshedAt).
- **Platform enum** — Blog, Substack, LinkedIn, Twitter, Reddit, YouTube (all defined, only Blog implemented).
- **ContentType enum** — Blog, Tweet, LinkedInPost (backend); frontend adds ThreadedTweet, SubstackNewsletter, RedditPost, YouTubeVideo, YouTubeShort.
- **Content state machine** — Idea -> Draft -> Review -> Approved -> Scheduled -> Published -> Archived. Hangfire scheduler for timed publishing.
- **Cross-post support** — `GenerateCrossPost` command exists for adapting content to target platforms.

### What exists in matthewkruczek-ai (Python scripts to port)

#### Medium (`medium/publish.py`, 307 lines)
- Extracts markdown from `medium/`, `substack/`, or `blog/drafts/` directories
- Strips YAML frontmatter and metadata blocks
- Converts inline SVG references to light-themed PNG images
- Injects canonical URLs pointing back to matthewkruczek.ai
- Default mode: prepares markdown for Medium's import tool (no API token needed)
- Optional mode: direct API publishing via integration token (deprecated for new tokens, but existing tokens still work)
- Medium REST API base: `https://api.medium.com/v1`

#### Medium Series (`medium/publish_series.py`, 180 lines)
- Manages "The AI Readiness Playbook" series (9 articles)
- `series-links.json` stores Medium post hashes, blog URLs, slugs, tags
- Prints URLs and subtitles for batch importing
- Can auto-open Medium import pages in browser

#### Substack (`substack/publish.py`, 392 lines)
- Full browser-based publishing (Playwright) — no official Substack API exists
- Automatically uploads images to Substack's CDN
- Injects subscribe widget after executive summary bullets
- Adds image captions from a predefined mapping
- Supports three modes: draft, publish immediately, schedule for future
- Authentication via email/password or saved cookies (`cookies.json`)
- Converts markdown to Substack's editor format

## New Connectors to Build

### 1. MediumConnector
- **API:** Medium REST API v1 (`https://api.medium.com/v1`)
- **Auth:** Integration token (bearer token)
- **Content format:** Markdown (Medium accepts markdown via API)
- **Features:**
  - Publish articles with title, content, tags, canonical URL
  - Set publish status (public, draft, unlisted)
  - Content format options: markdown or HTML
  - Series management (link related articles)
  - Return published URL and post ID for tracking

### 2. SubstackConnector
- **API:** No official API. Options to evaluate:
  - Browser automation via Playwright (port existing Python approach)
  - Substack's internal API (undocumented, used by their web app)
  - Email-based publishing (Substack supports publishing via email)
- **Auth:** Depends on approach chosen
- **Features:**
  - Publish/draft/schedule newsletter posts
  - Image upload to Substack CDN
  - Subscribe widget injection
  - Content transformation from markdown to Substack format

### 3. LinkedInConnector
- **API:** LinkedIn Community Management API v2
- **Auth:** OAuth 2.0 (3-legged)
- **Scopes needed:** `w_member_social` (personal posts), `r_member_social` (read engagement)
- **Known blocker:** User's Company Page is deactivated, needs reactivation for org posts
- **Features:**
  - Share articles (personal profile)
  - Share articles (company page, when reactivated)
  - Rich media attachments (images, documents)
  - Track engagement metrics (likes, comments, shares)
  - Character limit: 3000 for posts, 700 for article descriptions

### 4. TwitterConnector
- **API:** Twitter/X API v2
- **Auth:** OAuth 2.0 (PKCE) or OAuth 1.0a
- **Features:**
  - Post tweets (280 char limit)
  - Post threads (multiple tweets)
  - Media upload (images, video)
  - Track engagement metrics
  - Schedule tweets (via PBA's Hangfire scheduler, not Twitter's native scheduling)

## Architecture Requirements

### Common Interface
All connectors must implement a common `IPlatformConnector` interface:
- `PublishAsync(Content content, PublishOptions options)` -> `PlatformPublishResult`
- `ValidateCredentialsAsync()` -> `bool`
- `GetPlatformLimitsAsync()` -> `PlatformLimits` (character counts, media sizes, rate limits)
- Platform-specific content transformation (markdown -> platform format)

### Configuration
- Options pattern with `IOptionsMonitor<T>` for each platform
- Sections: `Publishing:Medium`, `Publishing:Substack`, `Publishing:LinkedIn`, `Publishing:Twitter`
- Secrets via User Secrets (dev) / Azure Key Vault (prod)
- Each connector should be independently enable/disable-able

### ContentPublisher Updates
- Route to appropriate connector based on target platform
- Support multi-platform publish (publish to Blog + Medium + Substack in one action)
- Create ContentPlatformPublish records for each target platform
- Handle partial failures (Blog succeeds but Medium fails)

### Content Transformation
- Each connector receives the canonical markdown content
- Each connector transforms to platform-specific format
- Cross-post generation already exists (`GenerateCrossPost` command) for AI-assisted adaptation
- Connectors handle mechanical transformation (markdown -> HTML, character truncation, etc.)

### OAuth Flow (LinkedIn, Twitter)
- OAuth callback endpoint in PBA.Api
- Token storage (encrypted, in database or secure config)
- Token refresh handling
- Connection status UI in frontend

### Frontend Updates
- Content editor: platform selection for publishing targets
- Publishing status dashboard per platform
- OAuth connection management (connect/disconnect LinkedIn, Twitter)
- Platform-specific preview (show how content will look on each platform)
- Metrics display from ContentPlatformPublish records

## Non-Goals (out of scope)
- Reddit connector (account is banned)
- YouTube connector (requires video production pipeline)
- Automated content scheduling AI (separate feature)
- Analytics aggregation dashboard (separate feature)
- Social media comment/reply management
