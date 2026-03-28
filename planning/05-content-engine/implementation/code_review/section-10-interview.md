# Code Review Interview: Section 10 — Background Processors

## Triage Summary

| Issue | Decision | Action |
|-------|----------|--------|
| HIGH-01: Volatile watermark | **Asked user** | Fixed: replaced with 2-hour lookback window |
| HIGH-02: DateTime/offset in GetOccurrences | **Let go** | Matches existing ContentCalendarService pattern exactly |
| MEDIUM-01: Single-instance dedup assumption | **Let go** | Acceptable for BackgroundService model |
| MEDIUM-02: GlobalLevel vs ResolveLevel | **Auto-fix** | Added comment explaining design decision |
| MEDIUM-03: Missing IRateLimiter | **Let go** | Rate limiting delegated to EngagementAggregator |
| MEDIUM-04: Missing TrendAggregation tests | **Let go** | Already in section 08 |
| MEDIUM-05: 2N query in GetSeriesPlatforms | **Auto-fix** | Combined into single Join query |
| LOW-01: Weak dedup test | **Auto-fix** | Fixed RRULE, slot count, and assertions |
| LOW-02: Test name mismatch | **Let go** | Current name accurately describes what's tested |
| LOW-03: Hardcoded timer intervals | **Let go** | Defer to section 12 (DI/config) |

## User Decisions

### HIGH-01: Volatile watermark → Fixed lookback window
**User chose:** Fixed 2-hour lookback window instead of DB-persisted watermark.
**Rationale:** RepurposingService is idempotent (returns Conflict for duplicates), so re-processing within the window is safe. Simpler than adding a new DB entity.

## Auto-fixes Applied

1. **MEDIUM-02:** Added comment to CalendarSlotProcessor explaining why GlobalLevel is used instead of ResolveLevel
2. **MEDIUM-05:** Combined 2-query GetSeriesPlatforms into single Join query
3. **LOW-01:** Fixed dedup test: changed RRULE to `FREQ=DAILY` (no COUNT), started series 1 day ago, expanded slots to cover full window, added `SaveChangesAsync Times.Never` assertion
