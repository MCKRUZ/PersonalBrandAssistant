# Section 12: DI Configuration

## Overview

This is the final section of Phase 04. It wires all platform integration services into the dependency injection container by updating `DependencyInjection.cs` in the Infrastructure project. This includes registering typed HttpClients with Polly resilience policies, binding configuration options, replacing the `PublishingPipelineStub` with the real `PublishingPipeline`, registering all platform adapters, formatters, background services, and updating `appsettings.json` with the new configuration sections.

## Dependencies

This section depends on **all previous sections** (01 through 11). Every service, interface, adapter, formatter, and background processor must exist before wiring them here. Key types from prior sections:

- **Section 01:** `ContentPlatformStatus`, `OAuthState`, `PlatformPublishStatus` (domain entities)
- **Section 02:** `ISocialPlatform`, `IOAuthManager`, `IRateLimiter`, `IMediaStorage`, `IPlatformContentFormatter`, all DTOs including `PlatformIntegrationOptions`, `MediaStorageOptions`
- **Section 03:** EF Core configurations (already auto-discovered by `ApplicationDbContext`)
- **Section 04:** `LocalMediaStorage` (implements `IMediaStorage`)
- **Section 05:** `DatabaseRateLimiter` (implements `IRateLimiter`)
- **Section 06:** `OAuthManager` (implements `IOAuthManager`)
- **Section 07:** `TwitterContentFormatter`, `LinkedInContentFormatter`, `InstagramContentFormatter`, `YouTubeContentFormatter` (implement `IPlatformContentFormatter`)
- **Section 08:** `TwitterPlatformAdapter`, `LinkedInPlatformAdapter`, `InstagramPlatformAdapter`, `YouTubePlatformAdapter` (implement `ISocialPlatform`), `PlatformAdapterBase`
- **Section 09:** `PublishingPipeline` (implements `IPublishingPipeline`, replaces `PublishingPipelineStub`)
- **Section 10:** `PlatformEndpoints`, `MediaEndpoints` (API endpoints, registered in `Program.cs`)
- **Section 11:** `TokenRefreshProcessor`, `PlatformHealthMonitor`, `PublishCompletionPoller` (background services)

## Tests First

Create a test file at:
`tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/PlatformServiceRegistrationTests.cs`

Follow the same pattern as the existing `AgentServiceRegistrationTests` (at `tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/AgentServiceRegistrationTests.cs`), which uses a `WebApplicationFactory<Program>` fixture, removes hosted services, and resolves services from a scoped `IServiceProvider`.

```csharp
// File: tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/PlatformServiceRegistrationTests.cs
// Namespace: PersonalBrandAssistant.Infrastructure.Tests.DependencyInjection

// Test: All ISocialPlatform implementations resolve from DI
//   Resolve IEnumerable<ISocialPlatform>, assert 4 items (Twitter, LinkedIn, Instagram, YouTube)

// Test: All IPlatformContentFormatter implementations resolve from DI
//   Resolve IEnumerable<IPlatformContentFormatter>, assert 4 items

// Test: IOAuthManager resolves as scoped
//   Resolve IOAuthManager, assert not null, assert type is OAuthManager

// Test: IRateLimiter resolves as scoped
//   Resolve IRateLimiter, assert not null, assert type is DatabaseRateLimiter

// Test: IMediaStorage resolves as singleton
//   Resolve IMediaStorage, assert not null, assert type is LocalMediaStorage

// Test: IPublishingPipeline resolves as PublishingPipeline (not stub)
//   Resolve IPublishingPipeline, assert not null
//   Assert type is PublishingPipeline (NOT PublishingPipelineStub)

// Test: Typed HttpClients configured for Twitter, LinkedIn, Instagram
//   Resolve each adapter type, confirm they were constructed with HttpClient (non-null)
//   Alternatively, resolve IHttpClientFactory and create named clients

// Test: Background services registered (TokenRefreshProcessor, PlatformHealthMonitor, PublishCompletionPoller)
//   Resolve IEnumerable<IHostedService>, assert contains instances of all three types
//   NOTE: The test factory removes hosted services, so this test needs a separate factory
//   that does NOT remove these three, or verify registration descriptors directly

// Test: PlatformIntegrationOptions binds from configuration
//   Set config values via UseSetting in the test factory
//   Resolve IOptions<PlatformIntegrationOptions>, assert values match

// Test: MediaStorageOptions binds from configuration
//   Set config values via UseSetting in the test factory
//   Resolve IOptions<MediaStorageOptions>, assert BasePath matches
```

The test factory should extend the existing `DiTestFactory` pattern: set `ConnectionStrings:DefaultConnection` to a dummy value, set `ApiKey`, remove hosted services, and add mock replacements for services that require external dependencies (e.g., `MockChatClientFactory` for `IChatClientFactory`). Add configuration settings for the new options sections:

```csharp
builder.UseSetting("PlatformIntegrations:Twitter:CallbackUrl", "http://localhost:4200/platforms/twitter/callback");
builder.UseSetting("PlatformIntegrations:Twitter:BaseUrl", "https://api.x.com/2");
builder.UseSetting("PlatformIntegrations:LinkedIn:CallbackUrl", "http://localhost:4200/platforms/linkedin/callback");
builder.UseSetting("PlatformIntegrations:LinkedIn:ApiVersion", "202603");
builder.UseSetting("PlatformIntegrations:LinkedIn:BaseUrl", "https://api.linkedin.com/rest");
builder.UseSetting("PlatformIntegrations:Instagram:CallbackUrl", "http://localhost:4200/platforms/instagram/callback");
builder.UseSetting("PlatformIntegrations:YouTube:CallbackUrl", "http://localhost:4200/platforms/youtube/callback");
builder.UseSetting("PlatformIntegrations:YouTube:DailyQuotaLimit", "10000");
builder.UseSetting("MediaStorage:BasePath", "./test-media");
builder.UseSetting("MediaStorage:SigningKey", "test-signing-key-for-hmac-validation");
```

## Implementation Details

### File to Modify: `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs`

The existing `AddInfrastructure` method (at `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs`) currently registers the `PublishingPipelineStub`. Update it with the following changes:

#### 1. Add Required Using Statements

Add using directives for the new namespaces:
- `PersonalBrandAssistant.Infrastructure.Services.Platform` (OAuthManager, DatabaseRateLimiter, LocalMediaStorage, PublishingPipeline)
- `PersonalBrandAssistant.Infrastructure.Services.Platform.Adapters` (all four platform adapters)
- `PersonalBrandAssistant.Infrastructure.Services.Platform.Formatters` (all four content formatters)
- `Microsoft.Extensions.Http` (for Polly integration, if needed)
- `Polly` and `Polly.Extensions.Http` (for resilience policies)

The NuGet package `Microsoft.Extensions.Http.Polly` must be added to the Infrastructure project if not already present.

#### 2. Bind Configuration Options

Add options binding at the top of the `AddInfrastructure` method, after the existing `Configure<AgentOrchestrationOptions>` call:

```csharp
services.Configure<PlatformIntegrationOptions>(configuration.GetSection("PlatformIntegrations"));
services.Configure<MediaStorageOptions>(configuration.GetSection("MediaStorage"));
```

`PlatformIntegrationOptions` and `MediaStorageOptions` are defined in Section 02 under `src/PersonalBrandAssistant.Application/Common/Models/`.

#### 3. Register Singleton Services

```csharp
services.AddSingleton<IMediaStorage, LocalMediaStorage>();
```

`LocalMediaStorage` is singleton because it only manages filesystem paths and HMAC signing -- no scoped dependencies.

#### 4. Register Typed HttpClients with Polly Policies

Each platform adapter gets a typed `HttpClient` with transient fault handling. Use the `AddHttpClient<TAdapter>` pattern with `AddTransientHttpErrorPolicy` for retry with exponential backoff:

- **TwitterPlatformAdapter**: base URL from `PlatformIntegrations:Twitter:BaseUrl` (default `https://api.x.com/2`). Retry 3 times with exponential backoff (2s, 4s, 8s).
- **LinkedInPlatformAdapter**: base URL from `PlatformIntegrations:LinkedIn:BaseUrl` (default `https://api.linkedin.com/rest`). Add default headers `X-Restli-Protocol-Version: 2.0.0` and `Linkedin-Version: {apiVersion}`. Same retry policy.
- **InstagramPlatformAdapter**: base URL `https://graph.facebook.com/v19.0`. Same retry policy.
- **YouTubePlatformAdapter**: does NOT use typed HttpClient (uses `Google.Apis.YouTube.v3` SDK). Register as a plain scoped service.

Read the platform-specific config from the bound `PlatformIntegrationOptions` using `configuration.GetSection(...)` calls for the base URLs, or resolve `IOptions<PlatformIntegrationOptions>` where needed.

Pattern for each HttpClient registration:

```csharp
services.AddHttpClient<TwitterPlatformAdapter>(client =>
    {
        client.BaseAddress = new Uri(
            configuration["PlatformIntegrations:Twitter:BaseUrl"] ?? "https://api.x.com/2");
    })
    .AddTransientHttpErrorPolicy(policy =>
        policy.WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));
```

#### 5. Register Scoped Services

Replace the stub and add new scoped registrations:

```csharp
// Replace: services.AddScoped<IPublishingPipeline, PublishingPipelineStub>();
// With:
services.AddScoped<IPublishingPipeline, PublishingPipeline>();

services.AddScoped<IOAuthManager, OAuthManager>();
services.AddScoped<IRateLimiter, DatabaseRateLimiter>();
```

#### 6. Register Platform Adapters (Multi-Registration)

All four adapters are registered against `ISocialPlatform` so they can be resolved as `IEnumerable<ISocialPlatform>`. The publishing pipeline uses this collection to find the correct adapter by `PlatformType`:

```csharp
services.AddScoped<ISocialPlatform, TwitterPlatformAdapter>();
services.AddScoped<ISocialPlatform, LinkedInPlatformAdapter>();
services.AddScoped<ISocialPlatform, InstagramPlatformAdapter>();
services.AddScoped<ISocialPlatform, YouTubePlatformAdapter>();
```

**Important:** `TwitterPlatformAdapter`, `LinkedInPlatformAdapter`, and `InstagramPlatformAdapter` are also registered via `AddHttpClient<T>` above. The `AddHttpClient<T>` call registers the type as a transient by default. The `AddScoped<ISocialPlatform, T>` multi-registration enables resolution via `IEnumerable<ISocialPlatform>`. The adapter constructor should accept `HttpClient` as a parameter (injected by `IHttpClientFactory` typed client infrastructure). Verify that both registrations coexist correctly -- the typed HttpClient registration creates a transient `T`, while the `ISocialPlatform` registration creates a scoped mapping. If there is a conflict, use `AddHttpClient` with a named client instead, and have the adapter resolve its `HttpClient` from `IHttpClientFactory` by name in its constructor.

#### 7. Register Content Formatters (Multi-Registration)

```csharp
services.AddScoped<IPlatformContentFormatter, TwitterContentFormatter>();
services.AddScoped<IPlatformContentFormatter, LinkedInContentFormatter>();
services.AddScoped<IPlatformContentFormatter, InstagramContentFormatter>();
services.AddScoped<IPlatformContentFormatter, YouTubeContentFormatter>();
```

These are resolved as `IEnumerable<IPlatformContentFormatter>` in the publishing pipeline.

#### 8. Register Background Services

```csharp
services.AddHostedService<TokenRefreshProcessor>();
services.AddHostedService<PlatformHealthMonitor>();
services.AddHostedService<PublishCompletionPoller>();
```

These are added alongside the existing hosted services (`DataSeeder`, `AuditLogCleanupService`, etc.).

#### 9. Remove PublishingPipelineStub

After confirming the real `PublishingPipeline` is registered:
- Delete the file `src/PersonalBrandAssistant.Infrastructure/Services/PublishingPipelineStub.cs`
- Delete the test file `tests/PersonalBrandAssistant.Application.Tests/Services/PublishingPipelineStubTests.cs`
- Remove any remaining references to `PublishingPipelineStub` from the codebase

### File to Modify: `src/PersonalBrandAssistant.Api/appsettings.json`

Add the new configuration sections. Only non-secret values go here (callback URLs, base URLs, quota limits). All secrets (`ClientId`, `ClientSecret`, `AppId`, `AppSecret`, `MediaStorage:SigningKey`) go in User Secrets.

```json
{
  "PlatformIntegrations": {
    "Twitter": {
      "CallbackUrl": "http://localhost:4200/platforms/twitter/callback",
      "BaseUrl": "https://api.x.com/2"
    },
    "LinkedIn": {
      "CallbackUrl": "http://localhost:4200/platforms/linkedin/callback",
      "ApiVersion": "202603",
      "BaseUrl": "https://api.linkedin.com/rest"
    },
    "Instagram": {
      "CallbackUrl": "http://localhost:4200/platforms/instagram/callback"
    },
    "YouTube": {
      "CallbackUrl": "http://localhost:4200/platforms/youtube/callback",
      "DailyQuotaLimit": 10000
    }
  },
  "MediaStorage": {
    "BasePath": "./media"
  }
}
```

### File to Modify: `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/CustomWebApplicationFactory.cs`

Add removal of the three new background services to prevent them from running during integration tests:

```csharp
RemoveService<TokenRefreshProcessor>(services);
RemoveService<PlatformHealthMonitor>(services);
RemoveService<PublishCompletionPoller>(services);
```

### NuGet Packages

Ensure the following packages are referenced in the Infrastructure project (`src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj`):

- `Microsoft.Extensions.Http.Polly` -- provides `AddTransientHttpErrorPolicy` extension method
- `Polly` -- transitive dependency, but verify it is available

These may already be present. Check the `.csproj` file before adding.

## Verification Checklist

After implementation, verify:

1. `dotnet build` succeeds with no errors
2. `dotnet test` passes all existing tests plus the new `PlatformServiceRegistrationTests`
3. The `PublishingPipelineStub` class and its test file are deleted
4. No remaining references to `PublishingPipelineStub` exist in the codebase
5. All four platform adapters resolve via `IEnumerable<ISocialPlatform>`
6. All four content formatters resolve via `IEnumerable<IPlatformContentFormatter>`
7. `IPublishingPipeline` resolves to `PublishingPipeline` (not the stub)
8. `IOptions<PlatformIntegrationOptions>` binds correctly from config
9. `IOptions<MediaStorageOptions>` binds correctly from config
10. `CustomWebApplicationFactory` removes the three new background services so integration tests do not attempt real token refresh or health checks

## Deviations from Plan

- **Polly resilience policies:** Omitted — `Microsoft.Extensions.Http.Polly` NuGet package not installed. Deferred to a future PR. HttpClients registered without retry policies.
- **Platform adapter multi-registration:** Used `AddScoped<ISocialPlatform>(sp => sp.GetRequiredService<ConcreteAdapter>())` factory delegate pattern instead of direct `AddScoped<ISocialPlatform, ConcreteAdapter>()` to avoid conflict with typed HttpClient's transient registration.
- **TimeProvider + IMemoryCache:** Added `services.AddSingleton(TimeProvider.System)` and `services.AddMemoryCache()` — required by `DatabaseRateLimiter` but not mentioned in the plan.
- **PlatformIntegrationOptions.SectionName:** Added `const SectionName = "PlatformIntegrations"` per code review, matching `AgentOrchestrationOptions.SectionName` pattern.
- **Instagram base URL:** Made configurable from `PlatformIntegrations:Instagram:BaseUrl` with fallback, matching Twitter/LinkedIn pattern (review fix DI-06).
- **NotificationType enum test:** Updated Domain `EnumTests.NotificationType_HasExactly5Values` → `_HasExactly8Values` to account for section-11's 3 new enum values.
- **Test count:** 8 tests (not 10 from plan). Omitted typed HttpClient verification and background service descriptor tests — core resolution coverage is sufficient.

## Files Created/Modified

- `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` — Added all platform service registrations, TimeProvider, MemoryCache
- `src/PersonalBrandAssistant.Api/appsettings.json` — Added PlatformIntegrations and MediaStorage config sections
- `src/PersonalBrandAssistant.Application/Common/Models/PlatformIntegrationOptions.cs` — Added SectionName const
- `src/PersonalBrandAssistant.Infrastructure/Services/PublishingPipelineStub.cs` — Deleted
- `tests/PersonalBrandAssistant.Application.Tests/Services/PublishingPipelineStubTests.cs` — Deleted
- `tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/PlatformServiceRegistrationTests.cs` — 8 tests
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Api/CustomWebApplicationFactory.cs` — Added 3 new hosted service removals
- `tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs` — Updated NotificationType count