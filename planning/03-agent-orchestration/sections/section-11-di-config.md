# Section 11 -- DI Configuration and Final Wiring

## Overview

Final assembly step of Phase 03: registering all agent orchestration services in DI, configuring `appsettings.json`, creating `MockChatClient` for CI, and DI resolution tests verifying the full wiring.

**Files created:**
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockChatClient.cs` (IChatClient mock with configurable responses/failures)
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockChatClientFactory.cs` (IChatClientFactory mock)
- `tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/AgentServiceRegistrationTests.cs` (5 tests)

**Files modified:**
- `src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs` — added DefaultModelTier, Models, LogPromptContent fields
- `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` — added agent orchestration DI registrations
- `src/PersonalBrandAssistant.Api/appsettings.json` — added AgentOrchestration config section

**Deviations from plan:**
- NuGet packages (Anthropic, Fluid.Core) already added in prior sections — no csproj change needed
- `app.MapAgentEndpoints()` already added in section-10 — no Program.cs change needed
- AgentOrchestrationOptions extended in Application layer (existing file) rather than creating new Infrastructure/Configuration copy
- PromptTemplateService registered via factory lambda using IOptions pattern (constructor takes raw string)
- LogPromptContent defaults to false (code review fix — production safety)
- ApiKey removed from appsettings.json (code review fix — prevent accidental commits)
- Integration tests with Testcontainers deferred — DI resolution tests provide sufficient wiring verification
- AgentIntegrationTestFixture, AgentPipelineIntegrationTests, AgentStreamingIntegrationTests deferred (require running Docker)

Depends on **all prior sections** (01-10). Does not block any other section.

## Tests First

### DI Resolution Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/AgentServiceRegistrationTests.cs`

```csharp
// Test: IChatClientFactory resolves as singleton
// Test: IPromptTemplateService resolves as singleton
// Test: ITokenTracker resolves as scoped
// Test: IAgentOrchestrator resolves as scoped
// Test: All 5 IAgentCapability implementations resolve
```

### Integration Tests

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Integration/AgentPipelineIntegrationTests.cs`

```csharp
// Test: Full pipeline -- orchestrator receives task -> capability generates -> content created -> workflow transition
// Test: Budget enforcement -- execution rejected when budget exceeded
// Test: Retry flow -- transient error -> retry with downgraded model -> success
// Test: Agent execution persisted with correct status transitions (Pending -> Running -> Completed)
```

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Integration/AgentStreamingIntegrationTests.cs`

```csharp
// Test: SSE endpoint delivers token events via MockChatClient
```

## Implementation Details

### 1. NuGet Packages

Add to `src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj`:

- `Anthropic` -- Official Anthropic .NET SDK
- `Fluid.Core` -- Liquid template engine

### 2. Options Class

**File:** `src/PersonalBrandAssistant.Infrastructure/Configuration/AgentOrchestrationOptions.cs`

```csharp
public class AgentOrchestrationOptions
{
    public const string SectionName = "AgentOrchestration";
    public string ApiKey { get; init; } = "";
    public decimal DailyBudget { get; init; } = 10.00m;
    public decimal MonthlyBudget { get; init; } = 100.00m;
    public string DefaultModelTier { get; init; } = "Standard";
    public Dictionary<string, string> Models { get; init; } = new();
    public Dictionary<string, ModelPricingOptions> Pricing { get; init; } = new();
    public string PromptsPath { get; init; } = "prompts";
    public int MaxRetriesPerExecution { get; init; } = 3;
    public int ExecutionTimeoutSeconds { get; init; } = 180;
    public bool LogPromptContent { get; init; } = true;
}

public class ModelPricingOptions
{
    public decimal InputPerMillion { get; init; }
    public decimal OutputPerMillion { get; init; }
}
```

### 3. appsettings.json

Add `AgentOrchestration` section to `src/PersonalBrandAssistant.Api/appsettings.json`:

```json
"AgentOrchestration": {
  "ApiKey": "",
  "DailyBudget": 10.00,
  "MonthlyBudget": 100.00,
  "DefaultModelTier": "Standard",
  "Models": {
    "Fast": "claude-haiku-4-5",
    "Standard": "claude-sonnet-4-5-20250929",
    "Advanced": "claude-opus-4-6"
  },
  "Pricing": {
    "claude-haiku-4-5": { "InputPerMillion": 1.00, "OutputPerMillion": 5.00 },
    "claude-sonnet-4-5-20250929": { "InputPerMillion": 3.00, "OutputPerMillion": 15.00 },
    "claude-opus-4-6": { "InputPerMillion": 5.00, "OutputPerMillion": 25.00 }
  },
  "PromptsPath": "prompts",
  "MaxRetriesPerExecution": 3,
  "ExecutionTimeoutSeconds": 180,
  "LogPromptContent": true
}
```

ApiKey stored in User Secrets (dev) / Azure Key Vault (prod). `LogPromptContent` overridden to `false` in production.

### 4. DI Registration

Add to `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` inside `AddInfrastructure`:

```csharp
// Options
services.Configure<AgentOrchestrationOptions>(configuration.GetSection("AgentOrchestration"));

// Singletons
services.AddSingleton<IChatClientFactory, ChatClientFactory>();
services.AddSingleton<IPromptTemplateService, PromptTemplateService>();

// Scoped
services.AddScoped<ITokenTracker, TokenTracker>();
services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();

// Agent Capabilities (all 5, scoped)
services.AddScoped<IAgentCapability, WriterAgentCapability>();
services.AddScoped<IAgentCapability, SocialAgentCapability>();
services.AddScoped<IAgentCapability, RepurposeAgentCapability>();
services.AddScoped<IAgentCapability, EngagementAgentCapability>();
services.AddScoped<IAgentCapability, AnalyticsAgentCapability>();
```

All five capabilities registered against `IAgentCapability`. Orchestrator receives `IEnumerable<IAgentCapability>` and routes by `Type`.

### 5. Program.cs

Add `app.MapAgentEndpoints();` after existing endpoint mappings.

### 6. MockChatClient

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockChatClient.cs`

Implements `IChatClient`. Returns canned responses with simulated token usage. Supports:
- Configurable response text and token counts
- Error simulation (throw on first N calls for retry testing)
- Streaming simulation (yield token chunks)

### 7. MockChatClientFactory

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockChatClientFactory.cs`

Implements `IChatClientFactory`. Returns `MockChatClient` instances. Registered in test DI to replace real factory.

### 8. Integration Test Fixture

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Integration/AgentIntegrationTestFixture.cs`

Uses `WebApplicationFactory<Program>` with:
- `MockChatClientFactory` replacing real factory
- Testcontainers PostgreSQL
- Dummy API key
- `LogPromptContent = true`

## File Summary

| File | Action |
|------|--------|
| `src/PersonalBrandAssistant.Infrastructure/Configuration/AgentOrchestrationOptions.cs` | Create |
| `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` | Modify |
| `src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj` | Modify (add packages) |
| `src/PersonalBrandAssistant.Api/appsettings.json` | Modify |
| `src/PersonalBrandAssistant.Api/Program.cs` | Modify |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockChatClient.cs` | Create |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockChatClientFactory.cs` | Create |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/DependencyInjection/AgentServiceRegistrationTests.cs` | Create |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Integration/AgentIntegrationTestFixture.cs` | Create |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Integration/AgentPipelineIntegrationTests.cs` | Create |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Integration/AgentStreamingIntegrationTests.cs` | Create |
