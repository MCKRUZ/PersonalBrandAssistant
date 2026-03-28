# Section 01 — Code Review Interview Transcript

## Review Triage

### Asked User (1 item)
1. **Missing TrendItem → TrendSource FK** — No referential integrity between TrendItem and TrendSource. User chose: **Add TrendSourceId FK** with SetNull delete behavior.

### Auto-Fixed (4 items)
1. **Unfiltered unique index on nullable DeduplicationKey** — Added `.HasFilter("\"DeduplicationKey\" IS NOT NULL")` to prevent NULL uniqueness violations.
2. **Redundant single-column ScheduledAt index** — Removed since the composite `(ScheduledAt, Platform)` index already covers ScheduledAt-only queries.
3. **Misleading test name** — Renamed `TrendItem_DeduplicationKey_IsDeterministic` to `TrendItem_DeduplicationKey_StoresValue` (test only stores/retrieves, doesn't verify determinism).
4. **Missing IsActive assertion** — Added `Assert.False(series.IsActive)` to `ContentSeries_DefaultValues_AreCorrect` test.

### Let Go (0 items)
No items dismissed.

## Changes Applied

| # | Fix | Files Modified |
|---|-----|---------------|
| 1 | Added `Guid? TrendSourceId` to TrendItem entity | `TrendItem.cs`, `TrendItemConfiguration.cs` |
| 2 | Added filter to DeduplicationKey unique index | `TrendItemConfiguration.cs` |
| 3 | Removed redundant ScheduledAt index | `CalendarSlotConfiguration.cs` |
| 4 | Renamed misleading test | `TrendItemTests.cs` |
| 5 | Added IsActive assertion | `ContentSeriesTests.cs` |
| 6 | Updated config test for composite index | `ApplicationDbContextConfigurationTests.cs` |
| 7 | Added TrendSourceId FK config test | `ApplicationDbContextConfigurationTests.cs` |
| 8 | Added TrendSourceId nullable entity test | `TrendItemTests.cs` |

## Verification
- Build: **Pass** (0 warnings, 0 errors)
- Tests: **668 passed** (158 Domain + 106 Application + 404 Infrastructure)
