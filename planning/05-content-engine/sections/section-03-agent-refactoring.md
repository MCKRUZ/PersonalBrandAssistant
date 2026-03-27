I now have all the context I need. Let me produce the section content.

# Section 03 — Agent Refactoring (Sidecar Migration)

## Overview

This section rewrites `AgentCapabilityBase` and `AgentOrchestrator` to use `ISidecarClient` instead of `IChatClientFactory`/`IChatClient`. It also removes `ChatClientFactory` and `TokenTrackingDecorator`. The public interface (`IAgentOrchestrator`, `IAgentCapability`, all five capability types) remains unchanged so that consumers see no breaking changes.

**Depends on:** section-02-sidecar-integration (provides `ISidecarClient`, `SidecarEvent` types, `SidecarClient` implementation)

**Blocks:** section-04 (content pipeline), section-05 (repurposing), section-07 (brand voice), section-08 (trend monitoring)

---

## Background: Current Architecture

The current agent system uses the Anthropic SDK directly:

- `IChatClientFactory` creates `IChatClient` instances per `ModelTier` (Fast/Standard/Advanced)
- `ChatClientFactory` wraps `AnthropicClient` with a `TokenTrackingDecorator` for usage tracking
- `AgentCapabilityBase.ExecuteAsync` builds `ChatMessage` lists and calls `context.ChatClient.GetResponseAsync(messages)`
- `AgentOrchestrator` handles model tier selection, retry with tier downgrade, and timeout

After this refactoring:

- All AI calls go through `ISidecarClient.SendTaskAsync(prompt, sessionId, ct)` which streams `SidecarEvent` records
- Model selection, retries against the Claude API, and prompt caching are handled by the sidecar (Claude Code CLI internally)
- The orchestrator simplifies significantly: no model tier selection, no tier downgrade, no HTTP transient error handling
- Token tracking shifts from the `TokenTrackingDecorator` intercepting `IChatClient` responses to parsing `TaskCompleteEvent` from the sidecar event stream

---

## Files to Modify

| File | Action |
|------|--------|
| `src/PersonalBrandAssistant.Application/Common/Models/AgentContext.cs` | Modify: replace `IChatClient ChatClient` and `ModelTier` with `ISidecarClient SidecarClient` and optional `string? SessionId` |
| `src/PersonalBrandAssistant.Application/Common/Models/AgentOrchestrationOptions.cs` | Modify: remove `Models`, `Pricing`, `DefaultModelTier` properties (move to sidecar config). Keep budget, timeout, retries, prompts path, log setting. |
| `src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AgentCapabilityBase.cs` | Rewrite: use `ISidecarClient` instead of `IChatClient` |
| `src/PersonalBrandAssistant.Infrastructure/Agents/AgentOrchestrator.cs` | Rewrite: replace `IChatClientFactory` with `ISidecarClient`, simplify retry logic |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/AgentOrchestratorTests.cs` | Rewrite: replace `IChatClientFactory` mocks with `ISidecarClient` mocks |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/AgentCapabilityBaseTests.cs` | New: test the refactored base capability |

## Files to Delete

| File | Reason |
|------|--------|
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IChatClientFactory.cs` | Replaced by `ISidecarClient` |
| `src/PersonalBrandAssistant.Infrastructure/Services/ChatClientFactory.cs` | Replaced by `SidecarClient` (from section-02) |
| `src/PersonalBrandAssistant.Infrastructure/Services/TokenTrackingDecorator.cs` | Token tracking moves to sidecar event parsing |
| `src/PersonalBrandAssistant.Infrastructure/Services/AgentExecutionContext.cs` | No longer needed; token tracking is explicit via `TaskCompleteEvent` |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ChatClientFactoryTests.cs` | Testing deleted class |

## Files Unchanged

All five concrete capability files remain structurally the same. They only inherit from `AgentCapabilityBase`, which changes internally. The capabilities themselves (`WriterAgentCapability`, `SocialAgentCapability`, `RepurposeAgentCapability`, `EngagementAgentCapability`, `AnalyticsAgentCapability`) do not need modification unless their `BuildOutput` override references `UsageDetails`, which `WriterAgentCapability` does --- its signature must change to accept token counts from the sidecar event stream instead of `UsageDetails?`.

---

## Tests (Write First)

### AgentCapabilityBaseTests

New file: `tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/AgentCapabilityBaseTests.cs`

These tests validate the refactored base class behavior.

```csharp
namespace PersonalBrandAssistant.Infrastructure.Tests.Agents;

/// <summary>
/// Tests for the refactored AgentCapabilityBase that uses ISidecarClient.
/// Uses a concrete test subclass (TestAgentCapability) to test the abstract base.
/// </summary>
public class AgentCapabilityBaseTests
{
    // Test: ExecuteAsync sends rendered prompt to ISidecarClient.SendTaskAsync
    // - Mock ISidecarClient, IPromptTemplateService
    // - Verify SendTaskAsync called with combined system+task prompt string
    // - Verify the sessionId from AgentContext is forwarded

    // Test: ExecuteAsync collects text from ChatEvent stream into AgentOutput.GeneratedText
    // - Mock ISidecarClient to yield: ChatEvent("assistant", "Hello "), ChatEvent("assistant", "World"), TaskCompleteEvent
    // - Assert output.GeneratedText == "Hello World"

    // Test: ExecuteAsync extracts token usage from TaskCompleteEvent
    // - Mock ISidecarClient to yield: TaskCompleteEvent(sessionId, inputTokens: 100, outputTokens: 50)
    // - Assert output.InputTokens == 100, output.OutputTokens == 50

    // Test: ExecuteAsync returns failure on ErrorEvent from sidecar
    // - Mock ISidecarClient to yield: ErrorEvent("Something broke")
    // - Assert result.IsSuccess == false, ErrorCode.InternalError

    // Test: ExecuteAsync returns failure when stream yields no text
    // - Mock ISidecarClient to yield only TaskCompleteEvent with zero tokens
    // - Assert result.IsSuccess == false (empty response)

    // Test: ExecuteAsync propagates cancellation
    // - Pass a pre-cancelled CancellationToken
    // - Assert OperationCanceledException or failure result
}
```

### AgentOrchestratorTests (Rewrite)

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/AgentOrchestratorTests.cs`

The existing test file is rewritten. Key changes:

- Replace `Mock<IChatClientFactory> _chatClientFactory` with `Mock<ISidecarClient> _sidecarClient`
- The `CreateOrchestrator` helper passes `_sidecarClient.Object` instead of `_chatClientFactory.Object`
- Remove `DowngradesModelTier` test (no tier downgrade with sidecar)
- Remove `RetriesOnTransientError` test based on `HttpRequestException` (sidecar handles API retries; orchestrator only does budget + timeout)
- Keep and adapt: routing tests, budget tests, execution lifecycle tests, content creation tests, status query tests

```csharp
namespace PersonalBrandAssistant.Infrastructure.Tests.Agents;

/// <summary>
/// Tests for the refactored AgentOrchestrator that uses ISidecarClient.
/// </summary>
public class AgentOrchestratorTests
{
    // Replace: Mock<IChatClientFactory> with Mock<ISidecarClient>
    // Keep: Mock<ITokenTracker>, Mock<IPromptTemplateService>, Mock<IApplicationDbContext>,
    //       Mock<IWorkflowEngine>, Mock<INotificationService>, Mock<ILogger<AgentOrchestrator>>

    // Test: ExecuteAsync_RoutesWriterTask_ToWriterCapability (keep, adapt context creation)
    // Test: ExecuteAsync_RoutesSocialTask_ToSocialCapability (keep, adapt context creation)
    // Test: ExecuteAsync_RoutesEachType_ToCorrectCapability (keep, adapt context creation)
    // Test: ExecuteAsync_ReturnsValidationFailed_WhenOverBudget (keep as-is)
    // Test: ExecuteAsync_CreatesAgentExecution_BeforeCallingCapability (keep, adapt)
    // Test: ExecuteAsync_SetsExecutionToCompleted_OnSuccess (keep, adapt)
    // Test: ExecuteAsync_SetsExecutionToFailed_OnCapabilityFailure (keep, adapt)

    // Test: ExecuteAsync_CreatesContent_WhenOutputCreatesContentIsTrue (keep, adapt)
    // Test: ExecuteAsync_DoesNotCreateContent_WhenOutputCreatesContentIsFalse (keep, adapt)
    // Test: ExecuteAsync_SubmitsToWorkflow_WhenContentIsCreated (keep, adapt)

    // Test: ExecuteAsync_NoLongerReferencesIChatClientFactory
    // - Verify AgentOrchestrator constructor does not accept IChatClientFactory
    // - This is a compile-time guarantee; the test asserts the constructor parameter types

    // Test: ExecuteAsync_RecordsTokenUsage_FromSidecarEvents
    // - Capability returns AgentOutput with InputTokens/OutputTokens from TaskCompleteEvent
    // - Verify _tokenTracker.RecordUsageAsync called with correct values

    // Test: ExecuteAsync_FailsPermanently_OnTimeout_SendsNotification (keep, adapt)

    // REMOVED: ExecuteAsync_RetriesOnTransientError (sidecar handles retries)
    // REMOVED: ExecuteAsync_DowngradesModelTier_OnSecondTransientFailure (no tier downgrade)
    // REMOVED: ExecuteAsync_DoesNotRetry_OnNonTransientError (orchestrator no longer catches HttpRequestException)

    // Test: GetExecutionStatusAsync_ReturnsExecution_ById (keep as-is)
    // Test: GetExecutionStatusAsync_ReturnsNotFound_ForUnknownId (keep as-is)
}
```

### Capability-Specific Prompt Tests

These validate that each capability still generates the correct prompt template name. They do not need a new file; they can live in `AgentCapabilityBaseTests` or in a separate `CapabilityPromptTests.cs`.

```csharp
// Test: WriterAgentCapability generates prompt with AgentName="writer", DefaultTemplate="blog-post"
// Test: SocialAgentCapability generates prompt with AgentName="social", DefaultTemplate="post"
// Test: RepurposeAgentCapability generates prompt with AgentName="repurpose", DefaultTemplate="blog-to-thread"
// Test: Budget tracking still works with sidecar token events
//   - Execute a capability, verify ITokenTracker.RecordUsageAsync is called with
//     InputTokens/OutputTokens from the AgentOutput (which came from TaskCompleteEvent)
```

---

## Implementation Details

### 1. Modify AgentContext

Remove `IChatClient ChatClient` and `ModelTier ModelTier`. Add `ISidecarClient SidecarClient` and `string? SessionId`.

The record becomes:

```csharp
public record AgentContext
{
    public required Guid ExecutionId { get; init; }
    public required BrandProfilePromptModel BrandProfile { get; init; }
    public ContentPromptModel? Content { get; init; }
    public required IPromptTemplateService PromptService { get; init; }
    public required ISidecarClient SidecarClient { get; init; }
    public string? SessionId { get; init; }
    public required Dictionary<string, string> Parameters { get; init; }
}
```

This is a breaking change for `AgentContext` consumers, but the only consumers are the orchestrator (which we rewrite) and the capability base (which we rewrite).

### 2. Modify AgentOrchestrationOptions

Remove model/pricing configuration properties. These are no longer relevant since the sidecar (Claude Code) handles model selection internally.

```csharp
public class AgentOrchestrationOptions
{
    public const string SectionName = "AgentOrchestration";

    public decimal DailyBudget { get; init; } = 10.00m;
    public decimal MonthlyBudget { get; init; } = 100.00m;
    public string PromptsPath { get; init; } = "prompts";
    public int ExecutionTimeoutSeconds { get; init; } = 180;
    public bool LogPromptContent { get; init; }
}
```

Remove `MaxRetriesPerExecution` --- the orchestrator no longer retries at the API level. Sidecar/Claude Code handles that internally. Remove `DefaultModelTier`, `Models`, `Pricing`, and `ModelPricingOptions`.

### 3. Rewrite AgentCapabilityBase.ExecuteAsync

The core change: instead of building `ChatMessage` lists and calling `context.ChatClient.GetResponseAsync`, the capability now:

1. Renders the system prompt and task prompt using `IPromptTemplateService` (same as before)
2. Combines them into a single task string (the sidecar expects a single prompt, not a chat message list)
3. Calls `context.SidecarClient.SendTaskAsync(combinedPrompt, context.SessionId, ct)`
4. Iterates the `IAsyncEnumerable<SidecarEvent>` stream, collecting:
   - `ChatEvent` with text: append to a `StringBuilder` for the response text
   - `FileChangeEvent`: collect into metadata (file paths, change types)
   - `TaskCompleteEvent`: extract `InputTokens` and `OutputTokens`
   - `ErrorEvent`: return a failure `Result`
5. Calls `BuildOutput(collectedText, inputTokens, outputTokens)` (signature changes from `UsageDetails?` to explicit int parameters)

The `BuildOutput` method signature changes:

```csharp
protected virtual Result<AgentOutput> BuildOutput(string responseText, int inputTokens, int outputTokens)
```

`WriterAgentCapability` overrides this, so its override must be updated to match the new signature. The title extraction logic remains the same.

### 4. Rewrite AgentOrchestrator

**Constructor changes:**
- Remove `IChatClientFactory _chatClientFactory` parameter
- Add `ISidecarClient _sidecarClient` parameter
- Keep all other dependencies

**ExecuteAsync simplification:**
- Remove the retry loop with model tier downgrade. The sidecar handles API retries internally.
- Remove `DowngradeModelTier` and `IsTransientError` private methods entirely.
- Keep: budget check, execution creation, brand profile loading, timeout via `CancellationTokenSource`, execution status tracking, content creation, notification on failure.

**BuildAgentContext changes:**
- Instead of `_chatClientFactory.CreateClient(tier)`, pass `_sidecarClient` directly
- No `ModelTier` parameter needed
- Pass `sessionId: null` (each execution gets a fresh sidecar session; session reuse is a future optimization)

**Token recording changes:**
- `RecordUsageAsync` still works the same way --- it reads `AgentOutput.InputTokens` and `AgentOutput.OutputTokens`. The difference is these values now come from `TaskCompleteEvent` instead of `UsageDetails`.
- The model ID string passed to `ITokenTracker.RecordUsageAsync` changes from the tier name to a fixed string like `"sidecar"` since the orchestrator no longer knows which model the sidecar used internally.

**Remove `AgentExecutionContext`:** The static `AsyncLocal<Guid?>` was used by `TokenTrackingDecorator` to correlate token usage with executions. Since `TokenTrackingDecorator` is deleted and token tracking is now explicit via `TaskCompleteEvent`, this ambient context is no longer needed.

### 5. Update WriterAgentCapability.BuildOutput

Change the override signature from `BuildOutput(string responseText, UsageDetails? usage)` to `BuildOutput(string responseText, int inputTokens, int outputTokens)`. The title extraction logic stays identical.

### 6. Delete Obsolete Files

- `IChatClientFactory.cs` --- interface no longer used
- `ChatClientFactory.cs` --- implementation no longer used
- `TokenTrackingDecorator.cs` --- token tracking is explicit via sidecar events
- `AgentExecutionContext.cs` --- ambient context no longer needed
- `ChatClientFactoryTests.cs` --- tests for deleted class

### 7. DI Registration Updates (Reference Only)

DI changes are handled in section-12 (Docker/DI configuration). For reference, the orchestrator registration changes from:

```csharp
// BEFORE
services.AddSingleton<IChatClientFactory, ChatClientFactory>();

// AFTER (section-12 handles this)
// IChatClientFactory registration removed
// ISidecarClient registered as singleton (from section-02)
```

The `AgentOrchestrator` constructor injection automatically resolves `ISidecarClient` instead of `IChatClientFactory` once DI is wired correctly.

---

## Key Design Decisions

1. **Single prompt string vs chat messages:** The sidecar accepts a single task string, not a message array. The capability combines system + task prompts with a separator (e.g., double newline). This is fine because the sidecar feeds the combined string to Claude Code CLI's `-p` flag.

2. **No retry at orchestrator level:** Claude Code handles API retries, rate limiting, and model fallback internally. The orchestrator only handles budget enforcement and execution timeout. This significantly simplifies the orchestrator.

3. **Model ID for token tracking:** Since the orchestrator no longer knows which model the sidecar used, token cost calculation may need adjustment. The `ITokenTracker.RecordUsageAsync` receives `"sidecar"` as the model ID. Cost-per-token pricing configuration moves to the sidecar's own config or is tracked differently. This is acceptable because Claude Code CLI reports actual costs.

4. **Session management:** Each execution creates a fresh sidecar session (`sessionId: null`). Session reuse for multi-step workflows (e.g., outline then draft for the same content) is deferred to section-04 (content pipeline) which can pass a session ID through multiple capability calls.

5. **File change events:** `AgentCapabilityBase` collects `FileChangeEvent` records into `AgentOutput.Metadata` as serialized entries (e.g., key `"file_changes"` with a JSON array value). This enables section-04's blog writing flow to capture commit hashes and file paths.