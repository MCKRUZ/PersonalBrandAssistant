# Section 09 -- Agent Orchestrator

## Overview

This section implements `AgentOrchestrator`, the central coordination class that routes agent tasks to capabilities, manages execution lifecycle (create, run, complete/fail/cancel), enforces budget limits, handles retry with model downgrade on transient errors, and optionally creates Content entities from agent output and submits them to the workflow engine.

**Files created:**
- `src/PersonalBrandAssistant.Infrastructure/Agents/AgentOrchestrator.cs` (290 lines)
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/AgentOrchestratorTests.cs` (20 tests)
- `tests/PersonalBrandAssistant.Infrastructure.Tests/Helpers/AsyncQueryableHelpers.cs` (async EF Core mock helper)

**Files modified:**
- `src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs` — added `ExecutionTimeoutSeconds` and `MaxRetriesPerExecution`

**Deviations from plan:**
- Used `FrozenDictionary` for capability registry (code review fix — immutability)
- Added exponential backoff between retries (code review fix — was missing)
- `RecordUsageAsync` passes actual tier (post-downgrade) instead of `execution.ModelUsed` (code review fix — stale tier tracking)
- Budget re-check added before each retry attempt (code review fix — TOCTOU race condition)
- `MapCapabilityToContentType` throws on unexpected types instead of silent default (code review fix)
- Workflow transition result is checked and logged on failure (code review fix)
- Notification message does not include raw error details (security — avoid leaking exception internals)

## Dependencies

- **Section 01:** `AgentExecution` entity with lifecycle methods (`Create`, `MarkRunning`, `Complete`, `Fail`, `Cancel`, `RecordUsage`)
- **Section 02:** `AgentCapabilityType`, `AgentExecutionStatus`, `ModelTier` enums. `AgentExecutionCompletedEvent`, `AgentExecutionFailedEvent` domain events.
- **Section 03:** `IAgentOrchestrator`, `IAgentCapability`, `ITokenTracker`, `IChatClientFactory`, `IPromptTemplateService`. Records: `AgentTask`, `AgentExecutionResult`, `AgentContext`, `AgentOutput`.
- **Section 04:** `DbSet<AgentExecution>` on `IApplicationDbContext`.
- **Section 07:** `ITokenTracker` with `IsOverBudgetAsync`, `RecordUsageAsync`.
- **Section 08:** Five `IAgentCapability` implementations.

### Existing Codebase Integration

- **`IWorkflowEngine`** -- `TransitionAsync(contentId, targetStatus, reason?, actor?, ct)`. Use `ActorType.Agent`.
- **`INotificationService`** -- `SendAsync(type, title, message, contentId?, ct)`. Notify on permanent failure.
- **`IApplicationDbContext`** -- `AgentExecutions` DbSet, `Contents` DbSet.
- **`Result<T>`** -- all methods return Result. Use `ErrorCode.ValidationFailed` for budget, `ErrorCode.NotFound` for missing entities.
- **`Content.Create()`** -- factory: `Content.Create(type, body, title?, targetPlatforms?, capturedAutonomyLevel)`.
- **`ActorType.Agent`** -- existing enum value for agent-attributed actions.

## Tests First

### Task Routing Tests

```csharp
// Test: ExecuteAsync routes Writer task to WriterAgentCapability
// Test: ExecuteAsync routes Social task to SocialAgentCapability
// Test: ExecuteAsync routes each AgentCapabilityType to correct capability
// Test: ExecuteAsync returns ValidationFailed when over budget
// Test: ExecuteAsync creates AgentExecution entity with Pending status before calling capability
// Test: ExecuteAsync sets execution to Running before capability.ExecuteAsync
// Test: ExecuteAsync sets execution to Completed on success with token usage
// Test: ExecuteAsync sets execution to Failed on capability failure
// Test: ExecuteAsync sets execution to Cancelled on timeout
// Test: ExecuteAsync creates Content entity when AgentOutput.CreatesContent is true
// Test: ExecuteAsync does NOT create Content when AgentOutput.CreatesContent is false
// Test: ExecuteAsync submits to workflow engine when content is created
// Test: ExecuteAsync uses ActorType.Agent for workflow transition
```

### Retry and Fallback Tests

```csharp
// Test: ExecuteAsync retries on transient errors (rate limit, 5xx)
// Test: ExecuteAsync does NOT retry on validation/prompt errors
// Test: ExecuteAsync downgrades model tier on second transient failure
// Test: ExecuteAsync fails permanently after max retries, sends notification
// Test: ExecuteAsync respects MaxRetriesPerExecution config
```

### Status Query Tests

```csharp
// Test: GetExecutionStatusAsync returns execution by ID
// Test: GetExecutionStatusAsync returns NotFound for unknown ID
// Test: ListExecutionsAsync filters by contentId
// Test: ListExecutionsAsync returns all when contentId is null
```

### Test Dependencies to Mock

- `IEnumerable<IAgentCapability>` -- one mock per capability type
- `ITokenTracker` -- `IsOverBudgetAsync`, `RecordUsageAsync`
- `IChatClientFactory` -- `CreateClient(ModelTier)`
- `IPromptTemplateService`
- `IApplicationDbContext` -- `AgentExecutions`, `Contents` DbSets
- `IWorkflowEngine` -- verify `TransitionAsync` calls
- `INotificationService` -- verify failure notifications
- `IOptions<AgentOrchestrationOptions>`
- `ILogger<AgentOrchestrator>`

## Implementation Details

### Constructor Dependencies

```csharp
public class AgentOrchestrator : IAgentOrchestrator
{
    // IEnumerable<IAgentCapability> capabilities (indexed to dict by Type)
    // ITokenTracker tokenTracker
    // IChatClientFactory chatClientFactory
    // IPromptTemplateService promptTemplateService
    // IApplicationDbContext dbContext
    // IWorkflowEngine workflowEngine
    // INotificationService notificationService
    // IOptions<AgentOrchestrationOptions> options
    // ILogger<AgentOrchestrator> logger
}
```

### ExecuteAsync -- Step-by-Step Flow

1. **Budget check:** `_tokenTracker.IsOverBudgetAsync(ct)`. If true, return `Result.Failure(ErrorCode.ValidationFailed, "Budget exceeded")`, send notification.

2. **Resolve capability:** Look up `task.Type` in capability dictionary. If not found, return failure.

3. **Create execution entity:** `AgentExecution.Create(task.Type, modelTier, task.ContentId)`. Add to DbSet, `SaveChangesAsync`.

4. **Determine model tier:** task parameter override > capability default > config default.

5. **Build AgentContext:** Load content (if contentId), brand profile, prompt service, chat client, parameters, model tier.

6. **Create timeout CTS:** `new CancellationTokenSource(TimeSpan.FromSeconds(options.ExecutionTimeoutSeconds))` linked with incoming `ct`.

7. **Mark Running:** `execution.MarkRunning()`, persist.

8. **Execute with retry loop:**
   - On success: break.
   - On transient error: increment attempt, downgrade tier on attempt 2+, retry.
   - On non-transient error: fail immediately.
   - If max retries exceeded: fail permanently, send notification.

9. **On success:** Record usage, complete execution, optionally create Content + submit to workflow.

10. **On timeout/cancel:** `execution.Cancel()`, persist.

11. **On permanent failure:** `execution.Fail(error)`, persist, notify.

### Transient Error Classification

```csharp
private static bool IsTransientError(Exception ex)
// HttpRequestException with 429, 500, 502, 503, 504
// TaskCanceledException from HttpClient timeout (not user cancellation)
// Anthropic SDK rate limit exceptions
```

### Model Tier Downgrade

```csharp
private static ModelTier? DowngradeModelTier(ModelTier current)
// Advanced -> Standard, Standard -> Fast, Fast -> null
```

### Content Creation from AgentOutput

When `CreatesContent` is true:
- Map capability type to `ContentType` (Writer -> BlogPost, Social -> SocialPost/Thread, Repurpose -> from params)
- `Content.Create(contentType, output.Text, output.Title, targetPlatforms, autonomyLevel)`
- Persist, then `_workflowEngine.TransitionAsync(content.Id, ContentStatus.Review, "Agent-generated", ActorType.Agent, ct)`
- For multi-output (RepurposeAgent `Items`), create multiple Content entities with `ParentContentId`

### GetExecutionStatusAsync / ListExecutionsAsync

Simple DbContext queries. Return `Result.NotFound` or `Result.Success` as appropriate.

### Error Handling

- Never throws to callers -- all errors wrapped in `Result<T>`
- Outermost try/catch ensures execution entity always reaches terminal state
- Budget exceeded = `ErrorCode.ValidationFailed`, not internal error

### Registration

Scoped in DI (depends on scoped `IApplicationDbContext`). See section-11.
