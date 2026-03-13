# Section 02 -- Domain Layer

## Overview

This section implements the entire domain layer for the Personal Brand Assistant. The domain layer is the innermost layer of the Clean Architecture and has zero external dependencies (no NuGet packages, no references to other projects). It contains entity definitions, enums, value objects (complex types), a content status state machine, and domain events.

**Project:** `src/PersonalBrandAssistant.Domain/PersonalBrandAssistant.Domain.csproj`

**Depends on:** Section 01 (scaffolding must be complete -- .sln, project files, and Directory.Build.props must exist)

**Blocks:** Section 03 (Application layer), Section 04 (Infrastructure layer), Section 08 (Testing)

---

## Tests First

All tests go in `tests/PersonalBrandAssistant.Domain.Tests/`. The test project references the Domain project and uses xUnit. Write these tests before implementing the domain classes.

### Entity Base Class Tests

File: `tests/PersonalBrandAssistant.Domain.Tests/Common/EntityBaseTests.cs`

- Test that an entity ID is generated as a valid UUIDv7 (version byte equals 7, timestamp is extractable).
- Test that two entities created sequentially have IDs that sort chronologically (second ID is greater than first).

### Content Status State Machine Tests

File: `tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentTests.cs`

These tests exercise the `TransitionTo(ContentStatus newStatus)` method on the `Content` entity:

- New Content defaults to `Draft` status.
- `Draft` to `Review` succeeds.
- `Draft` to `Archived` succeeds.
- `Draft` to `Published` throws `InvalidOperationException`.
- `Review` to `Draft` succeeds (send back for edits).
- `Review` to `Approved` succeeds.
- `Approved` to `Scheduled` succeeds.
- `Approved` to `Draft` succeeds (revert).
- `Scheduled` to `Publishing` succeeds.
- `Scheduled` to `Draft` succeeds (unschedule).
- `Publishing` to `Published` succeeds.
- `Publishing` to `Failed` succeeds.
- `Publishing` to `Draft` throws (cannot go back mid-publish).
- `Published` to `Archived` succeeds.
- `Published` to `Draft` throws (must archive first).
- `Failed` to `Draft` succeeds (retry).
- `Failed` to `Archived` succeeds.
- `Archived` to `Draft` succeeds (restore).
- `Archived` to `Published` throws.
- `TransitionTo` raises a `ContentStateChangedEvent` with the correct old and new status values on a successful transition.

### Content TargetPlatforms Tests

Same file as above:

- Content created with multiple `PlatformType` values stores them correctly.
- Content with an empty `TargetPlatforms` array is valid.

### ContentMetadata Tests

File: `tests/PersonalBrandAssistant.Domain.Tests/ValueObjects/ContentMetadataTests.cs`

- `ContentMetadata` with all fields populated creates a valid object.
- `ContentMetadata` with null optional fields (`AiGenerationContext`, `TokensUsed`, `EstimatedCost`) is valid.
- `Tags` and `SeoKeywords` are initialized as empty lists (not null).

### Platform Tests

File: `tests/PersonalBrandAssistant.Domain.Tests/Entities/PlatformTests.cs`

- Platform created with all required fields succeeds.
- `EncryptedAccessToken` and `EncryptedRefreshToken` are `byte[]` properties (not auto-decrypted strings).

### BrandProfile Tests

File: `tests/PersonalBrandAssistant.Domain.Tests/Entities/BrandProfileTests.cs`

- `BrandProfile` with valid fields creates successfully.
- `ToneDescriptors` and `Topics` initialize as empty lists.

### ContentCalendarSlot Tests

File: `tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentCalendarSlotTests.cs`

- Slot with a valid IANA `TimeZoneId` creates successfully.
- Slot with a `RecurrencePattern` stores the cron string.
- Non-recurring slot has a null `RecurrencePattern`.

### AuditLogEntry Tests

File: `tests/PersonalBrandAssistant.Domain.Tests/Entities/AuditLogEntryTests.cs`

- `AuditLogEntry` created with all required fields.
- `OldValue` and `NewValue` accept null.

### User Tests

File: `tests/PersonalBrandAssistant.Domain.Tests/Entities/UserTests.cs`

- User created with a valid `TimeZoneId`.
- `Settings` (UserSettings) is not null by default.

### Enum Tests

File: `tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs`

- `ContentType` has exactly 4 values: `BlogPost`, `SocialPost`, `Thread`, `VideoDescription`.
- `ContentStatus` has exactly 8 values: `Draft`, `Review`, `Approved`, `Scheduled`, `Publishing`, `Published`, `Failed`, `Archived`.
- `PlatformType` has exactly 4 values: `TwitterX`, `LinkedIn`, `Instagram`, `YouTube`.
- `AutonomyLevel` has exactly 4 values: `Manual`, `Assisted`, `SemiAuto`, `Autonomous`.

---

## Implementation Details

### File Structure

```
src/PersonalBrandAssistant.Domain/
├── Common/
│   ├── EntityBase.cs
│   ├── IAuditable.cs
│   └── IDomainEvent.cs
├── Entities/
│   ├── Content.cs
│   ├── Platform.cs
│   ├── BrandProfile.cs
│   ├── ContentCalendarSlot.cs
│   ├── AuditLogEntry.cs
│   └── User.cs
├── Enums/
│   ├── ContentType.cs
│   ├── ContentStatus.cs
│   ├── PlatformType.cs
│   └── AutonomyLevel.cs
├── ValueObjects/
│   ├── ContentMetadata.cs
│   ├── PlatformRateLimitState.cs
│   ├── PlatformSettings.cs
│   ├── VocabularyConfig.cs
│   └── UserSettings.cs
└── Events/
    └── ContentStateChangedEvent.cs
```

### Entity Base Class

File: `src/PersonalBrandAssistant.Domain/Common/EntityBase.cs`

All entities inherit from this base class. It provides:

- `Id` (Guid) -- Generated via `Guid.CreateVersion7()` (native .NET 10 UUIDv7). This gives temporal locality for index performance in PostgreSQL.
- `CreatedAt` (DateTimeOffset) -- Set by the auditable interceptor (Infrastructure layer) on insert.
- `UpdatedAt` (DateTimeOffset) -- Set by the auditable interceptor on insert and update.

The class should also maintain a list of domain events (`IReadOnlyList<IDomainEvent>`) with an `AddDomainEvent` protected method and a `ClearDomainEvents` public method (used by the dispatcher after publishing).

### IAuditable Interface

File: `src/PersonalBrandAssistant.Domain/Common/IAuditable.cs`

Marker interface with `CreatedAt` and `UpdatedAt` properties. `EntityBase` implements this. The Infrastructure layer's `SaveChangesInterceptor` detects entities implementing `IAuditable` and sets timestamps automatically.

### IDomainEvent Interface

File: `src/PersonalBrandAssistant.Domain/Common/IDomainEvent.cs`

Marker interface for domain events. Should extend `MediatR.INotification` -- however, since the Domain layer must not reference MediatR, define this as an empty marker interface. The Application layer will handle the MediatR integration by having event handlers accept these types. Alternatively, if keeping it truly dependency-free, define `IDomainEvent` as a plain marker and let the Application layer's dispatcher cast or wrap them for MediatR.

**Design decision:** Keep Domain completely dependency-free. `IDomainEvent` is a plain empty interface. The infrastructure or application layer dispatches them through MediatR by wrapping in a `DomainEventNotification<T>` adapter.

### Enums

All enums are simple, no attributes or methods needed.

File: `src/PersonalBrandAssistant.Domain/Enums/ContentType.cs`

```csharp
public enum ContentType { BlogPost, SocialPost, Thread, VideoDescription }
```

File: `src/PersonalBrandAssistant.Domain/Enums/ContentStatus.cs`

```csharp
public enum ContentStatus { Draft, Review, Approved, Scheduled, Publishing, Published, Failed, Archived }
```

File: `src/PersonalBrandAssistant.Domain/Enums/PlatformType.cs`

```csharp
public enum PlatformType { TwitterX, LinkedIn, Instagram, YouTube }
```

File: `src/PersonalBrandAssistant.Domain/Enums/AutonomyLevel.cs`

```csharp
public enum AutonomyLevel { Manual, Assisted, SemiAuto, Autonomous }
```

### Content Entity

File: `src/PersonalBrandAssistant.Domain/Entities/Content.cs`

This is the central domain entity. It inherits from `EntityBase`.

**Properties:**

| Property | Type | Notes |
|----------|------|-------|
| ContentType | ContentType (enum) | TPH discriminator |
| Title | string? | Optional (social posts may lack titles) |
| Body | string | Required content text or HTML |
| Status | ContentStatus | Defaults to Draft. Private setter -- only changed via TransitionTo |
| Metadata | ContentMetadata | Complex type mapped to jsonb by Infrastructure |
| ParentContentId | Guid? | Self-referential FK for content relationships |
| TargetPlatforms | PlatformType[] | PostgreSQL array with GIN index |
| ScheduledAt | DateTimeOffset? | Always UTC |
| PublishedAt | DateTimeOffset? | Always UTC |
| Version | uint | Optimistic concurrency via PostgreSQL xmin |

**Status State Machine -- `TransitionTo(ContentStatus newStatus)` method:**

This method is the only way to change `Status`. It must:

1. Look up allowed transitions from the current status in a static dictionary.
2. If the transition is not allowed, throw `InvalidOperationException` with a descriptive message including the current and attempted status.
3. If the transition is allowed, capture the old status, set the new status, and raise a `ContentStateChangedEvent` domain event.

The allowed transitions table (implement as a `static readonly Dictionary<ContentStatus, ContentStatus[]>`):

| From | Allowed To |
|------|-----------|
| Draft | Review, Archived |
| Review | Draft, Approved, Archived |
| Approved | Scheduled, Draft, Archived |
| Scheduled | Publishing, Draft, Archived |
| Publishing | Published, Failed |
| Published | Archived |
| Failed | Draft, Archived |
| Archived | Draft |

Use a static factory method `Create(ContentType type, string body, string? title = null, PlatformType[]? targetPlatforms = null)` that initializes the entity with `Draft` status, a new UUIDv7 Id, empty Metadata, and the provided values. This avoids exposing a public constructor with too many parameters.

### Value Objects (Complex Types)

These are not entities -- they have no identity. EF Core 10 maps them as complex types to jsonb columns. They are mutable classes (EF Core complex types require settable properties), but treat them as value-like in domain logic.

**ContentMetadata** (`src/PersonalBrandAssistant.Domain/ValueObjects/ContentMetadata.cs`):
- `Tags` (List\<string\>) -- Initialize to empty list in constructor.
- `SeoKeywords` (List\<string\>) -- Initialize to empty list in constructor.
- `PlatformSpecificData` (Dictionary\<string, string\>) -- Initialize to empty dictionary.
- `AiGenerationContext` (string?) -- Nullable.
- `TokensUsed` (int?) -- Nullable.
- `EstimatedCost` (decimal?) -- Nullable.

**PlatformRateLimitState** (`src/PersonalBrandAssistant.Domain/ValueObjects/PlatformRateLimitState.cs`):
- Design the fields to track rate limit windows, remaining calls, and reset times. Exact fields are flexible for now; a reasonable starting shape would include `RemainingCalls` (int?), `ResetAt` (DateTimeOffset?), and `WindowDuration` (TimeSpan?).

**PlatformSettings** (`src/PersonalBrandAssistant.Domain/ValueObjects/PlatformSettings.cs`):
- Platform-specific configuration. Start with `DefaultHashtags` (List\<string\>), `MaxPostLength` (int?), `AutoCrossPost` (bool).

**VocabularyConfig** (`src/PersonalBrandAssistant.Domain/ValueObjects/VocabularyConfig.cs`):
- `PreferredTerms` (List\<string\>) -- Words to favor.
- `AvoidTerms` (List\<string\>) -- Words to avoid.

**UserSettings** (`src/PersonalBrandAssistant.Domain/ValueObjects/UserSettings.cs`):
- `DefaultAutonomyLevel` (AutonomyLevel) -- Default to Manual.
- `NotificationsEnabled` (bool) -- Default to true.
- `Theme` (string) -- Default to "light".

### Platform Entity

File: `src/PersonalBrandAssistant.Domain/Entities/Platform.cs`

Inherits from `EntityBase`. Represents a social platform connection.

**Properties:**

| Property | Type | Notes |
|----------|------|-------|
| Type | PlatformType | Unique constraint enforced by Infrastructure |
| DisplayName | string | Human-readable name |
| IsConnected | bool | Whether OAuth is complete |
| EncryptedAccessToken | byte[]? | Encrypted via Data Protection API. Never auto-decrypted by EF. |
| EncryptedRefreshToken | byte[]? | Same encryption approach |
| TokenExpiresAt | DateTimeOffset? | |
| RateLimitState | PlatformRateLimitState | Complex type to jsonb |
| LastSyncAt | DateTimeOffset? | |
| Settings | PlatformSettings | Complex type to jsonb |
| Version | uint | Optimistic concurrency via xmin |

### BrandProfile Entity

File: `src/PersonalBrandAssistant.Domain/Entities/BrandProfile.cs`

Inherits from `EntityBase`. Configurable brand voice injected into AI prompts.

**Properties:**

| Property | Type | Notes |
|----------|------|-------|
| Name | string | Profile name |
| ToneDescriptors | List\<string\> | Initialize to empty list |
| StyleGuidelines | string | Prose description |
| VocabularyPreferences | VocabularyConfig | Complex type to jsonb |
| Topics | List\<string\> | Initialize to empty list |
| PersonaDescription | string | Who the brand represents |
| ExampleContent | List\<string\> | Few-shot examples for AI |
| IsActive | bool | |
| Version | uint | Optimistic concurrency via xmin |

### ContentCalendarSlot Entity

File: `src/PersonalBrandAssistant.Domain/Entities/ContentCalendarSlot.cs`

Inherits from `EntityBase`. Scheduled content slots for the content calendar.

**Properties:**

| Property | Type | Notes |
|----------|------|-------|
| ScheduledDate | DateOnly | |
| ScheduledTime | TimeOnly? | |
| TimeZoneId | string | IANA timezone (e.g., "America/New_York") |
| Theme | string? | |
| ContentType | ContentType | |
| TargetPlatform | PlatformType | |
| ContentId | Guid? | FK to Content, if assigned |
| IsRecurring | bool | |
| RecurrencePattern | string? | Standard 5-field cron (NCrontab). Null if non-recurring. |

### AuditLogEntry Entity

File: `src/PersonalBrandAssistant.Domain/Entities/AuditLogEntry.cs`

Inherits from `EntityBase`. Tracks state transitions and significant actions.

**Properties:**

| Property | Type | Notes |
|----------|------|-------|
| EntityType | string | |
| EntityId | Guid | |
| Action | string | |
| OldValue | string? | Structured JSON. Max 4KB. Never contains encrypted fields. |
| NewValue | string? | Same constraints |
| Timestamp | DateTimeOffset | |
| Details | string? | |

Note: `AuditLogEntry` does not need `IAuditable` -- it has its own `Timestamp` field and is write-once (never updated).

### User Entity

File: `src/PersonalBrandAssistant.Domain/Entities/User.cs`

Inherits from `EntityBase`. Simple single-user entity.

**Properties:**

| Property | Type | Notes |
|----------|------|-------|
| Email | string | |
| DisplayName | string | |
| TimeZoneId | string | IANA timezone. Default for calendar slots and UI. |
| Settings | UserSettings | Complex type to jsonb. Initialize to default instance. |

### Domain Events

File: `src/PersonalBrandAssistant.Domain/Events/ContentStateChangedEvent.cs`

A record implementing `IDomainEvent`:

```csharp
/// <summary>
/// Raised when content transitions between states via TransitionTo.
/// </summary>
public sealed record ContentStateChangedEvent(
    Guid ContentId,
    ContentStatus OldStatus,
    ContentStatus NewStatus) : IDomainEvent;
```

**Limitation note:** In-process domain events are not crash-resilient. If the process terminates mid-handler, the event is lost. This is acceptable for the foundation layer. An outbox pattern can be added later if autonomous workflows require guaranteed delivery.

---

## Implementation Checklist

1. Write all test stubs in `tests/PersonalBrandAssistant.Domain.Tests/` (they should all fail -- RED phase).
2. Implement enums (4 files in `Enums/`).
3. Implement `IDomainEvent`, `IAuditable`, and `EntityBase` in `Common/`.
4. Implement all value objects in `ValueObjects/` (5 files).
5. Implement `ContentStateChangedEvent` in `Events/`.
6. Implement `Content` entity with the state machine and `Create` factory method.
7. Implement remaining entities: `Platform`, `BrandProfile`, `ContentCalendarSlot`, `AuditLogEntry`, `User`.
8. Run tests -- all should pass (GREEN phase).
9. Refactor if needed (REFACTOR phase) -- ensure immutable patterns where possible, small files, clean naming.

---

## Key Design Decisions

- **Domain has zero NuGet dependencies.** No MediatR, no FluentValidation, no EF Core references in this project.
- **UUIDv7 via `Guid.CreateVersion7()`** -- native .NET 10, no third-party library needed. Provides chronological sorting and index locality.
- **State machine in the entity** -- `Content.TransitionTo()` is the single point for status changes. No external code should set `Status` directly (private setter).
- **Complex types are mutable classes** -- EF Core 10 complex types require settable properties. Initialize collection properties to empty collections in constructors to avoid null reference issues.
- **`byte[]` for encrypted tokens** -- Tokens are stored encrypted. No value converters. The Application layer calls `IEncryptionService` explicitly when it needs the plaintext.
- **All DateTimeOffset values are UTC** -- Timezone conversion happens at the UI layer using the user's `TimeZoneId`.