# Phase 04 Platform Integrations — Interview Transcript

## Q1: Twitter API tier?
**Answer:** Support both Free and Basic. Design for Free tier (write-only, 1,500 tweets/mo) but make it easy to upgrade to Basic ($200/mo) for engagement tracking.

## Q2: YouTube scope given no community posts API?
**Answer:** Video metadata + basic upload. Include video upload capability (1,600 quota units each, max 6/day on default quota). Skip community posts entirely.

## Q3: Rate limiter implementation?
**Answer:** Database-backed. Persist rate limit state to PostgreSQL. Survives restarts and supports multi-instance.

## Q4: Media file handling?
**Answer:** Persistent storage on NAS. Store in a media directory, keep files after upload for reuse across platforms.

## Q5: OAuth callback flow?
**Answer:** Frontend-initiated. Redirect users back to Angular app after OAuth. Angular sends the auth code to the API backend for token exchange.

## Q6: Multi-platform partial failure handling?
**Answer:** Partial success + notify. Mark successful platforms as published, notify user which platforms failed, allow manual retry per-platform.

## Q7: Media storage path?
**Answer:** Configurable via appsettings. Add `MediaStorage:BasePath` to configuration, default to `./media`.
