# Section 01: Data Model

## Overview

This section adds the foundational data model for the blog publishing workflow. It introduces four new entities (`ChatConversation`, `SubstackDetection`, `UserNotification`, `BlogPublishRequest`), five new columns on the existing `Content` entity, three new enums, new configuration options classes, and an EF Core migration with key indexes.

All subsequent sections depend on this one. No other sections need to be completed first.

---

## Dependencies

- **Depends on**: Nothing (foundation section)
- **Blocks**: All other sections (02 through 13)

---

## Existing Codebase Context

**Base classes** (in `src/PersonalBrandAssistant.Domain/Common/`):
- `EntityBase`: provides `Guid Id` (v7), domain events list
- `AuditableEntityBase : EntityBase, IAuditable`: adds `CreatedAt`, `UpdatedAt`

**Existing entities being modified**:
- `Content` (at `src/PersonalBrandAssistant.Domain/Entities/Content.cs`): has `ContentType`, `Title`, `Body`, `Status`, `Metadata` (JSON), `TargetPlatforms` (PlatformType[]), `ScheduledAt`, `PublishedAt`. Uses `AuditableEntityBase`.
- `ContentPlatformStatus` (at `src/PersonalBrandAssistant.Domain/Entities/ContentPlatformStatus.cs`): has `ContentId`, `Platform`, `Status`, `PlatformPostId`, `PostUrl`, `PublishedAt`, `Version` (concurrency). Uses `AuditableEntityBase`. Does NOT currently have a `ScheduledAt` field.

**Existing enums**:
- `PlatformType`: `TwitterX, LinkedIn, Instagram, YouTube, Reddit, PersonalBlog, Substack`
- `ContentType`: `BlogPost, SocialPost, Thread, VideoDescription`
- `ContentStatus`: `Draft, Review, Approved, Scheduled, Publishing, Published, Failed, Archived`
- `PlatformPublishStatus`: `Pending, Published, Failed, RateLimited, Skipped, Processing`
- `NotificationType`: existing values for content and platform notifications

**JSON column pattern**: Uses `JsonValueConverter<T>` with `HasColumnType("jsonb")` for PostgreSQL JSON columns.

**Options pattern**: Options classes live at `src/PersonalBrandAssistant.Application/Common/Models/` with `const string SectionName`.

**DbContext**: `ApplicationDbContext` at `src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs` with `IApplicationDbContext` interface.

**EF Configuration pattern**: Each entity has an `IEntityTypeConfiguration<T>` class in `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/`.

---

## Tests (Write These First)

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Data/BlogDataModelTests.cs`

```csharp
// --- New Entity Persistence ---
// Test: ChatConversation can be created and persisted with JSON Messages column
// Test: ChatConversation.Messages stores and retrieves JSON correctly (special chars, markdown, code blocks)
// Test: SubstackDetection can be created and persisted
// Test: SubstackDetection.RssGuid unique index prevents duplicate inserts
// Test: SubstackDetection.SubstackUrl unique index prevents duplicate detection
// Test: UserNotification can be created and persisted with Pending status
// Test: UserNotification unique filtered index on (ContentId, Type) where Status = Pending
// Test: BlogPublishRequest can be created and persisted

// --- Modified Content Entity ---
// Test: Content new columns persist correctly (SubstackPostUrl, BlogPostUrl, BlogDeployCommitSha, BlogDelayOverride, BlogSkipped)
// Test: Content.BlogDelayOverride stores TimeSpan correctly (null and non-null)
// Test: Content.BlogSkipped defaults to false

// --- ContentPlatformStatus ScheduledAt ---
// Test: ContentPlatformStatus.ScheduledAt persists correctly

// --- Dashboard Index ---
// Test: Content filtered index on (ContentType, Status) for BlogPost queries
```

### File: `tests/PersonalBrandAssistant.Domain.Tests/Entities/BlogEntityTests.cs`

```csharp
// Test: MatchConfidence enum has expected values (High, Medium, Low, None)
// Test: NotificationStatus enum has expected values (Pending, Acknowledged, Acted)
// Test: BlogPublishStatus enum has expected values (Staged, Publishing, Published, Failed)
// Test: ChatMessage record constructs correctly with Role, Content, Timestamp
// Test: SubstackDetection initializes with correct default values
// Test: UserNotification status transitions: Pending -> Acknowledged -> Acted
// Test: NotificationType enum includes SubstackDetected and BlogReady values
```

---

## Implementation Details

### 1. New Enums

**File: `src/PersonalBrandAssistant.Domain/Enums/MatchConfidence.cs`**
```csharp
public enum MatchConfidence { High, Medium, Low, None }
```

**File: `src/PersonalBrandAssistant.Domain/Enums/NotificationStatus.cs`**
```csharp
public enum NotificationStatus { Pending, Acknowledged, Acted }
```

**File: `src/PersonalBrandAssistant.Domain/Enums/BlogPublishStatus.cs`**
```csharp
public enum BlogPublishStatus { Staged, Publishing, Published, Failed }
```

### 2. Extend NotificationType Enum

**Modify: `src/PersonalBrandAssistant.Domain/Enums/NotificationType.cs`** -- Add `SubstackDetected` and `BlogReady` values.

### 3. New Entities

**File: `src/PersonalBrandAssistant.Domain/Entities/ChatConversation.cs`**
- Extends `AuditableEntityBase`
- `ContentId` (Guid, required, unique -- one conversation per content)
- `Messages` (`List<ChatMessage>`, JSON column)
- `ConversationSummary` (string?, for windowing)
- `LastMessageAt` (DateTimeOffset)
- `ChatMessage` record: `ChatMessage(string Role, string Content, DateTimeOffset Timestamp)`

**File: `src/PersonalBrandAssistant.Domain/Entities/SubstackDetection.cs`**
- Extends `EntityBase`
- `ContentId` (Guid?, nullable -- null if unmatched)
- `RssGuid` (string, required, unique indexed)
- `Title` (string, required)
- `SubstackUrl` (string, required, unique indexed)
- `PublishedAt` (DateTimeOffset)
- `DetectedAt` (DateTimeOffset)
- `Confidence` (MatchConfidence)
- `ContentHash` (string, SHA-256)

**File: `src/PersonalBrandAssistant.Domain/Entities/UserNotification.cs`**
- Extends `EntityBase`
- `Type` (string, required)
- `Message` (string, required)
- `ContentId` (Guid?, nullable)
- `Status` (NotificationStatus, default Pending)
- `CreatedAt` (DateTimeOffset)
- `AcknowledgedAt` (DateTimeOffset?, nullable)

**File: `src/PersonalBrandAssistant.Domain/Entities/BlogPublishRequest.cs`**
- Extends `AuditableEntityBase`
- `ContentId` (Guid, required)
- `Html` (string, required)
- `TargetPath` (string, required)
- `Status` (BlogPublishStatus, default Staged)
- `CommitSha`, `CommitUrl`, `BlogUrl` (nullable strings)
- `ErrorMessage` (nullable string)
- `VerificationAttempts` (int, default 0)

### 4. Modify Content Entity

**Modify: `src/PersonalBrandAssistant.Domain/Entities/Content.cs`** -- Add:
```csharp
public string? SubstackPostUrl { get; set; }
public string? BlogPostUrl { get; set; }
public string? BlogDeployCommitSha { get; set; }
public TimeSpan? BlogDelayOverride { get; set; }  // null = use global default
public bool BlogSkipped { get; set; }
```

### 5. Modify ContentPlatformStatus Entity

**Modify: `src/PersonalBrandAssistant.Domain/Entities/ContentPlatformStatus.cs`** -- Add:
```csharp
public DateTimeOffset? ScheduledAt { get; set; }
```

### 6. New Domain Event

**File: `src/PersonalBrandAssistant.Domain/Events/SubstackPublicationDetectedEvent.cs`**
```csharp
public sealed record SubstackPublicationDetectedEvent(
    Guid ContentId, string SubstackUrl, DateTimeOffset PublishedAt) : Common.IDomainEvent;
```

### 7. EF Core Configurations

Create four new configuration classes in `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/`:

- **ChatConversationConfiguration**: JSON column for Messages, unique index on ContentId, cascade delete
- **SubstackDetectionConfiguration**: unique index on RssGuid, unique index on SubstackUrl, max lengths
- **UserNotificationConfiguration**: unique filtered index on (ContentId, Type) where Status = Pending
- **BlogPublishRequestConfiguration**: cascade delete, concurrency token

### 8. Modify Existing Configurations

- **ContentConfiguration**: Add property configs for 5 new columns + filtered composite index on (ContentType, Status) for BlogPost
- **ContentPlatformStatusConfiguration**: Add ScheduledAt config

### 9. Update DbContext and Interface

Add four new `DbSet` properties to both `ApplicationDbContext` and `IApplicationDbContext`.

### 10. Configuration Options Classes

**File: `src/PersonalBrandAssistant.Application/Common/Models/BlogChatOptions.cs`**
- Model, MaxTokens, SystemPromptPath, RecentMessageCount, FinalizationMaxRetries

**File: `src/PersonalBrandAssistant.Application/Common/Models/BlogPublishOptions.cs`**
- RepoOwner, RepoName, Branch, ContentPath, FilePattern, TemplatePath, AuthorName, AuthorEmail, DeployVerificationUrlPattern, DeployVerificationInitialDelaySeconds, DeployVerificationMaxRetries

**File: `src/PersonalBrandAssistant.Application/Common/Models/PublishDelayOptions.cs`**
- DefaultSubstackToBlogDelay (7 days), RequiresConfirmation (true)

**Modify: `src/PersonalBrandAssistant.Application/Common/Models/SubstackOptions.cs`**
- Add PollingIntervalMinutes, MatchConfidenceThreshold, EnableConditionalGet

### 11. EF Core Migration

```bash
cd src/PersonalBrandAssistant.Infrastructure
dotnet ef migrations add AddBlogPublishingWorkflow --startup-project ../PersonalBrandAssistant.Api
```

---

## File Summary

### New Files
| File | Purpose |
|------|---------|
| `Domain/Enums/MatchConfidence.cs` | Enum |
| `Domain/Enums/NotificationStatus.cs` | Enum |
| `Domain/Enums/BlogPublishStatus.cs` | Enum |
| `Domain/Entities/ChatConversation.cs` | Entity + ChatMessage record |
| `Domain/Entities/SubstackDetection.cs` | RSS detection audit trail |
| `Domain/Entities/UserNotification.cs` | Three-state notification |
| `Domain/Entities/BlogPublishRequest.cs` | Staged blog publish |
| `Domain/Events/SubstackPublicationDetectedEvent.cs` | Domain event |
| `Infrastructure/Data/Configurations/ChatConversationConfiguration.cs` | EF config |
| `Infrastructure/Data/Configurations/SubstackDetectionConfiguration.cs` | EF config |
| `Infrastructure/Data/Configurations/UserNotificationConfiguration.cs` | EF config |
| `Infrastructure/Data/Configurations/BlogPublishRequestConfiguration.cs` | EF config |
| `Application/Common/Models/BlogChatOptions.cs` | Options |
| `Application/Common/Models/BlogPublishOptions.cs` | Options |
| `Application/Common/Models/PublishDelayOptions.cs` | Options |

### Files to Modify
| File | Change |
|------|--------|
| `Domain/Enums/NotificationType.cs` | Add SubstackDetected, BlogReady |
| `Domain/Entities/Content.cs` | Add 5 new properties |
| `Domain/Entities/ContentPlatformStatus.cs` | Add ScheduledAt |
| `Infrastructure/Data/ApplicationDbContext.cs` | Add 4 DbSets |
| `Application/Common/Interfaces/IApplicationDbContext.cs` | Add 4 DbSets |
| `Infrastructure/Data/Configurations/ContentConfiguration.cs` | New columns + index |
| `Infrastructure/Data/Configurations/ContentPlatformStatusConfiguration.cs` | ScheduledAt |
| `Application/Common/Models/SubstackOptions.cs` | Add polling options |

## Implementation Order

1. Create enums → 2. Extend NotificationType → 3. Create entities → 4. Create domain event → 5. Modify Content + ContentPlatformStatus → 6. Create EF configurations → 7. Modify existing configs → 8. Update DbContext → 9. Create options classes → 10. Generate migration → 11. Write tests

---

## Implementation Notes (Actual)

**Status:** COMPLETE
**Tests:** 7 domain + 12 integration = 19 passing
**Test files:**
- `tests/PersonalBrandAssistant.Domain.Tests/Entities/BlogEntityTests.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Data/BlogDataModelTests.cs`

**Deviations from plan:**
- EF migration generation deferred until all data model sections complete (avoids intermediate migrations)
- Also fixed pre-existing build errors in DashboardAggregatorTests (missing IEnumerable<ISocialPlatform> constructor arg)
