Now I have everything I need. Let me write the section content.

# Section 09: Scheduler -- DailyContentProcessor BackgroundService

## Overview

This section implements the `DailyContentProcessor`, a `BackgroundService` that fires the daily content automation pipeline at a configurable time using Cronos cron expressions with timezone awareness. It replaces the `PeriodicTimer` pattern used by other background jobs in the codebase with a `Task.Delay` + `CronExpression.GetNextOccurrence()` approach that supports arbitrary cron schedules and timezone-aware scheduling (important for DST transitions).

The processor is a thin scheduling wrapper. It does not contain business logic -- it resolves `IDailyContentOrchestrator` from a fresh `IServiceScope` and calls `ExecuteAsync()`. Before each execution it performs a date-based idempotency check against the `AutomationRuns` table to prevent duplicate runs from app restarts, deployments, or clock drift.

---

## Dependencies

**Depends on:**
- **section-01-foundation** -- provides `ContentAutomationOptions`, `AutomationRun` entity, `AutomationRunStatus` enum, `IDailyContentOrchestrator` interface, `AutomationRunResult` model, `DbSet<AutomationRun>` on `IApplicationDbContext`, Cronos NuGet package, and DI registration stubs
- **section-08-orchestrator** -- provides the real `DailyContentOrchestrator` implementation that this processor calls

**Blocks:**
- **section-10-api-endpoints** -- the API endpoints depend on having the full scheduler in place for manual trigger coordination

---

## Tests First

All tests go in `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/DailyContentProcessorTests.cs`. Use xUnit + Moq, consistent with existing PBA test conventions.

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/DailyContentProcessorTests.cs`

This test class tests the internal scheduling logic of `DailyContentProcessor`. The processor's `ExecuteAsync` loop is difficult to test directly (it runs indefinitely), so the processor should expose an `internal` method (following the existing pattern in `ScheduledPublishProcessor.ProcessDueContentAsync` and `CalendarSlotProcessor.ProcessAsync`) that contains the per-tick logic. This method is what the unit tests exercise.

**Unit Tests:**

1. **Processor skips execution when `Enabled = false` in options** -- Configure `ContentAutomationOptions` with `Enabled = false`. Call the internal execution method. Assert that `IDailyContentOrchestrator.ExecuteAsync` was never called. No `AutomationRun` should be created.

2. **Processor skips execution when an AutomationRun with `Completed` status exists for today** -- Arrange: Insert a `Completed` `AutomationRun` with `TriggeredAt` set to today (same calendar date in the configured timezone). Call the internal execution method. Assert `IDailyContentOrchestrator.ExecuteAsync` was never called.

3. **Processor skips execution when an AutomationRun with `Running` status exists for today** -- Same as above but with `Running` status. This prevents concurrent duplicate runs from manual triggers overlapping with the scheduled trigger.

4. **Processor creates a new IServiceScope and resolves IDailyContentOrchestrator per execution** -- Call the internal method twice. Assert `IServiceScopeFactory.CreateScope` was called twice and `IDailyContentOrchestrator` was resolved from the scoped provider each time. This verifies the processor does not cache scoped dependencies.

5. **Processor creates a failed AutomationRun record when orchestrator throws** -- Arrange: Mock `IDailyContentOrchestrator.ExecuteAsync` to throw an exception. Call the internal method. Assert an `AutomationRun` with `Status == Failed` and populated `ErrorDetails` was saved to the database.

6. **Processor continues to next scheduled occurrence after an error (does not crash)** -- Call the internal method when the orchestrator throws. Assert no exception propagates from the internal method (it catches and logs). The outer `ExecuteAsync` loop should not be disrupted.

7. **Cron expression parsing handles invalid expressions gracefully (logs error, does not start)** -- Configure `ContentAutomationOptions` with an invalid cron string like `"invalid_cron"`. Call the `StartAsync` or `ExecuteAsync` method. Assert a log error is written and the service exits its loop without crashing.

8. **TimeZone string maps correctly to TimeZoneInfo (handles "Eastern Standard Time")** -- Test the internal timezone resolution logic. Pass `"Eastern Standard Time"` and assert it resolves to a valid `TimeZoneInfo`. Also test with an invalid timezone string and assert graceful failure with logging.

9. **Processor passes configured options to orchestrator** -- Call the internal method with specific `ContentAutomationOptions` values. Capture the `ContentAutomationOptions` argument passed to `IDailyContentOrchestrator.ExecuteAsync`. Assert the options match what was configured.

10. **Idempotency check uses the configured timezone for "today" comparison** -- Set the configured timezone to `"Eastern Standard Time"`. Set the `IDateTimeProvider.UtcNow` to a time that is one date in UTC but a different date in Eastern (e.g., 2:00 AM UTC on Jan 15 = 9:00 PM ET on Jan 14). Insert a `Completed` run for Jan 14 in ET. Assert that the processor skips because it sees the run as "today" in ET.

**Integration Test:**

11. **Processor calls ExecuteAsync on the orchestrator when schedule fires (short interval)** -- Uses a cron expression that fires every second (`"* * * * * *"` -- requires Cronos with seconds support, or a very near-future occurrence). Register a mock orchestrator. Start the `DailyContentProcessor` as a hosted service. Wait briefly. Assert the orchestrator was called. Use `CancellationTokenSource` with a short timeout to stop the service cleanly.

### Test Setup Pattern

The tests should follow the existing `ScheduledPublishProcessor` testing approach. Since `ScheduledPublishProcessor` tests the `internal static` helper method directly, and `CalendarSlotProcessor` exposes `internal async Task ProcessAsync`, the `DailyContentProcessor` should similarly expose its per-tick logic as `internal async Task ProcessScheduledRunAsync(CancellationToken ct)`.

The test class needs:
- `Mock<IServiceScopeFactory>` and `Mock<IServiceScope>` -- to simulate scoped DI resolution
- `Mock<IDailyContentOrchestrator>` -- returned from the scoped service provider
- `Mock<IApplicationDbContext>` with a `MockQueryable` setup for `DbSet<AutomationRun>` -- for the idempotency check
- `Mock<IDateTimeProvider>` -- to control "now"
- `Mock<ILogger<DailyContentProcessor>>` -- for log verification
- `IOptions<ContentAutomationOptions>` -- built from `Options.Create(new ContentAutomationOptions { ... })`

The `InternalsVisibleTo` attribute must be set on the Infrastructure project (it already exists for the test project based on existing patterns like `ScheduledPublishProcessor` with `internal` methods).

---

## Implementation Details

### File to Create: `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/DailyContentProcessor.cs`

This is the only production file in this section. The `DailyContentProcessor` is a `BackgroundService` that:

1. Parses the configured cron expression on startup
2. Calculates the next occurrence in the configured timezone
3. Delays until that time
4. Performs an idempotency check
5. Resolves a scoped orchestrator and runs it
6. Loops back to step 2

#### Class Structure

```csharp
namespace PersonalBrandAssistant.Infrastructure.BackgroundJobs;

/// <summary>
/// Cron-scheduled BackgroundService that triggers the daily content automation pipeline.
/// Uses Cronos for timezone-aware cron scheduling instead of PeriodicTimer.
/// </summary>
public class DailyContentProcessor : BackgroundService
{
    // Constructor dependencies:
    //   IServiceScopeFactory _scopeFactory
    //   IDateTimeProvider _dateTimeProvider
    //   IOptions<ContentAutomationOptions> _options
    //   ILogger<DailyContentProcessor> _logger
}
```

#### Constructor

Inject:
- `IServiceScopeFactory` -- for creating scoped service lifetimes per execution
- `IDateTimeProvider` -- for testable time (existing PBA pattern, used by all background jobs)
- `IOptions<ContentAutomationOptions>` -- the automation configuration
- `ILogger<DailyContentProcessor>` -- structured logging

#### `ExecuteAsync` (the scheduling loop)

```
1. If options.Enabled is false, log informational message and return immediately
2. Try to parse options.CronExpression via Cronos.CronExpression.Parse()
   - On FormatException: log error with the invalid expression, return (do not crash the host)
3. Try to resolve TimeZoneInfo from options.TimeZone via TimeZoneInfo.FindSystemTimeZoneById()
   - On TimeZoneNotFoundException: log error, return
4. Enter loop:
   a. Calculate next occurrence: cronExpression.GetNextOccurrence(DateTimeOffset.UtcNow, timeZone)
   b. If next is null (no more occurrences possible -- theoretically impossible with standard cron): break
   c. Calculate delay: next.Value - _dateTimeProvider.UtcNow
   d. If delay > TimeSpan.Zero: await Task.Delay(delay, stoppingToken)
   e. Call ProcessScheduledRunAsync(stoppingToken)
   f. Catch OperationCanceledException when stoppingToken requested: break
   g. Catch all other exceptions: log error, continue loop (resilient to transient failures)
```

The key difference from existing PBA background jobs (which use `PeriodicTimer`) is that this service calculates delay dynamically based on cron expression evaluation. This enables schedule patterns like "9AM weekdays only" which `PeriodicTimer` cannot express.

#### `ProcessScheduledRunAsync` (internal, the per-tick logic)

This is the testable method that runs on each scheduled tick.

```
1. Resolve TimeZoneInfo from options.TimeZone
2. Determine "today" in the configured timezone:
   - Convert _dateTimeProvider.UtcNow to the target timezone
   - Extract the date component (DateOnly or start/end of day as DateTimeOffset)
3. Idempotency check:
   - Create a scope from _scopeFactory
   - Resolve IApplicationDbContext
   - Query AutomationRuns where:
     - Status == AutomationRunStatus.Completed OR Status == AutomationRunStatus.Running
     - TriggeredAt falls within "today" in the configured timezone
   - If any matching run exists: log "Skipping - run already exists for today" and return
4. Resolve IDailyContentOrchestrator from the same scope
5. Try:
   - Call orchestrator.ExecuteAsync(_options, stoppingToken)
   - Log success with run result details
6. Catch Exception:
   - Create a failed AutomationRun: AutomationRun.Create(), then .Fail(ex.Message, 0)
   - Save to database via context.AutomationRuns.Add() + context.SaveChangesAsync()
   - Log error (do NOT rethrow -- the loop must continue)
```

The "today" determination is timezone-aware. For example, at 2:00 AM UTC on January 15th, the Eastern time is 9:00 PM on January 14th. If a completed run exists for January 14th ET, and the cron fires at 2:00 PM UTC (9:00 AM ET on Jan 15th), the idempotency check must compare against Jan 15th ET, not Jan 15th UTC.

The query for "today in timezone" calculates the start-of-day and end-of-day boundaries in UTC:
```csharp
var nowInTz = TimeZoneInfo.ConvertTime(_dateTimeProvider.UtcNow, timeZone);
var todayStart = new DateTimeOffset(nowInTz.Date, timeZone.GetUtcOffset(nowInTz.Date));
var todayEnd = todayStart.AddDays(1);

var existingRun = await context.AutomationRuns
    .AnyAsync(r =>
        (r.Status == AutomationRunStatus.Completed || r.Status == AutomationRunStatus.Running)
        && r.TriggeredAt >= todayStart.UtcDateTime
        && r.TriggeredAt < todayEnd.UtcDateTime,
        ct);
```

#### Error Handling

Following the plan: on any error during orchestration, log it, create a `Failed` `AutomationRun` record, and continue to the next scheduled occurrence. No retry within the same day -- the next day's run will pick up fresh trends.

This matches the existing resilience pattern in `ScheduledPublishProcessor` and `CalendarSlotProcessor` where the `try/catch` inside the loop prevents a single failure from killing the background service.

#### Cron Parsing with Cronos

```csharp
var cronExpression = Cronos.CronExpression.Parse(options.CronExpression);
```

Cronos supports standard five-field cron expressions by default (`minute hour day month weekday`). The default `"0 9 * * 1-5"` means "at 9:00 AM, Monday through Friday."

`GetNextOccurrence` accepts a `TimeZoneInfo` to correctly handle DST transitions. When clocks spring forward at 2:00 AM, a 2:30 AM scheduled job is skipped (not fired twice or at the wrong time). When clocks fall back, the job fires once at the first occurrence.

### DI Registration Update

**File to modify:** `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs`

Section 01 (foundation) registered a stub `DailyContentProcessor`. This section replaces it with the real implementation. Since `AddHostedService<T>` with the same type just overwrites the existing registration, no code change is needed in `DependencyInjection.cs` if the stub was registered as `DailyContentProcessor` -- the real class file simply replaces the stub class.

If section 01 used a separate stub class name (e.g., `DailyContentProcessorStub`), then update the registration line from:
```csharp
services.AddHostedService<DailyContentProcessorStub>();
```
to:
```csharp
services.AddHostedService<DailyContentProcessor>();
```

Based on section-01-foundation's approach (stub file `NotImplementedStubs.cs` with a minimal `BackgroundService` that does nothing), the real `DailyContentProcessor.cs` replaces the stub behavior. The DI registration line `services.AddHostedService<DailyContentProcessor>()` should already point to the correct class name. Delete or empty out the stub entry for `DailyContentProcessor` in `NotImplementedStubs.cs`.

### Configuration Reference

The processor reads from `ContentAutomationOptions` (defined in section-01):

| Property | Type | Default | Used By Processor |
|----------|------|---------|-------------------|
| `CronExpression` | string | `"0 9 * * 1-5"` | Cron schedule parsing |
| `TimeZone` | string | `"Eastern Standard Time"` | Timezone for occurrence calculation and idempotency date check |
| `Enabled` | bool | `true` | Early exit if false |
| All other properties | various | various | Passed through to orchestrator |

### NuGet Dependency

The `Cronos` package was added in section-01-foundation. No additional packages are needed for this section.

---

## File Summary

### New Files

| File | Layer | Purpose |
|------|-------|---------|
| `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/DailyContentProcessor.cs` | Infrastructure | Cron-scheduled BackgroundService for daily pipeline |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/DailyContentProcessorTests.cs` | Tests | Unit + integration tests for scheduling and idempotency |

### Modified Files

| File | Change |
|------|--------|
| `src/PersonalBrandAssistant.Infrastructure/Services/ContentAutomation/NotImplementedStubs.cs` | Remove or hollow out the `DailyContentProcessor` stub (replaced by real implementation) |

---

## Key Design Decisions

1. **Cronos over PeriodicTimer** -- PeriodicTimer (used by all other PBA background jobs) only supports fixed intervals. The daily content pipeline needs weekday-only cron scheduling with timezone awareness. Cronos handles DST correctly and is battle-tested (from the HangfireIO ecosystem).

2. **Task.Delay instead of PeriodicTimer** -- The scheduling loop calculates `next occurrence - now` and uses `Task.Delay` for that duration. This is more efficient than a short-interval PeriodicTimer that polls "is it time yet?" every few seconds.

3. **Internal method for testability** -- Following the existing `ScheduledPublishProcessor.ProcessDueContentAsync` and `CalendarSlotProcessor.ProcessAsync` patterns, the per-tick logic lives in `ProcessScheduledRunAsync` marked `internal` so tests can call it directly without running the infinite loop.

4. **Timezone-aware idempotency** -- The "already ran today" check uses the configured timezone, not UTC. This prevents edge cases where UTC date boundaries cause double runs or missed runs for users in non-UTC timezones.

5. **Failed run recording on error** -- When the orchestrator throws, the processor creates a `Failed` `AutomationRun` record. This is important for the circuit breaker (section-08) which counts consecutive failures, and for the API endpoints (section-10) which display run history.

6. **No retry within the same day** -- Per the plan, if a run fails, the next attempt is the following scheduled day. The rationale is that trends are time-sensitive and a stale retry hours later produces lower-quality content.

---

## Verification

After implementation, run:

```bash
cd C:/Users/kruz7/OneDrive/Documents/Code\ Repos/MCKRUZ/personal-brand-assistant
dotnet build
dotnet test --filter "FullyQualifiedName~DailyContentProcessor"
```

All tests should pass. The `DailyContentProcessor` should be resolvable as an `IHostedService` from DI (verifiable via the existing DI registration test from section-01 if it references the final class name).