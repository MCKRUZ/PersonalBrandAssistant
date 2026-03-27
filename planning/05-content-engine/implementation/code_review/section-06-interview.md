# Section 06: Content Calendar — Code Review Interview

**Date:** 2026-03-16

## Triage Summary

| Finding | Action | Rationale |
|---------|--------|-----------|
| M1 | Auto-fix | Silent exception — added structured logging |
| M2 | Let go | Timezone validation changes CreateSeries contract; defer |
| M3 | Let go | Callers control the range; defensive but premature |
| M4 | Let go | Over-constraining for an internal service |
| M5 | Asked user → Fix now | Subquery for scalability |
| M6 | Let go | Needs entity change (concurrency token); separate concern |
| M7 | Auto-fix | Extracted magic number to named constant |
| M8 | Let go | Feature enhancement, not a bug |
| M9 | Let go | By-design for calendar views |
| L1-L6 | Let go | Low priority, cosmetic, or future work |

## Interview

### M5: Unbounded memory in AutoFillSlotsAsync
**Question:** Replace materialized assigned content ID list with NOT EXISTS subquery?
**User decision:** Fix now
**Applied:** Yes — replaced two-query pattern with single query using `!_dbContext.CalendarSlots.Any(s => s.ContentId == c.Id)`

## Auto-Fixes Applied

### M1: Silent exception in GenerateOccurrences
Changed bare `catch` to `catch (Exception ex)` with `_logger.LogWarning`. Made method non-static to access logger.

### M7: Magic number for occurrence match tolerance
Extracted `1` to `private const double OccurrenceMatchToleranceMinutes = 1.0`.

## Verification
All 712 tests pass after fixes.
