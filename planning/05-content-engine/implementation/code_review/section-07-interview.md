# Section 07: Brand Voice — Code Review Interview

**Date:** 2026-03-16

## Triage Summary

| Finding | Action | Rationale |
|---------|--------|-----------|
| H1 | Auto-fix | Check GenerateDraftAsync result — obvious correctness fix |
| H2 | Auto-fix | Validate score range 0-100 — data integrity |
| M1 | Let go | Prompt injection inherent to LLM usage |
| M2 | Auto-fix | Added comment documenting service locator reason |
| M3 | Auto-fix | Compiled regex via GeneratedRegex source gen |
| M4 | Asked user → Fix now | Word boundary regex for term matching |
| M5 | Auto-fix | Added ArgumentNullException.ThrowIfNull |
| M6 | Let go | 14 tests is solid, additional coverage is nice-to-have |
| L1-L4 | Let go | Cosmetic / acceptable patterns |

## Interview

### M4: Substring vs word boundary term matching
**Question:** Switch from Contains() to word boundary regex?
**User decision:** Fix now
**Applied:** Yes — used `\b` word boundary regex for both avoided and preferred term matching

## Auto-Fixes Applied

### H1: Unchecked GenerateDraftAsync result
Added check: if `!draftResult.IsSuccess`, propagate failure.

### H2: Score range validation
Added validation that all score dimensions are 0-100 after parsing LLM JSON.

### M2: Service locator comment
Added comment explaining circular dependency reason for IServiceProvider usage.

### M3: Compiled regex caching
Replaced `Regex.Replace` string patterns with `[GeneratedRegex]` source generator patterns via `partial class`.

### M5: Null guard
Added `ArgumentNullException.ThrowIfNull` for both `text` and `profile` parameters.

## Verification
All 726 tests pass after fixes (1 pre-existing flaky sidecar test intermittently fails on timing).
