# Phase 03 — AI Agent Orchestration: Implementation Plan

## 1. What We're Building

A hybrid AI agent layer for the Personal Brand Assistant that provides:

1. **Agent Orchestrator** — Routes tasks to appropriate agent capabilities, manages lifecycle
2. **Five Sub-Agents** — WriterAgent, SocialAgent, RepurposeAgent, EngagementAgent, AnalyticsAgent
3. **Claude API Integration** — Official Anthropic .NET SDK with streaming and tool use
4. **Prompt Management** — Liquid templates loaded from `prompts/` directory
5. **Token & Cost Tracking** — Per-request usage recording, budget enforcement, model tiering
6. **Agent State Persistence** — Database-backed execution tracking for monitoring and recovery
7. **Streaming API** — SSE endpoint for real-time content generation delivery

The system integrates with the existing Phase 02 workflow engine — agent-generated content feeds into the content lifecycle (Draft → Review → ...) with `ActorType.Agent` attribution.

## 2. Architecture Overview

### Layer Placement

Following the existing Clean Architecture:

```
Domain Layer:
  Entities/     AgentExecution, AgentExecutionLog
  Enums/        AgentCapabilityType, AgentExecutionStatus, ModelTier
  Events/       AgentExecutionCompletedEvent, AgentExecutionFailedEvent

Application Layer:
  Common/Interfaces/
    IAgentOrchestrator.cs
    IAgentCapability.cs
    IPromptTemplateService.cs
    ITokenTracker.cs
    IChatClientFactory.cs

Infrastructure Layer:
  Agents/
    AgentOrchestrator.cs
    Capabilities/
      WriterAgentCapability.cs
      SocialAgentCapability.cs
      RepurposeAgentCapability.cs
      EngagementAgentCapability.cs
      AnalyticsAgentCapability.cs
  Services/
    PromptTemplateService.cs
    TokenTracker.cs
    ChatClientFactory.cs
  Data/Configurations/
    AgentExecutionConfiguration.cs
    AgentExecutionLogConfiguration.cs

Api Layer:
  Endpoints/
    AgentEndpoints.cs

prompts/ (project root)
  writer/       system.liquid, blog-post.liquid, article.liquid
  social/       system.liquid, post.liquid, thread.liquid
  repurpose/    system.liquid, blog-to-thread.liquid, thread-to-posts.liquid
  engagement/   system.liquid, response.liquid, trend.liquid
  analytics/    system.liquid, insights.liquid
  shared/       brand-voice.liquid
```

### Dependency Flow

```
API → Application (IAgentOrchestrator) → Infrastructure (AgentOrchestrator)
  → IChatClientFactory (creates AnthropicClient per model tier)
  → IPromptTemplateService (loads and renders Liquid templates)
  → ITokenTracker (records usage after each call)
  → IWorkflowEngine (submits generated content to workflow)
```

## 3. Domain Entities

### AgentExecution

Tracks each agent execution run. Fields:

```csharp
class AgentExecution : AuditableEntityBase
    Guid? ContentId          // Content being worked on (null for analytics/engagement)
    AgentCapabilityType AgentType
    AgentExecutionStatus Status  // Pending, Running, Completed, Failed, Cancelled
    ModelTier ModelUsed
    string? ModelId          // Exact model string used (e.g., "claude-sonnet-4-5-20250929")
    int InputTokens
    int OutputTokens
    int CacheReadTokens
    int CacheCreationTokens
    decimal Cost             // Actual cost computed after execution completes
    DateTimeOffset StartedAt
    DateTimeOffset? CompletedAt
    TimeSpan? Duration       // CompletedAt - StartedAt for quick querying
    string? Error
    string? OutputSummary    // Brief summary of what was generated
```

### AgentExecutionLog

Audit log for debugging and monitoring. Prompt/response content logging is configurable and disabled in production by default to avoid leaking sensitive data.

```csharp
class AgentExecutionLog : EntityBase
    Guid AgentExecutionId
    int StepNumber
    string StepType          // "prompt", "tool_call", "tool_result", "completion"
    string? Content          // Truncated to 2000 chars max; null when logging disabled
    int TokensUsed
    DateTimeOffset Timestamp
```

Configuration: `AgentOrchestration:LogPromptContent` (default: true in Development, false in Production).

### New Enums

```csharp
enum AgentCapabilityType
    Writer, Social, Repurpose, Engagement, Analytics

enum AgentExecutionStatus
    Pending, Running, Completed, Failed, Cancelled

enum ModelTier
    Fast,      // Haiku — classification, simple tasks
    Standard,  // Sonnet — content generation
    Advanced   // Opus — complex reasoning
```

## 4. Core Interfaces

### IAgentOrchestrator

The central entry point. Accepts a task description and optional content context, routes to appropriate capability, returns result.

```csharp
interface IAgentOrchestrator
    Task<Result<AgentExecutionResult>> ExecuteAsync(AgentTask task, CancellationToken ct)
    Task<Result<AgentExecution>> GetExecutionStatusAsync(Guid executionId, CancellationToken ct)
    Task<Result<AgentExecution[]>> ListExecutionsAsync(Guid? contentId, CancellationToken ct)
```

`AgentTask` is a discriminated record:

```csharp
record AgentTask(AgentCapabilityType Type, Guid? ContentId, Dictionary<string, string> Parameters)
```

`AgentExecutionResult` contains the execution ID, generated content (if any), and status.

### IAgentCapability

Each agent capability implements this. The orchestrator dispatches to the right capability based on `AgentCapabilityType`.

```csharp
interface IAgentCapability
    AgentCapabilityType Type { get; }
    ModelTier DefaultModelTier { get; }
    Task<Result<AgentOutput>> ExecuteAsync(AgentContext context, CancellationToken ct)
```

`AgentContext` bundles: the content entity (if applicable), brand profile, prompt template service, chat client, and task parameters.

`AgentOutput` contains: generated text content, metadata (title suggestions, hashtags, etc.), token usage, and a `CreatesContent` flag indicating whether the orchestrator should create a Content entity and submit to workflow. Capabilities that produce content (Writer, Social, Repurpose) set this to true; data-only capabilities (Engagement, Analytics) set it to false.

### IChatClientFactory

Creates configured `IChatClient` instances. Wraps the Anthropic SDK with the token-tracking decorator.

```csharp
interface IChatClientFactory
    IChatClient CreateClient(ModelTier tier)
    IChatClient CreateStreamingClient(ModelTier tier)
```

Internally maps ModelTier to model IDs:
- Fast → `claude-haiku-4-5`
- Standard → `claude-sonnet-4-5-20250929`
- Advanced → `claude-opus-4-6`

### IPromptTemplateService

Loads Liquid templates from the `prompts/` directory and renders them with variable context.

```csharp
interface IPromptTemplateService
    Task<string> RenderAsync(string agentName, string templateName, Dictionary<string, object> variables)
    string[] ListTemplates(string agentName)
```

Uses the Fluid library (NuGet: `Fluid.Core`) for Liquid template rendering. Templates have access to brand voice, content context, and task-specific variables.

### ITokenTracker

Records and queries token usage and costs.

```csharp
interface ITokenTracker
    Task RecordUsageAsync(Guid executionId, string modelId, int inputTokens, int outputTokens, int cacheReadTokens, int cacheCreationTokens, CancellationToken ct)
    Task<decimal> GetCostForPeriodAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    Task<decimal> GetBudgetRemainingAsync(CancellationToken ct)
    Task<bool> IsOverBudgetAsync(CancellationToken ct)
```

Cost calculation uses a static pricing table mapping model IDs to per-million-token rates. Budget limits are configurable via `appsettings.json`:

```
AgentOrchestration:DailyBudget (default: 10.00)
AgentOrchestration:MonthlyBudget (default: 100.00)
AgentOrchestration:DefaultModelTier (default: "Standard")
```

## 5. Agent Orchestrator Implementation

### Task Routing

The orchestrator receives an `AgentTask`, resolves the matching `IAgentCapability` from DI (registered as keyed services or a dictionary), and executes:

1. Check budget atomically (DB transaction) — if over budget, return failure with ErrorCode.ValidationFailed
2. Create `AgentExecution` entity with status Pending, persist to DB
3. Resolve the `IAgentCapability` for the task type
4. Determine model tier: task override → capability default → config default
5. Build `AgentContext` with content, brand profile, prompt service, chat client
6. Create `CancellationTokenSource` with per-execution timeout (configurable, default 180s)
7. Set execution status to Running
8. Call `capability.ExecuteAsync(context, linkedCt)` — capabilities return `AgentOutput` only (no Content creation)
9. On success: update execution with token usage, cost, output summary; set status Completed
10. On cancellation/timeout: set status Cancelled with reason
11. On failure: update execution with error; set status Failed; retry if transient (see below)
12. If `AgentOutput.CreatesContent` is true and autonomy allows: orchestrator creates Content entity via `Content.Create()` and submits to workflow engine via `IWorkflowEngine.TransitionAsync(contentId, ContentStatus.Review, "Agent-generated", ActorType.Agent)`

### Retry & Fallback Strategy

Only retry on **transient errors** (rate limits, 5xx, network timeouts). Validation errors and prompt/parsing failures fail immediately — retrying the same broken prompt wastes tokens.

On transient failure:
- Attempt 1: Same model tier, wait for retry-after header if rate limited
- Attempt 2: Downgrade one tier (Opus → Sonnet, Sonnet → Haiku)
- Attempt 3: Fail permanently, notify via INotificationService

Rate limits: catch `AnthropicRateLimitException`, respect retry-after header with exponential backoff.

## 6. Sub-Agent Capabilities

### WriterAgent (Complex — Agentic Loop)

Generates long-form content (blog posts, articles). Uses an agentic loop pattern:
1. Load system prompt from `prompts/writer/system.liquid` with brand voice injected
2. Load task-specific template (e.g., `blog-post.liquid`) with topic, keywords, constraints
3. Send initial prompt to Claude (Sonnet or Opus depending on complexity)
4. If Claude requests tools (outline generation, section expansion), execute and feed back
5. Continue until final content is produced
6. Validate output structure (has title, body, metadata)
7. Return `AgentOutput` with `CreatesContent = true` — orchestrator handles Content creation

Default model: Standard (Sonnet). Override to Advanced (Opus) for articles >2000 words.

### SocialAgent (Simple — Single Call)

Generates social media posts and threads. Single Claude call with structured output:
1. Load system prompt + post template with platform constraints (character limits, hashtag rules)
2. Inject brand voice and content context
3. Send to Claude (Haiku for simple posts, Sonnet for threads)
4. Parse structured output: text, hashtags, suggested media
5. Return `AgentOutput` with `CreatesContent = true` — orchestrator creates Content entities

Default model: Fast (Haiku) for single posts, Standard (Sonnet) for threads.

### RepurposeAgent (Medium — Multi-Step)

Transforms existing content into different formats:
1. Load source content from Content entity
2. Load repurpose template (e.g., `blog-to-thread.liquid`)
3. Send to Claude with source content + target format constraints
4. For multi-output (blog → multiple social posts), parse array output
5. Return `AgentOutput` with `CreatesContent = true` and multiple output items — orchestrator creates Content entities with `ParentContentId`

Default model: Standard (Sonnet).

### EngagementAgent (Simple — Single Call)

Generates response suggestions and analyzes engagement trends:
1. Load engagement template with conversation context
2. Send to Claude (Haiku for response suggestions, Sonnet for trend analysis)
3. Return suggestions as structured output (no Content entity creation)

Default model: Fast (Haiku).

### AnalyticsAgent (Simple — Single Call)

Generates performance insights and content recommendations:
1. Load analytics template with performance data
2. Send to Claude (Haiku) for insight generation
3. Return structured recommendations

Default model: Fast (Haiku).

## 7. Prompt Template System

### Directory Structure

```
prompts/
  shared/
    brand-voice.liquid         # Injected into all content-generating prompts
  writer/
    system.liquid              # System prompt for WriterAgent
    blog-post.liquid           # Blog post generation task
    article.liquid             # Long-form article task
  social/
    system.liquid
    post.liquid                # Single social post
    thread.liquid              # Multi-post thread
  repurpose/
    system.liquid
    blog-to-thread.liquid
    thread-to-posts.liquid
    blog-to-social.liquid
  engagement/
    system.liquid
    response-suggestion.liquid
    trend-analysis.liquid
  analytics/
    system.liquid
    performance-insights.liquid
```

### Template Variables

Templates receive **prompt view model DTOs** (not raw entities) to control data exposure:
- `brand` — `BrandProfilePromptModel` (name, persona, tone, vocabulary, topics — no internal IDs or audit fields)
- `content` — `ContentPromptModel` (title, body, type, status — no workflow internals)
- `platforms` — Target platform constraints (char limits, formatting rules)
- `task` — Task-specific parameters from AgentTask.Parameters

### Rendering Pipeline

1. `IPromptTemplateService.RenderAsync("writer", "blog-post", variables)`
2. Service loads `prompts/writer/blog-post.liquid` from disk
3. Also loads `prompts/shared/brand-voice.liquid` and makes it available as `brand_voice_block`
4. Renders with Fluid library, returns assembled prompt string

Template caching: cache parsed templates in memory (ConcurrentDictionary). File-watcher invalidation enabled only in Development environment; production loads templates once at startup (restart to deploy prompt changes).

## 8. Token & Cost Tracking

### Per-Request Flow

1. `ChatClientFactory` wraps each `IChatClient` with a `TokenTrackingDecorator`
2. Decorator intercepts every `CompleteAsync` / streaming response
3. Extracts `usage` from response metadata (input_tokens, output_tokens, cache tokens)
4. Calls `ITokenTracker.RecordUsageAsync()` with execution ID and model ID
5. Token tracker persists to `AgentExecution` entity and checks budget

### Cost Calculation

Static pricing table (configurable in appsettings for future updates):

```
AgentOrchestration:Pricing:claude-haiku-4-5:InputPerMillion = 1.00
AgentOrchestration:Pricing:claude-haiku-4-5:OutputPerMillion = 5.00
AgentOrchestration:Pricing:claude-sonnet-4-5-20250929:InputPerMillion = 3.00
AgentOrchestration:Pricing:claude-sonnet-4-5-20250929:OutputPerMillion = 15.00
AgentOrchestration:Pricing:claude-opus-4-6:InputPerMillion = 5.00
AgentOrchestration:Pricing:claude-opus-4-6:OutputPerMillion = 25.00
```

### Budget Enforcement

Before each execution, orchestrator calls `ITokenTracker.IsOverBudgetAsync()`. If over:
- Log warning
- Return `Result.Failure(ErrorCode.ValidationFailed, "Daily/monthly budget exceeded")`
- Send notification via INotificationService

## 9. Streaming API

### SSE Endpoint

`POST /api/agents/stream` — Initiates an agent execution and streams the response as SSE.

Request body: `AgentTask` (type, contentId, parameters).

Response: `text/event-stream` with events:
- `data: {"type": "token", "text": "..."}` — Content token
- `data: {"type": "status", "status": "running"}` — Status update
- `data: {"type": "usage", "inputTokens": N, "outputTokens": N}` — Token usage
- `data: {"type": "complete", "executionId": "..."}` — Completion
- `data: {"type": "error", "message": "..."}` — Error

Implementation: Minimal API endpoint writes to `HttpContext.Response` with `text/event-stream` content type, flushing after each chunk. Uses `AnthropicClient.Messages.CreateStreaming()` internally.

Headers: `Content-Type: text/event-stream`, `Cache-Control: no-store`, `X-Accel-Buffering: no`.

Disconnect handling: a `finally` block ensures execution status is updated to Failed/Cancelled if the client disconnects mid-stream. Token usage is recorded from the last known usage snapshot.

### Non-Streaming Endpoint

`POST /api/agents/execute` — Asynchronous execution. Returns `202 Accepted` with `{ executionId }` for long-running tasks (WriterAgent, RepurposeAgent). Client polls `GET /api/agents/executions/{id}` for status. Short tasks (Haiku-based: Social, Engagement, Analytics) can use `?wait=true` query param to block until completion and return the full result inline.

### Status & History Endpoints

- `GET /api/agents/executions/{id}` — Get execution status
- `GET /api/agents/executions?contentId={id}` — List executions for content
- `GET /api/agents/usage?from={date}&to={date}` — Token usage summary
- `GET /api/agents/budget` — Current budget status

## 10. EF Core Configuration

### New DbSets

Add to `IApplicationDbContext`:
- `DbSet<AgentExecution> AgentExecutions`
- `DbSet<AgentExecutionLog> AgentExecutionLogs`

### Indexes

- `AgentExecution`: composite index on `(Status, AgentType)`, index on `ContentId`
- `AgentExecutionLog`: index on `AgentExecutionId`

## 11. DI Registration

New registrations in `Infrastructure/DependencyInjection.cs`:

- `services.AddSingleton<IChatClientFactory, ChatClientFactory>()` — Singleton since AnthropicClient is thread-safe
- `services.AddSingleton<IPromptTemplateService, PromptTemplateService>()` — Singleton with cached templates
- `services.AddScoped<ITokenTracker, TokenTracker>()`
- `services.AddScoped<IAgentOrchestrator, AgentOrchestrator>()`
- Register each `IAgentCapability` implementation as scoped
- NuGet packages to add: `Anthropic`, `Fluid.Core`

## 12. Configuration (appsettings.json)

```
AgentOrchestration:
  ApiKey: ""                           # Anthropic API key (User Secrets in dev)
  DailyBudget: 10.00
  MonthlyBudget: 100.00
  DefaultModelTier: "Standard"
  Models:
    Fast: "claude-haiku-4-5"
    Standard: "claude-sonnet-4-5-20250929"
    Advanced: "claude-opus-4-6"
  Pricing:
    claude-haiku-4-5:
      InputPerMillion: 1.00
      OutputPerMillion: 5.00
    claude-sonnet-4-5-20250929:
      InputPerMillion: 3.00
      OutputPerMillion: 15.00
    claude-opus-4-6:
      InputPerMillion: 5.00
      OutputPerMillion: 25.00
  PromptsPath: "prompts"
  MaxRetriesPerExecution: 3
  ExecutionTimeoutSeconds: 180
  LogPromptContent: true        # Set to false in Production
```

## 13. Testing Strategy

### Unit Tests
- **AgentOrchestrator** — mock IAgentCapability, verify routing, budget checks, retry logic
- **Each Capability** — mock IChatClient, verify prompt assembly, output parsing, Content creation
- **PromptTemplateService** — verify template loading, rendering, variable injection
- **TokenTracker** — verify cost calculation, budget enforcement
- **ChatClientFactory** — verify model tier mapping

### Integration Tests (require API key or mock)
- **Claude API round-trip** — send simple prompt, verify response parsing
- **Streaming** — verify SSE frame delivery
- **Full pipeline** — agent generates content → submits to workflow → audit trail correct

### Mocking Claude for CI
Create `MockChatClient` implementing `IChatClient` that returns canned responses. Register in test DI instead of real AnthropicClient. This enables CI to run without an API key.
