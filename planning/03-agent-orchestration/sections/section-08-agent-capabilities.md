# Section 08 -- Agent Capabilities

## Overview

This section implements the five agent capability classes that handle the actual AI interaction logic. Each capability implements `IAgentCapability` (defined in section-03) and uses `IPromptTemplateService` (section-05) and `IChatClient` (section-06) to build prompts, call the Claude API, and return structured `AgentOutput` results. The orchestrator (section-09) dispatches to these capabilities based on `AgentCapabilityType`.

**Dependencies:**
- Section 01 (domain entities): `AgentExecution`, `AgentExecutionLog`
- Section 02 (enums/events): `AgentCapabilityType`, `ModelTier`
- Section 03 (interfaces): `IAgentCapability`, `AgentContext`, `AgentOutput`, `AgentTask`, `BrandProfilePromptModel`, `ContentPromptModel`
- Section 05 (prompt system): `IPromptTemplateService` for loading and rendering Liquid templates
- Section 06 (chat client factory): `IChatClientFactory` for creating `IChatClient` instances

## Key Concepts

### IAgentCapability Interface (from section-03)

Each capability implements:

```csharp
interface IAgentCapability
{
    AgentCapabilityType Type { get; }
    ModelTier DefaultModelTier { get; }
    Task<Result<AgentOutput>> ExecuteAsync(AgentContext context, CancellationToken ct);
}
```

### AgentOutput (from section-03)

Return type from capabilities containing: generated text content, metadata (title suggestions, hashtags, etc.), token usage, and a `CreatesContent` flag. Content-producing capabilities (Writer, Social, Repurpose) set `CreatesContent = true`; data-only capabilities (Engagement, Analytics) set it to `false`. The orchestrator (section-09) is responsible for creating `Content` entities and submitting to the workflow engine -- capabilities never do this themselves.

## Files to Create

| File | Project | Purpose |
|------|---------|---------|
| `src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/WriterAgentCapability.cs` | Infrastructure | Long-form content generation with agentic loop |
| `src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/SocialAgentCapability.cs` | Infrastructure | Social media post/thread generation |
| `src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/RepurposeAgentCapability.cs` | Infrastructure | Content format transformation |
| `src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/EngagementAgentCapability.cs` | Infrastructure | Response suggestions and engagement analysis |
| `src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AnalyticsAgentCapability.cs` | Infrastructure | Performance insights and recommendations |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/WriterAgentCapabilityTests.cs` | Tests | Unit tests for WriterAgent |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/SocialAgentCapabilityTests.cs` | Tests | Unit tests for SocialAgent |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/RepurposeAgentCapabilityTests.cs` | Tests | Unit tests for RepurposeAgent |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/EngagementAgentCapabilityTests.cs` | Tests | Unit tests for EngagementAgent |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/AnalyticsAgentCapabilityTests.cs` | Tests | Unit tests for AnalyticsAgent |

## Tests First

All tests mock `IChatClient` (from `Microsoft.Extensions.AI`) and `IPromptTemplateService`. The project uses xUnit, Moq, and the AAA pattern.

### WriterAgentCapabilityTests

```csharp
// Test: ExecuteAsync loads system.liquid and blog-post.liquid templates
// Test: ExecuteAsync injects brand voice into prompt
// Test: ExecuteAsync sends prompt to chat client and returns AgentOutput
// Test: ExecuteAsync sets CreatesContent = true
// Test: ExecuteAsync validates output has title and body
// Test: ExecuteAsync returns failure when output validation fails
// Test: DefaultModelTier is Standard
// Test: ExecuteAsync uses Advanced tier for articles > 2000 words (via parameters)
```

### SocialAgentCapabilityTests

```csharp
// Test: ExecuteAsync loads social/system.liquid and post.liquid templates
// Test: ExecuteAsync parses structured output (text, hashtags)
// Test: ExecuteAsync sets CreatesContent = true
// Test: DefaultModelTier is Fast for single posts
// Test: ExecuteAsync uses Standard tier for threads (via parameters)
```

### RepurposeAgentCapabilityTests

```csharp
// Test: ExecuteAsync loads source content from context
// Test: ExecuteAsync loads repurpose template based on parameters
// Test: ExecuteAsync returns multiple output items for multi-output transforms
// Test: ExecuteAsync sets CreatesContent = true
// Test: DefaultModelTier is Standard
```

### EngagementAgentCapabilityTests

```csharp
// Test: ExecuteAsync returns suggestions as structured output
// Test: ExecuteAsync sets CreatesContent = false
// Test: DefaultModelTier is Fast
```

### AnalyticsAgentCapabilityTests

```csharp
// Test: ExecuteAsync returns recommendations as structured output
// Test: ExecuteAsync sets CreatesContent = false
// Test: DefaultModelTier is Fast
```

## Implementation Details

### Common Capability Pattern

All five capabilities share a common execution flow:

1. Render system prompt via `IPromptTemplateService.RenderAsync(agentName, "system", variables)` -- inject brand voice
2. Render task-specific prompt via `IPromptTemplateService.RenderAsync(agentName, templateName, variables)`
3. Build `IChatClient` messages (system message + user message)
4. Call `IChatClient.CompleteAsync(messages, ct)`
5. Parse the response content into `AgentOutput`
6. Return `Result<AgentOutput>.Success(output)` or failure if validation fails

Variables dictionary always includes:
- `"brand"` -- `BrandProfilePromptModel` from `AgentContext`
- `"content"` -- `ContentPromptModel` from `AgentContext` (may be null)
- `"task"` -- `AgentTask.Parameters` dictionary

### WriterAgentCapability

**Complexity:** High (agentic loop pattern)
**Default Model:** `ModelTier.Standard` (Sonnet)
**Templates:** `writer/system.liquid`, `writer/blog-post.liquid`, `writer/article.liquid`
**CreatesContent:** `true`

Execution flow:
1. Load system prompt with brand voice
2. Determine task template from parameters (default: `"blog-post"`)
3. Send initial prompt to Claude
4. If Claude requests tools (outline, section expansion), execute tool calls and feed back -- agentic loop until final content
5. Validate output: must contain title and non-empty body
6. Return `AgentOutput` with `CreatesContent = true`

Model tier override: `"targetWordCount"` > 2000 signals Advanced tier preference.

### SocialAgentCapability

**Complexity:** Low (single call)
**Default Model:** `ModelTier.Fast` (Haiku), `Standard` for threads
**Templates:** `social/system.liquid`, `social/post.liquid`, `social/thread.liquid`
**CreatesContent:** `true`

Parse structured output: JSON with `text`, `hashtags`, optional `suggestedMedia`.

### RepurposeAgentCapability

**Complexity:** Medium (multi-step)
**Default Model:** `ModelTier.Standard` (Sonnet)
**Templates:** `repurpose/system.liquid`, `repurpose/blog-to-thread.liquid`, etc.
**CreatesContent:** `true`

Multi-output: parse JSON array for blog-to-social transforms. Each item in `AgentOutput.Items`.

### EngagementAgentCapability

**Complexity:** Low (single call)
**Default Model:** `ModelTier.Fast` (Haiku)
**Templates:** `engagement/system.liquid`, `engagement/response-suggestion.liquid`, `engagement/trend-analysis.liquid`
**CreatesContent:** `false`

### AnalyticsAgentCapability

**Complexity:** Low (single call)
**Default Model:** `ModelTier.Fast` (Haiku)
**Templates:** `analytics/system.liquid`, `analytics/performance-insights.liquid`
**CreatesContent:** `false`

## Existing Project Patterns

- **Namespace:** `PersonalBrandAssistant.Infrastructure.Agents.Capabilities`
- **Result pattern:** `Result<AgentOutput>.Success(output)` / `Result<AgentOutput>.Failure(ErrorCode.ValidationFailed, "...")`
- **Immutability:** `AgentOutput` is a record. Use `with` expressions.
- **Error handling:** Capabilities catch `IChatClient` exceptions and return `Result.Failure` -- never throw. Orchestrator handles retry.
- **Logging:** `ILogger<T>` per capability.

## DI Registration (section-11)

Each capability registered as scoped `IAgentCapability`. Orchestrator receives `IEnumerable<IAgentCapability>` and routes by `capability.Type`.
