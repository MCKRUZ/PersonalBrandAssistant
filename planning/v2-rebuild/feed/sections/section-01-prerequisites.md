# Section 01: Prerequisites

## Overview

Before any Feed feature section can begin, two infrastructure gaps must be closed:

1. **`IAppDbContext` is missing the `FeedItems` DbSet.** The concrete `ApplicationDbContext` already exposes `DbSet<FeedItem> FeedItems`, but the `IAppDbContext` interface (injected by all Application-layer MediatR handlers) does not declare it. Without this, every Feed query and command will fail to compile.

2. **Two additional EF Core indexes are needed for Feed query performance.** The existing `FeedItemConfiguration` only indexes `(IsRead, CreatedAt)`. The Feed module's `GetFeedSummary` and `GetTrendingTopics` queries require composite indexes on `(Type, IsActedOn)` and `(Type, CreatedAt)`.

## Dependencies

None. This section has no prerequisites and blocks all subsequent sections (02-16).

## Tests

No tests needed for this section. It is a one-line interface change plus EF configuration additions. Correctness is verified by compilation (the interface change) and by the migration tool (the indexes). Downstream sections' tests will exercise these changes.

## Implementation

### 1. Add `FeedItems` to `IAppDbContext`

**File:** `src/PBA.Application/Common/Interfaces/IAppDbContext.cs`

Add the following property to the interface, alongside the existing DbSet declarations:

```csharp
DbSet<FeedItem> FeedItems { get; }
```

The concrete implementation already exists at `src/PBA.Infrastructure/Data/ApplicationDbContext.cs` line 16 (`public DbSet<FeedItem> FeedItems => Set<FeedItem>();`), so no changes are needed there.

The `FeedItem` entity lives in `PBA.Domain.Entities` and is already referenced by the `PBA.Application` project (the `using PBA.Domain.Entities;` directive is already present in `IAppDbContext.cs`).

### 2. Add composite indexes to `FeedItemConfiguration`

**File:** `src/PBA.Infrastructure/Data/Configurations/FeedItemConfiguration.cs`

Add two composite indexes inside the existing `Configure` method, after the current `HasIndex` call on line 17:

```csharp
builder.HasIndex(f => new { f.Type, f.IsActedOn });
builder.HasIndex(f => new { f.Type, f.CreatedAt });
```

These support:
- **`(Type, IsActedOn)`** -- Used by `GetFeedSummary` to count pending approvals (WHERE Type IN (AgentDraft, ApprovalRequest) AND IsActedOn = false).
- **`(Type, CreatedAt)`** -- Used by `GetTrendingTopics` to query recent TrendAlert items (WHERE Type = TrendAlert AND CreatedAt > 7 days ago).

The existing `(IsRead, CreatedAt)` index remains in place for the `ListFeedItems` query's default sort and read-status filtering.

### 3. Generate EF Core migration

After both changes, generate a migration to capture the new indexes:

```shell
dotnet ef migrations add AddFeedIndexes --project src/PBA.Infrastructure --startup-project src/PBA.Api
```

If the project uses auto-migration on startup in development, this will apply automatically. Otherwise, apply with:

```shell
dotnet ef database update --project src/PBA.Infrastructure --startup-project src/PBA.Api
```

## Verification

1. **Build succeeds:** `dotnet build` from the solution root compiles without errors. This confirms the interface change is compatible with the existing `ApplicationDbContext` implementation.
2. **Migration generates cleanly:** The migration should contain only `CreateIndex` operations for the two new composite indexes, with no other schema changes.
3. **Existing tests pass:** `dotnet test` should show no regressions, since this section only adds an interface member (already implemented) and database indexes.

## Files Modified

| File | Change |
|------|--------|
| `src/PBA.Application/Common/Interfaces/IAppDbContext.cs` | Add `DbSet<FeedItem> FeedItems { get; }` |
| `src/PBA.Infrastructure/Data/Configurations/FeedItemConfiguration.cs` | Add two `HasIndex` calls for `(Type, IsActedOn)` and `(Type, CreatedAt)` |
| `src/PBA.Infrastructure/Data/DesignTimeDbContextFactory.cs` | **NEW** — Created to fix EF migration tooling (DI resolution failure with IFreshRssClient) |
| `src/PBA.Infrastructure/Data/Migrations/20260515175231_AddFeedIndexes.cs` | Auto-generated migration (includes Feed indexes + pre-existing pending model changes) |
| `src/PBA.Infrastructure/Data/Migrations/20260515175231_AddFeedIndexes.Designer.cs` | Migration designer file |
| `src/PBA.Infrastructure/Data/Migrations/ApplicationDbContextModelSnapshot.cs` | Updated snapshot |

## Deviations from Plan

1. **DesignTimeDbContextFactory added** — Plan did not anticipate this. `dotnet ef migrations add` failed because the DI container couldn't resolve `IFreshRssClient`. Added `IDesignTimeDbContextFactory<ApplicationDbContext>` with a dummy Npgsql connection string for design-time tooling.
2. **Migration includes extra changes** — The generated migration picked up pre-existing pending model changes (HangfireJobId, IsDeleted on Contents, BrandProfile seed data, ContentPlatformPublish index). These are legitimate changes from prior work. Accepted as-is rather than splitting.
