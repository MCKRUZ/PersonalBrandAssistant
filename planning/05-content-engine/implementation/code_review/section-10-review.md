# Code Review: Section 10 -- Background Processors

**Reviewer:** code-reviewer agent
**Date:** 2026-03-16
**Verdict:** WARNING -- 0 CRITICAL, 2 HIGH, 5 MEDIUM, 3 LOW issues found

---

## HIGH Issues

### [HIGH-01] RepurposeOnPublishProcessor._lastProcessedAt is mutable instance state with no restart persistence

**File:** RepurposeOnPublishProcessor.cs, line 8 (field declaration)

The `_lastProcessedAt` field is initialized to `_dateTimeProvider.UtcNow` at construction time and updated after each sweep. If the host restarts (container bounce, deployment, crash), this value resets to "now" and all content published during the downtime window is silently skipped -- never repurposed.

This is a correctness issue for any deployment scenario where the process is not permanently running, which includes virtually every production environment.

**Fix:** Persist the last-processed watermark to the database. Add a `ProcessorWatermark` entity or a simple key-value setting row:

```csharp
// Inside ProcessAsync, after the sweep:
var watermark = await context.ProcessorWatermarks
    .FirstOrDefaultAsync(w => w.ProcessorName == "RepurposeOnPublish", ct);
if (watermark is null)
{
    watermark = new ProcessorWatermark { ProcessorName = "RepurposeOnPublish" };
    context.ProcessorWatermarks.Add(watermark);
}
watermark.LastProcessedAt = now;
await context.SaveChangesAsync(ct);
```

Alternatively, query by a `RepurposedAt` flag on the content entity itself so the processor is inherently idempotent without needing watermark state.

---

### [HIGH-02] CalendarSlotProcessor.GetOccurrences uses .DateTime on DateTimeOffset, discarding offset information

**File:** CalendarSlotProcessor.cs, line 153

```csharp
DtStart = new CalDateTime(series.StartsAt.DateTime, series.TimeZoneId),
```

`DateTimeOffset.DateTime` returns a `DateTime` with `DateTimeKind.Unspecified`, silently stripping the offset. If `series.StartsAt` was stored as UTC (offset +00:00), this works by coincidence. But if it carries any other offset (e.g., the user local time when the series was created), the resulting `CalDateTime` will be interpreted in `series.TimeZoneId` with the wrong base time, producing incorrect occurrence timestamps.

The same issue appears on line 159:

```csharp
var fromCal = new CalDateTime(from.UtcDateTime);
```

Here `.UtcDateTime` is used instead of `.DateTime`, which is inconsistent -- one path uses local DateTime, the other uses UTC DateTime.

**Fix:** Be explicit about UTC throughout:

```csharp
DtStart = new CalDateTime(series.StartsAt.UtcDateTime, "UTC"),
```

Then convert each occurrence back from UTC to the series timezone if needed for display. Or, if the intent is to generate occurrences in the series timezone, convert `StartsAt` to that timezone first using `TimeZoneInfo`:

```csharp
var tz = TimeZoneInfo.FindSystemTimeZoneById(series.TimeZoneId);
var localStart = TimeZoneInfo.ConvertTimeFromUtc(series.StartsAt.UtcDateTime, tz);
DtStart = new CalDateTime(localStart, series.TimeZoneId),
```

---

## MEDIUM Issues

### [MEDIUM-01] CalendarSlotProcessor deduplication assumes single-instance execution

**File:** CalendarSlotProcessor.cs, lines 92-128

The processor queries existing slots per series (lines 92-97), then adds new slots to the context (line 107). `SaveChangesAsync` is called once after iterating ALL series (line 126). The deduplication check would not see not-yet-saved slots from a concurrent tick if the processor were ever scaled out.

The current single-instance `BackgroundService` model prevents true concurrency, so this is acceptable today but fragile.

**Fix (document the assumption):** Add a comment noting the single-instance assumption. For future-proofing, add a unique index on `(ContentSeriesId, ScheduledAt, Platform)` at the DB level if one does not already exist in the Section 01 entity configuration.

---

### [MEDIUM-02] CalendarSlotProcessor auto-fill check uses GlobalLevel only, ignoring per-ContentType overrides

**File:** CalendarSlotProcessor.cs, lines 131-146

```csharp
if (autonomyConfig.GlobalLevel == AutonomyLevel.Autonomous)
```

This checks `GlobalLevel` directly instead of calling `autonomyConfig.ResolveLevel(contentType, platform)` as the `RepurposeOnPublishProcessor` does (line 361). The inconsistency is confusing. Auto-fill spans multiple content types, so there is no single content type to resolve against -- but this should be documented as a design decision rather than left ambiguous.

**Fix A (conservative):** Keep the GlobalLevel check and add a comment explaining why ResolveLevel is not used here.

**Fix B (granular):** Let `AutoFillSlotsAsync` internally check autonomy per slot before filling each one.

---

### [MEDIUM-03] EngagementAggregationProcessor does not implement rate limiting as specified in the plan

**File:** EngagementAggregationProcessor.cs, ProcessAsync method

The section plan specifies: "Call `IRateLimiter.CanMakeRequestAsync(entry.Platform, "engagement", ct)`. If rate-limited, skip and log." The implementation does not resolve `IRateLimiter` at all. It relies entirely on `IEngagementAggregator.FetchLatestAsync` returning a failure result with `ErrorCode.RateLimited`.

While this works if the aggregator internally checks rate limits, the processor makes the API call regardless and only learns about rate limiting after the fact -- potentially wasting API quota.

**Fix:** Either:
1. Add `IRateLimiter` resolution and pre-check before each `FetchLatestAsync` call (matches plan), or
2. Document that rate limiting is delegated to the aggregator and update the plan accordingly.

---

### [MEDIUM-04] Missing TrendAggregationProcessor tests in this diff

**File:** (absent)

The section plan specifies `TrendAggregationProcessorTests.cs` with 6 tests. The diff includes only 3 of 4 test files. If the processor and its tests were implemented in a prior section (Section 08 -- Trend Monitoring), this should be cross-referenced. Otherwise, this is a coverage gap.

---

### [MEDIUM-05] RepurposeOnPublishProcessor.GetSeriesPlatforms makes two sequential DB queries that could be one

**File:** RepurposeOnPublishProcessor.cs, lines 103-117

```csharp
var seriesSlot = await context.CalendarSlots
    .Where(s => s.ContentId == contentId && s.ContentSeriesId != null)
    .FirstOrDefaultAsync(ct);

// ...

var series = await context.ContentSeries
    .FirstOrDefaultAsync(s => s.Id == seriesSlot.ContentSeriesId, ct);
```

Two round-trips to the DB for what could be a single join. In a loop over N content items without explicit target platforms, this is 2N queries.

**Fix:** Combine into a single query:

```csharp
var platforms = await context.CalendarSlots
    .Where(s => s.ContentId == contentId && s.ContentSeriesId != null)
    .Join(context.ContentSeries,
        slot => slot.ContentSeriesId,
        series => series.Id,
        (slot, series) => series.TargetPlatforms)
    .FirstOrDefaultAsync(ct);

return platforms ?? [];
```

---

## LOW Issues

### [LOW-01] CalendarSlotProcessorTests.ProcessAsync_ExistingSlots_NoDuplicates does not actually assert deduplication

**File:** CalendarSlotProcessorTests.cs, lines 510-546

The test sets up a series with `FREQ=DAILR�COUNT=1` starting 30 days ago and 7 existing slots. It asserts no exception was thrown (`Assert.Null(ex)`) but never verifies that `CalendarSlots.Add` was not called or that `SaveChangesAsync` was skipped.

Worse, `COUNT=1` means only one occurrence is ever generated (30 days ago, which is before `_now` and outside the materialization window). The test passes because there are zero occurrences to process -- not because deduplication worked.

**Fix:** Use `FREQ=DAILSX no COUNT) so occurrences fall within the window, and add verifications:

```csharp
_dbContext.Verify(d => d.CalendarSlots.Add(It.IsAny<CalendarSlot>()), Times.Never);
_dbContext.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
```

---

### [LOW-02] EngagementAggregationProcessorTests plan vs implementation test name mismatch

**File:** EngagementAggregationProcessorTests.cs

The plan specifies `ProcessAsync_RetentionCleanup_RemovesOldSnapshots` but the implementation has `ProcessAsync_RetentionCleanup_CalledAfterFetch`. The implemented test only verifies `CleanupSnapshotsAsync` was called -- not that old snapshots are removed. The removal logic lives in the aggregator, so testing it here as a unit test is appropriate, but the name should match the plan or the plan should be updated.

---

### [LOW-03] Inconsistent timer interval patterns -- some hardcoded, some from options

**File:** RepurposeOnPublishProcessor.cs line 22, CalendarSlotProcessor.cs line 52

`RepurposeOnPublishProcessor` uses `TimeSpan.FromSeconds(60)` (hardcoded), `CalendarSlotProcessor` uses `TimeSpan.FromMinutes(15)` (hardcoded), but `EgagementAggregationProcessor` reads from `_options.EngagementAggregationIntervalHours`. The plan says the repurpose interval is "configurable via ContentEngineOptions" but it is not.

**Fix:** Add `RepurposeIntervalSeconds` and `SlotMaterializationIntervalMinutes` to `ContentEngineOptions` and read from options, matching the `EngagementAggregationProcessor` pattern.

---

## Completeness vs Section Plan

| Plan Requirement | Status | Notes |
|---|---|---|
| RepurposeOnPublishProcessor | DONE | HIGH-01: volatile watermark |
| CalendarSlotProcessor | DONE | HIGH-02: DateTime/offset issue |
| EngagementAggregationProcessor | DONE | MEDIUM-03: no IRateLimiter |
| TrendAggregationProcessor | NOT IN DIFF | May be in a prior section |
| RepurposeOnPublishProcessorTests (5 tests) | DONE (5 tests) | All plan scenarios covered |
| CalendarSlotProcessorTests (4 tests) | DONE (4 tests) | LOW-01: weak dedup assertion |
| EngagementAggregationProcessorTests (5 tests) | DONE (5 tests) | LOW-02: name mismatch |
| TrendAggregationProcessorTests (6 tests) | MISSING | MEDIUM-04 |
| ContentEngineOptions.SlotMaterializationDays | DONE | |

---

## Pattern Consistency

The three processors follow the established `BackgroundService` pattern well:
- PeriodicTimer loop with cancellation support
- IServiceScopeFactory for scoped DI
- Structured logging with contextual parameters
- Per-item error isolation with try/catch
- Internal processing methods for testability

One positive deviation from `ScheduledPublishProcessor`: the existing processor resolves `ApplicationDbContext` (concrete), while the new processors resolve `IApplicationDbContext` (interface). The interface approach is better for testability and should be the pattern going forward.

---

## Thread Safety

The `BackgroundService` base class ensures `ExecuteAsync` runs on a single long-lived task. `PeriodicTimer.WaitForNextTickAsync` serializes ticks, so there is no concurrent execution within a single processor instance. The main concern is `_lastProcessedAt` in `RepurposeOnPublishProcessor`, which is read and written only within the serialized loop -- no race condition in the current design. This would become a problem if the processor were ever multi-instanced, which is addressed by HIGH-01.

---

## Summary

The implementation is structurally solid and follows established patterns closely. The two HIGH issues are the most important:

1. **Volatile watermark** in `RepurposeOnPublishProcessor` means content published during process restarts will never be repurposed. This is a data loss scenario.
2. **DateTime/offset confusion** in `CalendarSlotProcessor.GetOccurrences` could produce slots at incorrect times depending on how `StartsAt` is stored and what timezone the series uses.

The MEDIUM issues are plan deviations (missing rate limiter, missing trend tests) and minor performance concerns. None are blocking but should be tracked.

Overall code quality is good -- clean separation of concerns, proper error handling, consistent logging, and reasonable test coverage for the implemented processors. Fix the HIGH issues and this section is ready to merge.
