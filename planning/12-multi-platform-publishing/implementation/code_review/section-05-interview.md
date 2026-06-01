# Section 05 Code Review Interview

## Critical Issues (Auto-Fixed)

### 1. DbContext Thread Safety in Parallel Secondaries
**Finding:** `Task.WhenAll` over lambdas that called `db.ContentPlatformPublishes.AnyAsync()` — EF Core DbContext is not thread-safe.
**Fix:** Moved idempotency check (AnyAsync) to BEFORE Task.WhenAll, collecting already-published platforms into a HashSet. Only HTTP connector calls are parallelized. DB writes (Add records) happen sequentially in the foreach after outcomes return.
**Status:** Applied

### 2. State Machine Throws on Idempotent Re-Entry
**Finding:** When primary was already published (idempotent re-publish scenario), content status is already `Published`. The state machine has no self-transition for `Publish`/`PublishNow` on `Published` state — Stateless throws `InvalidOperationException`.
**Fix:** Added guard: `if (content.Status != ContentStatus.Published)` before firing the state machine trigger.
**Status:** Applied

## Items Let Go

- Suggestion to add a test for idempotent primary-already-published + new secondaries scenario. Valid but not blocking — the guard fix covers the runtime behavior. Can be added later.
