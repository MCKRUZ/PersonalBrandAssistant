# Section 08: Trend Monitoring -- Code Review Interview

**Date:** 2026-03-16

## Triage Summary

| Finding | Action | Rationale |
|---------|--------|-----------|
| H1 | Auto-fix | Relevance scores now stored in Dictionary instead of mutating Description |
| H2 | Auto-fix | DeduplicationKey assignment moved to TrendMonitor.RefreshTrendsAsync (caller) |
| H3 | Auto-fix | Removed duplicate title-similarity loop in Deduplicate second pass |
| H4 | Auto-fix | Added limit <= 0 validation in GetSuggestionsAsync |
| H5 | Auto-fix | Added Math.Max(1, ...) guard for AggregationIntervalMinutes |
| M1 | Asked user -> Extract pollers | TrendMonitor.cs split into 4 pollers + core service |
| M2 | Auto-fix | Added `using` to SemaphoreSlim in HackerNewsPoller |
| M3 | Asked user -> Fix now | Cross-cycle dedup via DeduplicationKey lookup against existing TrendItems |
| M4 | Auto-fix | TrendSourceId now set on Reddit/HN items via poller receiving TrendSource param |
| M5 | Auto-fix | Cleaner null handling with range syntax in BuildRelevanceScoringPrompt |
| M6 | Let go | ContentType.BlogPost/PlatformType.LinkedIn defaults acceptable for now |
| M7 | Let go | Autonomy gating deferred to section-10 processor registration |
| M8 | Let go | LLM batching nice-to-have, can add when needed |
| L1-L4 | Let go | Cosmetic / acceptable |

## Interview

### M1: TrendMonitor.cs file size (575 lines)
**Question:** Extract HTTP pollers into separate classes?
**User decision:** Extract pollers
**Applied:** Created TrendPollers/ directory with ITrendSourcePoller interface, TrendRadarPoller, FreshRssPoller, RedditPoller, HackerNewsPoller. TrendMonitor.cs reduced to ~310 lines.

### M3: Cross-cycle deduplication
**Question:** Add dedup against previously persisted TrendItems?
**User decision:** Fix now
**Applied:** Added DeduplicationKey lookup against existing TrendItems before insert.

## Auto-Fixes Applied

### H1: Relevance score dictionary
Replaced Description field mutation with `Dictionary<int, float>` passed between ScoreItemsAsync and ClusterAndCreateSuggestions.

### H2: DeduplicationKey in caller
Removed `item.DeduplicationKey = key` from TrendDeduplicator.Deduplicate. Key is now set in TrendMonitor.RefreshTrendsAsync after deduplication.

### H3: Duplicate loop removal
Removed redundant second title-similarity check in Deduplicate second pass. Simplified to single `.Any()` check.

### H4: Limit validation
Added `if (limit <= 0) return Failure(ValidationFailed, ...)` in GetSuggestionsAsync.

### H5: Interval guard
Changed to `Math.Max(1, _options.AggregationIntervalMinutes)` in TrendAggregationProcessor.ExecuteAsync.

### M2: SemaphoreSlim disposal
Changed `var semaphore = new SemaphoreSlim(...)` to `using var semaphore = new SemaphoreSlim(...)` in HackerNewsPoller.

### M4: TrendSourceId on all pollers
All pollers now receive `TrendSource source` parameter and set `TrendSourceId = source.Id` on created items.

### M5: Cleaner null handling
Changed `item.Description?.Substring(0, Math.Min(...))` to `item.Description?.Length > 200 ? item.Description[..200] : item.Description ?? ""`.

## Additional Changes

### ParseRelevanceScores made internal
Changed from `private static` to `internal static` to enable direct unit testing. Added 3 tests for valid JSON, markdown-fenced, and invalid input.

### ITrendSourcePoller made public
Required due to TrendMonitor (public) taking `IEnumerable<ITrendSourcePoller>` in constructor.

## Verification
All 752 tests pass after fixes.
