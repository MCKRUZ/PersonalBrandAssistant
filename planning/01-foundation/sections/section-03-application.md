# Section 03: Application Layer

## Overview

This section implements the Application layer of the Clean Architecture -- the orchestration layer between API endpoints and Domain entities. It includes the `Result<T>` pattern for operation outcomes, interface contracts for infrastructure dependencies, MediatR pipeline behaviors for cross-cutting concerns (validation, logging), Content CRUD commands and queries with keyset pagination, and FluentValidation validators.

**Dependencies:** Section 02 (Domain Layer) must be complete. This section references domain entities (`Content`, `ContentStatus`, `ContentType`, `PlatformType`), the `Content.TransitionTo()` state machine method, and the entity base class with `Id`, `CreatedAt`, `UpdatedAt`.

**Blocks:** Sections 04 (Infrastructure), 05 (API), and 08 (Testing).

---

## Project Context

- **Project:** `PersonalBrandAssistant.Application`
- **Location:** `src/PersonalBrandAssistant.Application/`
- **References:** `PersonalBrandAssistant.Domain` only (no Infrastructure or API references)
- **NuGet packages (already added in Section 01):** MediatR, FluentValidation, FluentValidation.DependencyInjectionExtensions

---

## Tests First

All tests go in `tests/PersonalBrandAssistant.Application.Tests/`. Write these tests before implementing the corresponding code. Tests use xUnit and Moq.

### Result of T Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Common/ResultTests.cs`

- `Result.Success(value)` sets `IsSuccess=true`, `Value` to the provided value, `ErrorCode=None`, `Errors` as empty collection
- `Result.Failure(errorCode, errors)` sets `IsSuccess=false`, `Value=default`, `ErrorCode` to specified code, `Errors` populated with all provided messages
- `Result.NotFound(message)` sets `ErrorCode=NotFound` with a single error message
- `Result.ValidationFailure(errors)` sets `ErrorCode=ValidationFailed` with multiple error messages
- `Result.Conflict(message)` sets `ErrorCode=Conflict` with a single error message

### ValidationBehavior Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Behaviors/ValidationBehaviorTests.cs`

- Valid request passes through to the handler; handler result is returned unchanged
- Invalid request short-circuits before handler; returns `Result.ValidationFailure` with structured error list
- Request with no registered validators passes through to the handler (no-validator scenario)

### LoggingBehavior Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Behaviors/LoggingBehaviorTests.cs`

- Successful request logs start message and completion with elapsed duration
- Failed request logs start message and failure information
- Sensitive fields matching patterns `*Token*`, `*Password*`, `*Secret*` are excluded from log output

### CreateContentCommand Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/CreateContentCommandHandlerTests.cs`

- Valid command creates content with `Draft` status and returns the new `Guid` ID
- Command with missing/empty `Body` fails validation
- Command with invalid `ContentType` fails validation
- Created content has a UUIDv7-format ID and `CreatedAt` set

### UpdateContentCommand Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/UpdateContentCommandHandlerTests.cs`

- Updating `Draft` content succeeds; fields are updated
- Updating `Review` content succeeds
- Updating `Published` content returns failure (not in editable state)
- Updating non-existent content returns `NotFound`
- Updating with a stale version token returns `Conflict`

### DeleteContentCommand Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Commands/DeleteContentCommandHandlerTests.cs`

- Deleting existing content transitions it to `Archived` status
- Deleting already-`Archived` content returns an appropriate result (not a crash)
- Deleting non-existent content returns `NotFound`

### GetContentQuery Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Queries/GetContentQueryHandlerTests.cs`

- Existing content is returned with all fields populated
- Non-existent ID returns `NotFound`
- Archived content is excluded by default (global query filter applied at Infrastructure level, but handler should handle `null` from the query as `NotFound`)

### ListContentQuery Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Queries/ListContentQueryHandlerTests.cs`

- Returns paginated results with default sort by `CreatedAt` descending
- Filters by `ContentType` correctly
- Filters by `Status` correctly
- Keyset pagination returns correct next-page cursor based on `(CreatedAt, Id)`
- Maximum page size is capped at 50 regardless of requested size
- Empty result set returns an empty list, not an error
- Archived content is excluded from results

### FluentValidation Validator Tests

**File:** `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Validators/CreateContentValidatorTests.cs`

- `Body` is required (empty/null fails)
- `ContentType` must be a valid enum value

**File:** `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Validators/UpdateContentValidatorTests.cs`

- `Id` is required (empty Guid fails)
- At least one updatable field must be provided

**File:** `tests/PersonalBrandAssistant.Application.Tests/Features/Content/Validators/ListContentValidatorTests.cs`

- Page size must be between 1 and 50

---

## Implementation Details

### File Structure

```
src/PersonalBrandAssistant.Application/
├── Common/
│   ├── Errors/
│   │   └── ErrorCode.cs
│   ├── Models/
│   │   ├── Result.cs
│   │   └── PagedResult.cs
│   ├── Interfaces/
│   │   ├── IApplicationDbContext.cs
│   │   ├── IEncryptionService.cs
│   │   └── IDateTimeProvider.cs
│   └── Behaviors/
│       ├── ValidationBehavior.cs
│       └── LoggingBehavior.cs
├── Features/
│   └── Content/
│       ├── Commands/
│       │   ├── CreateContent/
│       │   │   ├── CreateContentCommand.cs
│       │   │   ├── CreateContentCommandHandler.cs
│       │   │   └── CreateContentCommandValidator.cs
│       │   ├── UpdateContent/
│       │   │   ├── UpdateContentCommand.cs
│       │   │   ├── UpdateContentCommandHandler.cs
│       │   │   └── UpdateContentCommandValidator.cs
│       │   └── DeleteContent/
│       │       ├── DeleteContentCommand.cs
│       │       ├── DeleteContentCommandHandler.cs
│       │       └── DeleteContentCommandValidator.cs
│       └── Queries/
│           ├── GetContent/
│           │   ├── GetContentQuery.cs
│           │   └── GetContentQueryHandler.cs
│           └── ListContent/
│               ├── ListContentQuery.cs
│               ├── ListContentQueryHandler.cs
│               └── ListContentQueryValidator.cs
└── DependencyInjection.cs
```

### ErrorCode Enum

**File:** `src/PersonalBrandAssistant.Application/Common/Errors/ErrorCode.cs`

Define an enum with values: `None`, `ValidationFailed`, `NotFound`, `Conflict`, `Unauthorized`, `InternalError`.

### Result of T

**File:** `src/PersonalBrandAssistant.Application/Common/Models/Result.cs`

A generic class `Result<T>` with:

- Properties: `T? Value`, `bool IsSuccess`, `ErrorCode ErrorCode`, `IReadOnlyList<string> Errors`
- Private constructor (force use of factory methods)
- Static factory methods:
  - `Success(T value)` -- returns result with `IsSuccess=true`, `Value=value`, `ErrorCode=None`, empty errors
  - `Failure(ErrorCode errorCode, params string[] errors)` -- returns result with `IsSuccess=false`, `Value=default`, specified error code and messages
  - `NotFound(string message)` -- shorthand for `Failure(ErrorCode.NotFound, message)`
  - `ValidationFailure(IEnumerable<string> errors)` -- shorthand for `Failure(ErrorCode.ValidationFailed, errors)`
  - `Conflict(string message)` -- shorthand for `Failure(ErrorCode.Conflict, message)`

Also provide a non-generic `Result` static class with the same factory methods for convenience (returning `Result<Unit>` or similar for void operations). Use MediatR's `Unit` type or a custom `Success` sentinel.

### PagedResult of T

**File:** `src/PersonalBrandAssistant.Application/Common/Models/PagedResult.cs`

A generic class for keyset-paginated results:

- `IReadOnlyList<T> Items` -- the current page of results
- `string? Cursor` -- opaque cursor for the next page (null if no more results). Encode as Base64 of `"{CreatedAt:ticks}_{Id}"`.
- `bool HasMore` -- whether more results exist beyond this page

### IApplicationDbContext

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs`

Interface exposing `DbSet<T>` properties for all domain entities:

```csharp
/// <summary>
/// Abstraction over EF Core DbContext for application layer access.
/// Implemented by Infrastructure's ApplicationDbContext.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Content> Contents { get; }
    DbSet<Platform> Platforms { get; }
    DbSet<BrandProfile> BrandProfiles { get; }
    DbSet<ContentCalendarSlot> ContentCalendarSlots { get; }
    DbSet<AuditLogEntry> AuditLogEntries { get; }
    DbSet<User> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

Note: This interface references `Microsoft.EntityFrameworkCore` for the `DbSet<T>` type. The Application project needs a reference to `Microsoft.EntityFrameworkCore` (abstractions only, no provider-specific packages).

### IEncryptionService

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IEncryptionService.cs`

```csharp
/// <summary>
/// Encrypts and decrypts sensitive strings (e.g., OAuth tokens).
/// Implemented by Infrastructure using ASP.NET Data Protection API.
/// </summary>
public interface IEncryptionService
{
    byte[] Encrypt(string plaintext);
    string Decrypt(byte[] ciphertext);
}
```

### IDateTimeProvider

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IDateTimeProvider.cs`

```csharp
/// <summary>
/// Abstraction over system clock for testability.
/// </summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
```

### ValidationBehavior

**File:** `src/PersonalBrandAssistant.Application/Common/Behaviors/ValidationBehavior.cs`

A MediatR `IPipelineBehavior<TRequest, TResponse>` that:

1. Accepts an `IEnumerable<IValidator<TRequest>>` via constructor injection (may be empty)
2. If no validators exist, calls `next()` and returns the handler result
3. If validators exist, runs all of them against the request
4. If any validation failures, short-circuits by returning `Result.ValidationFailure(errors)` -- does NOT call the handler
5. If all pass, calls `next()` and returns the handler result

The constraint: `TResponse` must be a `Result<T>`. Use reflection or a generic constraint to construct the failure result. A common pattern is to constrain `TResponse : IResult` where `IResult` is a marker interface on `Result<T>`, and use a static `Create` method for failures.

### LoggingBehavior

**File:** `src/PersonalBrandAssistant.Application/Common/Behaviors/LoggingBehavior.cs`

A MediatR `IPipelineBehavior<TRequest, TResponse>` that:

1. Logs the request type name at `Information` level before calling the handler
2. Starts a `Stopwatch`
3. Calls `next()` to execute the handler
4. Logs completion with elapsed milliseconds
5. On exception, logs at `Error` level with the exception
6. Uses Serilog's `ILogger` (injected via `ILogger<LoggingBehavior<TRequest, TResponse>>`)
7. Applies a destructuring policy: when logging request properties, excludes any property whose name contains `Token`, `Password`, `Secret`, or `Key`

### Content CRUD Commands

#### CreateContentCommand

**File:** `src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateContent/CreateContentCommand.cs`

A record implementing `IRequest<Result<Guid>>` with properties:
- `ContentType ContentType`
- `string Body`
- `string? Title`
- `PlatformType[]? TargetPlatforms`
- `ContentMetadata? Metadata`

#### CreateContentCommandHandler

**File:** `src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateContent/CreateContentCommandHandler.cs`

Handler logic:
1. Create a new `Content` entity (ID generated via `Guid.CreateVersion7()`, status defaults to `Draft`)
2. Set all provided fields from the command
3. Add to `IApplicationDbContext.Contents`
4. Call `SaveChangesAsync`
5. Return `Result.Success(content.Id)`

#### CreateContentCommandValidator

**File:** `src/PersonalBrandAssistant.Application/Features/Content/Commands/CreateContent/CreateContentCommandValidator.cs`

FluentValidation rules:
- `Body` must not be empty
- `ContentType` must be a defined enum value

#### UpdateContentCommand

**File:** `src/PersonalBrandAssistant.Application/Features/Content/Commands/UpdateContent/UpdateContentCommand.cs`

A record implementing `IRequest<Result<Unit>>` with properties:
- `Guid Id`
- `string? Title`
- `string? Body`
- `PlatformType[]? TargetPlatforms`
- `ContentMetadata? Metadata`
- `uint Version` -- for optimistic concurrency check

#### UpdateContentCommandHandler

Handler logic:
1. Find content by ID from `IApplicationDbContext.Contents`
2. If not found, return `Result.NotFound`
3. If content status is not `Draft` or `Review`, return `Result.Failure(ErrorCode.ValidationFailed, "Content is not in an editable state")`
4. Update provided fields (only non-null values)
5. Set the `Version` property from the command for concurrency check
6. Call `SaveChangesAsync`
7. Catch `DbUpdateConcurrencyException` and return `Result.Conflict("Content was modified by another process")`
8. Return `Result.Success(Unit.Value)`

#### UpdateContentCommandValidator

FluentValidation rules:
- `Id` must not be empty Guid
- At least one of `Title`, `Body`, `TargetPlatforms`, or `Metadata` must be provided

#### DeleteContentCommand

**File:** `src/PersonalBrandAssistant.Application/Features/Content/Commands/DeleteContent/DeleteContentCommand.cs`

A record implementing `IRequest<Result<Unit>>` with property:
- `Guid Id`

#### DeleteContentCommandHandler

Handler logic:
1. Find content by ID
2. If not found, return `Result.NotFound`
3. If already `Archived`, return success (idempotent) or an appropriate non-error result
4. Call `content.TransitionTo(ContentStatus.Archived)` -- if the transition is invalid from the current state (e.g., `Publishing`), catch `InvalidOperationException` and return a failure
5. Call `SaveChangesAsync`
6. Return `Result.Success(Unit.Value)`

### Content Queries

#### GetContentQuery

**File:** `src/PersonalBrandAssistant.Application/Features/Content/Queries/GetContent/GetContentQuery.cs`

A record implementing `IRequest<Result<Content>>` (or a DTO) with property:
- `Guid Id`

#### GetContentQueryHandler

Handler logic:
1. Query `IApplicationDbContext.Contents.FindAsync(id)` or `FirstOrDefaultAsync(c => c.Id == id)`
2. If null (either not found or filtered by global query filter for Archived), return `Result.NotFound`
3. Return `Result.Success(content)`

Note: Whether to return the domain entity directly or map to a DTO is a design choice. For the foundation, returning the entity is acceptable. A DTO can be introduced in a later split if needed.

#### ListContentQuery

**File:** `src/PersonalBrandAssistant.Application/Features/Content/Queries/ListContent/ListContentQuery.cs`

A record implementing `IRequest<Result<PagedResult<Content>>>` with properties:
- `ContentType? ContentType` -- optional filter
- `ContentStatus? Status` -- optional filter
- `int PageSize` -- default 20, max 50
- `string? Cursor` -- opaque cursor from previous page (null for first page)

#### ListContentQueryHandler

Handler logic:
1. Start with `IApplicationDbContext.Contents.AsQueryable()`
2. Apply filters: if `ContentType` provided, add `.Where(c => c.ContentType == query.ContentType)`; if `Status` provided, add `.Where(c => c.Status == query.Status)`
3. If `Cursor` is provided, decode it to extract `(DateTimeOffset createdAt, Guid id)` and apply keyset condition: `.Where(c => c.CreatedAt < cursorCreatedAt || (c.CreatedAt == cursorCreatedAt && c.Id < cursorId))`
4. Order by `.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)`
5. Take `PageSize + 1` to determine if more results exist
6. If result count exceeds `PageSize`, set `HasMore = true` and trim to `PageSize`. Build the next cursor from the last item's `(CreatedAt, Id)`.
7. Return `Result.Success(new PagedResult<Content>(items, cursor, hasMore))`

#### ListContentQueryValidator

FluentValidation rules:
- `PageSize` must be between 1 and 50

### DependencyInjection

**File:** `src/PersonalBrandAssistant.Application/DependencyInjection.cs`

A static class with an `AddApplication(this IServiceCollection services)` extension method that registers:
- MediatR services (scanning the Application assembly)
- FluentValidation validators (scanning the Application assembly)
- Pipeline behaviors: `ValidationBehavior` and `LoggingBehavior` (registered as `IPipelineBehavior<,>` open generics)

---

## Key Design Decisions

1. **Result over exceptions:** All handlers return `Result<T>` for expected failures. Exceptions are reserved for truly unexpected situations (infrastructure failures, bugs). This makes error handling explicit and composable.

2. **Keyset pagination over offset:** Keyset pagination using `(CreatedAt, Id)` as cursor provides consistent performance regardless of page depth. The cursor is encoded as an opaque Base64 string so the client never needs to understand the pagination internals.

3. **DbSet references in interface:** `IApplicationDbContext` exposes `DbSet<T>` directly rather than wrapping in repository abstractions. This keeps the Application layer thin while still allowing Infrastructure substitution for testing. EF Core's `DbSet<T>` is already a repository/unit-of-work abstraction.

4. **Validation at pipeline level:** FluentValidation runs automatically via the `ValidationBehavior` pipeline, so handlers never need to validate input themselves. This keeps handlers focused on business logic.

5. **Editable states:** Only `Draft` and `Review` content can be updated. All other states are considered locked. The delete command transitions to `Archived` using the domain state machine rather than physically deleting rows.