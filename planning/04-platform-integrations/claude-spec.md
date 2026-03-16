# Phase 04 Platform Integrations — Specification

## Overview

OAuth authentication, API adapters, content formatting, and media management for Twitter/X, LinkedIn, Instagram, and YouTube. All implementations sit behind an `ISocialPlatform` abstraction consumed by the workflow engine's `IPublishingPipeline`.

## Platforms

### Twitter/X
- OAuth 2.0 PKCE (2h access tokens, refresh tokens)
- Posting: tweets, threads (chained replies), media (chunked upload)
- Design for Free tier (1,500 tweets/mo, write-only) with upgrade path to Basic ($200/mo) for engagement read access
- Rate limits tracked per API response headers (`x-rate-limit-remaining`)

### LinkedIn
- OAuth 2.0 3-legged (60-day tokens)
- Posts API only (ugcPosts/Share API deprecated)
- Supports: text, images, video, documents, articles, multi-image, polls
- Versioned API headers required (`Linkedin-Version: YYYYMM`)
- Media upload via separate Images/Videos API returning URNs

### Instagram
- Meta Graph API (requires Facebook Business account linkage)
- OAuth: short-lived (1h) → long-lived (60 days, must refresh before expiry)
- Two-step publish: create media container → publish container
- Supports: feed images/video, reels, stories, carousels
- 100 API-published posts per 24h rolling
- Media must be publicly accessible URLs (except resumable video upload)

### YouTube
- Google OAuth 2.0 (refresh tokens don't expire unless revoked)
- Focus: video metadata updates (50 units) + basic video upload (1,600 units)
- Default quota: 10,000 units/day
- NO community posts API — excluded from scope
- Official SDK: `Google.Apis.YouTube.v3`

## Architecture Decisions

### OAuth Flow
Frontend-initiated: Angular redirects user to platform OAuth, receives auth code on callback, sends code to backend API for token exchange. Backend stores encrypted tokens.

### Token Storage
Encrypted via existing `IEncryptionService` (ASP.NET Data Protection). Platform entity already has `EncryptedAccessToken`/`EncryptedRefreshToken` byte[] fields. Background processor auto-refreshes tokens before expiry.

### Rate Limiting
Database-backed rate limiter. Persist rate limit state to PostgreSQL via Platform entity's `RateLimitState` JSONB field. Survives restarts. Tracks remaining calls and reset times per platform.

### Media Storage
Persistent local storage on NAS. Configurable path via `MediaStorage:BasePath` in appsettings (default `./media`). Files kept after upload for cross-platform reuse.

### Multi-Platform Publishing
Partial success model. When content targets multiple platforms:
- Publish to each independently
- Track per-platform publish status
- On partial failure: mark successful platforms as published, notify user of failures
- Allow per-platform manual retry

### Publishing Pipeline
Replace `PublishingPipelineStub` with real implementation that:
1. Loads Content with TargetPlatforms
2. Resolves ISocialPlatform adapter per platform
3. Formats content per platform (IPlatformContentFormatter)
4. Posts via adapter
5. Updates per-platform status
6. Handles partial failures with notification

## Interfaces

### ISocialPlatform
Per-platform adapter with operations: Post, Delete, GetEngagement (where supported), GetProfile, ValidateContent, RefreshToken

### IPlatformContentFormatter
Transforms generic Content into platform-specific format: truncation, hashtags, media validation, character limits

### IOAuthManager
Manages OAuth flows: GenerateAuthUrl, ExchangeCode, RefreshToken, RevokeToken, ValidateToken

### IRateLimiter
Database-backed rate limit tracking: CanMakeRequest, RecordRequest, GetRemainingQuota, WaitForReset

### IMediaStorage
Persistent file storage: SaveAsync, GetPathAsync, DeleteAsync, GetPublicUrlAsync (for Instagram)

## API Endpoints

- `GET /api/platforms` — list all platforms with connection status
- `GET /api/platforms/{type}/auth-url` — generate OAuth authorization URL
- `POST /api/platforms/{type}/callback` — exchange auth code for tokens
- `DELETE /api/platforms/{type}/disconnect` — revoke tokens, disconnect
- `GET /api/platforms/{type}/status` — connection health, rate limit state, token validity
- `POST /api/platforms/{type}/test-post` — publish a test post to verify connection

## Background Processors

- **TokenRefreshProcessor**: Periodically checks token expiry, refreshes before deadline (Twitter: every 90min, LinkedIn/Instagram: every 50 days)
- **PlatformHealthMonitor**: Validates connections, updates rate limit state, marks stale connections

## .NET Libraries

| Platform | Library |
|----------|---------|
| Twitter/X | HttpClient (direct v2 API) or LinqToTwitter |
| LinkedIn | HttpClient (direct REST) |
| Instagram | HttpClient (Meta Graph API) |
| YouTube | Google.Apis.YouTube.v3 (official) |

## Out of Scope
- YouTube community posts (no API)
- Content generation (Phase 03)
- Scheduling logic (Phase 02)
- Dashboard UI (Phase 06)
