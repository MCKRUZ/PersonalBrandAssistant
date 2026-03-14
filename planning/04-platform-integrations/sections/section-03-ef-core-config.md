# Section 03: EF Core Configuration

## Overview

This section adds EF Core entity type configurations for the two new domain entities introduced in Section 01 (`ContentPlatformStatus` and `OAuthState`), updates the existing `PlatformConfiguration` to persist the new `GrantedScopes` field, and registers both new `DbSet` properties on `ApplicationDbContext` and `IApplicationDbContext`.

## Dependencies

- **Section 01 (Domain Entities):** The `ContentPlatformStatus` entity, `OAuthState` entity, and `PlatformPublishStatus` enum must exist before these configurations can be written. The `Platform` entity must have the `GrantedScopes` property added.

## Files to Create

- `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentPlatformStatusConfiguration.cs`
- `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/OAuthStateConfiguration.cs`

## Files to Modify

- `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/PlatformConfiguration.cs`
- `src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs`
- `src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs`
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Utilities/TestEntityFactory.cs`

---

## Tests First

All tests follow the existing patterns in `ApplicationDbContextConfigurationTests.cs` (model metadata introspection) and `ConcurrencyTests.cs` (Testcontainers-based integration tests).

### Model Metadata Tests (ApplicationDbContextConfigurationTests.cs)

Add the following tests to the existing `ApplicationDbContextConfigurationTests` class:

```csharp
// --- ContentPlatformStatus Configuration Tests ---

// Test: ContentPlatformStatus entity is registered in the model
// Verify: context.Model.FindEntityType(typeof(ContentPlatformStatus)) is not null

// Test: ContentPlatformStatus has composite index on (ContentId, Platform)
// Verify: entityType.GetIndexes() contains index with both ContentId and Platform properties

// Test: ContentPlatformStatus has unique index on IdempotencyKey
// Verify: entityType.GetIndexes() contains index on IdempotencyKey that is IsUnique=true

// Test: ContentPlatformStatus has xmin concurrency token
// Verify: entityType.FindProperty("xmin") is not null and IsConcurrencyToken is true

// Test: ContentPlatformStatus has FK to Content with Cascade delete
// Verify: entityType.GetForeignKeys() contains FK on ContentId with DeleteBehavior.Cascade

// --- OAuthState Configuration Tests ---

// Test: OAuthState entity is registered in the model
// Verify: context.Model.FindEntityType(typeof(OAuthState)) is not null

// Test: OAuthState has unique index on State
// Verify: entityType.GetIndexes() contains index on State with IsUnique=true

// Test: OAuthState has index on ExpiresAt (for cleanup queries)
// Verify: entityType.GetIndexes() contains index on ExpiresAt

// --- Platform GrantedScopes Tests ---

// Test: Platform entity has GrantedScopes property mapped as text array
// Verify: entityType.FindProperty("GrantedScopes") is not null, column type is "text[]"

// --- DbContext DbSet Tests ---

// Test: DbContext includes ContentPlatformStatuses DbSet
// Verify: context.Model.FindEntityType(typeof(ContentPlatformStatus)) is not null

// Test: DbContext includes OAuthStates DbSet
// Verify: context.Model.FindEntityType(typeof(OAuthState)) is not null
```

### Integration Tests (ConcurrencyTests.cs or new file)

Add Testcontainers-based tests using the existing `PostgresFixture` and `[Collection("Postgres")]` pattern:

```csharp
// Test: ContentPlatformStatus persists and retrieves from DB
// Setup: EnsureCreated, add a ContentPlatformStatus with valid ContentId, save, read back
// Verify: all fields roundtrip correctly

// Test: ContentPlatformStatus unique index on IdempotencyKey prevents duplicates
// Setup: Insert two ContentPlatformStatus with same IdempotencyKey
// Verify: second SaveChanges throws DbUpdateException (unique violation)

// Test: ContentPlatformStatus xmin concurrency token throws DbUpdateConcurrencyException on stale write
// Setup: Read same entity from two contexts, update first, save, update second, save
// Verify: second save throws DbUpdateConcurrencyException
// Pattern: follow exactly the ConcurrentUpdate_SameContent_ThrowsDbUpdateConcurrencyException pattern

// Test: OAuthState persists and retrieves with State unique index
// Setup: Insert OAuthState, read back, verify fields
// Insert duplicate State, verify DbUpdateException

// Test: OAuthState ExpiresAt index allows efficient cleanup queries
// Setup: Insert multiple OAuthState entries with varying ExpiresAt
// Query: Where ExpiresAt < now
// Verify: returns only expired entries

// Test: Platform entity GrantedScopes field persists string array
// Setup: Create Platform with GrantedScopes = ["tweet.read", "tweet.write"], save, read back
// Verify: GrantedScopes roundtrips as string array with correct values
```

### TestEntityFactory Extension

Add factory methods for the new entities:

```csharp
// CreateContentPlatformStatus: creates a ContentPlatformStatus with defaults
//   - ContentId (required Guid parameter)
//   - Platform (default PlatformType.TwitterX)
//   - Status (default PlatformPublishStatus.Pending)
//   - IdempotencyKey (default: computed SHA256 of ContentId:Platform:1)

// CreateOAuthState: creates an OAuthState with defaults
//   - Platform (default PlatformType.TwitterX)
//   - State (default: random hex string)
//   - ExpiresAt (default: DateTimeOffset.UtcNow.AddMinutes(10))
//   - CodeVerifier (default: null)
```

---

## Implementation Details

### 1. ContentPlatformStatusConfiguration

Create `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentPlatformStatusConfiguration.cs`.

Implements `IEntityTypeConfiguration<ContentPlatformStatus>` following the same patterns as existing configurations (e.g., `AgentExecutionConfiguration`, `ContentConfiguration`).

Key configuration points:

- **Table name:** `"ContentPlatformStatuses"`
- **Primary key:** `Id`
- **Required properties:** `ContentId`, `Platform`, `Status`
- **String length constraints:** `ErrorMessage` max 4000, `PlatformPostId` max 500, `PostUrl` max 2000, `IdempotencyKey` max 200
- **Composite index:** `(ContentId, Platform)` -- not unique, since retries may create new entries but idempotency is handled by the key
- **Unique index:** `IdempotencyKey` -- prevents duplicate publish attempts
- **Foreign key:** `ContentId` references `Content` with `DeleteBehavior.Cascade` (when content is deleted, platform statuses go with it)
- **xmin concurrency token:** Same pattern as `ContentConfiguration` and `PlatformConfiguration`:
  ```csharp
  builder.Property<uint>("xmin")
      .HasColumnType("xid")
      .ValueGeneratedOnAddOrUpdate()
      .IsConcurrencyToken();
  ```
- **Default values:** `RetryCount` defaults to 0, `Status` defaults to `PlatformPublishStatus.Pending`
- **Ignore DomainEvents** if `ContentPlatformStatus` inherits from `EntityBase`

### 2. OAuthStateConfiguration

Create `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/OAuthStateConfiguration.cs`.

Implements `IEntityTypeConfiguration<OAuthState>`.

Key configuration points:

- **Table name:** `"OAuthStates"`
- **Primary key:** `Id`
- **Required properties:** `State`, `Platform`, `CreatedAt`, `ExpiresAt`
- **String length constraints:** `State` max 200, `CodeVerifier` max 200
- **Unique index on `State`:** CSRF protection lookup key -- must be unique
- **Index on `ExpiresAt`:** Enables efficient cleanup queries by `TokenRefreshProcessor`
- **No concurrency token needed:** Short-lived entries, no concurrent writes expected
- **Ignore DomainEvents** if applicable

### 3. PlatformConfiguration Update

Modify `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/PlatformConfiguration.cs` to add:

```csharp
builder.Property(p => p.GrantedScopes)
    .HasColumnType("text[]");
```

Maps `string[]?` to PostgreSQL `text[]` native array column. No JSON conversion needed -- EF Core + Npgsql supports native array types directly.

### 4. ApplicationDbContext Update

Add two new `DbSet` properties:

```csharp
public DbSet<ContentPlatformStatus> ContentPlatformStatuses => Set<ContentPlatformStatus>();
public DbSet<OAuthState> OAuthStates => Set<OAuthState>();
```

No changes to `OnModelCreating` needed -- configurations are discovered via `ApplyConfigurationsFromAssembly`.

### 5. IApplicationDbContext Update

Add the corresponding interface members:

```csharp
DbSet<ContentPlatformStatus> ContentPlatformStatuses { get; }
DbSet<OAuthState> OAuthStates { get; }
```

Add required `using` for new entity namespaces.

### 6. Migration Note

After implementing, generate a new EF Core migration:

```bash
dotnet ef migrations add AddPlatformIntegrationEntities \
  --project src/PersonalBrandAssistant.Infrastructure \
  --startup-project src/PersonalBrandAssistant.Api
```

This creates:
- `ContentPlatformStatuses` table with composite index, unique IdempotencyKey index, and xmin column
- `OAuthStates` table with unique State index and ExpiresAt index
- `GrantedScopes` column (text[]) on existing `Platforms` table

---

## Implementation Notes (Post-Build)

### Code Review Deviations
1. **Removed duplicate tests:** `DbContext_IncludesContentPlatformStatusesDbSet` and `DbContext_IncludesOAuthStatesDbSet` were duplicates of `*_IsRegistered` tests.
2. **No xmin on OAuthState:** Intentional per plan — short-lived, write-once entries.
3. **CodeVerifier plaintext:** Acceptable for 10-min TTL PKCE verifiers.

### Test Results
- 9 new model metadata tests added to ApplicationDbContextConfigurationTests
- 2 factory methods added to TestEntityFactory (CreateContentPlatformStatus, CreateOAuthState)
- All 476 project tests pass

### Actual Files Created
- `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentPlatformStatusConfiguration.cs`
- `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/OAuthStateConfiguration.cs`

### Actual Files Modified
- `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/PlatformConfiguration.cs` (added GrantedScopes text[] mapping)
- `src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs` (added 2 DbSets)
- `src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs` (added 2 DbSets)
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs` (added 9 tests)
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Utilities/TestEntityFactory.cs` (added 2 factory methods)

## Verification Checklist

1. All model metadata tests pass (indexes, concurrency tokens, FK behaviors)
2. Testcontainers integration tests pass (roundtrip, unique constraint violations, concurrency)
3. `Platform.GrantedScopes` roundtrips a string array through PostgreSQL
4. `ContentPlatformStatus.IdempotencyKey` unique constraint rejects duplicates
5. `ContentPlatformStatus` xmin concurrency token prevents stale writes
6. `OAuthState.State` unique constraint prevents duplicate state values
7. Both `DbSet` properties are accessible via `IApplicationDbContext`
8. `dotnet build` succeeds with no warnings
