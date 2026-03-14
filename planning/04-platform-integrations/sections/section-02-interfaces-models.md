# Section 02: Application Interfaces and Models

## Overview

This section defines all application-layer interfaces and DTOs (Data Transfer Objects) that form the contracts for the platform integration subsystem. These are placed in the `Application` project under `Common/Interfaces` and `Common/Models`, following the existing codebase conventions.

Every subsequent section in Phase 04 depends on these interfaces and models. They define the abstraction boundaries between the publishing pipeline, platform adapters, OAuth management, rate limiting, media storage, and content formatting.

## Dependencies

- **Section 01 (Domain Entities):** Must be completed first. This section references `PlatformType` enum (already exists at `src/PersonalBrandAssistant.Domain/Enums/PlatformType.cs`), `ContentType` enum (already exists), and the `Content` entity. It also references the `PlatformPublishStatus` enum and `ContentPlatformStatus` entity created in Section 01.

## Existing Codebase Context

The project uses a `Result<T>` pattern for error handling defined in `src/PersonalBrandAssistant.Application/Common/Models/Result.cs`. It supports `Success(T)`, `Failure(ErrorCode, errors)`, `NotFound(message)`, `ValidationFailure(errors)`, and `Conflict(message)`. Error codes are defined in the `ErrorCode` enum (`None`, `ValidationFailed`, `NotFound`, `Conflict`, `Unauthorized`, `InternalError`).

The existing `IPublishingPipeline` interface is at `src/PersonalBrandAssistant.Application/Common/Interfaces/IPublishingPipeline.cs` and returns `Result<MediatR.Unit>`. This interface will continue to be used; the new interfaces defined here are consumed by the pipeline implementation (Section 09).

Entity base classes use `Guid Id` (via `EntityBase`) with `Guid.CreateVersion7()` and auditable timestamps via `AuditableEntityBase`.

The `PlatformRateLimitState` value object exists at `src/PersonalBrandAssistant.Domain/ValueObjects/PlatformRateLimitState.cs` but is too simple for per-endpoint tracking. This section defines the extended version needed by the rate limiter; the domain value object update is handled separately in Section 01.

## Tests First

Create the test file at:
`tests/PersonalBrandAssistant.Application.Tests/Common/Models/PlatformIntegrationModelsTests.cs`

```csharp
namespace PersonalBrandAssistant.Application.Tests.Common.Models;

/// <summary>
/// Tests for platform integration DTOs and records.
/// Validates record equality, required fields, and correct structure.
/// </summary>
public class PlatformIntegrationModelsTests
{
    // Test: PlatformContent record equality works with same values
    // Two PlatformContent instances with identical values should be equal (record semantics)

    // Test: MediaFile uses FileId (not file path)
    // MediaFile should have a FileId property and no FilePath property
    // This ensures infrastructure paths are not leaked through the abstraction

    // Test: RateLimitDecision.Allowed=false includes RetryAt
    // When Allowed is false, RetryAt should be non-null and Reason should be populated

    // Test: OAuthTokens stores GrantedScopes array
    // OAuthTokens should accept and expose a string[] for GrantedScopes

    // Test: PlatformIntegrationOptions binds from configuration section
    // Verify PlatformIntegrationOptions has per-platform sub-options (Twitter, LinkedIn, Instagram, YouTube)
    // Each sub-option should have CallbackUrl and optional BaseUrl

    // Test: MediaStorageOptions binds BasePath and SigningKey
    // Verify both properties exist and can be set
}
```

Create the test file at:
`tests/PersonalBrandAssistant.Application.Tests/Common/Interfaces/PlatformInterfacesTests.cs`

```csharp
namespace PersonalBrandAssistant.Application.Tests.Common.Interfaces;

/// <summary>
/// Tests validating that platform integration interfaces have the correct
/// method signatures and return types. Uses reflection to ensure contracts
/// are stable.
/// </summary>
public class PlatformInterfacesTests
{
    // Test: ISocialPlatform defines Type property and all required methods
    // Verify PublishAsync, DeletePostAsync, GetEngagementAsync, GetProfileAsync, ValidateContentAsync

    // Test: IOAuthManager defines all OAuth lifecycle methods
    // Verify GenerateAuthUrlAsync, ExchangeCodeAsync, RefreshTokenAsync, RevokeTokenAsync

    // Test: IRateLimiter defines CanMakeRequestAsync, RecordRequestAsync, GetStatusAsync

    // Test: IMediaStorage defines SaveAsync, GetStreamAsync, GetPathAsync, DeleteAsync, GetSignedUrlAsync

    // Test: IPlatformContentFormatter defines Platform property and FormatAndValidate method
}
```

These tests are intentionally lightweight since they validate record semantics and interface shape. The real behavioral tests come in later sections when implementations are built.

## Implementation Details

### Interfaces

All interfaces go under `src/PersonalBrandAssistant.Application/Common/Interfaces/`.

#### ISocialPlatform.cs

File: `src/PersonalBrandAssistant.Application/Common/Interfaces/ISocialPlatform.cs`

The primary abstraction for platform adapters. Each platform (Twitter, LinkedIn, Instagram, YouTube) implements this interface.

```csharp
public interface ISocialPlatform
{
    PlatformType Type { get; }
    Task<Result<PublishResult>> PublishAsync(PlatformContent content, CancellationToken ct);
    Task<Result<Unit>> DeletePostAsync(string platformPostId, CancellationToken ct);
    Task<Result<EngagementStats>> GetEngagementAsync(string platformPostId, CancellationToken ct);
    Task<Result<PlatformProfile>> GetProfileAsync(CancellationToken ct);
    Task<Result<ContentValidation>> ValidateContentAsync(PlatformContent content, CancellationToken ct);
}
```

Uses `MediatR.Unit` for void-equivalent return from `DeletePostAsync`. All methods return `Result<T>` for consistent error handling. The `CancellationToken` parameter supports graceful shutdown from background services.

#### IOAuthManager.cs

File: `src/PersonalBrandAssistant.Application/Common/Interfaces/IOAuthManager.cs`

Handles the full OAuth lifecycle. Platform-specific OAuth details (endpoints, scopes, PKCE) are encapsulated in the implementation.

```csharp
public interface IOAuthManager
{
    Task<Result<OAuthAuthorizationUrl>> GenerateAuthUrlAsync(PlatformType platform, CancellationToken ct);
    Task<Result<OAuthTokens>> ExchangeCodeAsync(PlatformType platform, string code, string state, string? codeVerifier, CancellationToken ct);
    Task<Result<OAuthTokens>> RefreshTokenAsync(PlatformType platform, CancellationToken ct);
    Task<Result<Unit>> RevokeTokenAsync(PlatformType platform, CancellationToken ct);
}
```

Key design decisions:
- `GenerateAuthUrlAsync` returns `OAuthAuthorizationUrl` (containing `Url` and `State`) so the caller can pass the state to the frontend for the callback.
- `ExchangeCodeAsync` takes the `state` parameter for server-side CSRF validation and optional `codeVerifier` for PKCE flows (Twitter).
- `RefreshTokenAsync` loads the current platform from DB internally; the caller only specifies which platform.
- `RevokeTokenAsync` clears tokens and marks the platform as disconnected.

#### IRateLimiter.cs

File: `src/PersonalBrandAssistant.Application/Common/Interfaces/IRateLimiter.cs`

Database-backed rate limiting using the Platform entity's `RateLimitState` JSONB field.

```csharp
public interface IRateLimiter
{
    Task<RateLimitDecision> CanMakeRequestAsync(PlatformType platform, string endpoint, CancellationToken ct);
    Task RecordRequestAsync(PlatformType platform, string endpoint, int remaining, DateTimeOffset? resetAt, CancellationToken ct);
    Task<RateLimitStatus> GetStatusAsync(PlatformType platform, CancellationToken ct);
}
```

The `endpoint` parameter supports per-endpoint tracking (e.g., Twitter's `/tweets` vs `/media/upload` have different limits). `RecordRequestAsync` is called after each API call with values from response headers.

#### IMediaStorage.cs

File: `src/PersonalBrandAssistant.Application/Common/Interfaces/IMediaStorage.cs`

Abstracts file storage with signed URL support for platforms requiring public URLs (Instagram).

```csharp
public interface IMediaStorage
{
    Task<string> SaveAsync(Stream content, string fileName, string mimeType, CancellationToken ct);
    Task<Stream> GetStreamAsync(string fileId, CancellationToken ct);
    Task<string> GetPathAsync(string fileId, CancellationToken ct);
    Task<bool> DeleteAsync(string fileId, CancellationToken ct);
    Task<string> GetSignedUrlAsync(string fileId, TimeSpan expiry, CancellationToken ct);
}
```

`SaveAsync` returns a `fileId` (not a path). All other methods accept `fileId`. This keeps infrastructure paths out of the domain. `GetSignedUrlAsync` generates HMAC-signed URLs with expiry for serving media through a dedicated endpoint.

#### IPlatformContentFormatter.cs

File: `src/PersonalBrandAssistant.Application/Common/Interfaces/IPlatformContentFormatter.cs`

Combines content formatting and validation into a single operation to prevent divergence.

```csharp
public interface IPlatformContentFormatter
{
    PlatformType Platform { get; }
    Result<PlatformContent> FormatAndValidate(Content content);
}
```

Takes the domain `Content` entity and returns a platform-ready `PlatformContent` DTO wrapped in `Result<T>`. Validation failures are returned as `Result.Failure` with descriptive error codes (e.g., "Text exceeds 280 characters for Twitter"). The `Platform` property is used by the publishing pipeline to resolve the correct formatter for each target platform.

### Models (DTOs)

All models go under `src/PersonalBrandAssistant.Application/Common/Models/`.

#### PlatformContent.cs

File: `src/PersonalBrandAssistant.Application/Common/Models/PlatformContent.cs`

The platform-ready content DTO produced by formatters and consumed by adapters.

```csharp
public record PlatformContent(
    string Text,
    string? Title,
    ContentType ContentType,
    IReadOnlyList<MediaFile> Media,
    Dictionary<string, string> Metadata);

public record MediaFile(string FileId, string MimeType, string? AltText);
```

`Metadata` is a flexible dictionary for platform-specific data (e.g., Twitter thread numbering, YouTube tags, LinkedIn article URN). `MediaFile` uses `FileId` rather than file paths to avoid leaking infrastructure details.

#### PublishResult.cs

File: `src/PersonalBrandAssistant.Application/Common/Models/PublishResult.cs`

Returned by `ISocialPlatform.PublishAsync` on success.

```csharp
public record PublishResult(string PlatformPostId, string PostUrl, DateTimeOffset PublishedAt);
```

#### OAuthTokens.cs

File: `src/PersonalBrandAssistant.Application/Common/Models/OAuthTokens.cs`

Returned by `IOAuthManager` after token exchange or refresh.

```csharp
public record OAuthAuthorizationUrl(string Url, string State);
public record OAuthTokens(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt, string[]? GrantedScopes);
```

`OAuthAuthorizationUrl` is a separate record returned by `GenerateAuthUrlAsync`. `GrantedScopes` is nullable because not all platforms return scopes in token responses.

#### ContentValidation.cs

File: `src/PersonalBrandAssistant.Application/Common/Models/ContentValidation.cs`

Returned by `ISocialPlatform.ValidateContentAsync`.

```csharp
public record ContentValidation(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
```

Separates hard errors (prevent publishing) from warnings (informational, like "hashtag count near limit").

#### EngagementStats.cs

File: `src/PersonalBrandAssistant.Application/Common/Models/EngagementStats.cs`

Returned by `ISocialPlatform.GetEngagementAsync`.

```csharp
public record EngagementStats(
    int Likes,
    int Comments,
    int Shares,
    int Impressions,
    int Clicks,
    Dictionary<string, int> PlatformSpecific);
```

`PlatformSpecific` captures metrics unique to each platform (e.g., Twitter retweets, YouTube watch time, LinkedIn reactions by type).

#### PlatformProfile.cs

File: `src/PersonalBrandAssistant.Application/Common/Models/PlatformProfile.cs`

Returned by `ISocialPlatform.GetProfileAsync`.

```csharp
public record PlatformProfile(string PlatformUserId, string DisplayName, string? AvatarUrl, int? FollowerCount);
```

Used by health monitoring to validate the connection is alive.

#### RateLimitDecision.cs

File: `src/PersonalBrandAssistant.Application/Common/Models/RateLimitDecision.cs`

Returned by `IRateLimiter.CanMakeRequestAsync`.

```csharp
public record RateLimitDecision(bool Allowed, DateTimeOffset? RetryAt, string? Reason);
```

When `Allowed` is `false`, `RetryAt` tells the pipeline when to retry and `Reason` provides context (e.g., "YouTube daily quota exceeded", "Twitter rate limit resets at {time}"). The pipeline uses `RetryAt` to set `NextRetryAt` on `ContentPlatformStatus`.

#### RateLimitStatus.cs

File: `src/PersonalBrandAssistant.Application/Common/Models/RateLimitStatus.cs`

Returned by `IRateLimiter.GetStatusAsync`.

```csharp
public record RateLimitStatus(int? RemainingCalls, DateTimeOffset? ResetAt, bool IsLimited);
```

Aggregate status across all endpoints for a platform. Used by API endpoints to surface rate limit info.

#### PlatformIntegrationOptions.cs

File: `src/PersonalBrandAssistant.Application/Common/Models/PlatformIntegrationOptions.cs`

Configuration options bound from `appsettings.json` under the `PlatformIntegrations` section.

```csharp
public class PlatformIntegrationOptions
{
    public PlatformOptions Twitter { get; set; } = new();
    public PlatformOptions LinkedIn { get; set; } = new();
    public PlatformOptions Instagram { get; set; } = new();
    public PlatformOptions YouTube { get; set; } = new();
}

public class PlatformOptions
{
    public string CallbackUrl { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string? ApiVersion { get; set; }
    public int? DailyQuotaLimit { get; set; }
}
```

Secrets (ClientId, ClientSecret, AppId, AppSecret) are NOT in this options class. They come from User Secrets / Azure Key Vault and are accessed directly via `IConfiguration` in the OAuth manager implementation.

#### MediaStorageOptions.cs

File: `src/PersonalBrandAssistant.Application/Common/Models/MediaStorageOptions.cs`

Configuration for local media storage.

```csharp
public class MediaStorageOptions
{
    public string BasePath { get; set; } = "./media";
    public string? SigningKey { get; set; }
}
```

`SigningKey` is used for HMAC-signed URLs. Generated on first run if missing, stored in User Secrets. The implementation (Section 04) reads this from the options.

## Implementation Notes (Post-Build)

### Code Review Deviations from Plan
1. **Immutability fixes:** `PlatformContent.Metadata` changed from `Dictionary<string, string>` to `IReadOnlyDictionary<string, string>`. `EngagementStats.PlatformSpecific` changed from `Dictionary<string, int>` to `IReadOnlyDictionary<string, int>`.
2. **Security fix:** `OAuthTokens` now has a custom `ToString()` override that redacts `AccessToken` and `RefreshToken` to prevent accidental exposure in logs.
3. **Immutability fix:** `OAuthTokens.GrantedScopes` changed from `string[]?` to `IReadOnlyList<string>?`.

### Test Results
- 11 tests across 2 test files (6 model tests + 5 interface tests)
- All 329 project tests pass (107 Application + 222 Infrastructure)

## File Summary

New files created (17 total):

| File | Type |
|------|------|
| `src/PersonalBrandAssistant.Application/Common/Interfaces/ISocialPlatform.cs` | Interface |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IOAuthManager.cs` | Interface |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IRateLimiter.cs` | Interface |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IMediaStorage.cs` | Interface |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IPlatformContentFormatter.cs` | Interface |
| `src/PersonalBrandAssistant.Application/Common/Models/PlatformContent.cs` | Record + MediaFile record |
| `src/PersonalBrandAssistant.Application/Common/Models/PublishResult.cs` | Record |
| `src/PersonalBrandAssistant.Application/Common/Models/OAuthTokens.cs` | Records (OAuthAuthorizationUrl + OAuthTokens) |
| `src/PersonalBrandAssistant.Application/Common/Models/ContentValidation.cs` | Record |
| `src/PersonalBrandAssistant.Application/Common/Models/EngagementStats.cs` | Record |
| `src/PersonalBrandAssistant.Application/Common/Models/PlatformProfile.cs` | Record |
| `src/PersonalBrandAssistant.Application/Common/Models/RateLimitDecision.cs` | Record |
| `src/PersonalBrandAssistant.Application/Common/Models/RateLimitStatus.cs` | Record |
| `src/PersonalBrandAssistant.Application/Common/Models/PlatformIntegrationOptions.cs` | Class (options) |
| `src/PersonalBrandAssistant.Application/Common/Models/MediaStorageOptions.cs` | Class (options) |
| `tests/PersonalBrandAssistant.Application.Tests/Common/Models/PlatformIntegrationModelsTests.cs` | Test |
| `tests/PersonalBrandAssistant.Application.Tests/Common/Interfaces/PlatformInterfacesTests.cs` | Test |

No existing files are modified in this section. The `IPublishingPipeline` interface remains unchanged; the new pipeline implementation (Section 09) will implement it using these new interfaces.

## Namespace Conventions

All interfaces use namespace `PersonalBrandAssistant.Application.Common.Interfaces`.
All models use namespace `PersonalBrandAssistant.Application.Common.Models`.

These match the existing patterns in the codebase (e.g., `IEncryptionService`, `Result<T>`, `PagedResult`).

## Blocked Sections

The following sections depend on these interfaces and models and cannot begin until this section is complete:
- Section 04 (Media Storage) -- implements `IMediaStorage`
- Section 05 (Rate Limiter) -- implements `IRateLimiter`
- Section 06 (OAuth Manager) -- implements `IOAuthManager`
- Section 07 (Content Formatters) -- implements `IPlatformContentFormatter`
- Section 08 (Platform Adapters) -- implements `ISocialPlatform`
- Section 09 (Publishing Pipeline) -- consumes all interfaces
