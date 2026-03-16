# Section 01: Domain Entities

> **Status:** Implemented
> **Deviations from plan:** See [Deviations](#deviations-from-plan) below.

## Overview

This section covers all new domain entities, enums, modifications to existing entities, EF Core configurations, and updates to `IApplicationDbContext` / `ApplicationDbContext` for the Content Engine phase. This is the foundation layer -- every other section in Phase 05 depends on it.

**No dependencies on other sections.** This section was implemented first and in isolation.

---

## Tests First

All tests follow the existing project conventions: xUnit, AAA pattern, test class per entity/concept. Test naming: `{Method}_{Scenario}_{Expected}`.

### New Enum Tests

**File:** `tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs` (modify existing file)

Add tests to the existing `EnumTests` class for the three new enums:

- `TrendSourceType_HasExactly4Values` -- asserts TrendRadar, FreshRSS, Reddit, HackerNews
- `TrendSuggestionStatus_HasExactly3Values` -- asserts Pending, Accepted, Dismissed
- `CalendarSlotStatus_HasExactly4Values` -- asserts Open, Filled, Published, Skipped

### New Entity Tests

**File:** `tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentSeriesTests.cs`

- `ContentSeries_Inherits_AuditableEntityBase` -- verify Id, CreatedAt, UpdatedAt exist
- `ContentSeries_DefaultValues_AreCorrect` -- IsActive defaults appropriately, TargetPlatforms/ThemeTags initialize as empty collections
- `ContentSeries_StoresRecurrenceRule` -- set and retrieve an RRULE string like `FREQ=WEEKLY;BYDAY=TU;BYHOUR=9;BYMINUTE=0`

**File:** `tests/PersonalBrandAssistant.Domain.Tests/Entities/CalendarSlotTests.cs`

- `CalendarSlot_DefaultStatus_IsOpen` -- Status defaults to `CalendarSlotStatus.Open`
- `CalendarSlot_WithOverride_StoresOverriddenOccurrence` -- when `IsOverride=true`, `OverriddenOccurrence` holds the original timestamp
- `CalendarSlot_ContentId_IsNullable` -- ContentId can be null (unfilled slot)

**File:** `tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendSourceTests.cs`

- `TrendSource_RequiredFields_AreSet` -- Name, Type, PollIntervalMinutes set correctly
- `TrendSource_IsEnabled_DefaultsToTrue` -- or whatever default is chosen

**File:** `tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendItemTests.cs`

- `TrendItem_DeduplicationKey_StoresValue` -- stores and retrieves dedup key
- `TrendItem_TrendSourceId_IsNullable` -- TrendSourceId can be null
- `TrendItem_StoresAllFields` -- Title, Description, Url, SourceName, SourceType, DetectedAt

**File:** `tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendSuggestionTests.cs`

- `TrendSuggestion_DefaultStatus_IsPending` -- Status defaults to `TrendSuggestionStatus.Pending`
- `TrendSuggestion_StoresRelevanceScore` -- RelevanceScore between 0-1
- `TrendSuggestion_RelatedTrends_IsInitialized` -- collection is not null

**File:** `tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendSuggestionItemTests.cs`

- `TrendSuggestionItem_MapsJoinRelationship` -- stores TrendSuggestionId, TrendItemId, SimilarityScore

**File:** `tests/PersonalBrandAssistant.Domain.Tests/Entities/EngagementSnapshotTests.cs`

- `EngagementSnapshot_Impressions_IsNullable` -- Impressions can be null
- `EngagementSnapshot_Clicks_IsNullable` -- Clicks can be null
- `EngagementSnapshot_StoresAllEngagementFields` -- Likes, Comments, Shares, FetchedAt

### Content Entity Modification Tests

**File:** `tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentTests.cs` (modify existing)

- `Content_TreeDepth_DefaultsToZero` -- new field defaults to 0
- `Content_RepurposeSourcePlatform_IsNullable` -- can be null for non-repurposed content

### EF Configuration Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs` (modify existing)

Add tests using the existing `CreateInMemoryContext()` helper pattern (creates context with `UseNpgsql("Host=localhost;Database=fake")`):

- `ContentSeries_EfConfiguration_MapsTable` -- entity type exists in model, maps to "ContentSeries" table
- `ContentSeries_EfConfiguration_MapsPlatformTypeArray` -- TargetPlatforms maps to `integer[]`
- `ContentSeries_EfConfiguration_MapsThemeTagsAsJsonb` -- ThemeTags stored as jsonb
- `CalendarSlot_EfConfiguration_HasStatusIndex` -- index on Status
- `CalendarSlot_EfConfiguration_HasScheduledAtIndex` -- index on ScheduledAt
- `CalendarSlot_EfConfiguration_HasContentForeignKey` -- FK to Content
- `CalendarSlot_EfConfiguration_HasSeriesForeignKey` -- FK to ContentSeries
- `TrendSource_EfConfiguration_MapsTable` -- entity exists
- `TrendItem_EfConfiguration_HasDeduplicationKeyIndex` -- unique index on DeduplicationKey
- `TrendSuggestion_EfConfiguration_MapsSuggestedPlatformsArray` -- integer[]
- `TrendSuggestionItem_EfConfiguration_HasCompositeKey` -- composite PK on (TrendSuggestionId, TrendItemId)
- `EngagementSnapshot_EfConfiguration_HasCompositeIndex` -- index on (ContentPlatformStatusId, FetchedAt DESC)
- `Content_EfConfiguration_HasTreeDepthColumn` -- new column exists
- `Content_EfConfiguration_HasRepurposeSourcePlatformColumn` -- new nullable column exists
- `Content_EfConfiguration_HasRepurposingUniqueConstraint` -- unique index on (ParentContentId, RepurposeSourcePlatform, ContentType) filtered to non-null ParentContentId

### Migration Test

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/MigrationTests.cs` (modify existing)

- `Migration_CreatesAllNewTables` -- verify that model has entity types for all 7 new entities (ContentSeries, CalendarSlot, TrendSource, TrendItem, TrendSuggestion, TrendSuggestionItem, EngagementSnapshot)

---

## Implementation Details

### New Enums

**File:** `src/PersonalBrandAssistant.Domain/Enums/TrendSourceType.cs`

```csharp
namespace PersonalBrandAssistant.Domain.Enums;

public enum TrendSourceType { TrendRadar, FreshRSS, Reddit, HackerNews }
```

**File:** `src/PersonalBrandAssistant.Domain/Enums/TrendSuggestionStatus.cs`

```csharp
namespace PersonalBrandAssistant.Domain.Enums;

public enum TrendSuggestionStatus { Pending, Accepted, Dismissed }
```

**File:** `src/PersonalBrandAssistant.Domain/Enums/CalendarSlotStatus.cs`

```csharp
namespace PersonalBrandAssistant.Domain.Enums;

public enum CalendarSlotStatus { Open, Filled, Published, Skipped }
```

### New Domain Entities

All entities inherit from `AuditableEntityBase` (provides `Id`, `CreatedAt`, `UpdatedAt`, `DomainEvents`). Use the same patterns as existing entities like `ContentPlatformStatus` -- public setters, no private constructor (these are data-carrying entities, not aggregate roots with state machine logic).

**File:** `src/PersonalBrandAssistant.Domain/Entities/ContentSeries.cs`

Properties:
- `string Name` -- series name, required
- `string? Description` -- optional description
- `string RecurrenceRule` -- iCalendar RRULE string (e.g., `FREQ=WEEKLY;BYDAY=TU;BYHOUR=9;BYMINUTE=0`)
- `PlatformType[] TargetPlatforms` -- which platforms, stored as `integer[]` in PostgreSQL
- `ContentType ContentType` -- what kind of content this series produces
- `List<string> ThemeTags` -- topic/theme tags for matching, default `[]`
- `string TimeZoneId` -- IANA timezone ID for RRULE interpretation (e.g., `America/Chicago`)
- `bool IsActive` -- whether the series is actively generating slots
- `DateTimeOffset StartsAt` -- when the series begins
- `DateTimeOffset? EndsAt` -- optional end date

**File:** `src/PersonalBrandAssistant.Domain/Entities/CalendarSlot.cs`

This is a **new entity** that replaces / evolves from the existing `ContentCalendarSlot`. The old entity used `DateOnly`/`TimeOnly` and a simple `RecurrencePattern` string. The new `CalendarSlot` is richer -- it supports RRULE-based series linkage, status tracking, and occurrence overrides.

Properties:
- `DateTimeOffset ScheduledAt` -- when this slot is scheduled (full timestamp, not DateOnly)
- `PlatformType Platform` -- target platform
- `Guid? ContentSeriesId` -- FK to ContentSeries, null for manual one-off slots
- `Guid? ContentId` -- FK to Content, null until content is assigned
- `CalendarSlotStatus Status` -- default `Open`
- `bool IsOverride` -- true if this slot overrides a recurring occurrence
- `DateTimeOffset? OverriddenOccurrence` -- the original occurrence timestamp being overridden

**Migration note:** The old `ContentCalendarSlot` entity and its `ContentCalendarSlots` table should be migrated to the new `CalendarSlot`. Strategy: create the new table, migrate any existing data, then drop the old table. Alternatively, if there is no production data yet, simply remove the old entity and replace it.

**File:** `src/PersonalBrandAssistant.Domain/Entities/TrendSource.cs`

Properties:
- `string Name` -- source name, required
- `TrendSourceType Type` -- enum discriminator
- `string? ApiUrl` -- endpoint URL for polling
- `int PollIntervalMinutes` -- how often to poll, default configurable
- `bool IsEnabled` -- toggle for source, default `true`

**File:** `src/PersonalBrandAssistant.Domain/Entities/TrendItem.cs`

Properties:
- `string Title` -- trend title, required
- `string? Description` -- optional summary
- `string? Url` -- source URL
- `string SourceName` -- human-readable source name
- `TrendSourceType SourceType` -- enum
- `DateTimeOffset DetectedAt` -- when the trend was first seen
- `string? DeduplicationKey` -- hash for cross-source dedup (e.g., SHA256 of canonicalized URL or normalized title)
- `Guid? TrendSourceId` -- nullable FK to TrendSource (added during code review for referential integrity)

**File:** `src/PersonalBrandAssistant.Domain/Entities/TrendSuggestion.cs`

Properties:
- `string Topic` -- suggested content topic, required
- `string Rationale` -- why this trend is relevant
- `float RelevanceScore` -- 0.0 to 1.0, from LLM scoring
- `ContentType SuggestedContentType` -- what kind of content to create
- `PlatformType[] SuggestedPlatforms` -- which platforms to target, stored as `integer[]`
- `TrendSuggestionStatus Status` -- default `Pending`
- `ICollection<TrendSuggestionItem> RelatedTrends` -- navigation to join entity, default `new List<TrendSuggestionItem>()`

**File:** `src/PersonalBrandAssistant.Domain/Entities/TrendSuggestionItem.cs`

This is a join entity linking `TrendSuggestion` to `TrendItem` with additional data.

Properties:
- `Guid TrendSuggestionId` -- FK
- `Guid TrendItemId` -- FK
- `float SimilarityScore` -- how closely this trend item relates to the suggestion

This entity should **not** inherit from `AuditableEntityBase` -- it is a lightweight join table. Use `EntityBase` or no base class, with a composite primary key on `(TrendSuggestionId, TrendItemId)`.

**File:** `src/PersonalBrandAssistant.Domain/Entities/EngagementSnapshot.cs`

Properties:
- `Guid ContentPlatformStatusId` -- FK to ContentPlatformStatus
- `int Likes`
- `int Comments`
- `int Shares`
- `int? Impressions` -- nullable, not all platforms provide this
- `int? Clicks` -- nullable, not all platforms provide this
- `DateTimeOffset FetchedAt` -- when this snapshot was taken

### Content Entity Modifications

**File:** `src/PersonalBrandAssistant.Domain/Entities/Content.cs` (modify existing)

Add two new properties:

- `int TreeDepth { get; set; }` -- depth in the repurposing tree (0 = root/original content). Default 0.
- `PlatformType? RepurposeSourcePlatform { get; set; }` -- which platform the source content was for. Null for original content.

These are simple data properties. The `Create` factory method does not need to change (new properties default to 0 and null).

### EF Core Configurations

All configurations follow the established pattern: implement `IEntityTypeConfiguration<T>`, set table name, keys, indexes, max lengths, column types, FKs, concurrency tokens, and ignore `DomainEvents`.

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentSeriesConfiguration.cs`

Key configuration points:
- Table: `"ContentSeries"`
- `Name`: required, max length 200
- `RecurrenceRule`: required, max length 500
- `TargetPlatforms`: `integer[]` column type (PostgreSQL array, same pattern as `Content.TargetPlatforms`)
- `ThemeTags`: use `JsonValueConverter<List<string>>` with `jsonb` column type
- `TimeZoneId`: required, max length 100
- `xmin` concurrency token (same pattern as all other entities)
- Ignore `DomainEvents`
- Index on `IsActive`

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/CalendarSlotConfiguration.cs`

Key configuration points:
- Table: `"CalendarSlots"`
- FK to ContentSeries (`ContentSeriesId`, SetNull on delete)
- FK to Content (`ContentId`, SetNull on delete)
- Index on `Status`
- Composite index on `(ScheduledAt, Platform)` -- single-column ScheduledAt index removed as redundant
- `xmin` concurrency token
- Ignore `DomainEvents`

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendSourceConfiguration.cs`

Key configuration points:
- Table: `"TrendSources"`
- `Name`: required, max length 200
- `ApiUrl`: max length 2000
- Unique index on `(Name, Type)` to prevent duplicate source configurations
- `xmin` concurrency token
- Ignore `DomainEvents`

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendItemConfiguration.cs`

Key configuration points:
- Table: `"TrendItems"`
- `Title`: required, max length 500
- `Url`: max length 2000
- `SourceName`: required, max length 200
- `DeduplicationKey`: max length 128, unique filtered index (`WHERE "DeduplicationKey" IS NOT NULL`) for cross-source dedup
- FK to TrendSource (`TrendSourceId`, SetNull on delete) -- added during code review
- Index on `DetectedAt` (for time-based queries)
- `xmin` concurrency token
- Ignore `DomainEvents`

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendSuggestionConfiguration.cs`

Key configuration points:
- Table: `"TrendSuggestions"`
- `Topic`: required, max length 500
- `Rationale`: required, max length 2000
- `SuggestedPlatforms`: `integer[]` column type
- `Status`: required, default `TrendSuggestionStatus.Pending`
- Has many `TrendSuggestionItem` via navigation
- Index on `Status` (for filtering pending suggestions)
- `xmin` concurrency token
- Ignore `DomainEvents`

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendSuggestionItemConfiguration.cs`

Key configuration points:
- Table: `"TrendSuggestionItems"`
- Composite primary key: `(TrendSuggestionId, TrendItemId)`
- FK to TrendSuggestion (Cascade delete)
- FK to TrendItem (Cascade delete)
- No `xmin` concurrency token needed (join table, overwritten in bulk)
- If inheriting from `EntityBase`, ignore `DomainEvents`

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/EngagementSnapshotConfiguration.cs`

Key configuration points:
- Table: `"EngagementSnapshots"`
- FK to ContentPlatformStatus (`ContentPlatformStatusId`, Cascade delete)
- `Impressions` and `Clicks` are nullable (no `.IsRequired()`)
- Composite index on `(ContentPlatformStatusId, FetchedAt)` with descending FetchedAt -- this supports the retention query and "latest snapshot" lookup
- `xmin` concurrency token
- Ignore `DomainEvents`

### Modify Existing Content Configuration

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentConfiguration.cs` (modify existing)

Add:
- `builder.Property(c => c.TreeDepth).IsRequired().HasDefaultValue(0);`
- `builder.Property(c => c.RepurposeSourcePlatform);` (nullable, no special config needed)
- Unique filtered index for repurposing idempotency: `builder.HasIndex(c => new { c.ParentContentId, c.RepurposeSourcePlatform, c.ContentType }).IsUnique().HasFilter("\"ParentContentId\" IS NOT NULL");`

### Update IApplicationDbContext and ApplicationDbContext

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs` (modify existing)

Add DbSet properties:
- `DbSet<ContentSeries> ContentSeries { get; }`
- `DbSet<CalendarSlot> CalendarSlots { get; }`
- `DbSet<TrendSource> TrendSources { get; }`
- `DbSet<TrendItem> TrendItems { get; }`
- `DbSet<TrendSuggestion> TrendSuggestions { get; }`
- `DbSet<TrendSuggestionItem> TrendSuggestionItems { get; }`
- `DbSet<EngagementSnapshot> EngagementSnapshots { get; }`

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs` (modify existing)

Add matching `DbSet` properties using the `=> Set<T>()` pattern.

### Handle Old ContentCalendarSlot

The existing `ContentCalendarSlot` entity and its configuration will be replaced by the new `CalendarSlot`. Two approaches:

1. **If no production data exists** (likely, since this is pre-launch): Remove `ContentCalendarSlot` entity, its configuration, its DbSet from both `IApplicationDbContext` and `ApplicationDbContext`, and its tests. The migration drops `ContentCalendarSlots` and creates `CalendarSlots`.

2. **If data must be preserved**: Create a migration that renames/reshapes the table. Given the schema differences (DateOnly+TimeOnly vs DateTimeOffset, no status field, etc.), a data migration script is needed.

Recommendation: approach 1. Remove old entity entirely, replace with `CalendarSlot`.

### EF Migration

After all entities and configurations are in place, generate the migration:

```bash
dotnet ef migrations add AddContentEngineEntities --project src/PersonalBrandAssistant.Infrastructure --startup-project src/PersonalBrandAssistant.Api
```

The migration should create tables: `ContentSeries`, `CalendarSlots`, `TrendSources`, `TrendItems`, `TrendSuggestions`, `TrendSuggestionItems`, `EngagementSnapshots`. It should add columns `TreeDepth` and `RepurposeSourcePlatform` to `Contents`, and drop `ContentCalendarSlots` if taking approach 1.

---

## File Summary

### New Files

| File | Description |
|------|-------------|
| `src/PersonalBrandAssistant.Domain/Enums/TrendSourceType.cs` | Enum: TrendRadar, FreshRSS, Reddit, HackerNews |
| `src/PersonalBrandAssistant.Domain/Enums/TrendSuggestionStatus.cs` | Enum: Pending, Accepted, Dismissed |
| `src/PersonalBrandAssistant.Domain/Enums/CalendarSlotStatus.cs` | Enum: Open, Filled, Published, Skipped |
| `src/PersonalBrandAssistant.Domain/Entities/ContentSeries.cs` | RRULE-based recurring content series |
| `src/PersonalBrandAssistant.Domain/Entities/CalendarSlot.cs` | Individual calendar slot (replaces ContentCalendarSlot) |
| `src/PersonalBrandAssistant.Domain/Entities/TrendSource.cs` | Configured trend data source |
| `src/PersonalBrandAssistant.Domain/Entities/TrendItem.cs` | Detected trend entry |
| `src/PersonalBrandAssistant.Domain/Entities/TrendSuggestion.cs` | User-facing trend suggestion |
| `src/PersonalBrandAssistant.Domain/Entities/TrendSuggestionItem.cs` | Join entity: TrendSuggestion to TrendItem |
| `src/PersonalBrandAssistant.Domain/Entities/EngagementSnapshot.cs` | Point-in-time engagement metrics |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentSeriesConfiguration.cs` | EF config for ContentSeries |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/CalendarSlotConfiguration.cs` | EF config for CalendarSlot (replaces ContentCalendarSlotConfiguration) |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendSourceConfiguration.cs` | EF config for TrendSource |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendItemConfiguration.cs` | EF config for TrendItem |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendSuggestionConfiguration.cs` | EF config for TrendSuggestion |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/TrendSuggestionItemConfiguration.cs` | EF config for TrendSuggestionItem |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/EngagementSnapshotConfiguration.cs` | EF config for EngagementSnapshot |
| `tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentSeriesTests.cs` | Tests for ContentSeries entity |
| `tests/PersonalBrandAssistant.Domain.Tests/Entities/CalendarSlotTests.cs` | Tests for CalendarSlot entity |
| `tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendSourceTests.cs` | Tests for TrendSource entity |
| `tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendItemTests.cs` | Tests for TrendItem entity |
| `tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendSuggestionTests.cs` | Tests for TrendSuggestion entity |
| `tests/PersonalBrandAssistant.Domain.Tests/Entities/TrendSuggestionItemTests.cs` | Tests for TrendSuggestionItem join entity |
| `tests/PersonalBrandAssistant.Domain.Tests/Entities/EngagementSnapshotTests.cs` | Tests for EngagementSnapshot entity |

### Modified Files

| File | Change |
|------|--------|
| `src/PersonalBrandAssistant.Domain/Entities/Content.cs` | Add `TreeDepth` (int) and `RepurposeSourcePlatform` (nullable PlatformType) |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentConfiguration.cs` | Add TreeDepth/RepurposeSourcePlatform config + repurposing unique index |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs` | Add 7 new DbSet properties |
| `src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs` | Add 7 new DbSet properties |
| `tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs` | Add tests for 3 new enums |
| `tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentTests.cs` | Add tests for TreeDepth and RepurposeSourcePlatform |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs` | Add EF configuration tests for all new entities |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/MigrationTests.cs` | Add migration test for new tables |

### Removed Files (if taking approach 1 for ContentCalendarSlot replacement)

| File | Reason |
|------|--------|
| `src/PersonalBrandAssistant.Domain/Entities/ContentCalendarSlot.cs` | Replaced by CalendarSlot |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentCalendarSlotConfiguration.cs` | Replaced by CalendarSlotConfiguration |
| `tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentCalendarSlotTests.cs` | Replaced by CalendarSlotTests |

Also update `EnumTests` to adjust `ContentCalendarSlot`-related assertions if any exist, and remove `ContentCalendarSlots` DbSet references from `IApplicationDbContext` and `ApplicationDbContext`.

---

## Deviations from Plan

The following changes were made during implementation based on code review feedback:

| # | Deviation | Rationale |
|---|-----------|-----------|
| 1 | Added `Guid? TrendSourceId` FK to TrendItem entity + EF config with SetNull | Code review flagged missing referential integrity between TrendItem and TrendSource |
| 2 | DeduplicationKey unique index uses `.HasFilter("\"DeduplicationKey\" IS NOT NULL")` | Prevents NULL uniqueness violations on nullable column |
| 3 | Removed single-column `ScheduledAt` index from CalendarSlot | Redundant — composite `(ScheduledAt, Platform)` index already covers ScheduledAt-only queries |
| 4 | Renamed test `TrendItem_DeduplicationKey_IsDeterministic` → `TrendItem_DeduplicationKey_StoresValue` | Test only stores/retrieves, doesn't verify determinism |
| 5 | TrendSuggestionItem uses no base class (plain POCO) | Join table doesn't need Id, CreatedAt, or DomainEvents |
| 6 | EF migration not generated | No startup project configured for `dotnet ef` yet; migration deferred to later section |

## Test Results

- **Domain Tests:** 158 passed
- **Application Tests:** 106 passed
- **Infrastructure Tests:** 404 passed
- **Total:** 668 passed, 0 failed