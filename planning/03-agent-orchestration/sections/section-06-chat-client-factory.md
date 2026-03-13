# Section 06 -- Chat Client Factory

## Overview

This section implements `ChatClientFactory`, the Infrastructure layer service responsible for creating configured `IChatClient` instances based on `ModelTier`. It maps each tier (Fast, Standard, Advanced) to a concrete Anthropic model ID read from configuration, and wraps every client with a `TokenTrackingDecorator` that intercepts completions to record token usage.

**Depends on:** Section 03 (interfaces -- `IChatClientFactory`, `ModelTier` enum, `ITokenTracker` interface)

**Blocks:** Section 08 (agent capabilities), Section 09 (orchestrator)

## File Inventory

| File | Action | Project |
|------|--------|---------|
| `src/PersonalBrandAssistant.Infrastructure/Services/ChatClientFactory.cs` | Create | Infrastructure |
| `src/PersonalBrandAssistant.Infrastructure/Services/TokenTrackingDecorator.cs` | Create | Infrastructure |
| `src/PersonalBrandAssistant.Infrastructure/Services/AgentExecutionContext.cs` | Create | Infrastructure |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ChatClientFactoryTests.cs` | Create | Infrastructure.Tests |

## Background and Context

The system uses the official Anthropic .NET SDK (`Anthropic` NuGet package) which exposes `IChatClient` from `Microsoft.Extensions.AI`. The `ChatClientFactory` is a **singleton** (the underlying `AnthropicClient` is thread-safe) registered in DI. It reads model ID mappings from `appsettings.json` under the `AgentOrchestration:Models` section.

### Configuration Shape

```json
{
  "AgentOrchestration": {
    "ApiKey": "",
    "Models": {
      "Fast": "claude-haiku-4-5",
      "Standard": "claude-sonnet-4-5-20250929",
      "Advanced": "claude-opus-4-6"
    }
  }
}
```

The API key is stored in User Secrets during development and Azure Key Vault in production -- never hardcoded.

### Model Tier Mapping

| ModelTier | Default Model ID | Use Case |
|-----------|-----------------|----------|
| `Fast` | `claude-haiku-4-5` | Classification, simple tasks |
| `Standard` | `claude-sonnet-4-5-20250929` | Content generation |
| `Advanced` | `claude-opus-4-6` | Complex reasoning |

## Tests First

Create `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ChatClientFactoryTests.cs`.

All tests use xUnit with AAA pattern. Mock `IConfiguration` and `ITokenTracker` via Moq.

### Test Stubs

```csharp
// Test: CreateClient maps Fast tier to claude-haiku-4-5 model ID
//   Arrange: Configure IConfiguration with Models:Fast = "claude-haiku-4-5"
//   Act: Call CreateClient(ModelTier.Fast)
//   Assert: Returned client targets the correct model

// Test: CreateClient maps Standard tier to claude-sonnet-4-5-20250929 model ID

// Test: CreateClient maps Advanced tier to claude-opus-4-6 model ID

// Test: CreateClient wraps client with TokenTrackingDecorator
//   Arrange: Create factory with valid config
//   Act: Call CreateClient(ModelTier.Standard)
//   Assert: The returned client is (or wraps) a TokenTrackingDecorator

// Test: Model IDs come from configuration (not hardcoded)
//   Arrange: Configure Models:Fast = "custom-model-id"
//   Act: Call CreateClient(ModelTier.Fast)
//   Assert: The created client uses "custom-model-id", not the default haiku ID

// Test: CreateClient throws when API key is missing or empty

// Test: CreateStreamingClient returns a valid client for the requested tier
```

## Implementation Details

### ChatClientFactory

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/ChatClientFactory.cs`

**Namespace:** `PersonalBrandAssistant.Infrastructure.Services`

**Registration:** Singleton in DI (see Section 11).

Responsibilities:
1. Read `AgentOrchestration:ApiKey` from configuration on construction. Throw `InvalidOperationException` if missing.
2. Read `AgentOrchestration:Models` section to build a `Dictionary<ModelTier, string>` mapping tiers to model IDs.
3. On `CreateClient(ModelTier tier)`: create an `AnthropicClient` for the mapped model ID, wrap it with `TokenTrackingDecorator`, and return the `IChatClient`.
4. On `CreateStreamingClient(ModelTier tier)`: same as `CreateClient` -- the `IChatClient` interface supports both streaming and non-streaming calls.

Cache or reuse `AnthropicClient` instances per tier since the underlying HTTP client is thread-safe. Use a `ConcurrentDictionary<ModelTier, IChatClient>` to avoid creating new clients on every call.

Key design decisions:
- The factory does **not** hold a reference to `ITokenTracker` directly. Instead, `TokenTrackingDecorator` receives `IServiceScopeFactory` to resolve scoped `ITokenTracker` instances at call time (since the factory is singleton but `ITokenTracker` is scoped).
- Model ID resolution: if a tier is not configured, fall back to a sensible default and log a warning.

### TokenTrackingDecorator

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/TokenTrackingDecorator.cs`

This is a decorator (wrapper) around `IChatClient` that intercepts completion responses to extract token usage metadata.

Responsibilities:
1. Implement `IChatClient` by delegating all calls to the inner client.
2. After each `CompleteAsync` call, extract `usage` from the response metadata (input_tokens, output_tokens, cache_read_tokens, cache_creation_tokens).
3. Call `ITokenTracker.RecordUsageAsync()` with the extracted usage data.
4. For streaming responses, accumulate usage from the final streaming chunk (Anthropic sends usage in the `message_stop` event).

The execution ID for `RecordUsageAsync` flows through `AgentExecutionContext.CurrentExecutionId` (AsyncLocal).

### AgentExecutionContext

**File:** `src/PersonalBrandAssistant.Infrastructure/Services/AgentExecutionContext.cs`

A small static class providing ambient execution context via `AsyncLocal<Guid?>`. The orchestrator (Section 09) sets this before invoking a capability, and the `TokenTrackingDecorator` reads it to associate usage with the correct execution.

```csharp
// Static class with AsyncLocal<Guid?> CurrentExecutionId
// Set by orchestrator, read by TokenTrackingDecorator
```

## Verification

After implementation, run:

```bash
dotnet test tests/PersonalBrandAssistant.Infrastructure.Tests --filter "ChatClientFactoryTests"
```
