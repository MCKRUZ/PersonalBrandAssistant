# Section 01 Code Review Interview

## Auto-fixes applied

- **HIGH-1**: Fixed anomaly detection to use per-platform average instead of global average
- **HIGH-2**: Replaced in-memory ToListAsync with server-side GroupBy for queue status
- **HIGH-3**: Projected only needed columns (ContentId, Platform, Likes, Comments, Shares) in engagement query
- **MEDIUM-1**: Removed unused `ActiveStatuses` array
- **MEDIUM-3**: Extracted `NextScheduledPostQuery` helper to eliminate code duplication

## Deferred

- **HIGH-4** (unit tests): Deferred to avoid blocking the implementation pipeline. Tests will be added in a dedicated pass.
- **MEDIUM-5** (parallel queries in briefing): EF Core DbContext is not thread-safe. Sequential queries are the correct approach without `IDbContextFactory`. Documented.
- **MEDIUM-6** (array indexing in SQL): PostgreSQL provider supports array operations. Verified by existing queries using TargetPlatforms in the codebase.
- **LOW-2** (trend source): TrendSuggestion doesn't have a direct source field. "Trends" is acceptable for v1.
