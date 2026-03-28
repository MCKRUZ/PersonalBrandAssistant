# Section 09 Code Review Interview

## Auto-fixes Applied

| Finding | Fix |
|---------|-----|
| HIGH-2: Concurrency conflicts miscounted | Count as `succeeded++` (another instance is handling it) |
| MEDIUM-2: Rate limiter failure silent | Added log warning, fail-open by design |
| MEDIUM-4: Unused _mediaStorage | Removed from constructor and field (adapters handle media) |
| LOW-1: Missing assertion on concurrency test | Added `Assert.True(result.IsSuccess)` |

## Let Go

| Finding | Reason |
|---------|--------|
| HIGH-1: IdempotencyKey init-only on retry | Correct by design — on retry, version hasn't changed. Content edits create new status records |
| MEDIUM-1: Async/Processing handling | Deferred to section-11 background processors. PublishResult has no Metadata yet |
| MEDIUM-3: Path mismatch | PlatformServices/ is the correct project convention |
| MEDIUM-5: No transaction scope | Eventual consistency acceptable for this design |
| LOW-2: RateLimited counts as failure | Acceptable — RateLimited is a "not published" state from pipeline perspective |
| LOW-3: Test duplicates SHA256 logic | Acceptable — verifies exact key computation |

## Tests: 12 passing after fixes
