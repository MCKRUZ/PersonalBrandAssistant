# Section 01: EF Core Schema Updates

## Overview

This section updates the database schema to support the Content Studio. It adds missing `DbSet` properties to the `IAppDbContext` interface, adds new properties to the `Content` entity (HangfireJobId, IsDeleted, Children navigation), adds composite indexes and a soft-delete query filter, and seeds a default `BrandProfile` row.

**Depends on:** Nothing (this is a root section)
**Blocks:** section-02 (state machine), section-03 (DTOs/validators), section-04 (queries), section-05 (commands)

---

## Files to Modify

| File | Change |
|------|--------|
| `src/PBA.Application/Common/Interfaces/IAppDbContext.cs` | Add `ContentPlatformPublishes` and `BrandProfiles` DbSet properties |
| `src/PBA.Domain/Entities/Content.cs` | Add `HangfireJobId`, `IsDeleted`, `Children` properties |
| `src/PBA.Infrastructure/Data/Configurations/ContentConfiguration.cs` | Add self-referential `Children` nav config, soft-delete query filter |
| `src/PBA.Infrastructure/Data/Configurations/ContentPlatformPublishConfiguration.cs` | Add composite index on (Platform, Status) |
| `src/PBA.Infrastructure/Data/Configurations/BrandProfileConfiguration.cs` | Add seed data for default BrandProfile |

## Files to Create (Test)

| File | Purpose |
|------|---------|
| `tests/PBA.Infrastructure.Tests/Data/SchemaUpdateTests.cs` | All schema-related tests for this section |

---

## Tests FIRST

Create `tests/PBA.Infrastructure.Tests/Data/SchemaUpdateTests.cs` with the following test stubs. These tests use `ApplicationDbContext` with the EF Core in-memory provider (already referenced in the test project).

```csharp
namespace PBA.Infrastructure.Tests.Data;

public class SchemaUpdateTests
{
    private static ApplicationDbContext CreateContext();

    // --- IAppDbContext interface completeness ---

    [Fact]
    public void IAppDbContext_Exposes_ContentPlatformPublishes_DbSet()
    // Verify the IAppDbContext interface declares DbSet<ContentPlatformPublish> ContentPlatformPublishes.
    // Compile-time assertion -- if it compiles, it passes.

    [Fact]
    public void IAppDbContext_Exposes_BrandProfiles_DbSet()
    // Same verification for DbSet<BrandProfile> BrandProfiles.

    // --- Content entity new properties ---

    [Fact]
    public void Content_Has_HangfireJobId_Property()
    // Instantiate Content, set HangfireJobId to a string, assert it persists through save/reload.

    [Fact]
    public void Content_Has_IsDeleted_Property_DefaultFalse()
    // Instantiate Content, verify IsDeleted defaults to false. Save, reload, confirm.

    [Fact]
    public void Content_Has_Children_NavigationProperty()
    // Create parent Content and two child Content entities (ParentContentId = parent.Id).
    // Include Children on parent, verify count == 2.

    // --- Soft-delete query filter ---

    [Fact]
    public async Task SoftDelete_QueryFilter_Excludes_IsDeleted_Content()
    // Add two Content: one with IsDeleted=false, one with IsDeleted=true.
    // Query db.Contents.ToListAsync() -- assert only the non-deleted one is returned.

    [Fact]
    public async Task SoftDelete_Filter_Can_Be_Overridden_With_IgnoreQueryFilters()
    // Same setup as above, but query with db.Contents.IgnoreQueryFilters().ToListAsync().
    // Assert both are returned.

    // --- Composite index (structural) ---

    [Fact]
    public void ContentPlatformPublish_Has_Composite_Index_On_Platform_Status()
    // Use the model metadata to verify the index exists:
    // var entity = context.Model.FindEntityType(typeof(ContentPlatformPublish));
    // Assert entity has an index containing both Platform and Status properties.

    // --- Seed data ---

    [Fact]
    public async Task BrandProfile_Has_Seeded_Default_Row()
    // After context creation (which applies model-building + seeding), query BrandProfiles.
    // Note: EF in-memory provider does NOT run HasData seed. This test must use
    // a migration test or verify the HasData call exists via model metadata.
    // Alternative: verify via model.GetSeedData() or a convention test.
}
```

**Important testing notes:**
- The EF Core in-memory provider does **not** execute `HasData` seed operations. For the seed data test, verify that the model configuration declares the seed data using `context.Model.FindEntityType(typeof(BrandProfile))` and checking `GetSeedData()` on the entity type, or simply create a migration test that applies pending migrations to a real (or SQLite) database.
- The composite index test should use EF metadata inspection: `context.Model.FindEntityType(typeof(ContentPlatformPublish)).GetIndexes()`.
- The soft-delete query filter test works with the in-memory provider because `HasQueryFilter` is applied at the model level and respected by in-memory queries.

---

## Implementation Details

### 1. Update IAppDbContext Interface

**File:** `src/PBA.Application/Common/Interfaces/IAppDbContext.cs`

Add two new `DbSet` properties to align the interface with what `ApplicationDbContext` already exposes. The concrete `ApplicationDbContext` already has these -- the interface just needs to catch up.

```csharp
public interface IAppDbContext
{
    DbSet<Content> Contents { get; }
    DbSet<ContentPlatformPublish> ContentPlatformPublishes { get; }
    DbSet<BrandProfile> BrandProfiles { get; }
    DbSet<Idea> Ideas { get; }
    DbSet<SavedIdea> SavedIdeas { get; }
    DbSet<IdeaSource> IdeaSources { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

This requires adding `using PBA.Domain.Entities;` for `ContentPlatformPublish` and `BrandProfile` (already imported via the existing `Content` reference).

### 2. Update Content Entity

**File:** `src/PBA.Domain/Entities/Content.cs`

Add three new properties to the `Content` class:

```csharp
public string? HangfireJobId { get; set; }
public bool IsDeleted { get; set; }
public IReadOnlyList<Content> Children { get; set; } = [];
```

- `HangfireJobId` -- nullable string, stores the Hangfire job identifier for scheduled publishing (used by section-10)
- `IsDeleted` -- defaults to `false`, provides soft-delete alongside `Status=Archived` (the query filter below hides these from normal queries)
- `Children` -- navigation property for self-referential relationship. These are platform-specific adaptations of this content (distinct from `CrossPosts`, which are publish records)

### 3. Update ContentConfiguration

**File:** `src/PBA.Infrastructure/Data/Configurations/ContentConfiguration.cs`

Three changes needed:

**a) Change the self-referential FK to use `WithMany(c => c.Children)` instead of `WithMany()`:**

The existing line:
```csharp
builder.HasOne(c => c.ParentContent)
    .WithMany()
    .HasForeignKey(c => c.ParentContentId)
    .OnDelete(DeleteBehavior.SetNull);
```

Must become:
```csharp
builder.HasOne(c => c.ParentContent)
    .WithMany(c => c.Children)
    .HasForeignKey(c => c.ParentContentId)
    .OnDelete(DeleteBehavior.SetNull);
```

This tells EF that the inverse navigation of `ParentContent` is the `Children` collection.

**b) Add HangfireJobId column configuration:**
```csharp
builder.Property(c => c.HangfireJobId).HasMaxLength(200);
```

**c) Add soft-delete global query filter:**
```csharp
builder.HasQueryFilter(c => !c.IsDeleted);
```

This ensures all LINQ queries on `Contents` automatically exclude soft-deleted rows. Override with `.IgnoreQueryFilters()` when you need to include them (e.g., admin views, restore operations).

**Note on EF Core 10 named query filters:** The plan mentions checking for named filter support (`HasQueryFilter("name", ...)`). EF Core 10 may or may not support this. Use the standard unnamed form above. If named filters are confirmed available, the filter could be refactored later for independent toggle, but it is not required for this section.

### 4. Update ContentPlatformPublishConfiguration

**File:** `src/PBA.Infrastructure/Data/Configurations/ContentPlatformPublishConfiguration.cs`

Add a composite index on `(Platform, Status)` for efficient querying of published content per platform:

```csharp
builder.HasIndex(c => new { c.Platform, c.Status });
```

Also verify the FK to `Content` with cascade delete exists. It does -- it is configured from the `Content` side in `ContentConfiguration.cs` via `HasMany(c => c.CrossPosts).WithOne(p => p.Content)...OnDelete(DeleteBehavior.Cascade)`. No change needed for the FK.

### 5. Seed Default BrandProfile

**File:** `src/PBA.Infrastructure/Data/Configurations/BrandProfileConfiguration.cs`

Add `HasData` to seed one default BrandProfile row. This ensures the voice check and draft features always find a BrandProfile even before the user configures one.

```csharp
builder.HasData(new BrandProfile
{
    Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
    Personality = string.Empty,
    Tone = string.Empty,
    UpdatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
});
```

Use a fixed `Id` GUID so the seed is idempotent across migrations. Use a fixed `UpdatedAt` so migrations are deterministic (avoid `DateTimeOffset.UtcNow` which would generate a new migration diff every run).

**Important:** `BrandProfile.Topics`, `Vocabulary`, and `AvoidWords` are `List<string>` with `= []` defaults. EF Core HasData does not handle complex types well with jsonb. You may need to set these explicitly to `new List<string>()` in the seed or let them rely on their CLR defaults. Test that the migration generates cleanly.

### 6. Run EF Migration

After all changes, generate the migration:

```powershell
dotnet ef migrations add ContentStudio --project src/PBA.Infrastructure --startup-project src/PBA.Api
```

Verify the migration includes:
- `HangfireJobId` column added to `Content` table (nullable, max length 200)
- `IsDeleted` column added to `Content` table (non-nullable, default `false`)
- Self-referential FK configuration for `Content.ParentContentId` -> `Content.Id` (already exists, but may regenerate with `WithMany(c => c.Children)`)
- Composite index on `ContentPlatformPublish` for `(Platform, Status)`
- Seed insert for default `BrandProfile`
- Query filter metadata baked into the model snapshot

---

## Verification Checklist

1. `dotnet build` succeeds for all projects (the IAppDbContext change touches Application, Infrastructure, and any code referencing the interface)
2. All nine tests in `SchemaUpdateTests` pass
3. `dotnet ef migrations add` generates a clean migration with expected changes (deferred — no startup project wired yet)
4. Existing tests continue to pass (150/150 total)

---

## Implementation Notes (Post-Build)

### Deviations from Plan
- **Children type**: Plan specified `IReadOnlyList<Content>` — changed to `List<Content>` because EF Core needs mutable collections for navigation property fixup. `[]` initializer creates an array which throws on `Add()`.
- **CrossPosts type**: Same issue — changed from `IReadOnlyList<ContentPlatformPublish>` to `List<ContentPlatformPublish>` during code review.
- **Test count**: Plan specified 6 tests (later updated to 9). Final count: 9 tests covering IAppDbContext (2), Content properties (3), soft-delete filter (2), composite index (1), seed data (1).
- **Seed data test approach**: Plan noted `GetSeedData()` on runtime model — EF Core 10 throws. Used `IModelSource.GetModel(context, dependencies, designTime: true)` with `ModelCreationDependencies` to access design-time model where `GetSeedData()` works.
- **EF migration**: Deferred — no startup project (PBA.Api) wired to run `dotnet ef` commands yet.

### Files Modified
- `src/PBA.Application/Common/Interfaces/IAppDbContext.cs` — Added ContentPlatformPublishes, BrandProfiles DbSets
- `src/PBA.Domain/Entities/Content.cs` — Added HangfireJobId, IsDeleted, Children (List), CrossPosts changed to List
- `src/PBA.Infrastructure/Data/Configurations/ContentConfiguration.cs` — WithMany(c => c.Children), HangfireJobId max length, soft-delete filter
- `src/PBA.Infrastructure/Data/Configurations/ContentPlatformPublishConfiguration.cs` — Composite index (Platform, Status)
- `src/PBA.Infrastructure/Data/Configurations/BrandProfileConfiguration.cs` — Seed data

### Files Created
- `tests/PBA.Infrastructure.Tests/Data/SchemaUpdateTests.cs` — 9 tests
