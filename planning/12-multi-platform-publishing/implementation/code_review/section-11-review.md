# Section 11 Code Review: Retry Handler

## HIGH

### HIGH-1: Backoff array clarity -- index 0 never used by handler
`PublishRetryHandler.cs` -- `BackoffDelays[0]` (5min) is used by ContentPublisher for initial schedule, not by this handler. Array is misleading.

### HIGH-2: Missing CancellationToken on RetryAsync
`IPublishRetryHandler.cs:5` -- Hangfire 1.7+ supports CancellationToken injection for graceful shutdown. Current signature uses `CancellationToken.None` everywhere.

## MEDIUM

### MEDIUM-1: Race condition on concurrent retry execution
`PublishRetryHandler.cs:45-119` -- Two concurrent retries could double-increment RetryCount. Idempotency check covers success-success case but not fail-fail.

### MEDIUM-2: Status not explicitly set to Failed in HandleFailure
`PublishRetryHandler.cs:122-146` -- Relies on record already being Failed. Defensive to set explicitly.

### MEDIUM-3: Test doesn't verify actual delay passed to Hangfire
`PublishRetryHandlerTests.cs:290-293` -- Verifies Create called once but not the ScheduledState delay.

### MEDIUM-4: BackoffIncreases test uses wall-clock comparison
`PublishRetryHandlerTests.cs:326-337` -- Could flake under CI load. Capture time before/after.

### MEDIUM-5: Missing test -- connector throws exception
No test covers the catch(Exception) branch in RetryAsync.

### MEDIUM-6: Missing test -- nonexistent record ID
No test for the record-not-found guard.

### MEDIUM-7: Missing test -- unregistered platform connector
No test for missing connector guard path.

## LOW

### LOW-1: Fully qualified type in HandleFailure
`PublishRetryHandler.cs:122` -- Uses `Domain.Entities.ContentPlatformPublish` instead of adding using.

### LOW-2: Potential null Tags on Content
`PublishRetryHandler.cs:93` -- `Content.Tags.AsReadOnly()` could throw if Tags is null from DB.

### LOW-3: ContentPublisher doesn't yet schedule initial retries
Wiring gap -- ContentPublisher creates Failed records but doesn't schedule retry jobs yet.
