# Multi-Platform Publishing — Synthesized Specification

## Overview

Build native C# publishing connectors for Medium, Substack, LinkedIn, and Twitter/X inside PBA's existing ContentPublisher infrastructure. Replace standalone Python scripts with integrated connectors that route through the Content Studio state machine and provide a unified publishing experience from the Angular frontend.

## Architecture Decisions

### IPlatformConnector Interface + Keyed DI
Replace the current `IBlogConnector` with a unified `IPlatformConnector` interface. All connectors (Blog, Medium, Substack, LinkedIn, Twitter) implement this interface. Resolve via keyed DI using the `Platform` enum. The `ContentPublisher` orchestrator uses a factory/resolver to get the appropriate connector for each target platform.

### Content Transformation Pipeline
Extract content transformation from individual connectors into a shared pipeline. A central `IContentTransformer` processes raw markdown through pluggable `IPlatformFormatter` implementations. Each platform has a formatter that handles its specific needs (strip frontmatter, inject canonical URLs, convert images, enforce character limits). Connectors receive pre-formatted content ready for their API.

### Primary + Best-Effort Publishing
When publishing to multiple platforms simultaneously:
- The **primary platform** (typically Blog) must succeed for the publish to proceed
- Other platforms are **best-effort**: failures are recorded in `ContentPlatformPublish` with error details
- Failed publishes enter a **retry queue** (Hangfire recurring job) with exponential backoff
- Per-platform status visible in the UI via existing `ContentPlatformPublish` entity

### OAuth Token Storage
Store OAuth tokens (LinkedIn, Twitter) as encrypted columns in PostgreSQL. A `PlatformCredential` entity holds:
- Platform, AccessToken (encrypted), RefreshToken (encrypted), ExpiresAt, Scopes, IsActive
- Token refresh logic runs automatically before API calls when tokens are near expiry

### Full OAuth Flow
Build complete OAuth callback endpoints for LinkedIn and Twitter:
- `/api/auth/{platform}/authorize` — initiate OAuth flow, redirect to platform
- `/api/auth/{platform}/callback` — handle callback, exchange code for tokens, store encrypted
- Token refresh handled transparently by each connector before API calls

## Platform Connectors

### 1. MediumConnector
- **API:** Medium REST API v1 (`https://api.medium.com/v1`)
- **Auth:** Integration token (bearer). User has existing token from Python scripts.
- **Publish flow:** `GET /me` (get userId) → `POST /users/{userId}/posts` with markdown content
- **Content format:** Markdown (accepted natively by Medium API)
- **Transformation needs:** Strip YAML frontmatter, inject canonical URL to matthewkruczek.ai, resolve image URLs to absolute paths, convert SVG references to PNG
- **Config:** `MediumOptions` — IntegrationToken, DefaultPublishStatus (public/draft/unlisted), DefaultTags
- **Difficulty:** Low

### 2. SubstackConnector
- **API:** Reverse-engineered internal API (no official API exists)
- **Auth:** Cookie-based session (`substack.sid`, `substack.lli`). Cookies stored encrypted in DB, refreshed via email/password login when expired.
- **Approach:** Direct HTTP in C# — port Python `substack/publish.py` logic to HttpClient
- **Publish flow:** Login → Create draft (`POST /api/v1/drafts`) → Upload images to CDN → Publish (`POST /api/v1/drafts/{id}/publish`)
- **Content format:** Tiptap JSON document structure (markdown must be converted)
- **Transformation needs:** Strip title (Substack renders from metadata), remove references/author bio, inject subscribe widget, add image captions, convert markdown to Tiptap JSON
- **Config:** `SubstackOptions` — PublicationSlug, Email, Password (encrypted), DefaultAudience
- **Difficulty:** High. Cookie auth expires, Tiptap format is complex, endpoints are undocumented.

### 3. LinkedInConnector
- **API:** LinkedIn Community Management API v2 (`https://api.linkedin.com/rest/posts`)
- **Auth:** OAuth 2.0 three-legged flow. User has existing LinkedIn developer app.
- **Token lifetime:** Access=60 days, Refresh=365 days
- **Publish flow:** Validate token → Upload images if any (`POST /rest/images?action=initializeUpload` → PUT binary) → Create post (`POST /rest/posts`)
- **Content format:** Plain text (max 3000 chars) with optional article/image attachments
- **Transformation needs:** Truncate to 3000 chars, strip markdown formatting for plain text posts, or create article share with link
- **Headers:** `Linkedin-Version: YYYYMM`, `X-Restli-Protocol-Version: 2.0.0`
- **Key constraint:** No scheduled publishing via API — PBA's Hangfire handles scheduling
- **Config:** `LinkedInOptions` — ClientId, ClientSecret, RedirectUri, PersonId (URN), OrganizationId (optional)
- **Difficulty:** Medium

### 4. TwitterConnector
- **API:** Twitter/X API v2 (`https://api.x.com/2/tweets`)
- **Auth:** OAuth 2.0 with PKCE. Needs new developer app setup.
- **Token lifetime:** Access=2 hours (needs aggressive refresh), Refresh via `offline.access` scope
- **Publish flow:** Validate/refresh token → Upload media if any (INIT → APPEND → FINALIZE) → Create tweet → If thread, chain replies
- **Content format:** Plain text (max 280 chars)
- **Thread support:** No dedicated endpoint. Chain tweets via `reply.in_reply_to_tweet_id`.
- **Media caveat:** v2 media endpoints may return 403 with OAuth 2.0. May need OAuth 1.0a fallback for media upload.
- **Transformation needs:** Split long content into thread segments, strip/simplify markdown to plain text, handle hashtags
- **Config:** `TwitterOptions` — ClientId, ClientSecret, RedirectUri, ApiKey, ApiSecret (for OAuth 1.0a media fallback)
- **Difficulty:** Medium-High

## Domain Model Changes

### New Entity: PlatformCredential
- Id, Platform, UserId (optional), AccessToken (encrypted), RefreshToken (encrypted), ExpiresAt, RefreshExpiresAt, Scopes, IsActive, CreatedAt, UpdatedAt
- One active credential per platform

### Content Entity Updates
- Add `TargetPlatforms` (list of Platform enum) — which platforms to publish to
- `PrimaryPlatform` remains for the canonical version; `TargetPlatforms` tracks cross-post targets

### ContentPlatformPublish Updates
- Add `RetryCount`, `NextRetryAt` for retry queue
- `ErrorMessage` already exists

## API Endpoints

### OAuth
- `GET /api/auth/{platform}/authorize` — redirect to platform OAuth
- `GET /api/auth/{platform}/callback` — handle callback, store tokens
- `GET /api/auth/{platform}/status` — check connection status
- `DELETE /api/auth/{platform}` — disconnect (revoke + delete tokens)

### Publishing
- Update `POST /api/content/{id}/publish` — accept `targetPlatforms` array
- Update `PUT /api/content/{id}/schedule` — accept `targetPlatforms` array
- `POST /api/content/{id}/retry/{platform}` — retry failed platform publish
- `GET /api/content/{id}/publish-status` — get per-platform publish status

### Platform Management
- `GET /api/platforms` — list platforms with connection status and capabilities
- `GET /api/platforms/{platform}/limits` — character limits, media constraints

## Frontend Changes

### Content Editor
- Platform target checkboxes (set which platforms to publish to during editing)
- Confirmation modal at publish/schedule time showing per-platform preview
- Per-platform character count indicators (280 for Twitter, 3000 for LinkedIn, etc.)

### Platform Connections Page
- OAuth connect/disconnect buttons for LinkedIn, Twitter
- Token entry for Medium (integration token)
- Cookie-based login for Substack (email/password form)
- Connection status indicators (connected, expired, not configured)

### Content List
- Per-platform publish status badges on content cards
- Filter by platform publish status

## Configuration

### appsettings.json structure
```
Publishing:
  Medium:
    Enabled: true
    DefaultPublishStatus: "draft"
    DefaultTags: []
  Substack:
    Enabled: true
    PublicationSlug: "mattkruczek"
    DefaultAudience: "everyone"
  LinkedIn:
    Enabled: true
    ClientId: (from secrets)
    RedirectUri: "https://..."
  Twitter:
    Enabled: true
    ClientId: (from secrets)
    RedirectUri: "https://..."
```

### Secrets (User Secrets / KeyVault)
- `Publishing:Medium:IntegrationToken`
- `Publishing:Substack:Email`, `Publishing:Substack:Password`
- `Publishing:LinkedIn:ClientId`, `Publishing:LinkedIn:ClientSecret`
- `Publishing:Twitter:ClientId`, `Publishing:Twitter:ClientSecret`
- `Publishing:Twitter:ApiKey`, `Publishing:Twitter:ApiSecret` (OAuth 1.0a media fallback)
- `Encryption:Key` — for encrypting stored tokens

## Non-Goals
- Reddit connector (account banned)
- YouTube connector (requires video pipeline)
- Automated content scheduling AI
- Analytics aggregation dashboard
- Social media comment/reply management
- Medium series management (can be added later)
