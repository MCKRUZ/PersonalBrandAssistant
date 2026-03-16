# Section 09 Code Review Interview

## User Decisions

### Budget TOCTOU Race Condition (#1)
**Decision:** Re-check before retries
**Action:** Added `IsOverBudgetAsync` check at the start of each retry iteration (attempt > 0)

## Auto-Fixes Applied

### #3: Retry backoff delay
Added exponential backoff: `Task.Delay(2^attempt seconds)` between transient error retries

### #4: Model tier tracking after downgrade
Changed `RecordUsageAsync` to accept `actualTier` parameter instead of using `execution.ModelUsed` (which preserves the original tier)

### #5: Workflow transition failure handling
Now checks `TransitionAsync` result and logs error if transition fails

### #9: FrozenDictionary for capabilities
Changed `Dictionary` to `FrozenDictionary` for immutability

### #11: MapCapabilityToContentType throws on unexpected types
Changed default case from silent `SocialPost` fallback to `ArgumentOutOfRangeException`

### Default workflow mock in tests
Added default `TransitionAsync` setup returning success in `CreateOrchestrator` to prevent NullReferenceException

### Notification message sanitized
Removed raw error message from notification body (was leaking exception details)

## Let Go (Deferred)

- #2: Method length — acceptable for an orchestration method with clear flow
- #7: Pagination — out of scope for this section
- #8: Options validation — will be addressed in section-11 DI config
- #10: Duplicate capability guard — DI framework catches at startup
- #12-15: Test coverage gaps and suggestions — deferred to follow-up
