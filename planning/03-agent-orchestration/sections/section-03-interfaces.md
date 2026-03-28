# Section 03 -- Application Layer Interfaces and Supporting Records

## Overview

This section defines all Application layer interfaces and supporting record types for the AI Agent Orchestration system. These are **contracts only** -- no implementations. They live in the Application project under `Common/Interfaces/` and `Common/Models/` (following the existing project conventions). Every subsequent section (05 through 10) depends on these interfaces.

## Dependencies

- **Section 01 (Domain Entities):** `AgentExecution` entity is referenced by `IAgentOrchestrator` return types and `ITokenTracker.RecordUsageAsync`.
- **Section 02 (Enums/Events):** `AgentCapabilityType`, `AgentExecutionStatus`, `ModelTier` enums are used throughout these interfaces.

## Tests

The TDD plan states: **"Core Interfaces -- no tests (interfaces only)."** Interfaces and records are pure type definitions with no logic. No test file is needed for this section.

However, the **record types** (`AgentTask`, `AgentExecutionResult`, `AgentOutput`, `AgentContext`) should have basic construction tests to confirm they can be instantiated and their properties behave as expected. These are lightweight smoke tests.

### Test File

**File:** `tests/PersonalBrandAssistant.Application.Tests/Common/Models/AgentModelsTests.cs`

```csharp
// Test: AgentTask can be constructed with all properties
// Test: AgentTask.Parameters defaults to empty dictionary when not provided
// Test: AgentExecutionResult can be constructed with execution ID and output
// Test: AgentOutput with CreatesContent=true indicates content-producing capability
// Test: AgentOutput with CreatesContent=false indicates data-only capability
// Test: AgentContext bundles content, brand profile model, and task parameters
// Test: BrandProfilePromptModel contains only prompt-safe fields (no Id, no audit fields)
// Test: ContentPromptModel contains only prompt-safe fields (no workflow internals)
```

## File Inventory

All files go in the Application project. Create each as a new file:

| File | Purpose |
|------|---------|
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IAgentOrchestrator.cs` | Central orchestration contract |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IAgentCapability.cs` | Per-agent capability contract |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IPromptTemplateService.cs` | Liquid template rendering contract |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/ITokenTracker.cs` | Token usage and budget contract |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IChatClientFactory.cs` | Chat client creation contract |
| `src/PersonalBrandAssistant.Application/Common/Models/AgentTask.cs` | Task dispatch record |
| `src/PersonalBrandAssistant.Application/Common/Models/AgentExecutionResult.cs` | Orchestrator return type |
| `src/PersonalBrandAssistant.Application/Common/Models/AgentOutput.cs` | Capability return type |
| `src/PersonalBrandAssistant.Application/Common/Models/AgentContext.cs` | Capability input context |
| `src/PersonalBrandAssistant.Application/Common/Models/BrandProfilePromptModel.cs` | Prompt-safe brand DTO |
| `src/PersonalBrandAssistant.Application/Common/Models/ContentPromptModel.cs` | Prompt-safe content DTO |
| `tests/PersonalBrandAssistant.Application.Tests/Common/Models/AgentModelsTests.cs` | Record smoke tests |

## Interface Definitions

### IAgentOrchestrator

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IAgentOrchestrator.cs`

The central entry point for all agent operations. The API layer calls this; it routes to the correct `IAgentCapability`.

```csharp
namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IAgentOrchestrator
{
    Task<Result<AgentExecutionResult>> ExecuteAsync(AgentTask task, CancellationToken ct);
    Task<Result<AgentExecution>> GetExecutionStatusAsync(Guid executionId, CancellationToken ct);
    Task<Result<AgentExecution[]>> ListExecutionsAsync(Guid? contentId, CancellationToken ct);
}
```

Uses the existing `Result<T>` pattern from `PersonalBrandAssistant.Application.Common.Models`. Returns `AgentExecutionResult` on success, or `Result.Failure` with `ErrorCode.ValidationFailed` for budget violations, `ErrorCode.NotFound` for missing executions.

### IAgentCapability

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IAgentCapability.cs`

Each of the five agent types (Writer, Social, Repurpose, Engagement, Analytics) implements this interface. The orchestrator dispatches based on `AgentCapabilityType`.

```csharp
namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IAgentCapability
{
    AgentCapabilityType Type { get; }
    ModelTier DefaultModelTier { get; }
    Task<Result<AgentOutput>> ExecuteAsync(AgentContext context, CancellationToken ct);
}
```

Key design points:
- `Type` property allows the orchestrator to find the right capability from a collection of registered `IAgentCapability` instances.
- `DefaultModelTier` is the capability's preferred tier; the orchestrator may override based on task parameters or configuration.
- Returns `AgentOutput` (not Content entities). The orchestrator decides whether to create Content based on `AgentOutput.CreatesContent`.

### IChatClientFactory

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IChatClientFactory.cs`

Creates configured `IChatClient` instances from Microsoft.Extensions.AI, wrapped with token-tracking decorators.

```csharp
namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IChatClientFactory
{
    IChatClient CreateClient(ModelTier tier);
    IChatClient CreateStreamingClient(ModelTier tier);
}
```

`IChatClient` comes from `Microsoft.Extensions.AI`. The factory maps `ModelTier` to model IDs from configuration:

- `ModelTier.Fast` maps to `claude-haiku-4-5`
- `ModelTier.Standard` maps to `claude-sonnet-4-5-20250929`
- `ModelTier.Advanced` maps to `claude-opus-4-6`

These mappings are read from `appsettings.json` under `AgentOrchestration:Models`, not hardcoded.

### IPromptTemplateService

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IPromptTemplateService.cs`

Loads Liquid templates from the `prompts/` directory and renders them with variable context.

```csharp
namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface IPromptTemplateService
{
    Task<string> RenderAsync(string agentName, string templateName, Dictionary<string, object> variables);
    string[] ListTemplates(string agentName);
}
```

- `agentName` is the subdirectory (e.g., "writer", "social").
- `templateName` is the file name without extension (e.g., "blog-post" resolves to `prompts/writer/blog-post.liquid`).
- `variables` is a dictionary of template variables; keys like `"brand"` map to `BrandProfilePromptModel`, `"content"` to `ContentPromptModel`, `"task"` to task-specific parameters.

### ITokenTracker

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/ITokenTracker.cs`

Records token usage per execution and enforces budget limits.

```csharp
namespace PersonalBrandAssistant.Application.Common.Interfaces;

public interface ITokenTracker
{
    Task RecordUsageAsync(
        Guid executionId,
        string modelId,
        int inputTokens,
        int outputTokens,
        int cacheReadTokens,
        int cacheCreationTokens,
        CancellationToken ct);

    Task<decimal> GetCostForPeriodAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<decimal> GetBudgetRemainingAsync(CancellationToken ct);
    Task<bool> IsOverBudgetAsync(CancellationToken ct);
}
```

Budget configuration comes from `appsettings.json`:
- `AgentOrchestration:DailyBudget` (default: 10.00)
- `AgentOrchestration:MonthlyBudget` (default: 100.00)

`IsOverBudgetAsync` returns true if **either** daily or monthly budget is exceeded. The orchestrator calls this before every execution.

## Supporting Record Types

### AgentTask

**File:** `src/PersonalBrandAssistant.Application/Common/Models/AgentTask.cs`

The input to the orchestrator. Specifies what to do and optional context.

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

public record AgentTask(
    AgentCapabilityType Type,
    Guid? ContentId,
    Dictionary<string, string> Parameters);
```

- `Type` determines which `IAgentCapability` handles the task.
- `ContentId` is the existing Content entity to work with (null for analytics/engagement tasks that do not target specific content).
- `Parameters` contains task-specific key-value pairs (e.g., `"topic"`, `"targetLength"`, `"templateName"`, `"sourceFormat"`, `"targetFormat"`).

### AgentExecutionResult

**File:** `src/PersonalBrandAssistant.Application/Common/Models/AgentExecutionResult.cs`

Returned by `IAgentOrchestrator.ExecuteAsync` on success.

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

public record AgentExecutionResult(
    Guid ExecutionId,
    AgentExecutionStatus Status,
    AgentOutput? Output,
    Guid? CreatedContentId);
```

- `ExecutionId` is the `AgentExecution.Id` for status polling.
- `Status` reflects the final execution status.
- `Output` contains the generated content/data (null if execution failed).
- `CreatedContentId` is populated when the orchestrator creates a Content entity from the output.

### AgentOutput

**File:** `src/PersonalBrandAssistant.Application/Common/Models/AgentOutput.cs`

Returned by `IAgentCapability.ExecuteAsync`. Contains the raw output from the AI capability.

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

public record AgentOutput
{
    public required string GeneratedText { get; init; }
    public string? Title { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
    public bool CreatesContent { get; init; }
    public List<AgentOutputItem> Items { get; init; } = [];
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int CacheReadTokens { get; init; }
    public int CacheCreationTokens { get; init; }
}

public record AgentOutputItem(
    string Text,
    string? Title,
    Dictionary<string, string> Metadata);
```

Design notes:
- `CreatesContent = true` for Writer, Social, Repurpose capabilities. The orchestrator creates `Content.Create()` and submits to the workflow engine.
- `CreatesContent = false` for Engagement and Analytics capabilities that return data/suggestions only.
- `Items` is used by RepurposeAgent when a single source is transformed into multiple outputs (e.g., blog post to multiple social posts). Each item becomes a separate Content entity with `ParentContentId` set.
- `Metadata` holds capability-specific data: hashtags, suggested media URLs, platform constraints, etc.
- Token counts are reported back so the orchestrator can persist them to `AgentExecution`.

### AgentContext

**File:** `src/PersonalBrandAssistant.Application/Common/Models/AgentContext.cs`

Bundles everything a capability needs to execute. Built by the orchestrator before dispatching.

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

public record AgentContext
{
    public required Guid ExecutionId { get; init; }
    public required BrandProfilePromptModel BrandProfile { get; init; }
    public ContentPromptModel? Content { get; init; }
    public required IPromptTemplateService PromptService { get; init; }
    public required IChatClient ChatClient { get; init; }
    public required Dictionary<string, string> Parameters { get; init; }
    public required ModelTier ModelTier { get; init; }
}
```

- `ExecutionId` links back to the `AgentExecution` for logging.
- `BrandProfile` is the prompt-safe DTO (see below), not the raw `BrandProfile` entity.
- `Content` is populated when the task targets an existing Content entity (null for analytics/engagement without content context).
- `PromptService` and `ChatClient` are injected so capabilities do not need constructor dependencies on these services.
- `Parameters` are the task-specific parameters from `AgentTask.Parameters`.
- `ModelTier` is the resolved tier (task override, capability default, or config default).

Note: `IChatClient` in the `AgentContext` record comes from the `Microsoft.Extensions.AI` namespace. The Application project will need a package reference to `Microsoft.Extensions.AI.Abstractions` for this type.

### BrandProfilePromptModel

**File:** `src/PersonalBrandAssistant.Application/Common/Models/BrandProfilePromptModel.cs`

A prompt-safe projection of `BrandProfile`. Excludes internal IDs, audit fields, and version tracking. This is what templates receive as the `brand` variable.

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

public record BrandProfilePromptModel
{
    public required string Name { get; init; }
    public required string PersonaDescription { get; init; }
    public required IReadOnlyList<string> ToneDescriptors { get; init; }
    public required string StyleGuidelines { get; init; }
    public required IReadOnlyList<string> PreferredTerms { get; init; }
    public required IReadOnlyList<string> AvoidedTerms { get; init; }
    public required IReadOnlyList<string> Topics { get; init; }
    public required IReadOnlyList<string> ExampleContent { get; init; }
}
```

Maps from the existing `BrandProfile` entity (which has `Name`, `PersonaDescription`, `ToneDescriptors`, `StyleGuidelines`, `VocabularyPreferences` containing preferred/avoided terms, `Topics`, `ExampleContent`). The mapping is done by the orchestrator when building `AgentContext`.

### ContentPromptModel

**File:** `src/PersonalBrandAssistant.Application/Common/Models/ContentPromptModel.cs`

A prompt-safe projection of `Content`. Excludes workflow state machine internals, retry counts, and version fields.

```csharp
namespace PersonalBrandAssistant.Application.Common.Models;

public record ContentPromptModel
{
    public required string? Title { get; init; }
    public required string Body { get; init; }
    public required ContentType ContentType { get; init; }
    public required ContentStatus Status { get; init; }
    public required PlatformType[] TargetPlatforms { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}
```

Maps from the existing `Content` entity. The `Metadata` dictionary is a flattened view of `ContentMetadata` (the value object on Content).

## Package Dependencies

The Application project needs one new package reference for the `IChatClient` type used in `AgentContext`:

- `Microsoft.Extensions.AI.Abstractions` -- provides the `IChatClient` interface

This is the **abstractions-only** package (no concrete implementations). The Infrastructure project will reference the actual Anthropic SDK.

## Implementation Notes

1. **Namespace consistency:** All interfaces go in `PersonalBrandAssistant.Application.Common.Interfaces`. All records/models go in `PersonalBrandAssistant.Application.Common.Models`. This matches the existing project structure.

2. **Immutability:** All supporting types are records with `init`-only properties, following the project's immutability conventions.

3. **No logic in this section:** These are pure contracts and data shapes. All behavior is implemented in later sections (05-09).

4. **Using declarations needed:** Files will need `using` statements for:
   - `PersonalBrandAssistant.Domain.Entities` (for `AgentExecution`)
   - `PersonalBrandAssistant.Domain.Enums` (for `AgentCapabilityType`, `AgentExecutionStatus`, `ModelTier`, `ContentType`, `ContentStatus`, `PlatformType`)
   - `PersonalBrandAssistant.Application.Common.Models` (for `Result<T>`, `AgentTask`, `AgentOutput`, etc.)
   - `Microsoft.Extensions.AI` (for `IChatClient` in `AgentContext`)

5. **Dictionary defaults:** `AgentTask.Parameters` and `AgentOutput.Metadata` should never be null. The record definitions use `new()` default values or constructor parameters to ensure this.
