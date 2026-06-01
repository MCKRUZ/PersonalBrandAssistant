# Section 02 Code Review Interview

## Triage Summary

| # | Finding | Severity | Decision |
|---|---------|----------|----------|
| 1 | Migration drift — missing Idea/IdeaSource/SavedIdea configs | warning | Auto-fix: created IdeaConfiguration.cs, IdeaSourceConfiguration.cs, SavedIdeaConfiguration.cs, regenerated migration |
| 2 | PlatformCredential uniqueness gap | warning | User approved: added filtered unique index (WHERE IsActive = true) |
| 3 | ValidationBehavior reflection caching | suggestion | Let go — works correctly, optimization can come later |
| 4 | Platform should be init setter | suggestion | Auto-fix: changed to { get; init; } |
| 5 | List<Platform> vs IReadOnlyList | suggestion | Let go — EF Core requires mutable collection for change tracking |

## Interview

### Q1: PlatformCredential uniqueness
**Question:** Add filtered unique index (WHERE IsActive = true) or defer to service layer?
**User decision:** Add filtered unique index — DB-level guarantee prevents data corruption from concurrent requests.
**Action:** Added `builder.HasIndex(c => c.Platform).IsUnique().HasFilter("\"IsActive\" = true")` to PlatformCredentialConfiguration.

## Auto-Fixes Applied

1. **Migration drift fix** — Created IdeaConfiguration.cs, IdeaSourceConfiguration.cs, SavedIdeaConfiguration.cs to preserve existing column constraints, indexes, and FK behaviors. Regenerated migration 3 times to get it clean.
2. **Platform init setter** — Changed `Platform { get; set; }` to `Platform { get; init; }` on PlatformCredential entity.
3. **Pre-existing test fixes** — Fixed ContentType.BlogPost→Blog, Idea.SourceName required property, and missing ValidationBehavior (20+ test files).

## Verification
- All 437 tests pass (0 failures)
- Migration generates cleanly with no unintended schema drift
- All 4 source projects build with 0 errors
