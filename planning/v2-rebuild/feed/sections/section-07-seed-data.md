# Section 07: Feed Seed Data

**Status:** COMPLETE

## Overview

Create a `FeedSeedService` that populates the database with 30-50 diverse, realistic feed items across all `FeedItemType` values and priorities. Register a dev-only endpoint (`POST /api/feed/seed`) that invokes the service. This provides immediate visual feedback when developing the Feed UI and verifies the full backend pipeline end-to-end.

## Implementation Notes

- 34 seed items created (9 AgentDraft, 6 TrendAlert, 6 IdeaSuggestion, 6 AnalyticsHighlight, 4 ApprovalRequest, 3 SystemNotification)
- Deterministic via `Random(42)` — repeated seeds produce consistent results
- Code review fixes applied: priority inversion in TrendAlerts corrected, acted-on ratio adjusted from ~17% to ~30%
- 8 tests pass: type coverage, read/unread mix, acted/not-acted mix, priority variation, expired items, valid JSON, idempotency, count range
- Test file: `tests/PBA.Application.Tests/Features/Feed/Seeding/FeedSeedServiceTests.cs`

## Dependencies

- **section-02-dtos-validators-mappings** must be complete (FeedItemDto, FeedMappings, canonical JSON Data schemas). The seed service creates `FeedItem` entities directly and needs to produce valid `Data` JSON matching the schemas defined in section 02.

No other sections depend on this one -- it blocks nothing.

## What Gets Built

| Component | Project | Path |
|-----------|---------|------|
| `FeedSeedService` | PBA.Infrastructure | `src/PBA.Infrastructure/Seeding/FeedSeedService.cs` |
| `IFeedSeedService` interface | PBA.Application | `src/PBA.Application/Common/Interfaces/IFeedSeedService.cs` |
| Dev-only seed endpoint | PBA.Api | Registered inline in `Program.cs` |
| DI registration | PBA.Infrastructure | `src/PBA.Infrastructure/DependencyInjection.cs` |

---

## Background

### FeedItem Entity (already exists)

Located at `src/PBA.Domain/Entities/FeedItem.cs`:

```csharp
public class FeedItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public FeedItemType Type { get; set; }
    public required string Title { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? Data { get; set; }
    public string? ActionType { get; set; }
    public Guid? ActionTargetId { get; set; }
    public FeedItemPriority Priority { get; set; } = FeedItemPriority.Normal;
    public bool IsRead { get; set; }
    public bool IsActedOn { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
}
```

### Enums (already exist)

```csharp
public enum FeedItemType { AgentDraft, TrendAlert, AnalyticsHighlight, IdeaSuggestion, ApprovalRequest, SystemNotification }
public enum FeedItemPriority { Low, Normal, High, Urgent }
```

### Canonical JSON Data Schemas (from section-02)

Each `FeedItemType` uses a defined JSON structure in the `Data` column. The seed service must produce valid JSON matching these schemas:

```
AgentDraft:         { "contentType": "Blog", "primaryPlatform": "Substack", "wordCount": 1200 }
TrendAlert:         { "topic": "Claude Code", "source": "Twitter", "mentionCount": 45, "sentiment": "positive" }
IdeaSuggestion:     { "keywords": ["AI", "enterprise"], "confidence": 0.85, "sourceIdeaTitle": "..." }
AnalyticsHighlight: { "metric": "impressions", "currentValue": 500, "previousValue": 400, "delta": 25.0 }
ApprovalRequest:    { "contentType": "Blog", "primaryPlatform": "Substack", "requestedBy": "system" }
SystemNotification: { "category": "info", "link": "/settings" }
```

### IAppDbContext (already exists)

Located at `src/PBA.Application/Common/Interfaces/IAppDbContext.cs`. Currently does **not** include `DbSet<FeedItem> FeedItems`. The prerequisite section (section-01) adds it. If section-01 is not yet complete, the seed service will not compile. The `ApplicationDbContext` at `src/PBA.Infrastructure/Data/ApplicationDbContext.cs` already has `DbSet<FeedItem> FeedItems => Set<FeedItem>();`.

### Existing DI Pattern

`src/PBA.Infrastructure/DependencyInjection.cs` uses `AddInfrastructureDependencies` extension method. The seed service registration goes here, scoped.

### Existing Dev-Only Pattern

`Program.cs` already has a dev-only guard: `if (app.Environment.IsDevelopment()) app.UseHangfireDashboard("/hangfire");`. The seed endpoint uses the same pattern.

---

## Tests (Write First)

Test file: `tests/PBA.Application.Tests/Features/Feed/Seeding/FeedSeedServiceTests.cs`

Use an in-memory EF Core database (unique Guid DB name per test). The seed service injects `IAppDbContext`, so tests create a real `ApplicationDbContext` with the in-memory provider.

### Test Cases

```csharp
// FeedSeedServiceTests

// Test: SeedAsync_CreatesItemsOfAllFeedItemTypeValues
// Arrange: empty database
// Act: call SeedAsync
// Assert: db.FeedItems contains at least one item for each FeedItemType enum value

// Test: SeedAsync_CreatesMixOfReadAndUnreadItems
// Arrange: empty database
// Act: call SeedAsync
// Assert: db.FeedItems contains both IsRead=true and IsRead=false items

// Test: SeedAsync_CreatesMixOfActedAndNotActedItems
// Arrange: empty database
// Act: call SeedAsync
// Assert: db.FeedItems contains both IsActedOn=true and IsActedOn=false items

// Test: SeedAsync_CreatesItemsWithVaryingPriorities
// Arrange: empty database
// Act: call SeedAsync
// Assert: db.FeedItems contains items with at least 3 different FeedItemPriority values

// Test: SeedAsync_CreatesSomeExpiredItems
// Arrange: empty database
// Act: call SeedAsync
// Assert: at least one item has ExpiresAt in the past (< DateTimeOffset.UtcNow)

// Test: SeedAsync_CreatesItemsWithValidDataJson
// Arrange: empty database
// Act: call SeedAsync
// Assert: for each item with non-null Data, JsonDocument.Parse(item.Data) succeeds without throwing
// Assert: TrendAlert items' Data contains a "topic" field
// Assert: AnalyticsHighlight items' Data contains a "delta" field
```

---

## Implementation Details

### Interface: IFeedSeedService

File: `src/PBA.Application/Common/Interfaces/IFeedSeedService.cs`

Simple interface with a single method:

```csharp
public interface IFeedSeedService
{
    Task<int> SeedAsync(CancellationToken cancellationToken = default);
}
```

Returns the count of items created.

### Service: FeedSeedService

File: `src/PBA.Infrastructure/Seeding/FeedSeedService.cs`

Constructor injects `IAppDbContext`. The `SeedAsync` method:

1. Check if any `FeedItems` already exist. If so, return 0 (idempotent -- don't double-seed).
2. Build a list of 30-50 `FeedItem` entities with realistic, varied data.
3. Call `AddRange` then `SaveChangesAsync`.
4. Return the count of items created.

#### Item Distribution

- **8-10 AgentDraft items**: Titles like "AI Trends Weekly Draft", "LinkedIn Thought Leadership Post". Data JSON with varying contentType (Blog, LinkedInPost, Tweet) and wordCount. ActionType = "approve". Mix of priorities (mostly Normal, a couple High).
- **5-7 TrendAlert items**: Topics from a realistic set (e.g., "Claude Code", "AI Agents", ".NET 10", "Angular Signals", "Personal Branding"). Data JSON with source, mentionCount, sentiment. ActionType = "view". Include some High/Urgent priority.
- **5-7 IdeaSuggestion items**: Titles like "Content idea: AI in Enterprise". Data JSON with keywords array, confidence score, sourceIdeaTitle. ActionType = "create-content".
- **5-7 AnalyticsHighlight items**: Titles like "Impressions up 25%". Data JSON with metric, currentValue, previousValue, delta (both positive and negative deltas). ActionType = "view".
- **3-5 ApprovalRequest items**: Titles like "Review: Blog Post on AI Agents". Data JSON with contentType, primaryPlatform, requestedBy. ActionType = "approve". Include at least one Urgent.
- **2-3 SystemNotification items**: Titles like "API rate limit approaching", "New integration available". Data JSON with category and link. ActionType = "view". Low priority.

#### State Variation

- ~60% unread (`IsRead = false`), ~40% read
- ~70% not acted on (`IsActedOn = false`), ~30% acted on
- Items marked as acted-on should also be marked as read (acted implies read)
- 2-3 items with `ExpiresAt` in the past (for testing expired filtering)
- 1-2 items with `ExpiresAt` in the future (for testing non-expired items with expiry)

#### Time Distribution

Spread `CreatedAt` over the last 7 days using `DateTimeOffset.UtcNow.AddDays(-random)` with some clustering (more items in the last 24 hours than 7 days ago). Use a deterministic approach (not truly random) so repeated seeds produce consistent results -- use a fixed seed `Random` instance or sequential offsets.

#### ActionTargetId

For AgentDraft and ApprovalRequest items, set `ActionTargetId` to `null` (no real content exists yet in a fresh dev database). The feed UI should handle null ActionTargetId gracefully. For IdeaSuggestion items, also null.

### DI Registration

In `src/PBA.Infrastructure/DependencyInjection.cs`, add inside `AddInfrastructureDependencies`:

```csharp
services.AddScoped<IFeedSeedService, FeedSeedService>();
```

### Dev-Only Endpoint

In `src/PBA.Api/Program.cs`, inside the existing `if (app.Environment.IsDevelopment())` block (or create one if needed), add:

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/hangfire");

    app.MapPost("/api/feed/seed", async (IFeedSeedService seedService, CancellationToken ct) =>
    {
        var count = await seedService.SeedAsync(ct);
        return Results.Ok(new { seeded = count });
    });
}
```

This endpoint is only available in Development. It returns `{ "seeded": 42 }` (or `{ "seeded": 0 }` if items already exist).

---

## Verification Checklist

- [ ] `IFeedSeedService` interface created in Application layer
- [ ] `FeedSeedService` created in Infrastructure layer under `Seeding/`
- [ ] Service is idempotent (returns 0 if items already exist)
- [ ] Creates items covering all 6 `FeedItemType` values
- [ ] Creates items covering at least 3 `FeedItemPriority` values
- [ ] Creates mix of read/unread and acted/not-acted items
- [ ] Creates items with valid `Data` JSON matching canonical schemas
- [ ] Creates 2-3 expired items for filter testing
- [ ] `CreatedAt` spread over 7 days
- [ ] DI registration added to `DependencyInjection.cs`
- [ ] Dev-only `POST /api/feed/seed` endpoint registered in `Program.cs`
- [ ] Endpoint returns 404 in non-Development environments
- [ ] All 6 test cases pass
- [ ] `dotnet build` succeeds
