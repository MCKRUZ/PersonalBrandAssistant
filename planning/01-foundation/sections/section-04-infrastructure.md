# Section 04: Infrastructure Layer

## Overview

This section implements the Infrastructure layer of the Clean Architecture: `ApplicationDbContext` with all entity configurations, EF Core migrations, the encryption service, interceptors for auditing, seed data, audit log cleanup, and the `DependencyInjection.cs` registration entry point.

**Project:** `src/PersonalBrandAssistant.Infrastructure/`

**Depends on:**
- Section 01 (scaffolding -- project files, NuGet packages, project references must exist)
- Section 02 (domain -- all entities, enums, value objects, domain events)
- Section 03 (application -- `IApplicationDbContext`, `IEncryptionService`, `IDateTimeProvider`, `IAuditable` interface, `Result<T>`)

**Blocks:** Section 05 (API layer), Section 08 (testing)

---

## Tests First

All infrastructure tests live in `tests/PersonalBrandAssistant.Infrastructure.Tests/`. Integration tests use **Testcontainers** to spin up a real PostgreSQL 17 instance -- no in-memory fakes.

### ApplicationDbContext -- Entity Configuration Tests

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs`

These tests verify the EF Core model metadata rather than hitting the database directly. Use `context.Model.FindEntityType(typeof(Content))` to assert configuration.

- Content table uses TPH with `ContentType` discriminator column
- `Content.Metadata` maps to a jsonb column (complex type via `ToJson()`)
- `Content.TargetPlatforms` maps to a `PlatformType[]` array column with a GIN index
- Content has `xmin` concurrency token configured
- Content has a global query filter excluding `Status == Archived`
- `Platform.Type` has a unique constraint
- `ContentCalendarSlot` has a composite index on `(ScheduledDate, TargetPlatform)`

### Database Migration Tests

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/MigrationTests.cs`

Uses Testcontainers PostgreSQL. One-time container setup via xUnit `IAsyncLifetime` or class fixture.

- Migrations apply cleanly to an empty database (`context.Database.MigrateAsync()` completes without exception)
- All expected tables are created with correct columns and types (query `information_schema.columns`)
- Indexes exist: TargetPlatforms GIN index, ContentCalendarSlot composite index

### Encryption Service Tests

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/EncryptionServiceTests.cs`

These can be unit tests (no database needed). Set up Data Protection with an ephemeral provider for testing.

- `Encrypt(plaintext)` returns a non-null, non-empty byte array
- `Decrypt(encrypted)` returns the original plaintext
- Encrypting the same plaintext twice produces different ciphertext (Data Protection uses a random IV)
- `Decrypt` with tampered data throws `CryptographicException`
- Round-trip: `Decrypt(Encrypt(original))` equals original for various strings (empty string, unicode, long strings)

### Auditable Interceptor Tests

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Interceptors/AuditableInterceptorTests.cs`

Uses Testcontainers PostgreSQL with the real `ApplicationDbContext`.

- Inserting an entity implementing `IAuditable` sets `CreatedAt` to approximately current UTC time
- Inserting an entity sets `UpdatedAt` to approximately current UTC time
- Updating an entity updates `UpdatedAt` but preserves the original `CreatedAt`
- Entities not implementing `IAuditable` are unaffected (e.g., `AuditLogEntry` itself)

### Audit Log Interceptor Tests

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Interceptors/AuditLogInterceptorTests.cs`

Uses Testcontainers PostgreSQL.

- Modifying a `Content` entity creates an `AuditLogEntry` record
- The `AuditLogEntry` contains the correct `EntityType` (e.g., `"Content"`) and `EntityId`
- Encrypted fields (`EncryptedAccessToken`, `EncryptedRefreshToken`) are excluded from `OldValue`/`NewValue`
- `OldValue` and `NewValue` are valid JSON strings
- `OldValue`/`NewValue` are truncated at 4KB if the serialized change data exceeds that limit

### Seed Data Tests

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/DataSeederTests.cs`

Uses Testcontainers PostgreSQL.

- Seeder creates a default `BrandProfile` when the table is empty
- Seeder creates exactly 4 `Platform` records (one per `PlatformType`) when the table is empty
- Seeder creates a default `User` when the table is empty
- Seeder does NOT duplicate records on a second run (idempotent -- run `StartAsync` twice, assert counts unchanged)

### Audit Log Cleanup Service Tests

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AuditLogCleanupServiceTests.cs`

Uses Testcontainers PostgreSQL.

- Entries older than 90 days are deleted
- Entries within 90 days are preserved
- Cleanup runs without error on an empty table

### Optimistic Concurrency Tests

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/OptimisticConcurrencyTests.cs`

Uses Testcontainers PostgreSQL. Two separate `DbContext` instances reading the same entity, then both saving.

- Concurrent update to the same `Content` entity raises `DbUpdateConcurrencyException`
- Concurrent update to the same `Platform` entity raises `DbUpdateConcurrencyException`
- Sequential updates with fresh reads between them succeed without exception

### Global Query Filter Tests

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/QueryFilterTests.cs`

Uses Testcontainers PostgreSQL.

- Querying `Content` via `DbSet` excludes entries with `Status == Archived`
- Using `.IgnoreQueryFilters()` includes Archived content
- Directly querying an Archived content by ID via the normal `DbSet` returns null (filter active)

---

## Implementation Details

### File: `src/PersonalBrandAssistant.Infrastructure/Persistence/ApplicationDbContext.cs`

Implements `IApplicationDbContext` (defined in the Application layer).

**DbSet properties:**
- `DbSet<Content> Contents`
- `DbSet<Platform> Platforms`
- `DbSet<BrandProfile> BrandProfiles`
- `DbSet<ContentCalendarSlot> ContentCalendarSlots`
- `DbSet<AuditLogEntry> AuditLogEntries`
- `DbSet<User> Users`

**OnModelCreating override** calls `ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly())` to pick up all `IEntityTypeConfiguration<T>` implementations.

Register the Npgsql enum mapping for `PlatformType`, `ContentType`, `ContentStatus`, and `AutonomyLevel` at the NpgsqlDataSource level (in DI registration), not inside `OnModelCreating`. Use `NpgsqlDataSourceBuilder.MapEnum<T>()`.

### Entity Configurations

Each entity gets its own configuration file under `src/PersonalBrandAssistant.Infrastructure/Persistence/Configurations/`.

#### `ContentConfiguration.cs`

- Table name: `"Contents"`
- TPH discriminator: `.HasDiscriminator(c => c.ContentType)` with `.HasValue<Content>(ContentType.BlogPost)` etc. for each `ContentType` enum value. Since there are no derived classes, configure all discriminator values on the base `Content` entity.
- `Metadata` property: `.ComplexProperty(c => c.Metadata).ToJson()` -- this maps the entire `ContentMetadata` complex type to a single jsonb column.
- `TargetPlatforms`: `.HasColumnType("platform_type[]")` -- Npgsql maps `PlatformType[]` natively. Add a GIN index: `.HasIndex(c => c.TargetPlatforms).HasMethod("gin")`.
- `Version` (uint): `.IsRowVersion()` combined with `.UseXminAsConcurrencyToken()` on the entity. EF Core + Npgsql uses PostgreSQL's `xmin` system column.
- Global query filter: `.HasQueryFilter(c => c.Status != ContentStatus.Archived)`.
- `Body` is required. `Title` is optional. `ParentContentId` is an optional self-referential FK.
- Index on `Status` for filtered queries.
- Index on `ScheduledAt` for the publishing scheduler.

#### `PlatformConfiguration.cs`

- `Type` has a unique index (one connection per platform type).
- `EncryptedAccessToken` and `EncryptedRefreshToken` map as `byte[]` columns directly. No value converter -- encryption is handled by `IEncryptionService` in application code.
- `RateLimitState`: `.ComplexProperty(p => p.RateLimitState).ToJson()`.
- `Settings`: `.ComplexProperty(p => p.Settings).ToJson()`.
- `xmin` concurrency token via `.UseXminAsConcurrencyToken()`.

#### `BrandProfileConfiguration.cs`

- `ToneDescriptors`, `Topics`, `ExampleContent`: PostgreSQL text arrays (`List<string>` mapped natively by Npgsql).
- `VocabularyPreferences`: `.ComplexProperty(b => b.VocabularyPreferences).ToJson()`.
- `xmin` concurrency token.

#### `ContentCalendarSlotConfiguration.cs`

- Composite index on `(ScheduledDate, TargetPlatform)`.
- Optional FK to `Content` via `ContentId`.
- `RecurrencePattern` is optional string.

#### `AuditLogEntryConfiguration.cs`

- `Timestamp` indexed for cleanup query performance.
- `OldValue` and `NewValue` are optional, stored as `text`.
- No concurrency token needed.

#### `UserConfiguration.cs`

- `Settings`: `.ComplexProperty(u => u.Settings).ToJson()`.
- `Email` has a unique index.

### File: `src/PersonalBrandAssistant.Infrastructure/Interceptors/AuditableInterceptor.cs`

A `SaveChangesInterceptor` that overrides `SavingChangesAsync`.

Logic:
1. Get all `ChangeTracker` entries where the entity implements `IAuditable`.
2. For entries with `EntityState.Added`: set `CreatedAt` and `UpdatedAt` to `IDateTimeProvider.UtcNow`.
3. For entries with `EntityState.Modified`: set only `UpdatedAt` to `IDateTimeProvider.UtcNow`.
4. Call `base.SavingChangesAsync(...)` to continue the pipeline.

The interceptor receives `IDateTimeProvider` via constructor injection (registered in DI, passed when configuring DbContext options).

### File: `src/PersonalBrandAssistant.Infrastructure/Interceptors/AuditLogInterceptor.cs`

A `SaveChangesInterceptor` that overrides `SavingChangesAsync`.

Logic:
1. Capture modified entities BEFORE `SaveChangesAsync` (read original values from `OriginalValues`).
2. For each tracked entity with state `Added`, `Modified`, or `Deleted`, create an `AuditLogEntry`:
   - `EntityType` = entity CLR type name
   - `EntityId` = the entity's `Id` property (Guid)
   - `Action` = "Created", "Modified", or "Deleted"
   - `OldValue` = JSON of original values (for Modified/Deleted). Exclude properties containing "Encrypted", "Token", "Password", "Secret" in the name.
   - `NewValue` = JSON of current values (for Added/Modified). Same exclusion rules.
   - Truncate `OldValue`/`NewValue` to 4KB each.
   - `Timestamp` = `IDateTimeProvider.UtcNow`
3. Add the `AuditLogEntry` entities to the context.
4. Call `base.SavingChangesAsync(...)`.

Important: The interceptor must avoid creating audit log entries for `AuditLogEntry` itself (infinite recursion guard).

### File: `src/PersonalBrandAssistant.Infrastructure/Services/EncryptionService.cs`

Implements `IEncryptionService`.

Constructor takes `IDataProtector` (created from `IDataProtectionProvider` with purpose `"PersonalBrandAssistant.Secrets"`).

- `Encrypt(string plaintext) -> byte[]`: calls `protector.Protect(Encoding.UTF8.GetBytes(plaintext))`.
- `Decrypt(byte[] ciphertext) -> string`: calls `Encoding.UTF8.GetString(protector.Unprotect(ciphertext))`.

### File: `src/PersonalBrandAssistant.Infrastructure/Services/DateTimeProvider.cs`

Implements `IDateTimeProvider`.

- `UtcNow` property returns `DateTimeOffset.UtcNow`.

### File: `src/PersonalBrandAssistant.Infrastructure/Services/DataSeeder.cs`

Implements `IHostedService`.

`StartAsync`:
1. Create a scope from `IServiceScopeFactory`.
2. Get `ApplicationDbContext` from the scope.
3. Check if `BrandProfiles` is empty -- if so, add a default profile with placeholder values ("Default Profile", empty tone descriptors, etc.).
4. Check if `Platforms` is empty -- if so, add 4 `Platform` records (one per `PlatformType`), all with `IsConnected = false`.
5. Check if `Users` is empty -- if so, add a default user with email and timezone from `IConfiguration` (keys: `"DefaultUser:Email"`, `"DefaultUser:TimeZoneId"`) with fallbacks.
6. Call `SaveChangesAsync`.

`StopAsync`: no-op.

### File: `src/PersonalBrandAssistant.Infrastructure/Services/AuditLogCleanupService.cs`

Extends `BackgroundService`.

`ExecuteAsync`:
1. Loop with a 24-hour delay (`Task.Delay(TimeSpan.FromHours(24), stoppingToken)`).
2. Each iteration: create a scope, get `ApplicationDbContext`, delete all `AuditLogEntries` where `Timestamp < DateTimeOffset.UtcNow.AddDays(-retentionDays)`.
3. `retentionDays` read from `IConfiguration` key `"AuditLog:RetentionDays"`, default 90.
4. Log the number of deleted entries via `ILogger`.
5. Wrap in try/catch to prevent the background service from crashing on transient errors.

### File: `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs`

Static class with extension method `AddInfrastructure(this IServiceCollection services, IConfiguration configuration)`.

Registers:
1. **NpgsqlDataSource** via `NpgsqlDataSourceBuilder` with enum mappings for `PlatformType`, `ContentType`, `ContentStatus`, `AutonomyLevel`. Connection string from `configuration.GetConnectionString("DefaultConnection")`.
2. **ApplicationDbContext** via `AddDbContext<ApplicationDbContext>` using `UseNpgsql(dataSource)`. Add interceptors: `AuditableInterceptor`, `AuditLogInterceptor`.
3. **IEncryptionService** as singleton: `EncryptionService`.
4. **IDateTimeProvider** as singleton: `DateTimeProvider`.
5. **Data Protection** via `AddDataProtection()` with `.PersistKeysToFileSystem(new DirectoryInfo(configuration["DataProtection:KeyPath"] ?? "/data-protection-keys"))` and `.SetApplicationName("PersonalBrandAssistant")`.
6. **DataSeeder** as hosted service.
7. **AuditLogCleanupService** as hosted service.
8. **Health check** for PostgreSQL via `AddHealthChecks().AddNpgSql(connectionString)`.

### EF Core Migration

After all configurations are in place, generate the initial migration:

```
dotnet ef migrations add InitialCreate --project src/PersonalBrandAssistant.Infrastructure --startup-project src/PersonalBrandAssistant.Api
```

The migration will be generated at `src/PersonalBrandAssistant.Infrastructure/Persistence/Migrations/`. Do not hand-edit the migration unless the generated output is incorrect.

---

## Configuration Requirements

The following configuration keys must be available (via `appsettings.json`, User Secrets, or environment variables):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=pba;Username=postgres;Password=..."
  },
  "DataProtection": {
    "KeyPath": "/data-protection-keys"
  },
  "AuditLog": {
    "RetentionDays": 90
  },
  "DefaultUser": {
    "Email": "user@example.com",
    "TimeZoneId": "America/New_York"
  }
}
```

---

## Actual Implementation Notes

### Deviations from Plan

1. **File paths**: Files placed under `Data/` not `Persistence/` (e.g., `Data/ApplicationDbContext.cs`, `Data/Configurations/`, `Data/Interceptors/`).
2. **JsonValueConverter instead of ToJson()**: EF Core 10 preview + Npgsql has a `NullReferenceException` in `ModificationCommand.WriteJsonObject` when using `OwnsOne().ToJson()`. Created `JsonValueConverter<T>` using `ValueConverter<T, string>` with `HasColumnType("jsonb")` for all complex types.
3. **xmin concurrency**: `UseXminAsConcurrencyToken()` removed in Npgsql EF Core 10. Used `builder.Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken()` instead.
4. **No TPH discriminator**: Content doesn't use TPH since there are no derived types.
5. **No GIN index on TargetPlatforms**: Used `integer[]` column type instead of mapped enum array. GIN index deferred.
6. **No enum mapping via NpgsqlDataSourceBuilder**: Enums stored as integers, not PostgreSQL custom types.
7. **EF Core migration deferred**: Requires API project as startup project (section-05).
8. **Migration tests, concurrency tests, query filter tests**: Deferred — require migrations applied to real database (section-05+).

### Code Review Fixes Applied
- Added `ArgumentNullException.ThrowIfNull` to `EncryptionService.Encrypt/Decrypt`
- Fixed `AuditLogCleanupService` delay-before-first-run bug (cleanup now runs immediately on startup)
- Added `xmin` concurrency tokens to `ContentCalendarSlotConfiguration` and `UserConfiguration`
- Injected `IDateTimeProvider` into `AuditLogCleanupService` instead of `DateTimeOffset.UtcNow`

### Test Summary
- 24 tests total (7 unit + 17 integration via Testcontainers)
- Encryption: 7 tests (round-trip, uniqueness, various strings)
- DbContext configuration: 7 tests (indexes, filters, concurrency tokens)
- Auditable interceptor: 3 integration tests
- Audit log interceptor: 3 integration tests
- Data seeder: 4 integration tests

## Key Files Summary (Actual)

| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs` | DbContext implementing IApplicationDbContext |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentConfiguration.cs` | jsonb, xmin, query filter, indexes |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/PlatformConfiguration.cs` | Unique type, encrypted tokens, jsonb |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/BrandProfileConfiguration.cs` | jsonb, xmin |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentCalendarSlotConfiguration.cs` | Composite index, optional FK, xmin |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/AuditLogEntryConfiguration.cs` | Timestamp index |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/UserConfiguration.cs` | jsonb settings, unique email, xmin |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/JsonValueConverter.cs` | Generic JSON value converter for jsonb columns |
| `src/PersonalBrandAssistant.Infrastructure/Data/Interceptors/AuditableInterceptor.cs` | Sets CreatedAt/UpdatedAt |
| `src/PersonalBrandAssistant.Infrastructure/Data/Interceptors/AuditLogInterceptor.cs` | Creates AuditLogEntry on changes |
| `src/PersonalBrandAssistant.Infrastructure/Services/EncryptionService.cs` | Data Protection API wrapper |
| `src/PersonalBrandAssistant.Infrastructure/Services/DateTimeProvider.cs` | IDateTimeProvider implementation |
| `src/PersonalBrandAssistant.Infrastructure/Services/DataSeeder.cs` | Seeds default data on startup |
| `src/PersonalBrandAssistant.Infrastructure/Services/AuditLogCleanupService.cs` | 90-day retention cleanup |
| `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` | Service registration entry point |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/TestFixtures/PostgresFixture.cs` | Shared Testcontainers fixture |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/` | 5 test files, 24 tests total |

---

## Implementation Checklist

1. Create entity configuration files (all 6 configurations)
2. Create `ApplicationDbContext` implementing `IApplicationDbContext`
3. Create `AuditableInterceptor`
4. Create `AuditLogInterceptor`
5. Create `EncryptionService`
6. Create `DateTimeProvider`
7. Create `DataSeeder` hosted service
8. Create `AuditLogCleanupService` background service
9. Create `DependencyInjection.cs` with `AddInfrastructure` extension method
10. Generate the initial EF Core migration
11. Write all tests listed in the Tests First section
12. Verify: `dotnet build` succeeds, `dotnet test` passes for the Infrastructure.Tests project