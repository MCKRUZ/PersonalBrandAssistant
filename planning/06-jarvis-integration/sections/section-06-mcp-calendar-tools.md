# Section 06: MCP Calendar Tools

## Overview

An MCP tool class `CalendarTools` exposing three tools that wrap the existing content calendar service. These tools allow Jarvis to query the content schedule, place approved content into specific time slots, and reschedule existing content -- all via voice commands or chat.

Write operations (`pba_schedule_content`, `pba_reschedule_content`) validate against calendar conflicts before executing.

## Dependencies

- **section-04-mcp-server-infrastructure**: The MCP server infrastructure must be in place. Tool classes are discovered via `[McpServerToolType]` assembly scanning.
- **section-09-mcp-idempotency-audit** (downstream): Write tools (`pba_schedule_content`, `pba_reschedule_content`) will gain idempotency and audit trail support in section 09.

## Existing Services

The tools wrap these existing application-layer interfaces:

- `IContentCalendarService` -- `GetSlotsAsync`, `CreateManualSlotAsync`, `AssignContentAsync`, `AutoFillSlotsAsync`
- `IApplicationDbContext` -- Direct queries for calendar slots and content items

The `CalendarSlot` entity represents a time slot in the content calendar:

```csharp
// Located in PersonalBrandAssistant.Domain.Entities
public class CalendarSlot : AuditableEntityBase
{
    public DateTimeOffset SlotTime { get; set; }
    public PlatformType Platform { get; set; }
    public ContentType ContentType { get; set; }
    public CalendarSlotStatus Status { get; set; }
    public Guid? ContentId { get; set; }
    public Content? Content { get; set; }
    public Guid? ContentSeriesId { get; set; }
    // ...
}
```

`CalendarSlotStatus` enum: `Open`, `Assigned`, `Published`, `Skipped`.

## Tests (Write First)

Test file: `tests/PersonalBrandAssistant.Application.Tests/McpServer/CalendarToolsTests.cs`

Use xUnit + Moq. Mock `IContentCalendarService` and `IApplicationDbContext`.

```csharp
// --- pba_get_calendar ---

// Test: returns items in date range
//   Seed 3 calendar slots within range, 2 outside
//   Call pba_get_calendar with the date range
//   Assert result JSON contains exactly 3 items with correct fields

// Test: filters by platform
//   Seed slots for LinkedIn and Twitter within range
//   Call pba_get_calendar with platform: "LinkedIn"
//   Assert result contains only LinkedIn slots

// Test: returns empty for range with no content
//   Call with a date range that has no seeded slots
//   Assert result JSON contains an empty array


// --- pba_schedule_content ---

// Test: creates schedule for available slot
//   Seed content in Approved status, no conflicting slot
//   Call pba_schedule_content with contentId, dateTime, platform
//   Assert calendar service was called to create and assign the slot
//   Assert result JSON indicates success with scheduled time

// Test: fails for conflicting time slot
//   Seed an existing slot at the same dateTime and platform
//   Call pba_schedule_content
//   Assert result JSON contains conflict error

// Test: validates contentId exists
//   Call with a non-existent content ID
//   Assert result JSON contains not-found error

// Test: validates content is in schedulable state
//   Seed content in Draft status
//   Call pba_schedule_content
//   Assert result JSON contains error about content state


// --- pba_reschedule_content ---

// Test: moves to new time slot
//   Seed a scheduled content item with an assigned slot
//   Call pba_reschedule_content with contentId and new dateTime
//   Assert the old slot is released and new slot is created/assigned
//   Assert result JSON indicates success with new time

// Test: fails if new slot conflicts
//   Seed a scheduled content item and another slot at the target time
//   Call pba_reschedule_content
//   Assert result JSON contains conflict error

// Test: fails if content is not currently scheduled
//   Seed content in Draft status
//   Call pba_reschedule_content
//   Assert result JSON contains error about content not being scheduled
```

## File Paths

### New Files

- `src/PersonalBrandAssistant.Api/McpTools/CalendarTools.cs` -- The tool class with 3 MCP tools.
- `tests/PersonalBrandAssistant.Application.Tests/McpServer/CalendarToolsTests.cs` -- Tests.

## Tool Definitions

### pba_get_calendar

```csharp
[McpServerTool]
[Description("Returns scheduled content for a date range. Use when asked 'what's scheduled this week', 'show my content calendar', or 'what's posting tomorrow'. Returns content ID, platform, scheduled time, title, content type, and slot status for each item.")]
public static async Task<string> pba_get_calendar(
    IServiceProvider serviceProvider,
    [Description("Start date in ISO 8601 format (e.g., '2026-03-23')")] string startDate,
    [Description("End date in ISO 8601 format (e.g., '2026-03-30')")] string endDate,
    [Description("Optional platform filter: Twitter, LinkedIn, Reddit, Blog. Omit for all platforms.")] string? platform,
    CancellationToken ct)
```

Implementation logic:
1. Parse `startDate` and `endDate` to `DateTimeOffset`. Return validation error if parsing fails.
2. If `platform` is provided, parse to `PlatformType`. Return validation error if invalid.
3. Resolve `IContentCalendarService` from a new DI scope.
4. Call `GetSlotsAsync(from, to, ct)` to get all slots in the range.
5. If `platform` is specified, filter the results to that platform.
6. Project results into response shape: slotId, contentId (nullable), platform, scheduledAt, title (from joined content), contentType, status.
7. Order by `SlotTime` ascending.
8. Serialize and return.

### pba_schedule_content

```csharp
[McpServerTool]
[Description("Schedules approved content for a specific time slot. Use when asked to 'schedule this for Thursday', 'post this at 9am on LinkedIn', or 'queue this for tomorrow'. Validates the time slot is available and the content is in a schedulable state (Approved). Returns success with scheduled time or error with reason.")]
public static async Task<string> pba_schedule_content(
    IServiceProvider serviceProvider,
    [Description("The content ID to schedule (GUID format)")] string contentId,
    [Description("Target date and time in ISO 8601 format (e.g., '2026-03-25T09:00:00Z')")] string dateTime,
    [Description("Target platform: Twitter, LinkedIn, Reddit, or Blog")] string platform,
    CancellationToken ct)
```

Implementation logic:
1. Parse `contentId` to GUID, `dateTime` to `DateTimeOffset`, `platform` to `PlatformType`. Return validation errors for any parse failure.
2. Resolve `IApplicationDbContext` and `IContentCalendarService` from a new DI scope.
3. Load the content item. Return not-found error if missing.
4. Validate content status is `Approved`. Return error with current status if not.
5. Check for conflicting slots: query `CalendarSlots` where `SlotTime` matches the target time (within a configurable window, e.g., same hour) AND `Platform` matches AND `Status` is not `Skipped`. If a conflict exists, return a conflict error with the existing slot's details.
6. Create a new calendar slot via `CreateManualSlotAsync` with the target time, platform, and content type.
7. Assign the content to the slot via `AssignContentAsync`.
8. Update the content's `ScheduledAt` and transition to `Scheduled` status.
9. Return success with contentId, scheduledAt, platform, and slotId.

### pba_reschedule_content

```csharp
[McpServerTool]
[Description("Moves scheduled content to a new time slot. Use when asked to 'move this to Friday', 'reschedule the LinkedIn post', or 'push this back to next week'. Validates the new slot is available. Returns success with new time or error with reason.")]
public static async Task<string> pba_reschedule_content(
    IServiceProvider serviceProvider,
    [Description("The content ID to reschedule (GUID format)")] string contentId,
    [Description("New target date and time in ISO 8601 format (e.g., '2026-03-28T14:00:00Z')")] string newDateTime,
    CancellationToken ct)
```

Implementation logic:
1. Parse `contentId` to GUID and `newDateTime` to `DateTimeOffset`. Return validation errors for parse failures.
2. Resolve `IApplicationDbContext` and `IContentCalendarService` from a new DI scope.
3. Load the content item. Return not-found error if missing.
4. Validate content is in `Scheduled` status. Return error if not (e.g., "Content is not currently scheduled. Current status: Draft").
5. Find the current calendar slot assigned to this content (where `ContentId == contentId` and `Status == Assigned`). Return error if no assigned slot found.
6. Determine the platform from the existing slot.
7. Check for conflicts at the new time/platform (same logic as `pba_schedule_content`). Return conflict error if occupied.
8. Release the old slot: set its `Status` to `Skipped` and clear its `ContentId`.
9. Create a new slot at the new time, assign the content.
10. Update the content's `ScheduledAt` to the new time.
11. Save changes and return success with contentId, previousTime, newTime, platform.

## Conflict Detection

Calendar conflict detection uses a tolerance window rather than exact timestamp matching. Two slots conflict when:
- They target the same platform
- Their times are within 30 minutes of each other
- Neither slot has status `Skipped`

This prevents scheduling two LinkedIn posts 5 minutes apart while allowing posts on different platforms at the same time.

The conflict check query:

```csharp
var hasConflict = await dbContext.CalendarSlots
    .AnyAsync(s =>
        s.Platform == targetPlatform &&
        s.Status != CalendarSlotStatus.Skipped &&
        s.SlotTime >= targetTime.AddMinutes(-30) &&
        s.SlotTime <= targetTime.AddMinutes(30),
        ct);
```

## Response Serialization

Same pattern as section 05 -- all tools return JSON strings using `System.Text.Json.JsonSerializer` with camelCase naming policy.

Calendar-specific response shapes:

```json
// pba_get_calendar success
{
  "items": [
    {
      "slotId": "guid",
      "contentId": "guid",
      "platform": "LinkedIn",
      "scheduledAt": "2026-03-25T09:00:00Z",
      "title": "AI Trends in 2026",
      "contentType": "Article",
      "status": "Assigned"
    }
  ],
  "count": 1,
  "dateRange": { "start": "2026-03-23", "end": "2026-03-30" }
}

// pba_schedule_content success
{
  "contentId": "guid",
  "slotId": "guid",
  "scheduledAt": "2026-03-25T09:00:00Z",
  "platform": "LinkedIn",
  "message": "Content scheduled successfully"
}

// Conflict error
{
  "error": true,
  "message": "Time slot conflict: LinkedIn already has content scheduled at 2026-03-25T09:30:00Z",
  "existingSlot": {
    "slotId": "guid",
    "contentId": "guid",
    "scheduledAt": "2026-03-25T09:30:00Z"
  }
}
```

## Implementation Notes

- Each tool method creates its own DI scope via `serviceProvider.CreateScope()`.
- Date parsing should use `DateTimeOffset.TryParse` with `CultureInfo.InvariantCulture` and `DateTimeStyles.RoundtripKind` for reliable ISO 8601 parsing from LLM-generated input.
- The `pba_get_calendar` tool joins `CalendarSlots` with `Contents` via `Include(s => s.Content)` to get the title and content type. Slots without assigned content (status `Open`) still appear in the results with null content fields.
- The `pba_reschedule_content` tool performs the old-slot release and new-slot creation within a single `SaveChangesAsync` call for transactional consistency.
- Limit `pba_get_calendar` results to a maximum of 100 items to avoid overwhelming the LLM context window. If the date range would return more, include a count and a message suggesting a narrower range.
