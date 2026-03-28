# Section 02 -- Autonomy Configuration

## Overview

This section implements the `AutonomyConfiguration` entity, its value objects, EF Core configuration, DataSeeder update, and all unit tests for the resolution logic. The autonomy configuration is a singleton entity (one row in the database, keyed by `Guid.Empty`) that stores the global autonomy level plus structured override arrays for content type, platform, and content-type-plus-platform combinations.

The `ResolveLevel` method implements a priority hierarchy: ContentType+Platform override wins over Platform override, which wins over ContentType override, which wins over the Global level.

## Dependencies

- **Section 01 (Domain Entities):** This section assumes the `Content` entity modifications (e.g., `CapturedAutonomyLevel`) and new entities (`Notification`, `WorkflowTransitionLog`) from section 01 are already in place. However, the `AutonomyConfiguration` entity itself has no runtime dependency on those -- it only depends on existing enums (`AutonomyLevel`, `ContentType`, `PlatformType`) which already exist in the codebase.

## Existing Codebase Context

The following enums already exist and are used by this section:

- `PersonalBrandAssistant.Domain.Enums.AutonomyLevel` -- values: `Manual`, `Assisted`, `SemiAuto`, `Autonomous`
- `PersonalBrandAssistant.Domain.Enums.ContentType` -- values: `BlogPost`, `SocialPost`, `Thread`, `VideoDescription`
- `PersonalBrandAssistant.Domain.Enums.PlatformType` -- values: `TwitterX`, `LinkedIn`, `Instagram`, `YouTube`

The base class `AuditableEntityBase` (in `PersonalBrandAssistant.Domain.Common`) provides `Id` (Guid, defaults to `Guid.CreateVersion7()`), `CreatedAt`, `UpdatedAt`, and domain event support.

The `DataSeeder` is an `IHostedService` in `PersonalBrandAssistant.Infrastructure.Services` that seeds default data on startup using `ApplicationDbContext`. It currently seeds `BrandProfile`, `Platform`, and `User` entities.

The `IApplicationDbContext` interface in `PersonalBrandAssistant.Application.Common.Interfaces` exposes `DbSet<T>` properties. A new `DbSet<AutonomyConfiguration>` must be added here (and in `ApplicationDbContext`).

EF Core configurations live in `PersonalBrandAssistant.Infrastructure.Data.Configurations` and use `IEntityTypeConfiguration<T>`. JSONB columns use a `JsonValueConverter<T>` already present in that namespace.

## Tests First

All tests go in `tests/PersonalBrandAssistant.Domain.Tests/`. Create a new test file for the entity and its resolution logic.

### File: `tests/PersonalBrandAssistant.Domain.Tests/Entities/AutonomyConfigurationTests.cs`

Tests to implement (xUnit, no mocking needed -- pure domain logic):

1. **Constructor sets Id to Guid.Empty** -- Creating an `AutonomyConfiguration` must produce an entity whose `Id` is `Guid.Empty`, enforcing the singleton pattern at the domain level.

2. **ResolveLevel returns GlobalLevel when no overrides exist** -- Given an `AutonomyConfiguration` with `GlobalLevel = Assisted` and empty override lists, calling `ResolveLevel(ContentType.BlogPost, PlatformType.LinkedIn)` returns `Assisted`.

3. **ResolveLevel returns ContentType override when it matches** -- Given overrides containing `{ ContentType = BlogPost, Level = SemiAuto }`, calling `ResolveLevel(ContentType.BlogPost, null)` returns `SemiAuto` (not the global level).

4. **ResolveLevel returns Platform override when it matches (wins over ContentType)** -- Given both a ContentType override for `BlogPost = SemiAuto` and a Platform override for `LinkedIn = Autonomous`, calling `ResolveLevel(ContentType.BlogPost, PlatformType.LinkedIn)` returns `Autonomous`.

5. **ResolveLevel returns ContentType+Platform override when both match (wins over all)** -- Given all three override types, the most specific (ContentType+Platform) wins. For example, global = Manual, ContentType(BlogPost) = Assisted, Platform(LinkedIn) = SemiAuto, ContentType+Platform(BlogPost, LinkedIn) = Autonomous. Calling `ResolveLevel(ContentType.BlogPost, PlatformType.LinkedIn)` returns `Autonomous`.

6. **ResolveLevel falls through correctly when specific override is missing** -- Given a ContentType+Platform override for (BlogPost, LinkedIn) but calling with (SocialPost, LinkedIn), it should fall through to the Platform override for LinkedIn, then ContentType for SocialPost, then Global -- whichever is the most specific match that exists.

7. **Override value objects are records with value equality** -- Verify that two `ContentTypeOverride` instances with the same `ContentType` and `Level` are equal (record semantics).

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Data/Configurations/AutonomyConfigurationConfigurationTests.cs`

These are integration tests requiring Testcontainers (follow existing `PostgresFixture` pattern):

1. **AutonomyConfiguration enforces singleton (Guid.Empty PK)** -- Inserting two `AutonomyConfiguration` entities should fail with a unique constraint violation.

2. **JSONB columns store/retrieve overrides correctly** -- Save an `AutonomyConfiguration` with populated override lists, reload from database, verify all overrides round-trip correctly.

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/DataSeederTests.cs`

1. **DataSeeder creates default AutonomyConfiguration** -- After `DataSeeder.StartAsync`, the database should contain exactly one `AutonomyConfiguration` with `GlobalLevel = Manual` and empty override lists.

## Implementation Details

### 1. Value Objects for Override Lists

**File:** `src/PersonalBrandAssistant.Domain/ValueObjects/ContentTypeOverride.cs`

A `record` with two properties:
- `ContentType ContentType`
- `AutonomyLevel Level`

**File:** `src/PersonalBrandAssistant.Domain/ValueObjects/PlatformOverride.cs`

A `record` with two properties:
- `PlatformType PlatformType`
- `AutonomyLevel Level`

**File:** `src/PersonalBrandAssistant.Domain/ValueObjects/ContentTypePlatformOverride.cs`

A `record` with three properties:
- `ContentType ContentType`
- `PlatformType PlatformType`
- `AutonomyLevel Level`

These are simple immutable records -- no behavior, just data carriers. Using records gives value equality for free.

### 2. AutonomyConfiguration Entity

**File:** `src/PersonalBrandAssistant.Domain/Entities/AutonomyConfiguration.cs`

Extends `AuditableEntityBase`. Key design decisions:

- The constructor must override the base `Id` to set it to `Guid.Empty` (singleton enforcement). Use `protected init` on the base class -- since `EntityBase.Id` has `protected init`, set it in the constructor or use a static factory.
- `GlobalLevel` property of type `AutonomyLevel`, defaults to `Manual`.
- Three `List<T>` properties for the override arrays, initialized to empty lists.
- A `ResolveLevel(ContentType type, PlatformType? platform)` method implementing the priority hierarchy.

**ResolveLevel algorithm:**

```
1. If platform is not null, check ContentTypePlatformOverrides for (type, platform) match
   -> If found, return that level
2. If platform is not null, check PlatformOverrides for platform match
   -> If found, return that level
3. Check ContentTypeOverrides for type match
   -> If found, return that level
4. Return GlobalLevel
```

This is pure domain logic with no dependencies -- use LINQ `FirstOrDefault` on each list. The method should be deterministic and side-effect free.

### 3. EF Core Configuration

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/AutonomyConfigurationConfiguration.cs`

Implements `IEntityTypeConfiguration<AutonomyConfiguration>`:

- Table name: `"AutonomyConfigurations"`
- Primary key: `Id` (which will always be `Guid.Empty`)
- `GlobalLevel` stored as integer, required
- `ContentTypeOverrides` stored as JSONB using the existing `JsonValueConverter<List<ContentTypeOverride>>`
- `PlatformOverrides` stored as JSONB using `JsonValueConverter<List<PlatformOverride>>`
- `ContentTypePlatformOverrides` stored as JSONB using `JsonValueConverter<List<ContentTypePlatformOverride>>`
- `xmin` concurrency token (same pattern as `ContentConfiguration`)
- Ignore `DomainEvents` navigation (same pattern as `ContentConfiguration`)

### 4. IApplicationDbContext Update

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs`

Add a new property:

```csharp
DbSet<AutonomyConfiguration> AutonomyConfigurations { get; }
```

The concrete `ApplicationDbContext` must also add this `DbSet` property.

### 5. ApplicationDbContext Update

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs`

Add:

```csharp
public DbSet<AutonomyConfiguration> AutonomyConfigurations => Set<AutonomyConfiguration>();
```

### 6. DataSeeder Update

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/DataSeeder.cs`

Add a new block in `StartAsync`, after the existing seed blocks and before `SaveChangesAsync`:

```csharp
if (!await context.AutonomyConfigurations.AnyAsync(cancellationToken))
{
    context.AutonomyConfigurations.Add(AutonomyConfiguration.CreateDefault());
    _logger.LogInformation("Seeded default AutonomyConfiguration");
}
```

This requires a `CreateDefault()` static factory method on `AutonomyConfiguration` that creates an instance with `GlobalLevel = Manual` and empty override lists. Alternatively, the parameterless constructor can serve this purpose if the defaults are correct -- but a named factory method makes intent explicit.

## File Summary

Files to create:
- `src/PersonalBrandAssistant.Domain/ValueObjects/ContentTypeOverride.cs`
- `src/PersonalBrandAssistant.Domain/ValueObjects/PlatformOverride.cs`
- `src/PersonalBrandAssistant.Domain/ValueObjects/ContentTypePlatformOverride.cs`
- `src/PersonalBrandAssistant.Domain/Entities/AutonomyConfiguration.cs`
- `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/AutonomyConfigurationConfiguration.cs`
- `tests/PersonalBrandAssistant.Domain.Tests/Entities/AutonomyConfigurationTests.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Data/Configurations/AutonomyConfigurationConfigurationTests.cs`

Files to modify:
- `src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs` -- add `DbSet<AutonomyConfiguration>`
- `src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs` -- add `DbSet<AutonomyConfiguration>`
- `src/PersonalBrandAssistant.Infrastructure/Services/DataSeeder.cs` -- seed default autonomy configuration

## Implementation Order

1. Create the three value object records (no dependencies)
2. Create the `AutonomyConfiguration` entity with `ResolveLevel` method
3. Write domain unit tests and verify they pass
4. Create EF Core configuration
5. Update `IApplicationDbContext` and `ApplicationDbContext`
6. Update `DataSeeder`
7. Write and verify infrastructure integration tests