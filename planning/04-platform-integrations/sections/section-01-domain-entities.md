# Section 01: Domain Entities & Enums

## Overview

This section introduces three new domain artifacts and updates one existing entity to support per-platform publish tracking and OAuth state management. These are foundational types that the rest of Phase 04 builds upon.

**New files to create:**
- `src/PersonalBrandAssistant.Domain/Entities/ContentPlatformStatus.cs`
- `src/PersonalBrandAssistant.Domain/Entities/OAuthState.cs`
- `src/PersonalBrandAssistant.Domain/Enums/PlatformPublishStatus.cs`

**Existing files to modify:**
- `src/PersonalBrandAssistant.Domain/Entities/Platform.cs` (add `GrantedScopes` property)
- `src/PersonalBrandAssistant.Domain/ValueObjects/PlatformRateLimitState.cs` (extend for per-endpoint tracking)

**Test files created/modified:**
- `tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentPlatformStatusTests.cs` (4 tests)
- `tests/PersonalBrandAssistant.Domain.Tests/Entities/OAuthStateTests.cs` (3 tests)
- `tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs` (added `PlatformPublishStatus` test)
- `tests/PersonalBrandAssistant.Domain.Tests/Entities/PlatformTests.cs` (added 2 `GrantedScopes` tests)
- `tests/PersonalBrandAssistant.Domain.Tests/ValueObjects/PlatformRateLimitStateTests.cs` (3 tests — added during code review for missing coverage)

**Dependencies:** None. This is the first section and has no prerequisites.

**Blocks:** Section 02 (interfaces and models) and Section 03 (EF Core configuration) depend on these entities.

---

## Tests (Write First)

All tests use xUnit following the existing AAA pattern in the project. Add tests before writing implementations.

### ContentPlatformStatusTests

File: `tests/PersonalBrandAssistant.Domain.Tests/Entities/ContentPlatformStatusTests.cs`

```csharp
// Test: ContentPlatformStatus initializes with default values (Pending status, 0 retries)
// - Create a new ContentPlatformStatus, assert Status == PlatformPublishStatus.Pending, RetryCount == 0

// Test: ContentPlatformStatus.IdempotencyKey is set and immutable after construction
// - Verify IdempotencyKey can be set via init-only property and is retrievable

// Test: ContentPlatformStatus gets a valid Guid Id from EntityBase
// - Create instance, assert Id != Guid.Empty

// Test: ContentPlatformStatus can store all publish outcome fields
// - Set PlatformPostId, PostUrl, PublishedAt, ErrorMessage — verify they round-trip
```

### OAuthStateTests

File: `tests/PersonalBrandAssistant.Domain.Tests/Entities/OAuthStateTests.cs`

```csharp
// Test: OAuthState.ExpiresAt is set relative to CreatedAt
// - Create OAuthState with CreatedAt = now, ExpiresAt = now + 10min, verify ExpiresAt > CreatedAt

// Test: OAuthState stores State, Platform, and optional CodeVerifier
// - Create instance with all fields populated, assert each field value

// Test: OAuthState gets a valid Guid Id
// - Create instance, assert Id != Guid.Empty
```

### PlatformPublishStatus Enum Test

File: `tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs` (add to existing file)

```csharp
// Test: PlatformPublishStatus enum has all expected values (Pending, Published, Failed, RateLimited, Skipped, Processing)
// - Assert exactly 6 values, Assert.Contains for each named value
```

### Platform GrantedScopes Test

File: `tests/PersonalBrandAssistant.Domain.Tests/Entities/PlatformTests.cs` (add to existing file)

```csharp
// Test: Platform.GrantedScopes stores and retrieves string array
// - Set GrantedScopes = new[] { "tweet.read", "tweet.write" }, assert correct values

// Test: Platform.GrantedScopes defaults to null
// - Create Platform without setting GrantedScopes, assert it is null
```

---

## Implementation Details

### 1. PlatformPublishStatus Enum

File: `src/PersonalBrandAssistant.Domain/Enums/PlatformPublishStatus.cs`

Define an enum with exactly six values representing the lifecycle of a per-platform publish attempt:

- `Pending` -- initial state, lease acquired but not yet published
- `Published` -- successfully posted to the platform
- `Failed` -- publish attempt failed (error recorded in `ErrorMessage`)
- `RateLimited` -- deferred due to rate limiting (retry time in `NextRetryAt`)
- `Skipped` -- content validation failed for this platform (not an error, just incompatible)
- `Processing` -- async upload in progress (Instagram video containers, YouTube uploads)

Follow the existing single-line enum pattern used in `ContentStatus.cs` and `PlatformType.cs`.

### 2. ContentPlatformStatus Entity

File: `src/PersonalBrandAssistant.Domain/Entities/ContentPlatformStatus.cs`

This entity tracks the publish status of a single piece of content on a single platform. It enables multi-platform publishing where each platform's result is tracked independently.

**Base class:** Inherit from `AuditableEntityBase` (provides `Id`, `CreatedAt`, `UpdatedAt`, domain events). The `Id` is auto-generated via `Guid.CreateVersion7()` from `EntityBase`.

**Properties:**

| Property | Type | Purpose |
|----------|------|---------|
| `ContentId` | `Guid` | FK to Content entity |
| `Platform` | `PlatformType` | Which platform this status is for |
| `Status` | `PlatformPublishStatus` | Current publish status (default: `Pending`) |
| `PlatformPostId` | `string?` | Platform-assigned post ID after publish |
| `PostUrl` | `string?` | Public URL of the published post |
| `ErrorMessage` | `string?` | Error details on failure |
| `IdempotencyKey` | `string?` | `SHA256(ContentId:Platform:ContentVersion)` -- prevents duplicate posts on retry |
| `RetryCount` | `int` | Number of retry attempts (default: 0) |
| `NextRetryAt` | `DateTimeOffset?` | When to retry (set from rate limit `RetryAt`) |
| `PublishedAt` | `DateTimeOffset?` | When the post was published |
| `Version` | `uint` | xmin concurrency token for optimistic locking |

**Design notes:**
- `Status` should default to `PlatformPublishStatus.Pending` in the property initializer
- `RetryCount` should default to `0` in the property initializer
- Use the same namespace pattern as other entities: `PersonalBrandAssistant.Domain.Entities`
- Import `PersonalBrandAssistant.Domain.Common` and `PersonalBrandAssistant.Domain.Enums`

### 3. OAuthState Entity

File: `src/PersonalBrandAssistant.Domain/Entities/OAuthState.cs`

Temporary storage for OAuth flow state parameters and PKCE code verifiers. Entries are created when generating an OAuth URL and deleted after successful code exchange. Expired entries are cleaned up by the `TokenRefreshProcessor` background job.

**Base class:** Inherit from `EntityBase` (provides `Id`, domain events). This does NOT need `AuditableEntityBase` since these are short-lived records (10-minute TTL) that don't need audit timestamps.

**Properties:**

| Property | Type | Purpose |
|----------|------|---------|
| `State` | `string` | Cryptographically random state parameter for CSRF protection |
| `Platform` | `PlatformType` | Which platform this OAuth flow is for |
| `CodeVerifier` | `string?` | PKCE code_verifier (used only by Twitter OAuth 2.0) |
| `CreatedAt` | `DateTimeOffset` | When the OAuth flow was initiated |
| `ExpiresAt` | `DateTimeOffset` | When this state expires (CreatedAt + 10 minutes) |

**Design notes:**
- `State` is the primary lookup field (validated during callback)
- `CodeVerifier` is nullable because only Twitter uses PKCE
- `CreatedAt` and `ExpiresAt` are explicitly defined here (not from `AuditableEntityBase`) since this entity only needs creation time, not update tracking

### 4. Platform Entity Update

File: `src/PersonalBrandAssistant.Domain/Entities/Platform.cs`

Add a single new property to the existing `Platform` class:

```csharp
public string[]? GrantedScopes { get; set; }
```

This tracks which OAuth scopes were granted when the user connected the platform. Used by `PlatformHealthMonitor` to validate that required scopes are still present. Nullable because it is `null` for disconnected platforms.

Place it after the `TokenExpiresAt` property to keep token-related fields grouped together.

### 5. PlatformRateLimitState Value Object Update

File: `src/PersonalBrandAssistant.Domain/ValueObjects/PlatformRateLimitState.cs`

The existing `PlatformRateLimitState` has simple flat fields (`RemainingCalls`, `ResetAt`, `WindowDuration`). Extend it to support per-endpoint rate limit tracking and YouTube daily quota. The new shape:

**Properties to add:**

| Property | Type | Purpose |
|----------|------|---------|
| `Endpoints` | `Dictionary<string, EndpointRateLimit>` | Per-endpoint tracking (keyed by endpoint name) |
| `DailyQuotaUsed` | `int?` | YouTube daily quota consumed |
| `DailyQuotaLimit` | `int?` | YouTube daily quota cap (default 10,000) |
| `QuotaResetAt` | `DateTimeOffset?` | YouTube quota reset time (midnight PT) |

The existing `RemainingCalls`, `ResetAt`, `WindowDuration` properties should be kept for backward compatibility (they serve as a simple aggregate view).

**New supporting class** (same file or a new file at `src/PersonalBrandAssistant.Domain/ValueObjects/EndpointRateLimit.cs`):

```csharp
public class EndpointRateLimit
{
    public int? RemainingCalls { get; set; }
    public DateTimeOffset? ResetAt { get; set; }
}
```

Initialize `Endpoints` to `new()` in the property initializer to avoid null reference issues when the JSONB column is first read.

---

## Existing Codebase Context

Key patterns to follow from the existing codebase:

- **Entity base classes** are in `PersonalBrandAssistant.Domain.Common`. `EntityBase` provides `Id` (Guid v7) and domain events. `AuditableEntityBase` adds `CreatedAt`/`UpdatedAt`.
- **Enums** are single-line definitions in `PersonalBrandAssistant.Domain.Enums` (e.g., `public enum ContentStatus { Draft, Review, ... }`).
- **Value objects** live in `PersonalBrandAssistant.Domain.ValueObjects` as plain classes with public get/set properties (used as JSONB in PostgreSQL).
- **Tests** use xUnit with `[Fact]` attributes. Entity tests verify construction, default values, and field storage. Enum tests verify exact member counts and names via `Enum.GetValues<T>()`.
- The `Platform` entity already has `PlatformRateLimitState RateLimitState` as a property, and `uint Version` for xmin concurrency.
