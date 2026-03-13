# 04 — Platform Integrations

## Overview
OAuth authentication and API adapters for Twitter/X, LinkedIn, Instagram, and YouTube. All implementations sit behind an `ISocialPlatform` abstraction for consistent consumption by the workflow engine and content engine.

## Requirements Reference
See `../requirements.md` for full project context and `../deep_project_interview.md` for design decisions.

Key interview insight: All four platforms are must-haves at launch. User wants comprehensive integration, not just posting.

## Scope

### Platform Abstraction Layer
- `ISocialPlatform` interface with operations: Post, Delete, GetEngagement, GetProfile, ValidateContent
- `IPlatformContentFormatter` — transforms generic content into platform-specific format
- Platform capability flags (supports threads, supports media, supports scheduling, etc.)
- Platform health monitoring (API status, rate limit state, token validity)

### Twitter/X Integration
- OAuth 2.0 with PKCE (Twitter API v2)
- Posting: tweets, threads (multi-tweet), with media (images, video)
- Character limit handling (280 chars, URL shortening rules)
- Hashtag and mention formatting
- Engagement: likes, retweets, replies, quote tweets
- Rate limits: 300 tweets/3hrs (app-level), 200 tweets/15min (user-level)

### LinkedIn Integration
- OAuth 2.0 (LinkedIn Marketing API or Community Management API)
- Posting: text posts, articles, image/video posts
- Character limit: 3,000 for posts, longer for articles
- Hashtag support, @mention formatting
- Engagement: reactions, comments, shares
- Rate limits: varies by endpoint

### Instagram Integration
- Meta Graph API (requires Facebook Business account linkage)
- Posting: feed posts (image/carousel/video), Stories, Reels (as available via API)
- Caption formatting (hashtags, mentions, line breaks)
- Media upload requirements (aspect ratios, file sizes)
- Engagement: likes, comments
- Rate limits: 25 API calls/user/hour (Content Publishing)

### YouTube Integration
- YouTube Data API v3 (OAuth 2.0)
- Scope: video metadata updates, community posts, playlist management
- Thumbnail upload
- Video description and tags optimization
- Engagement: comments, likes
- Quota: 10,000 units/day (uploads are 1,600 units each)
- Note: Video upload is expensive quota-wise; focus on metadata and community posts initially

### OAuth Management
- Encrypted token storage (AES-256 or similar via EF Core value converter)
- Automatic token refresh before expiry
- Token revocation handling
- Connection status monitoring (valid, expired, revoked)
- Re-authorization flow when tokens are invalid

### Rate Limiting
- Per-platform rate limit tracking
- Centralized rate limiter service
- Backoff and retry with jitter
- Queue integration — don't dequeue faster than platform allows
- Rate limit state exposed to workflow engine's scheduler

### Content Formatting
- Platform-specific content transformation:
  - Truncation with ellipsis and "read more" link
  - Hashtag strategy per platform
  - Media format conversion/validation
  - URL handling (shortening, preview cards)
  - Emoji support validation

## Out of Scope
- Content generation/writing (→ 03, 05)
- Scheduling logic (→ 02)
- Dashboard for platform management (→ 06)

## Key Decisions Needed During /deep-plan
1. Twitter API tier — Free, Basic ($100/mo), or Pro ($5000/mo)? Impacts rate limits significantly.
2. Instagram — require Facebook Business account linkage? Adds complexity.
3. YouTube — focus on metadata+community posts or include video upload?
4. Rate limiter — in-memory (simpler) vs Redis-backed (survives restarts)?
5. Media handling — store locally then upload, or stream-through?

## Dependencies
- **Depends on:** `01-foundation` (domain models, encrypted storage), `02-workflow-engine` (publishing triggers)
- **Blocks:** `05-content-engine` (needs platform constraints for formatting)

## Interfaces Consumed
- Domain models from 01 (Content, Platform)
- Encrypted storage from 01
- Publishing triggers from 02 (workflow engine calls platform adapters)

## Interfaces Produced
- `ISocialPlatform` — per-platform implementations
- `IPlatformContentFormatter` — content transformation per platform
- `IRateLimiter` — rate limit state and enforcement
- `IOAuthManager` — token management and authorization flows
- Platform management API endpoints (connect, disconnect, status)

## Definition of Done
- At least Twitter/X and LinkedIn fully integrated (OAuth + posting + engagement retrieval)
- Instagram and YouTube OAuth working, basic posting functional
- `ISocialPlatform` abstraction fully defined and implemented for all 4 platforms
- OAuth token refresh working automatically
- Rate limiting prevents exceeding platform quotas
- Content formatting produces valid platform-specific output
- Integration tests against platform sandbox/test accounts
