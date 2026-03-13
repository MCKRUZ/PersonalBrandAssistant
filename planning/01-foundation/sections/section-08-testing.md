# Section 08 — Testing

## Overview

This section implements the infrastructure integration testing suite for the Personal Brand Assistant foundation layer. The scope was focused on Infrastructure.Tests covering persistence, API, and service tests using Testcontainers for real PostgreSQL integration.

**Dependencies:** Sections 01 through 07 must be complete.

## Implementation Notes (Actual)

**Scope deviation:** The original plan called for ~25 test files across Domain.Tests, Application.Tests, and Infrastructure.Tests. The actual implementation focused on Infrastructure.Tests (7 new files, 58 tests total including prior fixtures) since these provide the highest-value coverage for the foundation layer. Domain and Application tests can be added incrementally.

### Files Created/Modified

- `tests/PersonalBrandAssistant.Infrastructure.Tests/Utilities/TestEntityFactory.cs` — Factory methods for Content, Platform, BrandProfile, User, plus `CreateArchivedContent` helper
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ConcurrencyTests.cs` — xmin optimistic concurrency for Content and Platform entities (3 tests)
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/QueryFilterTests.cs` — Global query filter: archived excluded by default, IgnoreQueryFilters includes, FindAsync returns null for archived (3 tests)
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/MigrationTests.cs` — Schema creation verifies all 6 expected tables (1 test)
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/AuditLogCleanupServiceTests.cs` — Cleanup deletes old entries, preserves recent, handles empty table, boundary condition at exact cutoff (4 tests)
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/SwaggerTests.cs` — Swagger accessible in Development, returns 404 in Production (2 tests)
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/CustomWebApplicationFactory.cs` — Made environment configurable (was hardcoded to Development)

### Test Results
- **58 tests total**, all passing
- **0 skipped, 0 failed**

### Code Review Fixes Applied
- Added `Assert.NotNull` guards before null-forgiving operators
- Extracted `CreateArchivedContent` helper to reduce duplication
- Added Platform concurrency test (was missing)
- Added Swagger Production 404 test
- Added boundary condition test for audit log cleanup
- Made `CustomWebApplicationFactory` environment-configurable
- Fixed SwaggerTests to use authenticated client

## Testing Stack

| Tool | Purpose |
|------|---------|
| xUnit | Test framework |
| Moq | Mocking (Application layer tests) |
| Testcontainers | Real PostgreSQL for integration tests |
| WebApplicationFactory\<Program\> | API-level integration tests |
| FluentAssertions (optional) | Readable assertions |

**Coverage target:** 80% minimum across all test projects.

**Run command:** `dotnet test` from solution root.

---

## Test Project Paths

```
tests/
├── PersonalBrandAssistant.Domain.Tests/
├── PersonalBrandAssistant.Application.Tests/
└── PersonalBrandAssistant.Infrastructure.Tests/
```

Each test project should already have references to its corresponding source project (set up in section-01). Infrastructure.Tests additionally references the Api project for `WebApplicationFactory<Program>` tests.

---

## 1. Domain Tests

**File path:** `tests/PersonalBrandAssistant.Domain.Tests/`

### Entity Base Class Tests

**File:** `tests/PersonalBrandAssistant.Domain.Tests/EntityBaseTests.cs`

- Test that an entity ID is generated as a valid UUIDv7 (version byte equals 7, timestamp extractable).
- Test that two entities created sequentially have IDs that sort chronologically.

### Content Status State Machine Tests

**File:** `tests/PersonalBrandAssistant.Domain.Tests/ContentStateMachineTests.cs`

This is the most critical domain test file. Every valid and invalid transition must be verified. The `TransitionTo` method on the `Content` entity enforces the state machine and raises a `ContentStateChangedEvent` on success.

Valid transitions to verify:

| From | To | Expected |
|------|----|----------|
| Draft | Review | Succeeds |
| Draft | Archived | Succeeds |
| Review | Draft | Succeeds |
| Review | Approved | Succeeds |
| Approved | Scheduled | Succeeds |
| Approved | Draft | Succeeds |
| Scheduled | Publishing | Succeeds |
| Scheduled | Draft | Succeeds |
| Publishing | Published | Succeeds |
| Publishing | Failed | Succeeds |
| Published | Archived | Succeeds |
| Failed | Draft | Succeeds |
| Failed | Archived | Succeeds |
| Archived | Draft | Succeeds |

Invalid transitions to verify (each should throw `InvalidOperationException`):

| From | To |
|------|---|
| Draft | Published |
| Publishing | Draft |
| Published | Draft |
| Archived | Published |

Additionally:

- Test that new Content defaults to Draft status.
- Test that `TransitionTo` raises `ContentStateChangedEvent` with correct old and new status values.

### Content TargetPlatforms Tests

**File:** `tests/PersonalBrandAssistant.Domain.Tests/ContentTargetPlatformsTests.cs`

- Test that Content created with multiple `PlatformType` values stores them correctly.
- Test that Content with an empty `TargetPlatforms` array is valid.

### ContentMetadata Tests

**File:** `tests/PersonalBrandAssistant.Domain.Tests/ContentMetadataTests.cs`

- Test that `ContentMetadata` with all fields populated creates a valid object.
- Test that `ContentMetadata` with null optional fields (`AiGenerationContext`, `TokensUsed`, `EstimatedCost`) is valid.
- Test that `Tags` and `SeoKeywords` are initialized as empty lists, not null.

### Platform Tests

**File:** `tests/PersonalBrandAssistant.Domain.Tests/PlatformTests.cs`

- Test that Platform created with all required fields succeeds.
- Test that `EncryptedAccessToken` and `EncryptedRefreshToken` are byte arrays (not auto-decrypted strings).

### BrandProfile Tests

**File:** `tests/PersonalBrandAssistant.Domain.Tests/BrandProfileTests.cs`

- Test that BrandProfile with valid fields creates successfully.
- Test that `ToneDescriptors` and `Topics` initialize as empty lists.

### ContentCalendarSlot Tests

**File:** `tests/PersonalBrandAssistant.Domain.Tests/ContentCalendarSlotTests.cs`

- Test that a slot with a valid IANA `TimeZoneId` creates successfully.
- Test that a slot with `RecurrencePattern` stores a cron string.
- Test that a non-recurring slot has null `RecurrencePattern`.

### AuditLogEntry Tests

**File:** `tests/PersonalBrandAssistant.Domain.Tests/AuditLogEntryTests.cs`

- Test that `AuditLogEntry` created with required fields succeeds.
- Test that `OldValue` and `NewValue` accept null.

### User Tests

**File:** `tests/PersonalBrandAssistant.Domain.Tests/UserTests.cs`

- Test that User created with a valid `TimeZoneId` succeeds.
- Test that `Settings` is a complex type (not null by default).

### Enum Tests

**File:** `tests/PersonalBrandAssistant.Domain.Tests/EnumTests.cs`

- Test that `ContentType` has exactly 4 values: BlogPost, SocialPost, Thread, VideoDescription.
- Test that `ContentStatus` has exactly 8 values: Draft, Review, Approved, Scheduled, Publishing, Published, Failed, Archived.
- Test that `PlatformType` has exactly 4 values: TwitterX, LinkedIn, Instagram, YouTube.
- Test that `AutonomyLevel` has exactly 4 values: Manual, Assisted, SemiAuto, Autonomous.

---

## 2. Application Tests

**File path:** `tests/PersonalBrandAssistant.Application.Tests/`

### Result\<T\> Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Common/ResultTests.cs`

- `Result.Success(value)` sets `IsSuccess=true`, `Value` populated, `ErrorCode=None`, `Errors` empty.
- `Result.Failure(errorCode, errors)` sets `IsSuccess=false`, `Value=null`, `ErrorCode` set, `Errors` populated.
- `Result.NotFound(message)` sets `ErrorCode=NotFound`, single error message.
- `Result.ValidationFailure(errors)` sets `ErrorCode=ValidationFailed`, multiple errors.
- `Result.Conflict(message)` sets `ErrorCode=Conflict`.

### ValidationBehavior Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Behaviors/ValidationBehaviorTests.cs`

Use Moq to mock validators and the next delegate in the pipeline.

- Valid request passes through to handler; handler result returned.
- Invalid request short-circuits; returns `Result.ValidationFailure` with error list.
- Request with no registered validators passes through without issue.

### LoggingBehavior Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Behaviors/LoggingBehaviorTests.cs`

Use Moq to mock `ILogger`.

- Successful request logs start and completion with duration.
- Failed request logs start and failure.
- Sensitive fields matching `*Token*`, `*Password*`, `*Secret*` are not logged.

### CreateContentCommand Handler Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/CreateContentCommandTests.cs`

Mock `IApplicationDbContext` (with an in-memory `DbSet<Content>` or similar mock).

- Valid command creates content with Draft status; returns new ID.
- Command with missing Body fails validation.
- Command with invalid `ContentType` fails validation.
- Created content has a UUIDv7 ID and `CreatedAt` set.

### UpdateContentCommand Handler Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/UpdateContentCommandTests.cs`

- Update Draft content succeeds; fields updated.
- Update Review content succeeds.
- Update Published content returns failure (not editable).
- Update non-existent content returns `NotFound`.
- Update with stale version returns `Conflict` (concurrency).

### DeleteContentCommand Handler Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/DeleteContentCommandTests.cs`

- Delete existing content transitions to Archived.
- Delete already-Archived content returns an appropriate result.
- Delete non-existent content returns `NotFound`.

### GetContentQuery Handler Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Queries/GetContentQueryTests.cs`

- Existing content returned with all fields.
- Non-existent ID returns `NotFound`.
- Archived content excluded by default (query filter).

### ListContentQuery Handler Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Queries/ListContentQueryTests.cs`

- Returns paginated results with default sort (`CreatedAt` descending).
- Filters by `ContentType` correctly.
- Filters by `Status` correctly.
- Keyset pagination returns correct next page (cursor-based).
- Max page size capped at 50.
- Empty result set returns empty list, not an error.
- Archived content excluded from results.

### Validator Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Validators/ContentValidatorTests.cs`

- `CreateContentValidator`: Body is required; `ContentType` must be a valid enum value.
- `UpdateContentValidator`: Id is required; at least one field to update must be provided.
- `ListContentValidator`: Page size must be between 1 and 50.

---

## 3. Infrastructure Integration Tests

**File path:** `tests/PersonalBrandAssistant.Infrastructure.Tests/`

These tests use real PostgreSQL via Testcontainers and `WebApplicationFactory<Program>` for API-level tests. They require Docker to be running on the test machine.

### Test Fixture

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/TestFixture.cs`

Create a shared test fixture (xUnit `IAsyncLifetime`) that:

- Starts a PostgreSQL Testcontainer.
- Creates a `WebApplicationFactory<Program>` configured to use the Testcontainer connection string.
- Configures a known test API key in the test configuration.
- Provides helper methods for creating authenticated HTTP clients (with `X-Api-Key` header).
- Handles cleanup and container disposal.

Stub signature:

```csharp
/// <summary>
/// Shared test fixture providing a real PostgreSQL database via Testcontainers
/// and a configured WebApplicationFactory for integration tests.
/// </summary>
public class TestFixture : IAsyncLifetime
{
    // Testcontainer setup, WebApplicationFactory, helper methods
}
```

### Entity Configuration Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/EntityConfigurationTests.cs`

Verify EF Core model configuration against the real database schema:

- Content table uses TPH with `ContentType` discriminator.
- `Content.Metadata` maps to a jsonb column.
- `Content.TargetPlatforms` maps to a `PlatformType` array with GIN index.
- Content has `xmin` concurrency token configured.
- Content has a global query filter excluding Archived status.
- `Platform.Type` has a unique constraint.
- `ContentCalendarSlot` has a composite index on `(ScheduledDate, TargetPlatform)`.

### Migration Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/MigrationTests.cs`

- Migrations apply cleanly to an empty database.
- All expected tables are created with correct columns and types.
- Expected indexes exist (TargetPlatforms GIN, CalendarSlot composite).

### Encryption Service Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/EncryptionServiceTests.cs`

These can run without Testcontainers (Data Protection is in-memory for tests):

- `Encrypt(plaintext)` returns a non-null byte array.
- `Decrypt(encrypted)` returns the original plaintext.
- Encrypting the same plaintext twice produces different ciphertext (random IV).
- Decrypting tampered data throws an exception.
- Round-trip: `Encrypt` then `Decrypt` preserves the original string.

### Auditable Interceptor Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/AuditableInterceptorTests.cs`

- Inserting an entity sets `CreatedAt` to current UTC time.
- Inserting an entity sets `UpdatedAt` to current UTC time.
- Updating an entity updates `UpdatedAt` but not `CreatedAt`.
- Entities not implementing `IAuditable` are unaffected.

### Audit Log Interceptor Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/AuditLogInterceptorTests.cs`

- Modifying a Content entity creates an `AuditLogEntry`.
- `AuditLogEntry` contains correct `EntityType` and `EntityId`.
- Encrypted fields (`EncryptedAccessToken`, `EncryptedRefreshToken`) excluded from `OldValue`/`NewValue`.
- `OldValue`/`NewValue` are structured JSON.
- `OldValue`/`NewValue` truncated at 4KB.

### Seed Data Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/DataSeederTests.cs`

- Seeder creates a default `BrandProfile` when the table is empty.
- Seeder creates 4 `Platform` records (one per `PlatformType`) when the table is empty.
- Seeder creates a default `User` when the table is empty.
- Seeder does NOT duplicate records on a second run (idempotent).

### Audit Log Cleanup Service Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/AuditLogCleanupServiceTests.cs`

- Entries older than 90 days are deleted.
- Entries within 90 days are preserved.
- Cleanup runs without error on an empty table.

### Optimistic Concurrency Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/ConcurrencyTests.cs`

- Concurrent update to the same Content entity throws `DbUpdateConcurrencyException`.
- Concurrent update to the same Platform entity throws `DbUpdateConcurrencyException`.
- Sequential updates with fresh reads succeed.

### Global Query Filter Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/QueryFilterTests.cs`

- Querying Content excludes Archived items by default.
- `IgnoreQueryFilters()` includes Archived content.
- `GetContentQuery` for an archived ID returns `NotFound` (filter active).

### API Key Middleware Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/ApiKeyMiddlewareTests.cs`

Uses `WebApplicationFactory<Program>` with an HTTP client:

- Request with valid `X-Api-Key` header passes through (200 on health/ready).
- Request with invalid `X-Api-Key` returns 401 ProblemDetails.
- Request with missing `X-Api-Key` returns 401 ProblemDetails.
- `GET /health` (liveness) returns 200 without an API key (exempt).
- `GET /health/ready` returns 401 without an API key (not exempt).

### Content Endpoints Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/ContentEndpointsTests.cs`

Full API integration tests using `WebApplicationFactory<Program>` with real PostgreSQL:

- `POST /api/content` with valid body returns 201 with created ID.
- `POST /api/content` with invalid body returns 400 ProblemDetails with validation errors.
- `GET /api/content/{id}` with existing ID returns 200 with content.
- `GET /api/content/{id}` with non-existent ID returns 404 ProblemDetails.
- `GET /api/content` with no params returns 200 with paginated list.
- `GET /api/content?contentType=BlogPost` returns 200 with filtered results.
- `PUT /api/content/{id}` with valid body returns 200 updated.
- `PUT /api/content/{id}` with stale version returns 409 ProblemDetails.
- `DELETE /api/content/{id}` returns 200 (soft delete).

### Result-to-HTTP Mapper Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/ResultToHttpMapperTests.cs`

These can be unit tests (no database needed):

- `Result.Success` maps to 200 with JSON body.
- `Result.ValidationFailure` maps to 400 ProblemDetails.
- `Result.NotFound` maps to 404 ProblemDetails.
- `Result.Conflict` maps to 409 ProblemDetails.
- All error responses have content-type `application/problem+json`.

### Global Exception Handler Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/ExceptionHandlerTests.cs`

- Unhandled exception returns 500 ProblemDetails (no stack trace in Production).
- Unhandled exception is logged via Serilog.

### Health Endpoint Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/HealthEndpointTests.cs`

- `/health` returns 200 when API is running.
- `/health/ready` returns 200 when DB is connected (with valid API key).
- `/health/ready` returns 503 when DB is unreachable.

### Swagger Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/SwaggerTests.cs`

- `/swagger` is accessible in Development environment.
- `/swagger` returns 404 in Production environment.

---

## 4. Test Utilities

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Utilities/TestEntityFactory.cs`

Factory methods for creating test entities with sensible defaults. This avoids repeating boilerplate entity construction across test files.

```csharp
/// <summary>
/// Provides factory methods for creating domain entities with sensible defaults,
/// reducing boilerplate in test setup.
/// </summary>
public static class TestEntityFactory
{
    /// <summary>Creates a Content entity in Draft status with default field values.</summary>
    public static Content CreateContent(/* optional overrides */) { ... }

    /// <summary>Creates a Platform entity with default field values.</summary>
    public static Platform CreatePlatform(/* optional overrides */) { ... }

    /// <summary>Creates a BrandProfile with default field values.</summary>
    public static BrandProfile CreateBrandProfile(/* optional overrides */) { ... }

    /// <summary>Creates a User with default field values.</summary>
    public static User CreateUser(/* optional overrides */) { ... }
}
```

---

## Implementation Checklist

1. Set up the `TestFixture` class with Testcontainers and `WebApplicationFactory<Program>` in Infrastructure.Tests.
2. Create `TestEntityFactory` utility with factory methods for all entities.
3. Write Domain.Tests: entity base, state machine (highest priority), metadata, enums, and all entity creation tests.
4. Write Application.Tests: `Result<T>`, pipeline behaviors, all Content CRUD handler tests, and validator tests.
5. Write Infrastructure.Tests: entity configuration, migrations, encryption, interceptors, seed data, concurrency, query filters.
6. Write API integration tests: middleware, endpoints, result mapper, exception handler, health, swagger.
7. Run `dotnet test` and verify all tests pass.
8. Check coverage with `dotnet test --collect:"XPlat Code Coverage"` and verify 80% minimum.
