# Section 10: Background Processors

## Overview

This section implements three `BackgroundService` classes that drive the Content Engine's automated processing loops. Each processor follows the pattern established by `ScheduledPublishProcessor` and `RetentionCleanupService`: `PeriodicTimer`-based loop, `IServiceScopeFactory` for scoped DI, structured logging, cancellation support, and error isolation.

**Note:** TrendAggregationProcessor was already implemented in Section 08 (Trend Monitoring) and is not duplicated here.

## Files Created

- `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/RepurposeOnPublishProcessor.cs`
- `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/CalendarSlotProcessor.cs`
- `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/EngagementAggregationProcessor.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/RepurposeOnPublishProcessorTests.cs` (5 tests)
- `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/CalendarSlotProcessorTests.cs` (4 tests)
- `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/EngagementAggregationProcessorTests.cs` (5 tests)

## Files Modified

- `src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs` — Added `SlotMaterializationDays = 7`

## Deviations from Original Plan

1. **TrendAggregationProcessor not included:** Already implemented in Section 08, including its tests.
2. **IApplicationDbContext instead of ApplicationDbContext:** All 3 processors resolve `IApplicationDbContext` (interface) instead of the concrete `ApplicationDbContext`. This improves testability and matches newer test patterns (PublishCompletionPoller, TokenRefreshProcessor, PlatformHealthMonitor).
3. **Fixed lookback window instead of volatile watermark:** RepurposeOnPublishProcessor uses a 2-hour fixed lookback window instead of tracking `_lastProcessedAt` in memory. This survives process restarts. Safe because RepurposingService is idempotent (returns Conflict for duplicates).
4. **Rate limiting delegated to EngagementAggregator:** The processor does not resolve `IRateLimiter` directly. Rate limit handling is inside `EngagementAggregator.FetchLatestAsync`, which returns `ErrorCode.RateLimited` on failure.
5. **GetSeriesPlatforms uses single Join query:** Combined the original 2-query pattern into a single LINQ Join for efficiency.
6. **CalendarSlotProcessor auto-fill uses GlobalLevel:** Uses `autonomyConfig.GlobalLevel` instead of `ResolveLevel(contentType, platform)` because auto-fill spans multiple content types with no single type to resolve against.

## Test Coverage

14 tests across 3 test files, all passing:

| Test Class | Tests | Coverage |
|-----------|-------|----------|
| RepurposeOnPublishProcessorTests | 5 | Published triggers repurpose, manual skips, semi-auto works, failure continues, idempotent on conflict |
| CalendarSlotProcessorTests | 4 | Materializes slots, dedup prevents duplicates, autonomous auto-fill, manual no auto-fill |
| EngagementAggregationProcessorTests | 5 | Retention window query, per-entry fetch, rate limited skip, platform error continues, cleanup called |

## Code Review Fixes Applied

- HIGH-01: Replaced volatile `_lastProcessedAt` watermark with fixed 2-hour lookback window
- MEDIUM-02: Added comment explaining GlobalLevel usage in CalendarSlotProcessor
- MEDIUM-05: Combined 2N query in GetSeriesPlatforms into single Join
- LOW-01: Fixed dedup test to actually test deduplication (correct RRULE, full slot coverage, proper assertion)
