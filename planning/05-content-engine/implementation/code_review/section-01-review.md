# Code Review: Section 01 - Domain Entities

## Critical Issues
1. TrendItem has no FK to TrendSource - missing referential integrity
2. Unique index on nullable DeduplicationKey needs filter

## Warnings
3. Redundant single-column ScheduledAt index on CalendarSlot (composite covers it)
4. ContentSeries.IsActive no default in EF config (C# default false aligns with PG)
5. No navigation properties on CalendarSlot (follows existing pattern)

## Suggestions
- Rename TrendItem_DeduplicationKey_IsDeterministic test
- Add IsActive assertion to ContentSeries default test
- EngagementSnapshot could use EntityBase instead of AuditableEntityBase
