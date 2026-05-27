# Section 05: ContentPublisher Refactor

## Overview

This section refactors `ContentPublisher` to replace its hardcoded `if (Platform == Blog)` routing with keyed DI connector resolution. It also introduces multi-platform publishing (primary + best-effort secondaries), idempotency checks, and updates the `PublishContent` MediatR command to accept `TargetPlatforms`.

## Dependencies

- **Section 01 (interfaces-and-types):** Provides `IPlatformConnector`, `PlatformPublishRequest`, `PlatformPublishResult`, `PublishResult`, `PlatformPublishOutcome`, `PublishMode` types in the Application layer.
- **Section 02 (domain-model-changes):** Provides `Content.TargetPlatforms` property, `ContentPlatformPublish.RetryCount` and `NextRetryAt` fields.
- **Section 04 (blog-connector-migration):** `BlogConnector` already implements `IPlatformConnector` (not `IBlogConnector`). `IBlogConnector` is removed.

## Background

### Current Architecture

The current `ContentPublisher` at `src/PBA.Infrastructure/Publishing/ContentPublisher.cs` has three problems:

1. **Hardcoded routing:** `if (content.PrimaryPlatform == Platform.Blog)` only publishes to Blog. Adding platforms means adding more conditionals.
2. **Single-platform only:** No concept of publishing to multiple platforms per content item.
3. **No idempotency:** If Hangfire retries a job (e.g., after an app crash), the same content could be double-published.

The current `IContentPublisher` interface at `src/PBA.Application/Common/Interfaces/IContentPublisher.cs`:
```csharp
public interface IContentPublisher
{
    Task PublishAsync(Guid contentId);
}
```

The current `PublishContent` MediatR command at `src/PBA.Application/Features/Content/Commands/PublishContent.cs` takes only `Guid ContentId` and injects `IBlogConnector` directly, duplicating the platform routing logic that also lives in `ContentPublisher`.

Both `HangfireContentScheduler` and `ScheduledPublishReconciler` call `IContentPublisher.PublishAsync(Guid)` -- this signature must be preserved.

### Target Architecture

```
PublishAsync(contentId, targetPlatforms?)
  -> load content
  -> determine target platforms:
       explicit parameter > content.TargetPlatforms > [content.PrimaryPlatform]
  -> idempotency: skip platforms with existing Published records
  -> publish primary platform via keyed connector
  -> if primary fails: abort, return failure
  -> if primary succeeds: fire state machine trigger
  -> publish each secondary platform (parallel, best-effort):
       -> transform content
       -> publish via keyed connector
       -> create ContentPlatformPublish record (Success or Failed)
       -> if failed: schedule retry via IPublishRetryHandler
  -> return aggregate PublishResult
```

Content is considered "published" once the primary platform succeeds. Secondary failures are recorded and retried automatically. The content state machine status is `Published` regardless of secondary outcomes.

---

## Tests (Write First)

Test file: `tests/PBA.Infrastructure.Tests/Publishing/ContentPublisherTests.cs`

This file already exists with 4 tests that use `IBlogConnector`. All existing tests must be migrated to the new architecture. The test class should use `IServiceProvider` with keyed DI mocks instead of injecting `IBlogConnector` directly.

### Test Setup Changes

The `ContentPublisher` constructor changes from:
```csharp
ContentPublisher(IAppDbContext db, IBlogConnector blogConnector, ILogger<ContentPublisher> logger)
```
To:
```csharp
ContentPublisher(IAppDbContext db, IServiceProvider serviceProvider, IContentTransformer transformer, ILogger<ContentPublisher> logger)
```

The test fixture needs a `ServiceCollection` that registers mock `IPlatformConnector` instances as keyed services. The `IContentTransformer` is also mocked.

### Test Stubs

```csharp
// --- Migrated existing tests (rewritten for new architecture) ---

// PublishAsync_PublishesContent_WhenStatusIsScheduled
//   Arrange: Scheduled Blog content, mock Blog connector returns success
//   Act: PublishAsync(contentId)
//   Assert: Content status is Published, PublishedAt is set

// PublishAsync_SkipsPublishing_WhenStatusIsNoLongerScheduled
//   Arrange: Content with Approved status (not Scheduled)
//   Act: PublishAsync(contentId)
//   Assert: Status unchanged, connector never called

// PublishAsync_InvokesPlatformConnector_ForBlogPlatform
//   Arrange: Scheduled Blog content
//   Act: PublishAsync(contentId)
//   Assert: Blog IPlatformConnector.PublishAsync called once

// PublishAsync_CreatesContentPlatformPublishRecord
//   Arrange: Scheduled Blog content, connector returns URL
//   Act: PublishAsync(contentId)
//   Assert: ContentPlatformPublish record exists with Published status and URL

// --- New tests ---

// PublishAsync_ResolvesConnectorByPlatform_ViaKeyedDI
//   Arrange: Scheduled content with PrimaryPlatform = Medium, register Medium keyed connector
//   Act: PublishAsync(contentId)
//   Assert: Medium connector's PublishAsync was called (not Blog)

// PublishAsync_PrimaryFails_AbortsWithoutPublishingSecondaries
//   Arrange: Content with TargetPlatforms = [Blog, Medium], Blog connector returns failure
//   Act: PublishAsync(contentId)
//   Assert: Medium connector never called, content status NOT Published

// PublishAsync_PrimarySucceeds_FiresStateMachineTrigger
//   Arrange: Scheduled content, Blog connector succeeds
//   Act: PublishAsync(contentId)
//   Assert: Content.Status == Published, Content.PublishedAt is set

// PublishAsync_SecondaryFails_CreatesFailedContentPlatformPublishRecord
//   Arrange: Content with TargetPlatforms = [Blog, Medium], Blog succeeds, Medium fails
//   Act: PublishAsync(contentId)
//   Assert: Blog has Published record, Medium has Failed record with ErrorMessage

// PublishAsync_SecondaryFails_SchedulesRetryJob
//   NOTE: Retry scheduling is handled by Section 11 (IPublishRetryHandler).
//   For this section, verify that a Failed ContentPlatformPublish record is created
//   with RetryCount = 0. The actual scheduling is tested in section-11.

// PublishAsync_SkipsPlatformWithExistingPublishedRecord (idempotency)
//   Arrange: Content with TargetPlatforms = [Blog], pre-existing Published record for Blog
//   Act: PublishAsync(contentId)
//   Assert: Blog connector NOT called, existing record unchanged

// PublishAsync_NoTargetPlatforms_UsesContentTargetPlatforms
//   Arrange: Content with TargetPlatforms = [Blog, LinkedIn], call PublishAsync(id, null)
//   Act: PublishAsync(contentId, null)
//   Assert: Both Blog and LinkedIn connectors called

// PublishAsync_NoContentTargetPlatforms_UsesPrimaryPlatformOnly
//   Arrange: Content with empty TargetPlatforms, PrimaryPlatform = Blog
//   Act: PublishAsync(contentId, null)
//   Assert: Only Blog connector called

// PublishAsync_GuidOverload_CallsFullMethodWithNullTargets (Hangfire compat)
//   Arrange: Scheduled Blog content
//   Act: PublishAsync(contentId) (Guid-only overload)
//   Assert: Behaves identically to calling the full method with null targets

// PublishAsync_ParallelSecondaries_AllPublishIndependently
//   Arrange: Content with TargetPlatforms = [Blog, Medium, LinkedIn, Twitter],
//            Blog succeeds, Medium fails, LinkedIn succeeds, Twitter fails
//   Act: PublishAsync(contentId)
//   Assert: 4 ContentPlatformPublish records: Blog Published, Medium Failed,
//           LinkedIn Published, Twitter Failed. State is Published (primary succeeded).
```

### PublishContent MediatR Command Tests

Test file: `tests/PBA.Application.Tests/Features/Content/Commands/PublishContentTests.cs`

If this file doesn't exist, create it. If it does, update existing tests to remove `IBlogConnector` dependency and inject `IContentPublisher` instead.

```csharp
// PublishContent_WithTargetPlatforms_PassesPlatformsToPublisher
//   Arrange: Content exists, mock IContentPublisher
//   Act: Handle(new Command(contentId, [Platform.Blog, Platform.Medium]))
//   Assert: Publisher called with correct target platforms

// PublishContent_WithoutTargetPlatforms_PassesNullToPublisher
//   Arrange: Content exists, mock IContentPublisher
//   Act: Handle(new Command(contentId))
//   Assert: Publisher called with null target platforms

// PublishContent_ContentNotFound_ReturnsNotFound
//   Arrange: No content in DB
//   Act: Handle(new Command(nonExistentId))
//   Assert: Result.IsSuccess == false, FailureType == NotFound
```

---

## Implementation Details

### 1. Update `IContentPublisher` Interface

**File:** `src/PBA.Application/Common/Interfaces/IContentPublisher.cs`

Add a new overload that accepts target platforms. Keep the existing `PublishAsync(Guid)` for Hangfire compatibility.

```csharp
public interface IContentPublisher
{
    Task PublishAsync(Guid contentId);

    Task<PublishResult> PublishAsync(Guid contentId, IReadOnlyList<Platform>? targetPlatforms, CancellationToken ct);
}
```

`PublishResult` and `PlatformPublishOutcome` are defined in Section 01. They are records in the Application layer:
```csharp
public record PublishResult(bool PrimarySuccess, string? PrimaryUrl, IReadOnlyList<PlatformPublishOutcome> SecondaryOutcomes);
public record PlatformPublishOutcome(Platform Platform, bool Success, string? Url, string? Error);
```

### 2. Refactor `ContentPublisher` Implementation

**File:** `src/PBA.Infrastructure/Publishing/ContentPublisher.cs`

Replace the entire implementation. Key design decisions:

**Constructor dependencies:**
- `IAppDbContext db` -- EF context for loading content and saving publish records
- `IServiceProvider serviceProvider` -- for keyed DI resolution of `IPlatformConnector` instances
- `IContentTransformer transformer` -- shared transformation pipeline (Section 03)
- `ILogger<ContentPublisher> logger`

**Platform resolution:**
```csharp
var connector = serviceProvider.GetKeyedService<IPlatformConnector>(platform);
if (connector is null)
    // log warning and create Failed record with "No connector registered" error
```

**Determining target platforms (priority order):**
1. Explicit `targetPlatforms` parameter (from MediatR handler or API)
2. `content.TargetPlatforms` (set during content creation/editing)
3. `[content.PrimaryPlatform]` (fallback -- publish only to primary)

**Primary platform identification:**
The primary platform is always `content.PrimaryPlatform`. If it appears in the target list, it is published first. All other targets are secondaries.

**Idempotency check:**
Before publishing to any platform, query `ContentPlatformPublishes` for an existing record with `Status == Published` for that content + platform combination. If found, skip that platform entirely.

**State machine trigger:**
- For scheduled content (`Status == Scheduled`): fire `ContentTrigger.Publish`
- For direct publish (`Status == Approved`): fire `ContentTrigger.PublishNow`
- The trigger fires only once, after the primary platform succeeds

**Secondary publishing (parallel, best-effort):**
```csharp
var secondaryTasks = secondaryPlatforms.Select(async platform =>
{
    try
    {
        var transformed = await transformer.TransformAsync(content, platform, ct);
        var request = new PlatformPublishRequest(content, transformed, content.Tags, canonicalUrl, PublishMode.Publish);
        var result = await connector.PublishAsync(request, ct);
        return new PlatformPublishOutcome(platform, result.Success, result.PublishedUrl, result.ErrorMessage);
    }
    catch (Exception ex)
    {
        return new PlatformPublishOutcome(platform, false, null, ex.Message);
    }
});

var outcomes = await Task.WhenAll(secondaryTasks);
```

**Guid-only overload (Hangfire compatibility):**
```csharp
public async Task PublishAsync(Guid contentId)
{
    await PublishAsync(contentId, targetPlatforms: null, CancellationToken.None);
}
```

### 3. Update `PublishContent` MediatR Command

**File:** `src/PBA.Application/Features/Content/Commands/PublishContent.cs`

The command currently injects `IBlogConnector` directly and duplicates routing logic. Refactor to delegate entirely to `IContentPublisher`.

**Command record change:**
```csharp
public record Command(Guid ContentId, IReadOnlyList<Platform>? TargetPlatforms = null) : IRequest<Result<PublishResult>>;
```

Note the return type changes from `Result` to `Result<PublishResult>` to surface per-platform outcomes to the API layer.

**Handler simplification:**
The handler should:
1. Validate content exists
2. Check content is in a publishable state (Approved or Scheduled)
3. Delegate to `IContentPublisher.PublishAsync(contentId, targetPlatforms, ct)`
4. Return the `PublishResult`

The handler no longer needs to inject `IBlogConnector`, create `ContentPlatformPublish` records, or fire state machine triggers -- all of that moves into `ContentPublisher`.

### 4. Update DI Registration

**File:** `src/PBA.Infrastructure/DependencyInjection.cs`

Remove:
```csharp
services.AddScoped<IBlogConnector, BlogConnector>();
```

The `IBlogConnector` interface no longer exists (removed in Section 04). BlogConnector is now registered as a keyed `IPlatformConnector` service:
```csharp
services.AddKeyedScoped<IPlatformConnector, BlogConnector>(Platform.Blog);
```

The `ContentPublisher` registration stays the same:
```csharp
services.AddScoped<IContentPublisher, ContentPublisher>();
```

### 5. Update Callers of IBlogConnector

After this section, no code should reference `IBlogConnector`. The only places that previously referenced it:
- `ContentPublisher` -- refactored to use keyed `IPlatformConnector`
- `PublishContent.Handler` -- refactored to delegate to `IContentPublisher`
- `DependencyInjection.cs` -- registration removed
- `TestWebApplicationFactory` -- any mock of `IBlogConnector` must be updated to mock `IPlatformConnector` keyed by `Platform.Blog`

**File:** `tests/PBA.Api.Tests/TestWebApplicationFactory.cs` -- update mock registration if `IBlogConnector` is mocked there.

---

## Files Created/Modified (Actual)

| File | Action |
|------|--------|
| `src/PBA.Application/Common/Interfaces/IContentPublisher.cs` | Modified: added `PublishAsync(Guid, IReadOnlyList<Platform>?, CancellationToken)` returning `PublishResult` |
| `src/PBA.Infrastructure/Publishing/ContentPublisher.cs` | Rewritten: keyed DI via IServiceProvider, multi-platform, idempotency, parallel connector HTTP calls with sequential DB writes |
| `src/PBA.Application/Features/Content/Commands/PublishContent.cs` | Rewritten: simplified to delegate to IContentPublisher, return type `Result<PublishResult>`, removed IAppDbContext + IBlogConnector dependencies |
| `tests/PBA.Infrastructure.Tests/Publishing/ContentPublisherTests.cs` | Rewritten: ServiceCollection-based keyed DI setup, 14 tests (4 migrated + 10 new) |
| `tests/PBA.Application.Tests/Features/Content/Commands/PublishContentHandlerTests.cs` | Created: 3 tests for handler delegation |
| `tests/PBA.Api.Tests/TestWebApplicationFactory.cs` | Modified: removed IContentPublisher mock, added IContentTransformer mock so real ContentPublisher runs in integration tests |

**Not modified (no changes needed):**
- `src/PBA.Infrastructure/DependencyInjection.cs` — keyed Blog connector already registered in section-04, IBlogConnector already removed

## Edge Cases and Risks (Resolved)

1. **EF Core InMemory and keyed DI in tests:** Resolved — ServiceCollection with keyed singleton mocks, built into IServiceProvider for ContentPublisher constructor.

2. **State machine trigger selection:** Resolved — ContentPublisher checks `content.Status` to pick `Publish` vs `PublishNow` trigger. Added guard `if (content.Status != ContentStatus.Published)` to handle idempotent re-entry where primary is already published.

3. **Transaction scope:** Resolved — single `SaveChangesAsync` at end. Primary failure aborts early with its own SaveChanges.

4. **Canonical URL for secondaries:** Resolved — primary published first, its URL passed as `canonicalUrl` to secondary transforms.

5. **Missing connector registration:** Resolved — `PublishToPlatformAsync` returns `PlatformPublishResult(false, ...)` with descriptive error when no connector registered.

6. **DbContext thread safety (code review finding):** Resolved — idempotency check moved before Task.WhenAll into a HashSet; only connector HTTP calls parallelized; DB writes sequential.

7. **Guid-only overload Hangfire compat:** Preserved — overload guards for Scheduled status only, then delegates to full method.

## Implementation Deviations

- **PublishContent.Handler** simplified further than planned: removed `IAppDbContext` dependency entirely since ContentPublisher handles content existence validation. Handler only delegates and maps results.
- **DependencyInjection.cs** not modified — keyed Blog connector was already registered in section-04.
