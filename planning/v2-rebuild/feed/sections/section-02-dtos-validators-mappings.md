# Section 2: Feed DTOs, Validators, Mappings, and Data Schemas

## Overview

This section creates all data transfer objects (DTOs), request records, FluentValidation validators, entity-to-DTO mappings, and documents the canonical JSON schemas for the `FeedItem.Data` column. These types are consumed by every subsequent backend section (queries, commands, endpoints, SignalR, seed data).

## Dependencies

- **Section 01 (Prerequisites)** must be complete: `IAppDbContext` must have `DbSet<FeedItem> FeedItems` and the two EF indexes on `(Type, IsActedOn)` and `(Type, CreatedAt)`.
- Domain entities and enums already exist: `FeedItem` (`src/PBA.Domain/Entities/FeedItem.cs`), `FeedItemType` and `FeedItemPriority` (`src/PBA.Domain/Enums/`).

## File Structure

All files go under `src/PBA.Application/Features/Feed/`:

```
src/PBA.Application/Features/Feed/
  Dtos/
    FeedItemDto.cs
    FeedSummaryDto.cs
    TrendingTopicDto.cs
    ActOnFeedItemRequest.cs
    BatchReadRequest.cs
    BatchDismissRequest.cs
    BatchActRequest.cs
  Validators/
    ActOnFeedItemRequestValidator.cs
    BatchActRequestValidator.cs
    BatchDismissRequestValidator.cs
  Mappings/
    FeedMappings.cs
```

## Tests First

Write these tests before implementing. Backend tests use xUnit, in-memory EF Core (Guid DB name per test), Moq for external dependencies. Arrange-Act-Assert pattern.

Test files go under `tests/PBA.Application.Tests/Features/Feed/`.

### Validator Tests

**`Validators/ActOnFeedItemRequestValidatorTests.cs`**

```csharp
// Test: rejects empty action string
// Test: rejects null action
// Test: accepts valid action ("approve", "dismiss", "view", "edit", "schedule", "create-content")
```

The validator should ensure `Action` is non-empty and is one of the known action strings. The known actions are: `"approve"`, `"dismiss"`, `"view"`, `"edit"`, `"schedule"`, `"create-content"`.

**`Validators/BatchActRequestValidatorTests.cs`**

```csharp
// Test: rejects empty Ids list
// Test: rejects null Ids
// Test: rejects empty action string
// Test: accepts valid request with IDs and action
```

**`Validators/BatchDismissRequestValidatorTests.cs`**

```csharp
// Test: accepts valid FeedItemType
```

### Mapping Tests

**`Mappings/FeedMappingsTests.cs`**

```csharp
// Test: ToDto maps all fields correctly from FeedItem entity
// Test: ToDto handles null optional fields (Data, ActionType, ActionTargetId, ExpiresAt)
```

Create a `FeedItem` entity with all fields populated, call `ToDto()`, and assert every property matches. Then create a second entity with `Data = null`, `ActionType = null`, `ActionTargetId = null`, `ExpiresAt = null` and verify `ToDto()` handles nulls without throwing.

## Implementation Details

### DTOs

All DTOs are immutable records with `init`-only properties, following the established pattern in `src/PBA.Application/Features/Content/Dtos/ContentDto.cs`.

**`FeedItemDto.cs`** -- mirrors the `FeedItem` entity 1:1:

```csharp
record FeedItemDto
{
    Guid Id
    FeedItemType Type
    string Title
    string Summary
    string? Data               // raw JSON, deserialized client-side
    string? ActionType         // default action label ("approve", "view", etc.)
    Guid? ActionTargetId       // target entity ID for the action
    FeedItemPriority Priority
    bool IsRead
    bool IsActedOn
    DateTimeOffset CreatedAt
    DateTimeOffset? ExpiresAt
}
```

**`FeedSummaryDto.cs`** -- aggregated stats for the stats bar:

```csharp
record FeedSummaryDto
{
    int UnreadCount
    int PendingApprovals
    int TrendingCount
    double EngagementDelta     // average delta from AnalyticsHighlight items
}
```

**`TrendingTopicDto.cs`** -- grouped trending topic:

```csharp
record TrendingTopicDto
{
    string Topic
    int Count
    DateTimeOffset LatestAt    // most recent CreatedAt for this topic
}
```

**`ActOnFeedItemRequest.cs`** -- request body for acting on a single feed item:

```csharp
record ActOnFeedItemRequest
{
    string Action                              // required: "approve", "dismiss", "view", etc.
    Dictionary<string, string>? AdditionalData // optional: extra context for specific actions
}
```

**`BatchReadRequest.cs`** -- request body for batch mark-as-read:

```csharp
record BatchReadRequest
{
    FeedItemType? Type   // null = all visible items
    bool? IsRead         // filter by current read state
}
```

**`BatchDismissRequest.cs`** -- request body for batch dismiss:

```csharp
record BatchDismissRequest
{
    FeedItemType Type    // required: which type to bulk dismiss
}
```

**`BatchActRequest.cs`** -- request body for batch action:

```csharp
record BatchActRequest
{
    List<Guid> Ids       // required: specific item IDs to act on
    string Action        // required: action to apply to all
}
```

### Canonical JSON Schemas for FeedItem.Data

Each `FeedItemType` uses a defined JSON structure in the `Data` column. These are parsed client-side or in-memory (not in EF LINQ queries) since the expected scale (50-200 items/day) makes in-memory processing trivial.

| FeedItemType | JSON Schema |
|---|---|
| AgentDraft | `{ "contentType": "Blog", "primaryPlatform": "Substack", "wordCount": 1200 }` |
| TrendAlert | `{ "topic": "Claude Code", "source": "Twitter", "mentionCount": 45, "sentiment": "positive" }` |
| IdeaSuggestion | `{ "keywords": ["AI", "enterprise"], "confidence": 0.85, "sourceIdeaTitle": "..." }` |
| AnalyticsHighlight | `{ "metric": "impressions", "currentValue": 500, "previousValue": 400, "delta": 25.0 }` |
| ApprovalRequest | `{ "contentType": "Blog", "primaryPlatform": "Substack", "requestedBy": "system" }` |
| SystemNotification | `{ "category": "info", "link": "/settings" }` |

The `ActionType` field on `FeedItem` stores the default/recommended action label for that card (e.g., "approve", "view"). It is set by the background process that creates the feed item and displayed as the primary action button label on the card.

### Validators

Follow the established FluentValidation pattern (see `src/PBA.Application/Features/Ideas/Validators/CreateIdeaValidator.cs`). Validators are auto-discovered via assembly scanning and applied through the MediatR pipeline behavior.

**`ActOnFeedItemRequestValidator.cs`**

Validates the `ActOnFeedItemRequest` record:
- `Action` must be non-empty
- `Action` must be one of the known action strings: `"approve"`, `"dismiss"`, `"view"`, `"edit"`, `"schedule"`, `"create-content"`

**`BatchActRequestValidator.cs`**

Validates the `BatchActRequest` record:
- `Ids` must not be null or empty
- `Action` must be non-empty and a known action string (same set as above)

**`BatchDismissRequestValidator.cs`**

Validates the `BatchDismissRequest` record:
- `Type` must be a valid `FeedItemType` enum value (use `.IsInEnum()`)

### Mappings

**`FeedMappings.cs`** -- static extension method class following the pattern in `src/PBA.Application/Features/Content/Mappings/ContentMappings.cs`.

Provide a `ToDto()` extension method on `FeedItem` that performs a straightforward 1:1 field mapping to `FeedItemDto`. All properties map directly by name. Nullable fields (`Data`, `ActionType`, `ActionTargetId`, `ExpiresAt`) pass through as-is.

```csharp
public static class FeedMappings
{
    public static FeedItemDto ToDto(this FeedItem feedItem) => new FeedItemDto { /* 1:1 mapping */ };
}
```

## Downstream Consumers

These types are used by:
- **Section 03 (Queries)**: `FeedItemDto`, `FeedSummaryDto`, `TrendingTopicDto` as return types
- **Section 04 (Commands)**: `ActOnFeedItemRequest`, `BatchReadRequest`, `BatchDismissRequest`, `BatchActRequest` as input types; validators run via MediatR pipeline
- **Section 05 (Endpoints)**: All DTOs and request records flow through the API layer
- **Section 06 (SignalR Hub)**: `FeedItemDto` and `FeedSummaryDto` for push notifications
- **Section 07 (Seed Data)**: Data JSON schemas for generating realistic test items
- **Mappings**: `ToDto()` is called in queries and commands to project entities to DTOs

## Actual Implementation Notes

### Deviations from Plan
1. `BatchActRequest.Ids` uses `IReadOnlyList<Guid>` instead of `List<Guid>` (code review fix — project immutability convention)
2. `ActOnFeedItemRequest.AdditionalData` uses `IReadOnlyDictionary<string, string>?` instead of `Dictionary<string, string>?` (same reason)
3. `ActOnFeedItemRequestValidator.KnownActions` is a `static readonly string[]` shared by `BatchActRequestValidator` — avoids duplication

### Test Count
19 tests across 4 files: 3 validator test classes + 1 mapping test class
