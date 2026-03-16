I have enough context now. Let me generate the section content.

# Section 05 — Content Repurposing

## Overview

This section implements the content repurposing system: an `IRepurposingService` interface and its `RepurposingService` implementation. The service transforms a "pillar" content piece (e.g., a blog post) into derivative content for other platforms (threads, social posts, video descriptions). It supports tree-structured parent-child relationships, max depth enforcement, idempotency constraints, and autonomy-driven behavior.

**Dependencies:**
- Section 01 (Domain Entities) -- `Content` entity with `TreeDepth` and `RepurposeSourcePlatform` fields, plus the `ContentType` and `PlatformType` enums
- Section 04 (Content Pipeline) -- `IContentPipeline` for generating draft content from repurpose tasks, `ISidecarClient` for AI generation

**Blocks:**
- Section 10 (Background Processors) -- `RepurposeOnPublishProcessor` consumes `IRepurposingService`
- Section 11 (API Endpoints) -- `RepurposingEndpoints` exposes repurposing via HTTP

---

## Tests First

All tests use xUnit + Moq + MockQueryable. Naming: `{Class}Tests`, methods `{Method}_{Scenario}_{Expected}`. AAA pattern.

### Test File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/RepurposingServiceTests.cs`

```csharp
namespace PersonalBrandAssistant.Infrastructure.Tests.Services.ContentServices;

/// <summary>
/// Tests for RepurposingService covering:
/// - Child content creation per target platform
/// - ParentContentId linkage on children
/// - RepurposeSourcePlatform tracking
/// - Max tree depth enforcement (default 3)
/// - Idempotency: skip if child already exists for (ParentContentId, Platform, ContentType)
/// - Suggestion generation with confidence scores
/// - Content tree retrieval (recursive descendant query)
/// </summary>
public class RepurposingServiceTests
{
    // Mocks: IApplicationDbContext, ISidecarClient, IContentPipeline, IOptions<ContentEngineOptions>

    // RepurposeAsync_WithValidSource_CreatesChildContentForEachTargetPlatform
    //   Arrange: Source content (BlogPost, Published), target platforms [TwitterX, LinkedIn]
    //   Act: Call RepurposeAsync
    //   Assert: Two new Content entities created, both in Draft status

    // RepurposeAsync_SetsParentContentIdOnChildren
    //   Arrange: Source content with known Id
    //   Act: Call RepurposeAsync
    //   Assert: Each child's ParentContentId == source.Id

    // RepurposeAsync_SetsRepurposeSourcePlatform
    //   Arrange: Source content originally for LinkedIn
    //   Act: Repurpose to TwitterX
    //   Assert: Child's RepurposeSourcePlatform == PlatformType.LinkedIn

    // RepurposeAsync_RespectsMaxTreeDepth_FailsIfExceeded
    //   Arrange: Source content at TreeDepth == 3 (default max)
    //   Act: Call RepurposeAsync
    //   Assert: Result.IsSuccess == false, ErrorCode == ValidationFailed

    // RepurposeAsync_IsIdempotent_SkipsExistingChildForSameParentPlatformType
    //   Arrange: Child already exists for (ParentId, TwitterX, Thread)
    //   Act: Call RepurposeAsync with same target
    //   Assert: No duplicate created, result contains only newly created IDs

    // SuggestRepurposingAsync_ReturnsSuggestionsWithConfidenceScores
    //   Arrange: Source content (BlogPost), mock sidecar returns suggestions JSON
    //   Act: Call SuggestRepurposingAsync
    //   Assert: Returns list of RepurposingSuggestion with Platform, SuggestedType, Rationale, ConfidenceScore

    // GetContentTreeAsync_ReturnsFullDescendantTree
    //   Arrange: Root -> Child A -> Grandchild B; Root -> Child C
    //   Act: Call GetContentTreeAsync(rootId)
    //   Assert: Returns 3 descendants (A, B, C) in a tree structure
}
```

### Test File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/RepurposingAutonomyTests.cs`

```csharp
namespace PersonalBrandAssistant.Infrastructure.Tests.Services.ContentServices;

/// <summary>
/// Tests for autonomy-driven repurposing behavior:
/// - Autonomous: auto-triggers on publish for all configured platforms
/// - SemiAuto: auto-triggers only for published content (not just approved)
/// - Manual: creates suggestions only, no auto-generation
/// </summary>
public class RepurposingAutonomyTests
{
    // AutoTrigger_Autonomous_RepurposesOnPublish
    //   Arrange: AutonomyLevel.Autonomous, content transitions to Published
    //   Assert: RepurposeAsync called for all configured target platforms

    // AutoTrigger_SemiAuto_RepurposesOnlyPublishedContent
    //   Arrange: AutonomyLevel.SemiAuto, content transitions to Published
    //   Assert: RepurposeAsync called
    //   Arrange: AutonomyLevel.SemiAuto, content transitions to Approved
    //   Assert: RepurposeAsync NOT called

    // AutoTrigger_Manual_CreatesSuggestionsOnly
    //   Arrange: AutonomyLevel.Manual, content transitions to Published
    //   Assert: SuggestRepurposingAsync called, RepurposeAsync NOT called
}
```

### Test File: `tests/PersonalBrandAssistant.Infrastructure.Tests/BackgroundJobs/RepurposeOnPublishProcessorTests.cs`

These tests are documented here for context but are **implemented in Section 10** (Background Processors):

```csharp
/// <summary>
/// Tests for RepurposeOnPublishProcessor:
/// - Triggers on Published status change
/// - Checks autonomy level before processing
/// - Is idempotent on duplicate events
/// </summary>
```

---

## Implementation Details

### 1. Application Layer: Interface and Models

#### File: `src/PersonalBrandAssistant.Application/Common/Interfaces/IRepurposingService.cs`

```csharp
namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IRepurposingService
{
    /// <summary>
    /// Creates derivative content for each target platform from the source content.
    /// Returns the IDs of newly created child Content entities.
    /// </summary>
    Task<Result<IReadOnlyList<Guid>>> RepurposeAsync(
        Guid sourceContentId, PlatformType[] targetPlatforms, CancellationToken ct);

    /// <summary>
    /// Uses AI to suggest repurposing opportunities with confidence scores.
    /// Does not create content — advisory only.
    /// </summary>
    Task<Result<IReadOnlyList<RepurposingSuggestion>>> SuggestRepurposingAsync(
        Guid contentId, CancellationToken ct);

    /// <summary>
    /// Returns the full descendant tree of content relationships starting from the root.
    /// </summary>
    Task<Result<IReadOnlyList<Content>>> GetContentTreeAsync(Guid rootId, CancellationToken ct);
}
```

#### File: `src/PersonalBrandAssistant.Application/Common/Models/RepurposingSuggestion.cs`

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

public record RepurposingSuggestion(
    PlatformType Platform,
    ContentType SuggestedType,
    string Rationale,
    float ConfidenceScore);
```

### 2. Domain Layer: Content Entity Modifications

Section 01 adds two fields to the `Content` entity. For this section's purposes, the following fields must exist:

- `Content.TreeDepth` (int, default 0) -- Tracks the depth of this content in the repurposing tree. Root content has depth 0, first-level derivatives have depth 1, etc.
- `Content.RepurposeSourcePlatform` (nullable `PlatformType`) -- Records which platform the source content was originally targeted at when this piece was repurposed from it.

The `Content.ParentContentId` (nullable `Guid`) already exists and supports arbitrary parent-child depth.

### 3. EF Core: Idempotency Constraint

Section 01 adds the EF Core configuration. The critical constraint for repurposing is:

A unique index on `(ParentContentId, Platform, ContentType)` where `ParentContentId IS NOT NULL`. This prevents duplicate repurposed children from event replays or concurrent triggers. The configuration should use a filtered unique index:

```csharp
// In ContentConfiguration.cs (added by Section 01)
builder.HasIndex(c => new { c.ParentContentId, c.RepurposeSourcePlatform, c.ContentType })
    .IsUnique()
    .HasFilter("\"ParentContentId\" IS NOT NULL");
```

Note: The index uses `RepurposeSourcePlatform` rather than a separate "target platform" column because each child Content targets a specific platform and the combination of parent + source platform + content type must be unique.

### 4. Infrastructure Layer: RepurposingService

#### File: `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/RepurposingService.cs`

The service depends on:
- `IApplicationDbContext` -- EF Core database access
- `ISidecarClient` -- AI generation via the claude-code-sidecar (from Section 02)
- `IOptions<ContentEngineOptions>` -- Configuration including `MaxTreeDepth` (default 3)

**RepurposeAsync flow:**

1. Load source content by ID. Return `NotFound` if missing.
2. Validate tree depth: if `source.TreeDepth >= options.MaxTreeDepth`, return `ValidationFailed("Maximum repurposing depth exceeded")`.
3. For each target platform:
   a. Determine the appropriate `ContentType` for the platform (e.g., TwitterX gets Thread, LinkedIn gets SocialPost). This mapping can be a simple dictionary or derived from the suggestion engine.
   b. Check idempotency: query for existing child with matching `(ParentContentId, RepurposeSourcePlatform, ContentType)`. Skip if found.
   c. Parse source content into structured components (key points, quotes, statistics) -- this is done by sending an extraction prompt to the sidecar.
   d. Send a repurposing prompt to the sidecar with: source components, platform constraints (character limits, formatting rules), brand voice context.
   e. Create a new `Content` entity using `Content.Create(...)` with:
      - `ParentContentId` = source content ID
      - `TreeDepth` = source.TreeDepth + 1
      - `RepurposeSourcePlatform` = the platform the source content was targeting (first of `source.TargetPlatforms`, or null if multi-platform)
      - `TargetPlatforms` = `[targetPlatform]`
      - `Body` = generated text from sidecar
      - `CapturedAutonomyLevel` = current autonomy level
   f. Add to DbContext.
4. `SaveChangesAsync` -- the unique constraint enforces idempotency at the DB level as a safety net.
5. Return the list of newly created content IDs.

**SuggestRepurposingAsync flow:**

1. Load source content by ID.
2. Send a suggestion prompt to the sidecar asking it to analyze the content and recommend platforms + content types for repurposing, with a rationale and confidence score for each.
3. Parse the sidecar's structured JSON response into `RepurposingSuggestion` records.
4. Return the list sorted by confidence score descending.

**GetContentTreeAsync flow:**

1. Use a recursive CTE query (via raw SQL or a manual iterative approach with EF Core) to load all descendants of the given root ID.
2. The query walks `ParentContentId` relationships, collecting all children, grandchildren, etc.
3. Return as a flat list. The caller can reconstruct the tree using `ParentContentId` references.

PostgreSQL recursive CTE approach:

```sql
WITH RECURSIVE tree AS (
    SELECT * FROM "Contents" WHERE "ParentContentId" = @rootId
    UNION ALL
    SELECT c.* FROM "Contents" c
    INNER JOIN tree t ON c."ParentContentId" = t."Id"
)
SELECT * FROM tree;
```

In EF Core, use `FromSqlInterpolated` for this query (per security rules, always use interpolated form for parameterization).

### 5. Configuration

#### In `ContentEngineOptions` (created by Section 04):

```csharp
public class ContentEngineOptions
{
    public const string SectionName = "ContentEngine";
    // ... other options from Section 04 ...
    public int MaxTreeDepth { get; set; } = 3;
}
```

Bound from `appsettings.json`:

```json
{
  "ContentEngine": {
    "MaxTreeDepth": 3
  }
}
```

### 6. Autonomy-Driven Behavior

The repurposing service itself does not enforce autonomy -- it executes when called. The autonomy logic lives in the caller (the `RepurposeOnPublishProcessor` background service from Section 10, and the API endpoints from Section 11):

| Autonomy Level | Behavior on Content Publish |
|---|---|
| **Autonomous** | `RepurposeOnPublishProcessor` auto-triggers `RepurposeAsync` for all configured target platforms |
| **SemiAuto** | Processor triggers only when content reaches `Published` status (not just `Approved`) |
| **Manual** | Processor calls `SuggestRepurposingAsync` to create advisory suggestions; user triggers `RepurposeAsync` manually via API |

The autonomy level is read from the existing `AutonomyConfiguration` entity or user settings at runtime.

### 7. Platform-to-ContentType Mapping

A simple default mapping used when the caller does not specify content types:

| Target Platform | Default ContentType |
|---|---|
| TwitterX | Thread |
| LinkedIn | SocialPost |
| Instagram | SocialPost |
| YouTube | VideoDescription |

This mapping should be a static dictionary in `RepurposingService` or configurable via `ContentEngineOptions`.

### 8. DI Registration

In `DependencyInjection.cs` (Section 12 handles the full wiring, but the registration for this service is):

```csharp
services.AddScoped<IRepurposingService, RepurposingService>();
```

---

## File Summary

| File | Action |
|---|---|
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IRepurposingService.cs` | Create |
| `src/PersonalBrandAssistant.Application/Common/Models/RepurposingSuggestion.cs` | Create |
| `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/RepurposingService.cs` | Create |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/RepurposingServiceTests.cs` | Create |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/RepurposingAutonomyTests.cs` | Create |
| `src/PersonalBrandAssistant.Domain/Entities/Content.cs` | Modified by Section 01 (TreeDepth, RepurposeSourcePlatform) |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentConfiguration.cs` | Modified by Section 01 (unique index) |
| `src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs` | Modified (add MaxTreeDepth) -- created by Section 04 |

---

## Implementation Notes (Actual vs Planned)

**Date implemented:** 2026-03-16

### Files actually created
- `src/PersonalBrandAssistant.Application/Common/Interfaces/IRepurposingService.cs`
- `src/PersonalBrandAssistant.Application/Common/Models/RepurposingSuggestion.cs`
- `src/PersonalBrandAssistant.Application/Common/Models/ContentEngineOptions.cs` — Created here (not section-04)
- `src/PersonalBrandAssistant.Infrastructure/Services/ContentServices/RepurposingService.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ContentServices/RepurposingServiceTests.cs`
- `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` — Modified (added IRepurposingService + ContentEngineOptions binding)

### Deviations from plan
1. **RepurposingAutonomyTests deferred** — Autonomy logic is in the caller (Section 10 processor), not RepurposingService. Tests belong there.
2. **GetContentTreeAsync uses iterative BFS** instead of recursive CTE — per-level `ToListAsync` queries instead of raw SQL. Simpler and avoids `FromSqlInterpolated` complexity.
3. **Idempotency check includes TargetPlatforms** — Plan only checked `(RepurposeSourcePlatform, ContentType)` but LinkedIn and Instagram both map to SocialPost, causing false collisions. Fixed in review.

### Code review fixes applied
1. GetContentTreeAsync: replaced full-table `.ToList()` with iterative per-level `ToListAsync()` BFS
2. Idempotency: added `TargetPlatforms.Contains(targetPlatform)` to dedup check
3. Existing children query: `.ToList()` → `.ToListAsync(ct)`

### Test counts
- RepurposingServiceTests: 12 tests
- **Total: 12 tests for section-05**