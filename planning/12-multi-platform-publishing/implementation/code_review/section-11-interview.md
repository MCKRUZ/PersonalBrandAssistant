# Section 11 Code Review Interview

## Auto-fixes Applied

### HIGH-2: CancellationToken on RetryAsync
**Fix:** Added `CancellationToken ct = default` parameter to interface and implementation. All async calls now pass `ct` instead of `CancellationToken.None`. Hangfire 1.7+ injects a token that fires on shutdown/job deletion.

### HIGH-1: BackoffDelays array clarity
**Fix:** Added comment: `// [0] = initial retry (used by ContentPublisher), [1-2] = follow-up retries (used here)`. Kept all 3 entries (Option B) since the full schedule in one place aids discoverability.

### MEDIUM-2: Explicit Failed status in HandleFailure
**Fix:** Added `record.Status = PublishStatus.Failed;` in HandleFailure. Defensive against future callers that might pass non-Failed records.

### MEDIUM-5: Missing test for connector exception
**Fix:** Added `RetryAsync_ConnectorThrows_IncrementsRetryAndStoresMessage` — connector throws HttpRequestException, verifies RetryCount incremented and ErrorMessage stored.

### MEDIUM-6: Missing test for nonexistent record
**Fix:** Added `RetryAsync_NonexistentRecord_ReturnsWithoutError` — passes random Guid, verifies no connector calls and no exception thrown.

### LOW-1: Fully qualified type name
**Fix:** Added `using PBA.Domain.Entities;` and changed `Domain.Entities.ContentPlatformPublish` to `ContentPlatformPublish` in HandleFailure signature.

## Let Go

- MEDIUM-1: Race condition on concurrent execution (spec acknowledges, acceptable trade-off vs concurrency token complexity)
- MEDIUM-3: Test delay verification on Hangfire ScheduledState (BackoffIncreases test already validates NextRetryAt values)
- MEDIUM-4: Wall-clock time comparison in test (1-minute tolerance is generous enough for CI)
- MEDIUM-7: Missing unregistered platform test (low value, would need different service provider setup per test)
- LOW-2: Potential null Tags (entity defaults to [], EF won't produce null from list)
- LOW-3: ContentPublisher wiring gap (future section concern, not this section's scope)
