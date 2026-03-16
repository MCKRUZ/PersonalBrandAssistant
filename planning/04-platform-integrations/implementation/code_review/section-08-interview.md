# Section 08 Code Review Interview

## User Decision Items

### Post ID format validation (CRITICAL-2) — Fix all three
User chose: **Fix all three** (post ID validation, Instagram URL, YouTube quota)

Applied:
- Twitter: `^\d{1,20}$` regex validation on delete/engagement
- LinkedIn: `^urn:li:(share|ugcPost):\d+$` regex on delete
- Instagram: `^\d{1,25}$` regex on delete/engagement
- YouTube: `^[A-Za-z0-9_\-]{11}$` regex on delete/engagement

### Instagram post URL (HIGH-2) — Return placeholder URL
Instagram Graph API returns numeric media IDs, not shortcodes for web URLs.
Fix: Return `https://www.instagram.com/` as placeholder. Comment notes permalink can be resolved via `/{media-id}?fields=permalink`.

### YouTube quota tracking (HIGH-4) — Record quota cost directly
Fix: Call `_ytRateLimiter.RecordRequestAsync` with `UploadQuotaCost` (1600) and approximate midnight Pacific reset time.

## Auto-fixes Applied

| Finding | Fix |
|---------|-----|
| HIGH-1: LinkedIn fabricated GUID | Return `Result.Failure` when `x-restli-id` header missing |
| HIGH-3: Thread partial failure | Return `Result.Failure` with posted/total count on thread break |
| HIGH-5: Mutable Dictionary | Added `.AsReadOnly()` to all `PlatformSpecific` dictionaries |
| MEDIUM-3: Twitter 280-char validation | Added `content.Text.Length > 280` check |
| MEDIUM-4: YouTube redundant media check | Removed `if (content.Media.Count > 0)` guard |
| MEDIUM-5: 429 error code | Changed from `ValidationFailed` to `InternalError` |
| LOW-3: Protected fields → properties | Changed to `protected IMediaStorage MediaStorage { get; }` and `protected ILogger Logger { get; }` |

## Let Go

| Finding | Reason |
|---------|--------|
| CRITICAL-1: IG token in URL | Meta Graph API convention; DI config in section-12 will handle logging redaction |
| MEDIUM-1: LinkedIn redundant profile call | Optimization, not correctness issue |
| MEDIUM-2: CancellationToken between steps | HttpClient.SendAsync already observes token |
| MEDIUM-6: Test coverage gaps | Missing tests cover unimplemented features (Polly retry, chunked upload, carousel) |
| LOW-1: MediatR for Unit | Acceptable dependency |
| LOW-2: YouTube category constant | Fine as inline comment |

## Test Updates
- Fixed test data to use valid post ID formats (numeric for Twitter, 11-char for YouTube)
- All 24 tests pass after fixes
