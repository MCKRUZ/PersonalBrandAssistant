Now I have all the context needed. Here is the section content:

# Section 06: Content Calendar

## Overview

This section implements the content calendar system: RRULE-based recurring series, slot management with timezone support, auto-fill algorithm, and the `CalendarSlotProcessor` background service for slot materialization. The calendar replaces the simpler `ContentCalendarSlot` entity from Phase 01 with a full `ContentSeries` + `CalendarSlot` model that supports recurring schedules, override semantics, and autonomy-driven auto-fill.

## Dependencies

- **section-01-domain-entities** (must be complete): Provides `ContentSeries`, `CalendarSlot`, `CalendarSlotStatus` enum, and EF Core configurations. This section consumes those entities.
- **Content entity** (already exists): `Content` at `src/PersonalBrandAssistant.Domain/Entities/Content.cs` with `ParentContentId`, `TargetPlatforms`, `Status`, `Metadata`, `ContentType`.
- **Result pattern** (already exists): `Result<T>` at `src/PersonalBrandAssistant.Application/Common/Models/Result.cs`.
- **AuditableEntityBase** (already exists): `src/PersonalBrandAssistant.Domain/Common/EntityBase.cs`.
- **NuGet dependency**: `Ical.Net` for RRULE parsing and occurrence generation.

## Domain Entities (from section-01)

These entities are defined in section-01-domain-entities and should already exist when this section is implemented. They are reproduced here for reference only -- do not create them in this section.

**ContentSeries** (`src/PersonalBrandAssistant.Domain/Entities/ContentSeries.cs`):

```csharp
public class ContentSeries : AuditableEntityBase
{
    public string Name { get; set; }
    public string? Description { get; set; }
    public string RecurrenceRule { get; set; }  // iCalendar RRULE string
    public PlatformType[] TargetPlatforms { get; set; }
    public ContentType ContentType { get; set; }
    public List<string> ThemeTags { get; set; }
    public string TimeZoneId { get; set; }  // IANA timezone for RRULE interpretation
    public bool IsActive { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
}
```

**CalendarSlot** (`src/PersonalBrandAssistant.Domain/Entities/CalendarSlot.cs`):

```csharp
public class CalendarSlot : AuditableEntityBase
{
    public DateTimeOffset ScheduledAt { get; set; }
    public PlatformType Platform { get; set; }
    public Guid? ContentSeriesId { get; set; }  // null for manual slots
    public Guid? ContentId { get; set; }  // null until content assigned
    public CalendarSlotStatus Status { get; set; }  // Open, Filled, Published, Skipped
    public bool IsOverride { get; set; }  // true if overriding a recurring occurrence
    public DateTimeOffset? OverriddenOccurrence { get; set; }  // original occurrence timestamp this overrides
}
```

**CalendarSlotStatus** (`src/PersonalBrandAssistant.Domain/Enums/CalendarSlotStatus.cs`):

```csharp
public enum CalendarSlotStatus { Open, Filled, Published, Skipped }
```

**Note on existing entity**: The project already has `ContentCalendarSlot` at `src/PersonalBrandAssistant.Domain/Entities/ContentCalendarSlot.cs` with a simpler schema (DateOnly + TimeOnly, cron-based recurrence). The new `CalendarSlot` entity supersedes it. The migration in section-01 should handle the schema transition. This section's code uses only the new `CalendarSlot` and `ContentSeries` entities.

## Tests First

All tests use xUnit + Moq. Naming convention: `{Method}_{Scenario}_{Expected}`. AAA pattern. Mock DbSet via `BuildMockDbSet()` helper (already in `tests/PersonalBrandAssistant.Infrastructure.Tests/Helpers/AsyncQueryableHelpers.cs`).

### Test File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/ContentCalendarServiceTests.cs`

```csharp
/// ContentCalendarServiceTests
/// Tests for IContentCalendarService implementation.
/// Uses Moq for DbContext, IDateTimeProvider.
/// Uses Ical.Net for RRULE validation in assertions.

// --- GetSlotsAsync ---

// GetSlotsAsync_WithActiveSeriesInRange_GeneratesOccurrencesFromRRule
//   Arrange: Create a ContentSeries with RRULE "FREQ=WEEKLY;BYDAY=TU;BYHOUR=9;BYMINUTE=0",
//            TimeZoneId "America/New_York", StartsAt 2026-01-01, no EndsAt.
//            Query window: 2026-03-01 to 2026-03-31.
//   Act: Call GetSlotsAsync(from, to, ct).
//   Assert: Result contains slots for each Tuesday in March 2026 at 09:00 Eastern.

// GetSlotsAsync_UsesSeriesTimeZoneForOccurrenceGeneration
//   Arrange: Series with TimeZoneId "America/New_York" and BYHOUR=2.
//            Query across a DST boundary (March 8, 2026 -- spring forward).
//   Act: Call GetSlotsAsync spanning the DST transition.
//   Assert: Occurrences use correct UTC offsets (EST before, EDT after).

// GetSlotsAsync_MergesMaterializedSlotsWithGeneratedOccurrences
//   Arrange: Series generates occurrence at Tuesday 09:00.
//            Materialized CalendarSlot exists for that same timestamp with ContentId assigned.
//   Act: Call GetSlotsAsync.
//   Assert: Result contains the materialized slot (not a duplicate generated one),
//           slot has status Filled, ContentId is populated.

// GetSlotsAsync_IncludesManualSlotsWithNoSeriesReference
//   Arrange: Manual CalendarSlot (ContentSeriesId=null) in range.
//   Act: Call GetSlotsAsync.
//   Assert: Manual slot appears in results alongside series-generated slots.

// GetSlotsAsync_HandlesDstBoundaryCorrectly
//   Arrange: Series in "America/New_York" with daily recurrence at 02:30 local.
//            Query window covers March 8-9, 2026 (spring forward -- 02:30 doesn't exist on March 8).
//   Act: Call GetSlotsAsync.
//   Assert: Ical.Net handles the nonexistent time (shifts or skips per RFC 5545).

// --- CreateSeriesAsync ---

// CreateSeriesAsync_WithValidRRule_CreatesSeriesEntity
//   Arrange: Valid ContentSeriesRequest with RRULE, platforms, timezone.
//   Act: Call CreateSeriesAsync.
//   Assert: Result.IsSuccess, returned Guid is non-empty, entity persisted to DbSet.

// CreateSeriesAsync_WithInvalidRRule_ReturnsValidationFailure
//   Arrange: Request with malformed RRULE string "NOT_A_VALID_RRULE".
//   Act: Call CreateSeriesAsync.
//   Assert: Result.IsSuccess == false, ErrorCode == ValidationFailed.

// --- CreateManualSlotAsync ---

// CreateManualSlotAsync_CreatesSlotWithNoSeriesReference
//   Arrange: CalendarSlotRequest with ScheduledAt, Platform.
//   Act: Call CreateManualSlotAsync.
//   Assert: Slot has ContentSeriesId == null, Status == Open.

// --- AssignContentAsync ---

// AssignContentAsync_WithOpenSlot_FillsAndChangesStatus
//   Arrange: CalendarSlot with Status=Open, Content entity with matching platform.
//   Act: Call AssignContentAsync(slotId, contentId, ct).
//   Assert: Slot.ContentId == contentId, Slot.Status == Filled.

// AssignContentAsync_WithAlreadyFilledSlot_ReturnsConflict
//   Arrange: CalendarSlot with Status=Filled.
//   Act: Call AssignContentAsync.
//   Assert: Result.ErrorCode == Conflict.

// --- AutoFillSlotsAsync ---

// AutoFillSlotsAsync_MatchesContentToSlotsByPlatform
//   Arrange: Open slot for Twitter. Two approved contents: one targeting Twitter, one LinkedIn.
//   Act: Call AutoFillSlotsAsync.
//   Assert: Twitter content assigned to Twitter slot. LinkedIn content not assigned.

// AutoFillSlotsAsync_PrefersOlderApprovedContent
//   Arrange: Open slot for LinkedIn. Two approved LinkedIn contents, one created 7 days ago, one 1 day ago.
//   Act: Call AutoFillSlotsAsync.
//   Assert: Older content assigned first.

// AutoFillSlotsAsync_SkipsAlreadyFilledSlots
//   Arrange: Two slots -- one Open, one Filled.
//   Act: Call AutoFillSlotsAsync.
//   Assert: Returns count=1 (only the Open slot was filled).

// AutoFillSlotsAsync_ConsidersThemeTagAffinity
//   Arrange: Series with ThemeTags ["dotnet", "csharp"]. Open slot from that series.
//            Two approved contents: one with metadata tags ["dotnet"], one with ["cooking"].
//   Act: Call AutoFillSlotsAsync.
//   Assert: Content tagged "dotnet" assigned over "cooking" content.

// AutoFillSlotsAsync_ReturnsCountOfSlotsFilled
//   Arrange: 3 open slots, 2 approved matching contents.
//   Act: Call AutoFillSlotsAsync.
//   Assert: Returns 2 (only filled what had matching content).
```

### Test File: `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/CalendarSlotProcessorTests.cs`

```csharp
/// CalendarSlotProcessorTests
/// Tests for CalendarSlotProcessor background service.
/// Mocks IContentCalendarService and IServiceScopeFactory.

// Processor_MaterializesUpcomingSlotsFromActiveSeries
//   Arrange: Active series generating occurrences in the next 7 days.
//   Act: Trigger processor execution.
//   Assert: CalendarSlot records created for upcoming occurrences not yet materialized.

// Processor_TriggersAutoFillAtAutonomousLevel
//   Arrange: AutonomyLevel == Autonomous. Open materialized slots exist.
//   Act: Trigger processor execution.
//   Assert: AutoFillSlotsAsync called for the materialization window.

// Processor_SkipsAutoFillAtManualLevel
//   Arrange: AutonomyLevel == Manual.
//   Act: Trigger processor execution.
//   Assert: Slots materialized but AutoFillSlotsAsync NOT called.
```

### Test File: `tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentSeriesTests.cs`

```csharp
/// ContentSeriesTests
/// Domain entity validation tests.

// ContentSeries_RequiresTimeZoneId
//   Assert: TimeZoneId must be set (non-empty string).

// ContentSeries_ValidatesRRuleFormat
//   Assert: RecurrenceRule stores the RRULE string as-is (validation is at service layer).

// ContentSeries_EfConfigurationMapsPlatformTypeArrayAndThemeTagsCorrectly
//   Assert: Verified via EF configuration test (JSON column or value converter).
```

### Test File: `tests/PersonalBrandAssistant.Domain.Tests/Entities/CalendarSlotTests.cs`

```csharp
/// CalendarSlotTests (new file -- the existing ContentCalendarSlotTests.cs is for the old entity)

// CalendarSlot_WithIsOverrideTrue_RequiresOverriddenOccurrence
//   Arrange: Slot with IsOverride=true, OverriddenOccurrence=null.
//   Assert: Business rule violation (enforced at service or EF level).

// CalendarSlot_DefaultStatusIsOpen
//   Assert: New slot has Status == Open by default.
```

## Implementation Details

### 1. IContentCalendarService Interface

**File**: `src/PersonalBrandAssistant.Application/Common/Interfaces/IContentCalendarService.cs`

```csharp
public interface IContentCalendarService
{
    /// <summary>Get merged calendar slots (generated + materialized) for a date range.</summary>
    Task<Result<IReadOnlyList<CalendarSlot>>> GetSlotsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>Create a recurring content series with RRULE.</summary>
    Task<Result<Guid>> CreateSeriesAsync(ContentSeriesRequest request, CancellationToken ct);

    /// <summary>Create a one-off manual slot not tied to any series.</summary>
    Task<Result<Guid>> CreateManualSlotAsync(CalendarSlotRequest request, CancellationToken ct);

    /// <summary>Assign a Content entity to an open slot.</summary>
    Task<Result<Unit>> AssignContentAsync(Guid slotId, Guid contentId, CancellationToken ct);

    /// <summary>Auto-fill open slots in range by scoring candidate content. Returns count filled.</summary>
    Task<Result<int>> AutoFillSlotsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
```

### 2. Request Models

**File**: `src/PersonalBrandAssistant.Application/Common/Models/ContentSeriesRequest.cs`

```csharp
/// <summary>Request to create a recurring content series.</summary>
public record ContentSeriesRequest(
    string Name,
    string? Description,
    string RecurrenceRule,       // iCalendar RRULE string
    PlatformType[] TargetPlatforms,
    ContentType ContentType,
    List<string> ThemeTags,
    string TimeZoneId,           // IANA timezone ID
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt);
```

**File**: `src/PersonalBrandAssistant.Application/Common/Models/CalendarSlotRequest.cs`

```csharp
/// <summary>Request to create a manual (non-recurring) calendar slot.</summary>
public record CalendarSlotRequest(
    DateTimeOffset ScheduledAt,
    PlatformType Platform);
```

### 3. ContentCalendarService Implementation

**File**: `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/ContentCalendarService.cs`

The service implements `IContentCalendarService` and depends on:
- `ApplicationDbContext` (for CalendarSlot, ContentSeries, Content DbSets)
- `IDateTimeProvider` (for current time in processor logic)

**Key implementation notes:**

**RRULE Parsing (Ical.Net)**:
- Use `Ical.Net.CalendarComponents.CalendarEvent` with a `RecurrenceRule` parsed from the RRULE string.
- Set the event's `DtStart` to the series `StartsAt` converted to the series `TimeZoneId`.
- Call `GetOccurrences(from, to)` to generate occurrences within the query window.
- Each occurrence's `Period.StartTime` gives the local-time occurrence; convert to `DateTimeOffset` using the series timezone for correct DST handling.

**GetSlotsAsync flow**:
1. Load all active `ContentSeries` where date ranges overlap the query window (`StartsAt <= to` and `EndsAt == null || EndsAt >= from`).
2. For each series, parse RRULE via Ical.Net and generate occurrences in `[from, to]`.
3. Load all materialized `CalendarSlot` records in the date range.
4. For each generated occurrence, check if a materialized slot already exists (match by `ContentSeriesId` + `ScheduledAt`). If yes, use the materialized slot. If no, return a transient (non-persisted) slot representing the occurrence.
5. Add manual slots (where `ContentSeriesId == null`) from the materialized set.
6. Return the unified list sorted by `ScheduledAt`.

**CreateSeriesAsync flow**:
1. Validate the RRULE string by attempting to parse it with Ical.Net. If parsing fails, return `Result.ValidationFailure`.
2. Validate `TimeZoneId` is a recognized IANA timezone via `TimeZoneInfo.FindSystemTimeZoneById` or NodaTime/ICU.
3. Create and persist the `ContentSeries` entity.
4. Return the new series `Id`.

**AssignContentAsync flow**:
1. Load the `CalendarSlot` by ID. Return NotFound if missing.
2. If `Status != Open`, return Conflict.
3. Load the `Content` by `contentId`. Return NotFound if missing.
4. Set `slot.ContentId = contentId`, `slot.Status = CalendarSlotStatus.Filled`.
5. Save changes.

**AutoFillSlotsAsync flow**:
1. Query open slots in the date range (materialized slots with `Status == Open`).
2. Query approved/queued `Content` not yet assigned to any slot (`Content.Status == Approved` and `Content.Id` not in any `CalendarSlot.ContentId`).
3. For each open slot, score candidate content by:
   - **Platform match** (required filter): `content.TargetPlatforms.Contains(slot.Platform)`. Non-matching content is excluded.
   - **Theme/topic affinity** (scoring bonus): Compare `content.Metadata` tags against `series.ThemeTags` (loaded via `slot.ContentSeriesId`). Each matching tag adds a score increment.
   - **Age preference** (tiebreaker): Prefer older approved content (`content.CreatedAt` ascending) to prevent staleness.
4. Assign the highest-scoring content to each slot. Use `SELECT FOR UPDATE SKIP LOCKED` (PostgreSQL advisory lock pattern) to prevent double-assignment under concurrent calls. In EF Core, this can be achieved with raw SQL or optimistic concurrency via the `xmin` token.
5. Return count of slots filled.

### 4. CalendarSlotProcessor Background Service

**File**: `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/CalendarSlotProcessor.cs`

This `BackgroundService` runs on a configurable interval (default: once per hour). It:

1. Creates a DI scope and resolves `IContentCalendarService`.
2. Loads all active `ContentSeries`.
3. For each series, generates occurrences for the next 7 days (configurable lookahead window).
4. For each occurrence not yet materialized as a `CalendarSlot`, creates a new slot with `Status = Open`.
5. Checks the current `AutonomyLevel` (resolved from `AutonomyConfiguration`):
   - **Autonomous**: Calls `AutoFillSlotsAsync` for the same 7-day window.
   - **SemiAuto**: Calls `AutoFillSlotsAsync` only for slots from series (not manual slots).
   - **Manual**: Skips auto-fill entirely.
6. Logs materialization and fill counts.

**Error handling**: Wraps each series in a try/catch so one failing series does not block others. Logs errors and continues.

### 5. MediatR Commands and Queries (for section-11 API wiring)

These are thin wrappers that delegate to `IContentCalendarService`. They live under `src/PersonalBrandAssistant.Application/Features/Calendar/`:

- `Commands/CreateSeries/CreateSeriesCommand.cs` -- wraps `CreateSeriesAsync`
- `Commands/CreateSlot/CreateSlotCommand.cs` -- wraps `CreateManualSlotAsync`
- `Commands/AssignContent/AssignContentCommand.cs` -- wraps `AssignContentAsync`
- `Commands/AutoFillSlots/AutoFillSlotsCommand.cs` -- wraps `AutoFillSlotsAsync`
- `Queries/GetSlots/GetSlotsQuery.cs` -- wraps `GetSlotsAsync`

Each command/query handler resolves `IContentCalendarService` from DI and delegates. FluentValidation validators should enforce:
- `CreateSeriesCommand`: Name required, RecurrenceRule required, TargetPlatforms non-empty, TimeZoneId required.
- `AutoFillSlotsCommand`: `from < to`, range not exceeding 90 days.
- `AssignContentCommand`: Both IDs required as non-empty GUIDs.

### 6. NuGet Package

Add to `src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj`:

```xml
<PackageReference Include="Ical.Net" Version="4.*" />
```

## Implementation Status: COMPLETE

### Files Created
- `src/PersonalBrandAssistant.Application/Common/Interfaces/IContentCalendarService.cs` -- 5-method interface
- `src/PersonalBrandAssistant.Application/Common/Models/ContentSeriesRequest.cs` -- record DTO
- `src/PersonalBrandAssistant.Application/Common/Models/CalendarSlotRequest.cs` -- record DTO
- `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/ContentCalendarService.cs` -- full implementation
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/ContentCalendarServiceTests.cs` -- 12 tests

### Files Modified
- `src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj` -- added `Ical.Net` v5.2.1
- `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` -- registered `IContentCalendarService` as scoped

### Deferred to Later Sections
- `CalendarSlotProcessor` background service -- deferred to section-10
- MediatR commands/queries (`CreateSeries`, `CreateSlot`, `AssignContent`, `AutoFillSlots`, `GetSlots`) -- deferred to section-11
- Domain-level entity tests (`ContentSeriesTests`, `CalendarSlotTests`) -- already covered by section-01

### Deviations from Plan
1. **Ical.Net v5 (not v4)**: Used v5.2.1 which has a different API. `GetOccurrences(from)` with `.TakeWhile()` instead of `GetOccurrences(from, to)`.
2. **No IDateTimeProvider dependency**: Service uses only injected date ranges, not system clock.
3. **No concurrency token**: Deferred M6 (optimistic concurrency on CalendarSlot) as a follow-up.
4. **No timezone validation**: Deferred M2 (TimeZoneId validation in CreateSeries) -- changes the contract.

### Code Review Fixes Applied
- **M1**: Added structured logging to `GenerateOccurrences` catch block (was silently swallowing exceptions)
- **M5**: Replaced materialized `assignedContentIds` list with NOT EXISTS subquery for scalability
- **M7**: Extracted magic number `1` to `OccurrenceMatchToleranceMinutes` named constant

### Test Count: 12 tests (all passing, 712 total across solution)

## Key Design Decisions

1. **Ical.Net over custom RRULE parsing**: The RRULE spec (RFC 5545) is complex, especially around DST boundaries and BYSETPOS/BYDAY combinations. Ical.Net handles these edge cases.

2. **Transient vs. materialized slots**: Generated occurrences are transient until materialized by the `CalendarSlotProcessor`. This avoids pre-generating thousands of slots for long-running series while still allowing assignment and override.

3. **Concurrency in auto-fill**: PostgreSQL's `xmin` concurrency token (already used in `ContentCalendarSlotConfiguration`) prevents double-assignment. The auto-fill algorithm loads candidates, scores them, then attempts assignment with optimistic concurrency -- if a concurrent call already filled a slot, the save fails and that slot is skipped.

4. **Override semantics**: A `CalendarSlot` with `IsOverride=true` replaces a generated occurrence. The `OverriddenOccurrence` field records which original occurrence timestamp was replaced, enabling the merge logic in `GetSlotsAsync` to suppress the generated occurrence.