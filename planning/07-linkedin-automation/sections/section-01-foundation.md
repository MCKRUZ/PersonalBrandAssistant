Now I have all the context I need. Let me generate the section content.

# Section 01: Foundation -- Entities, Interfaces, Configuration, Migration, NuGet Packages, DI Registration

## Overview

This section builds all foundational pieces that every other section depends on. It introduces:

- The `AutomationRun` domain entity and its enum
- Two new columns on the existing `Content` entity (`ImageFileId`, `ImageRequired`)
- Configuration option classes (`ContentAutomationOptions`, `ComfyUiOptions`)
- All new interfaces (`IDailyContentOrchestrator`, `IComfyUiClient`, `IImageGenerationService`, `IImagePromptService`, `IImageResizer`)
- An EF Core migration for the `AutomationRuns` table and `Contents` column additions
- NuGet package additions (`Cronos`, `SkiaSharp`)
- DI registration stubs for all new services
- New `NotificationType` enum values for automation events
- `DbSet<AutomationRun>` on `ApplicationDbContext` and `IApplicationDbContext`

No business logic is implemented here. All interface implementations are registered as stubs (or are wired to implementations built in later sections). The goal is that once this section is complete, the solution compiles and all downstream sections have their contracts defined.

---

## Dependencies

None. This section has no dependencies on other sections and must be completed first.

**Blocks:** All other sections (02 through 10).

---

## Tests First

All tests go in the `tests/PersonalBrandAssistant.Infrastructure.Tests` project. Use xUnit + Moq, consistent with the existing test conventions.

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Domain/AutomationRunTests.cs`

Tests for the `AutomationRun` entity:

- **AutomationRun can be created with Running status** -- Call the static `Create` factory. Assert `Status == AutomationRunStatus.Running`, `TriggeredAt` is set, `CompletedAt` is null.
- **AutomationRun status can be updated to Completed with CompletedAt and DurationMs** -- Create a Running instance, call a `Complete(durationMs)` method. Assert `Status == Completed`, `CompletedAt` is non-null, `DurationMs` is recorded.
- **AutomationRun status can be updated to Failed with ErrorDetails** -- Create a Running instance, call `Fail(errorDetails, durationMs)`. Assert `Status == Failed`, `ErrorDetails` is recorded, `CompletedAt` is non-null.
- **AutomationRun records PartialFailure status** -- Create a Running instance, call a `PartialFailure(errorDetails, durationMs)` method. Assert status is `PartialFailure`.

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Domain/ContentImagePropertiesTests.cs`

Tests for the new properties on `Content`:

- **Content.ImageFileId defaults to null** -- Create via `Content.Create(...)`. Assert `ImageFileId` is null.
- **Content.ImageRequired defaults to false** -- Create via `Content.Create(...)`. Assert `ImageRequired` is false.
- **Content.ImageFileId can be set** -- Create, set `ImageFileId = "abc"`. Assert it persists.
- **Content.ImageRequired can be set to true** -- Create, set `ImageRequired = true`. Assert it is true.

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/AutomationRunPersistenceTests.cs`

Integration tests using the Postgres test fixture (follows existing `MigrationTests` / `QueryFilterTests` patterns):

- **AutomationRun persists to database and can be queried by date** -- Save an `AutomationRun`, query by `TriggeredAt` date. Assert found.
- **Idempotency query correctly finds Completed run for today** -- Save a `Completed` run for today. Query where `Status == Completed` and `TriggeredAt` is today. Assert count == 1.
- **Idempotency query correctly finds Running run for today** -- Same as above but with `Running` status.
- **Idempotency query does not match yesterday's run** -- Save a `Completed` run for yesterday. Query for today. Assert count == 0.

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Configuration/ContentAutomationOptionsTests.cs`

Tests for option binding:

- **ContentAutomationOptions binds from configuration section** -- Build an `IConfiguration` from in-memory dictionary. Bind to `ContentAutomationOptions`. Assert all fields map correctly.
- **ContentAutomationOptions uses correct defaults** -- Create instance with no config overrides. Assert `CronExpression == "0 9 * * 1-5"`, `TimeZone == "Eastern Standard Time"`, `Enabled == true`, `TopTrendsToConsider == 5`.
- **ComfyUiOptions binds nested ImageGeneration section** -- Bind the `ContentAutomation:ImageGeneration` section. Assert `BaseUrl`, `TimeoutSeconds`, `DefaultWidth`, `DefaultHeight` map correctly.

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/ContentAutomationServiceRegistrationTests.cs`

DI registration smoke tests (follows existing `ContentEngineServiceRegistrationTests` pattern):

- **IDailyContentOrchestrator is resolvable from DI** -- Build service provider, resolve `IDailyContentOrchestrator`. Assert not null.
- **IComfyUiClient is resolvable from DI** -- Same pattern.
- **IImageGenerationService is resolvable from DI** -- Same.
- **IImagePromptService is resolvable from DI** -- Same.
- **IImageResizer is resolvable from DI** -- Same.
- **DailyContentProcessor is registered as hosted service** -- Assert `IHostedService` enumerable contains a `DailyContentProcessor`.

---

## Implementation Details

### 1. New NuGet Packages

**File to modify:** `src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj`

Add two new package references. Note: the project already has `NCrontab` -- `Cronos` replaces it for this feature because Cronos handles DST correctly and supports timezone-aware occurrence calculation. `NCrontab` remains for existing background jobs that use it.

```xml
<PackageReference Include="Cronos" Version="1.4.0" />
<PackageReference Include="SkiaSharp" Version="3.119.0" />
```

### 2. AutomationRunStatus Enum

**New file:** `src/PersonalBrandAssistant.Domain/Enums/AutomationRunStatus.cs`

```csharp
namespace PersonalBrandAssistant.Domain.Enums;

public enum AutomationRunStatus { Running, Completed, PartialFailure, Failed }
```

### 3. AutomationRun Entity

**New file:** `src/PersonalBrandAssistant.Domain/Entities/AutomationRun.cs`

Follows the existing entity conventions: inherits `AuditableEntityBase`, uses `Guid.CreateVersion7()` IDs, static factory method, private constructor.

Fields:
- `Guid Id` (from EntityBase)
- `DateTimeOffset TriggeredAt`
- `AutomationRunStatus Status`
- `Guid? SelectedSuggestionId` -- which trend suggestion was chosen
- `Guid? PrimaryContentId` -- the parent content created
- `string? ImageFileId` -- the original 1536x1536 source image
- `string? ImagePrompt` -- the FLUX prompt used
- `string? SelectionReasoning` -- why the AI picked this topic
- `string? ErrorDetails` -- error info on failure
- `DateTimeOffset? CompletedAt`
- `long DurationMs`
- `int PlatformVersionCount` -- how many platform-specific children were created

Factory method `Create()` sets `TriggeredAt = DateTimeOffset.UtcNow`, `Status = Running`.

Mutation methods:
- `Complete(long durationMs)` -- sets `Status = Completed`, `CompletedAt = DateTimeOffset.UtcNow`, `DurationMs`
- `Fail(string errorDetails, long durationMs)` -- sets `Status = Failed`, `ErrorDetails`, `CompletedAt`, `DurationMs`
- `PartialFailure(string errorDetails, long durationMs)` -- same but `Status = PartialFailure`

### 4. Content Entity Changes

**File to modify:** `src/PersonalBrandAssistant.Domain/Entities/Content.cs`

Add two new properties:

```csharp
public string? ImageFileId { get; set; }
public bool ImageRequired { get; set; }
```

These are simple mutable properties (like `Title` and `Body`). `ImageFileId` stores the platform-specific cropped image file ID. `ImageRequired` flags content that must have an image before publishing.

### 5. NotificationType Enum Extension

**File to modify:** `src/PersonalBrandAssistant.Domain/Enums/NotificationType.cs`

Add new values for automation events:

```csharp
public enum NotificationType
{
    // ... existing values ...
    ContentReadyForReview,
    ContentApproved,
    ContentRejected,
    ContentPublished,
    ContentFailed,
    PlatformDisconnected,
    PlatformTokenExpiring,
    PlatformScopeMismatch,
    // New automation values:
    AutomationImageFailed,
    AutomationPipelineCompleted,
    AutomationNoTrends,
    AutomationConsecutiveFailure,
}
```

### 6. ContentAutomationOptions

**New file:** `src/PersonalBrandAssistant.Application/Common/Models/ContentAutomationOptions.cs`

Configuration record that maps to the `ContentAutomation` section of appsettings:

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

public class ContentAutomationOptions
{
    public const string SectionName = "ContentAutomation";

    public string CronExpression { get; set; } = "0 9 * * 1-5";
    public string TimeZone { get; set; } = "Eastern Standard Time";
    public bool Enabled { get; set; } = true;
    public string AutonomyLevel { get; set; } = "SemiAuto";
    public int TopTrendsToConsider { get; set; } = 5;
    public string[] TargetPlatforms { get; set; } = ["LinkedIn"];
    public ImageGenerationOptions ImageGeneration { get; set; } = new();
    public PlatformPromptOptions PlatformPrompts { get; set; } = new();
}
```

### 7. ImageGenerationOptions (nested)

Nested within the same file or a separate file. Contains ComfyUI-specific settings:

```csharp
public class ImageGenerationOptions
{
    public bool Enabled { get; set; } = true;
    public string ComfyUiBaseUrl { get; set; } = "http://192.168.50.47:8188";
    public string WorkflowTemplate { get; set; } = "flux-text-to-image";
    public int TimeoutSeconds { get; set; } = 120;
    public int HealthCheckTimeoutSeconds { get; set; } = 5;
    public int DefaultWidth { get; set; } = 1536;
    public int DefaultHeight { get; set; } = 1536;
    public string ModelCheckpoint { get; set; } = "flux1-dev-fp8.safetensors";
    public int CircuitBreakerThreshold { get; set; } = 3;
}
```

### 8. PlatformPromptOptions (nested)

```csharp
public class PlatformPromptOptions
{
    public string? LinkedIn { get; set; }
    public string? TwitterX { get; set; }
    public string? PersonalBlog { get; set; }
}
```

### 9. AutomationRunResult Model

**New file:** `src/PersonalBrandAssistant.Application/Common/Models/AutomationRunResult.cs`

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

public record AutomationRunResult(
    bool Success,
    Guid RunId,
    Guid? PrimaryContentId,
    string? ImageFileId,
    int PlatformVersionCount,
    string? Error,
    long DurationMs);
```

### 10. ImageGenerationResult Model

**New file:** `src/PersonalBrandAssistant.Application/Common/Models/ImageGenerationResult.cs`

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

public record ImageGenerationResult(bool Success, string? FileId, string? Error, long DurationMs);
```

### 11. New Interfaces

All in `src/PersonalBrandAssistant.Application/Common/Interfaces/`.

**IDailyContentOrchestrator.cs:**
```csharp
namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IDailyContentOrchestrator
{
    Task<AutomationRunResult> ExecuteAsync(ContentAutomationOptions options, CancellationToken ct);
}
```

**IComfyUiClient.cs:**
```csharp
namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IComfyUiClient
{
    /// Queues a prompt workflow to ComfyUI. Returns the prompt ID.
    Task<string> QueuePromptAsync(System.Text.Json.Nodes.JsonObject workflow, CancellationToken ct);

    /// Waits for a queued prompt to complete. Returns the execution result.
    Task<ComfyUiResult> WaitForCompletionAsync(string promptId, CancellationToken ct);

    /// Downloads a generated image from ComfyUI output.
    Task<byte[]> DownloadImageAsync(string filename, string subfolder, CancellationToken ct);

    /// Checks if ComfyUI is reachable and ready.
    Task<bool> IsAvailableAsync(CancellationToken ct);
}
```

**IImageGenerationService.cs:**
```csharp
namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IImageGenerationService
{
    Task<ImageGenerationResult> GenerateAsync(string prompt, ImageGenerationOptions options, CancellationToken ct);
}
```

**IImagePromptService.cs:**
```csharp
namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IImagePromptService
{
    /// Generates a FLUX-optimized image prompt from post content.
    Task<string> GeneratePromptAsync(string postContent, CancellationToken ct);
}
```

**IImageResizer.cs:**
```csharp
namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IImageResizer
{
    /// Resizes a source image for each platform. Returns platform -> fileId mapping.
    Task<IReadOnlyDictionary<PlatformType, string>> ResizeForPlatformsAsync(
        string sourceFileId, PlatformType[] platforms, CancellationToken ct);
}
```

### 12. ComfyUiResult Model

**New file:** `src/PersonalBrandAssistant.Application/Common/Models/ComfyUiResult.cs`

This is the structured result from `WaitForCompletionAsync`:

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

public record ComfyUiResult(
    bool Success,
    string? OutputFilename,
    string? OutputSubfolder,
    string? Error);
```

### 13. EF Core Configuration for AutomationRun

**New file:** `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/AutomationRunConfiguration.cs`

Follows existing configuration pattern (e.g., `ContentConfiguration`):

- Table name: `AutomationRuns`
- Primary key: `Id`
- Required fields: `TriggeredAt`, `Status`
- Index on `TriggeredAt` (for idempotency date checks)
- Index on `Status` (for monitoring queries)
- Ignore `DomainEvents`
- PostgreSQL `xmin` concurrency token (same pattern as `ContentConfiguration`)

### 14. Content Configuration Changes

**File to modify:** `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/ContentConfiguration.cs`

Add configuration for the two new columns:

```csharp
builder.Property(c => c.ImageFileId).HasMaxLength(500);
builder.Property(c => c.ImageRequired).IsRequired().HasDefaultValue(false);
```

### 15. ApplicationDbContext and IApplicationDbContext Changes

**File to modify:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs`

Add:
```csharp
DbSet<AutomationRun> AutomationRuns { get; }
```

**File to modify:** `src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs`

Add:
```csharp
public DbSet<AutomationRun> AutomationRuns => Set<AutomationRun>();
```

### 16. EF Core Migration

Generate a new migration after all entity/configuration changes are in place:

```bash
cd src/PersonalBrandAssistant.Infrastructure
dotnet ef migrations add AddContentAutomation --output-dir Data/Migrations --project . --startup-project ../PersonalBrandAssistant.Api
```

The migration will:
- Create the `AutomationRuns` table with all columns and indexes
- Add nullable `ImageFileId` (varchar 500) column to `Contents`
- Add `ImageRequired` (bool, default false) column to `Contents`

### 17. appsettings.json Configuration Section

**File to modify:** `src/PersonalBrandAssistant.Api/appsettings.json`

Add the `ContentAutomation` section at root level:

```json
"ContentAutomation": {
  "CronExpression": "0 9 * * 1-5",
  "TimeZone": "Eastern Standard Time",
  "Enabled": true,
  "AutonomyLevel": "SemiAuto",
  "TopTrendsToConsider": 5,
  "TargetPlatforms": ["LinkedIn"],
  "ImageGeneration": {
    "Enabled": true,
    "ComfyUiBaseUrl": "http://192.168.50.47:8188",
    "WorkflowTemplate": "flux-text-to-image",
    "TimeoutSeconds": 120,
    "HealthCheckTimeoutSeconds": 5,
    "DefaultWidth": 1536,
    "DefaultHeight": 1536,
    "ModelCheckpoint": "flux1-dev-fp8.safetensors",
    "CircuitBreakerThreshold": 3
  },
  "PlatformPrompts": {
    "LinkedIn": null,
    "TwitterX": null,
    "PersonalBlog": null
  }
}
```

### 18. DI Registration

**File to modify:** `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs`

Add a new using statement for the `ContentAutomation` namespace (once the services folder exists) and register all new services. Place these registrations in a clearly commented block within `AddInfrastructure`:

```csharp
// Content automation
services.Configure<ContentAutomationOptions>(
    configuration.GetSection(ContentAutomationOptions.SectionName));
```

For the initial foundation, register stub/placeholder implementations that will be replaced by real implementations in later sections. Options:

**Option A (preferred):** Register the interfaces with `NotImplementedException`-throwing stubs. This lets the DI container resolve all interfaces immediately, and each subsequent section replaces its stub with the real implementation.

**Option B:** Register only the configuration and interfaces that have implementations from this section (just the options). Other sections add their own registrations. This is cleaner but means DI tests can only verify registration after all sections are complete.

Given that DI registration tests are listed above, use **Option A** -- create a single placeholder file `src/PersonalBrandAssistant.Infrastructure/Services/ContentAutomation/NotImplementedStubs.cs` with stub classes for all five interfaces. Each stub throws `NotImplementedException` on every method call. The DI registrations are:

```csharp
services.AddScoped<IDailyContentOrchestrator, DailyContentOrchestrator>();
services.AddSingleton<IComfyUiClient, ComfyUiClient>();
services.AddScoped<IImageGenerationService, ImageGenerationService>();
services.AddScoped<IImagePromptService, ImagePromptService>();
services.AddSingleton<IImageResizer, ImageResizer>();
services.AddHostedService<DailyContentProcessor>();
```

Until the real implementations exist (sections 02-09), register stub classes instead. The stub file should have internal visibility so it does not leak outside the Infrastructure project. Each subsequent section replaces the stub registration with the real class.

For `DailyContentProcessor`, register a minimal `BackgroundService` that does nothing until section 09 provides the real implementation.

### 19. TestEntityFactory Extension

**File to modify:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Utilities/TestEntityFactory.cs`

Add factory methods for creating test `AutomationRun` instances:

```csharp
public static AutomationRun CreateAutomationRun() => AutomationRun.Create();

public static AutomationRun CreateCompletedAutomationRun(long durationMs = 5000)
{
    var run = AutomationRun.Create();
    run.Complete(durationMs);
    return run;
}

public static AutomationRun CreateFailedAutomationRun(string error = "Test error", long durationMs = 1000)
{
    var run = AutomationRun.Create();
    run.Fail(error, durationMs);
    return run;
}
```

---

## File Summary

### New Files

| File | Layer | Purpose |
|------|-------|---------|
| `src/.../Domain/Enums/AutomationRunStatus.cs` | Domain | Enum: Running, Completed, PartialFailure, Failed |
| `src/.../Domain/Entities/AutomationRun.cs` | Domain | Tracks each pipeline execution |
| `src/.../Application/Common/Models/ContentAutomationOptions.cs` | Application | Configuration POCO (includes nested `ImageGenerationOptions`, `PlatformPromptOptions`) |
| `src/.../Application/Common/Models/AutomationRunResult.cs` | Application | Return type for orchestrator |
| `src/.../Application/Common/Models/ImageGenerationResult.cs` | Application | Return type for image generation |
| `src/.../Application/Common/Models/ComfyUiResult.cs` | Application | Return type for ComfyUI completion |
| `src/.../Application/Common/Interfaces/IDailyContentOrchestrator.cs` | Application | Orchestrator contract |
| `src/.../Application/Common/Interfaces/IComfyUiClient.cs` | Application | ComfyUI client contract |
| `src/.../Application/Common/Interfaces/IImageGenerationService.cs` | Application | Image generation contract |
| `src/.../Application/Common/Interfaces/IImagePromptService.cs` | Application | Image prompt generation contract |
| `src/.../Application/Common/Interfaces/IImageResizer.cs` | Application | Image resizer contract |
| `src/.../Infrastructure/Data/Configurations/AutomationRunConfiguration.cs` | Infrastructure | EF Core config for AutomationRuns table |
| `src/.../Infrastructure/Data/Migrations/[timestamp]_AddContentAutomation.cs` | Infrastructure | EF Core migration |
| `src/.../Infrastructure/Services/ContentAutomation/NotImplementedStubs.cs` | Infrastructure | Temporary stubs for DI resolution |
| `tests/.../Domain/AutomationRunTests.cs` | Tests | Entity unit tests |
| `tests/.../Domain/ContentImagePropertiesTests.cs` | Tests | New Content property tests |
| `tests/.../Persistence/AutomationRunPersistenceTests.cs` | Tests | DB integration tests |
| `tests/.../Configuration/ContentAutomationOptionsTests.cs` | Tests | Config binding tests |
| `tests/.../DependencyInjection/ContentAutomationServiceRegistrationTests.cs` | Tests | DI smoke tests |

### Modified Files

| File | Change |
|------|--------|
| `src/.../Domain/Entities/Content.cs` | Add `ImageFileId` and `ImageRequired` properties |
| `src/.../Domain/Enums/NotificationType.cs` | Add 4 automation notification types |
| `src/.../Application/Common/Interfaces/IApplicationDbContext.cs` | Add `DbSet<AutomationRun>` |
| `src/.../Infrastructure/Data/ApplicationDbContext.cs` | Add `AutomationRuns` DbSet |
| `src/.../Infrastructure/Data/Configurations/ContentConfiguration.cs` | Add `ImageFileId` and `ImageRequired` column config |
| `src/.../Infrastructure/DependencyInjection.cs` | Add option binding + service registrations |
| `src/.../Infrastructure/PersonalBrandAssistant.Infrastructure.csproj` | Add `Cronos` and `SkiaSharp` packages |
| `src/.../Api/appsettings.json` | Add `ContentAutomation` configuration section |
| `tests/.../Utilities/TestEntityFactory.cs` | Add `AutomationRun` factory methods |

---

## Verification

After implementation, run:

```bash
cd C:/Users/kruz7/OneDrive/Documents/Code\ Repos/MCKRUZ/personal-brand-assistant
dotnet build
dotnet test --filter "FullyQualifiedName~AutomationRun|FullyQualifiedName~ContentImageProperties|FullyQualifiedName~ContentAutomationOptions|FullyQualifiedName~ContentAutomationServiceRegistration"
```

All tests should pass. The solution should compile with no errors. The EF Core migration should apply cleanly against a fresh database.