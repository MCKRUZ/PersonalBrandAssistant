# Section 07: Staggered Publish Scheduling

## Overview

Implements configurable delay between Substack and PersonalBlog publishing. When Substack publication is confirmed, PBA computes blog deploy date (default +7 days), notifies user, and on confirmation schedules the blog. Modifies ScheduledPublishProcessor to create notifications instead of auto-publishing for PersonalBlog.

**Depends on:** Section 01 (BlogDelayOverride, BlogSkipped, ContentPlatformStatus.ScheduledAt), Section 05 (IGitHubPublishService), Section 06 (SubstackPublicationDetectedEvent)
**Blocks:** Section 12 (Blog Dashboard), Section 13 (Pipeline Integration)

---

## Tests (Write First)

### Delay Calculation Tests
File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/BlogServices/PublishDelayCalculatorTests.cs`

```csharp
// Test: Calculate blog scheduled date = Substack published + default delay (7 days)
// Test: Calculate blog scheduled date with custom BlogDelayOverride
// Test: BlogDelayOverride null uses global default delay
// Test: BlogSkipped = true skips blog scheduling entirely
// Test: BlogSkipped and BlogDelayOverride are independent (override set but skipped = no schedule)
```

### Scheduling Flow Tests
File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/BlogServices/BlogSchedulingServiceTests.cs`

```csharp
// Test: Substack publication triggers blog scheduling notification when RequiresConfirmation = true
// Test: User confirmation creates ContentPlatformStatus for PersonalBlog with ScheduledAt
// Test: Platform ordering: PersonalBlog publish blocked when Substack not yet Published
// Test: Platform ordering: PersonalBlog publish allowed when Substack is Published
// Test: ScheduledPublishProcessor creates notification (not auto-publish) for PersonalBlog
// Test: ScheduledPublishProcessor notification is idempotent (no duplicates)
```

### Blog Pipeline API Tests
File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/BlogPipelineEndpointTests.cs`

```csharp
// Test: GET /api/blog-pipeline returns all BlogPost content with platform statuses
// Test: GET /api/blog-pipeline filters by status correctly
// Test: GET /api/blog-pipeline filters by date range
// Test: POST /api/blog-pipeline/{id}/schedule sets ContentPlatformStatus.ScheduledAt
// Test: PUT /api/blog-pipeline/{id}/delay updates BlogDelayOverride
// Test: POST /api/blog-pipeline/{id}/skip-blog sets BlogSkipped = true
```

---

## Implementation Details

### IBlogSchedulingService
File: `src/PersonalBrandAssistant.Application/Common/Interfaces/IBlogSchedulingService.cs`

```csharp
public interface IBlogSchedulingService
{
    Task OnSubstackPublicationConfirmedAsync(Guid contentId, DateTimeOffset substackPublishedAt, CancellationToken ct);
    Task<Result<DateTimeOffset>> ConfirmBlogScheduleAsync(Guid contentId, CancellationToken ct);
    Task<Result<Unit>> ValidateBlogPublishAllowedAsync(Guid contentId, CancellationToken ct);
}
```

### BlogSchedulingService
File: `src/PersonalBrandAssistant.Infrastructure/Services/BlogServices/BlogSchedulingService.cs`

**OnSubstackPublicationConfirmedAsync**: Check BlogSkipped (return early), verify PersonalBlog in TargetPlatforms, compute `scheduledAt = substackPublishedAt + (BlogDelayOverride ?? default)`. If RequiresConfirmation, create notification. Otherwise, call ConfirmBlogScheduleAsync directly.

**ConfirmBlogScheduleAsync**: Find/create ContentPlatformStatus for PersonalBlog, set Status=Scheduled + ScheduledAt.

**ValidateBlogPublishAllowedAsync**: Check Substack ContentPlatformStatus is Published. Return failure if not.

### Modify ScheduledPublishProcessor
Modify: `src/PersonalBrandAssistant.Infrastructure/BackgroundJobs/ScheduledPublishProcessor.cs`

In ProcessDueContentAsync: if content targets PersonalBlog AND is BlogPost, check if pending BlogReady notification exists. If not, create one via INotificationService. Skip pipeline.PublishAsync. For all other platforms, existing logic unchanged.

### Blog Pipeline Endpoints
File: `src/PersonalBrandAssistant.Api/Endpoints/BlogPipelineEndpoints.cs`

```
GET  /api/blog-pipeline                  → List all BlogPost with Substack+Blog statuses
POST /api/blog-pipeline/{id}/schedule    → Confirm blog schedule
PUT  /api/blog-pipeline/{id}/delay       → Update BlogDelayOverride
POST /api/blog-pipeline/{id}/skip-blog   → Set BlogSkipped = true
```

### PublishDelayRule Value Object
File: `src/PersonalBrandAssistant.Domain/ValueObjects/PublishDelayRule.cs`
```csharp
public record PublishDelayRule(PlatformType SourcePlatform, PlatformType TargetPlatform, TimeSpan DefaultDelay, bool RequiresConfirmation);
```

---

## Files
| File | Action |
|------|--------|
| `Application/Common/Interfaces/IBlogSchedulingService.cs` | Create |
| `Infrastructure/Services/BlogServices/BlogSchedulingService.cs` | Create |
| `Domain/ValueObjects/PublishDelayRule.cs` | Create |
| `Api/Endpoints/BlogPipelineEndpoints.cs` | Create |
| `Infrastructure/BackgroundJobs/ScheduledPublishProcessor.cs` | Modify |
| `Infrastructure/DependencyInjection.cs` | Modify |
| `Api/Program.cs` | Modify |
