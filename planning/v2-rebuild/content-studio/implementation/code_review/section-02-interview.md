# Section 02 Code Review Interview

## Auto-Fixed

### HIGH: Consolidate double Configure calls
- **Finding**: Draft and Scheduled states each had two `Configure` blocks — permits in one, entry actions in another
- **Action**: Merged into single `Configure` per state with entry action first, then permits
- **Rationale**: Stateless merges additively so it worked, but split blocks are a readability/maintenance trap

### HIGH: Test namespace mismatch
- **Finding**: Test file at `Features/ContentStudio/` declared `namespace PBA.Application.Tests.Features.Content`
- **Action**: Changed to `PBA.Application.Tests.Features.ContentStudio`
- **Rationale**: Left over from the rename during implementation

### MEDIUM: Missing past-ScheduledAt guard test
- **Finding**: Schedule guard checks `HasValue && > UtcNow` but only null case was tested
- **Action**: Added `Fire_Schedule_FromApproved_FailsWhenScheduledAtInPast` test
- **Rationale**: Both guard conditions should be tested

## Let Go

### MEDIUM: OnEntryAsync with sync work
- **Finding**: Entry actions are sync property mutations wrapped in `OnEntryAsync` + `Task.CompletedTask`
- **Decision**: Let go. Stateless requires async entry actions when using `FireAsync`. The overhead is negligible.

### MEDIUM: DateTimeOffset.UtcNow in guard
- **Finding**: Schedule guard uses `DateTimeOffset.UtcNow` directly, non-deterministic
- **Decision**: Let go. Tests use 1-day margins which are safe. TimeProvider injection deferred to sections 05/06.

### LOW: Stateless package in test project
- **Finding**: Possibly redundant package reference
- **Decision**: Let go. Required because `Create()` returns `StateMachine<,>` type used in tests.

## Verification
- 21 tests pass after fixes (added 1 new test)
