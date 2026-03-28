# Code Review: Section 09 — Publishing Pipeline

**Verdict:** Block — 2 HIGH issues

## HIGH Issues

### [HIGH-1] IdempotencyKey init-only but stale on retry path
On retry (existingStatus not null), IdempotencyKey cannot be updated because it's init-only. If content.Version changed, the key is stale.

### [HIGH-2] Concurrency exception counts as neither success nor failure
All concurrency conflicts → misleading "All platforms failed" error. Should count as succeeded (another instance handles it).

## MEDIUM Issues

### [MEDIUM-1] Missing async/Processing platform handling
Always sets Published; plan says IG video/YouTube should be Processing.

### [MEDIUM-2] Rate limiter failure silently falls through to publish
### [MEDIUM-3] File path in namespace matches project convention (PlatformServices), not plan (Platform)
### [MEDIUM-4] _mediaStorage injected but unused
### [MEDIUM-5] Multiple SaveChangesAsync without transaction scope

## LOW Issues

### [LOW-1] No assertion on return value for concurrency test
### [LOW-2] RateLimited/Skipped count as failures in overall tally
### [LOW-3] Test duplicates SHA256 logic
