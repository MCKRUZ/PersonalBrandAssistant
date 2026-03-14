# Phase 03 — AI Agent Orchestration: TDD Plan

Mirrors `claude-plan.md` structure. Tests use existing project conventions: xUnit, Moq, MockQueryable.Moq, AAA pattern, Testcontainers for integration.

## 3. Domain Entities

### AgentExecution
```csharp
// Test: AgentExecution.Create() sets Id (UUIDv7), Status=Pending, StartedAt
// Test: AgentExecution.MarkRunning() sets Status=Running
// Test: AgentExecution.Complete() sets Status=Completed, CompletedAt, Duration
// Test: AgentExecution.Fail() sets Status=Failed, Error, CompletedAt
// Test: AgentExecution.Cancel() sets Status=Cancelled, CompletedAt
// Test: AgentExecution.RecordUsage() sets token counts and cost
// Test: AgentExecution with null ContentId is valid (analytics/engagement)
// Test: AgentExecution status transitions — only valid transitions allowed
```

### AgentExecutionLog
```csharp
// Test: AgentExecutionLog.Create() sets Id, StepNumber, Timestamp
// Test: AgentExecutionLog.Content truncated to 2000 chars
// Test: AgentExecutionLog.Content is null when logging disabled
```

### Enums
```csharp
// Test: AgentCapabilityType has all 5 values (Writer, Social, Repurpose, Engagement, Analytics)
// Test: AgentExecutionStatus has 5 values (Pending, Running, Completed, Failed, Cancelled)
// Test: ModelTier has 3 values (Fast, Standard, Advanced)
```

## 4. Core Interfaces — no tests (interfaces only)

## 5. Agent Orchestrator

### Task Routing
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

### Retry & Fallback
```csharp
// Test: ExecuteAsync retries on AnthropicRateLimitException (transient)
// Test: ExecuteAsync does NOT retry on validation/prompt errors
// Test: ExecuteAsync downgrades model tier on second transient failure
// Test: ExecuteAsync fails permanently after max retries, sends notification
// Test: ExecuteAsync respects MaxRetriesPerExecution config
```

### Status Queries
```csharp
// Test: GetExecutionStatusAsync returns execution by ID
// Test: GetExecutionStatusAsync returns NotFound for unknown ID
// Test: ListExecutionsAsync filters by contentId
// Test: ListExecutionsAsync returns all when contentId is null
```

## 6. Sub-Agent Capabilities

### WriterAgentCapability
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

### SocialAgentCapability
```csharp
// Test: ExecuteAsync loads social/system.liquid and post.liquid templates
// Test: ExecuteAsync parses structured output (text, hashtags)
// Test: ExecuteAsync sets CreatesContent = true
// Test: DefaultModelTier is Fast for single posts
// Test: ExecuteAsync uses Standard tier for threads (via parameters)
```

### RepurposeAgentCapability
```csharp
// Test: ExecuteAsync loads source content from context
// Test: ExecuteAsync loads repurpose template based on parameters
// Test: ExecuteAsync returns multiple output items for multi-output transforms
// Test: ExecuteAsync sets CreatesContent = true
// Test: DefaultModelTier is Standard
```

### EngagementAgentCapability
```csharp
// Test: ExecuteAsync returns suggestions as structured output
// Test: ExecuteAsync sets CreatesContent = false
// Test: DefaultModelTier is Fast
```

### AnalyticsAgentCapability
```csharp
// Test: ExecuteAsync returns recommendations as structured output
// Test: ExecuteAsync sets CreatesContent = false
// Test: DefaultModelTier is Fast
```

## 7. Prompt Template System

### PromptTemplateService
```csharp
// Test: RenderAsync loads template from correct path (agentName/templateName.liquid)
// Test: RenderAsync injects brand_voice_block from shared/brand-voice.liquid
// Test: RenderAsync renders variables into template
// Test: RenderAsync caches parsed templates (second call doesn't re-read file)
// Test: RenderAsync throws when template file not found
// Test: ListTemplates returns all .liquid files for agent
// Test: Template variables use prompt view model DTOs (BrandProfilePromptModel, ContentPromptModel)
```

## 8. Token & Cost Tracking

### TokenTracker
```csharp
// Test: RecordUsageAsync updates AgentExecution with token counts
// Test: RecordUsageAsync calculates cost based on model pricing config
// Test: GetCostForPeriodAsync sums costs in date range
// Test: GetBudgetRemainingAsync returns daily budget minus today's spend
// Test: IsOverBudgetAsync returns true when daily budget exceeded
// Test: IsOverBudgetAsync returns true when monthly budget exceeded
// Test: IsOverBudgetAsync returns false when under both budgets
// Test: Cost calculation uses correct per-million-token rates per model
```

### ChatClientFactory
```csharp
// Test: CreateClient maps Fast to claude-haiku-4-5 model ID
// Test: CreateClient maps Standard to claude-sonnet-4-5-20250929 model ID
// Test: CreateClient maps Advanced to claude-opus-4-6 model ID
// Test: CreateClient wraps client with TokenTrackingDecorator
// Test: Model IDs come from configuration (not hardcoded)
```

## 9. Streaming API — Endpoint Tests

```csharp
// Test: POST /api/agents/stream returns text/event-stream content type
// Test: POST /api/agents/stream sets Cache-Control: no-store
// Test: POST /api/agents/stream emits token events during generation
// Test: POST /api/agents/stream emits complete event with executionId
// Test: POST /api/agents/stream emits error event on failure
// Test: POST /api/agents/execute returns 202 Accepted with executionId
// Test: POST /api/agents/execute?wait=true returns full result inline
// Test: GET /api/agents/executions/{id} returns execution status
// Test: GET /api/agents/executions?contentId={id} returns filtered list
// Test: GET /api/agents/usage returns token usage summary for date range
// Test: GET /api/agents/budget returns current budget status
```

## 10. EF Core Configuration

```csharp
// Test: AgentExecution persists and retrieves all fields correctly
// Test: AgentExecutionLog persists with FK to AgentExecution
// Test: AgentExecution has composite index on (Status, AgentType)
// Test: AgentExecution has index on ContentId
// Test: AgentExecutionLog has index on AgentExecutionId
// Test: DbContext includes AgentExecutions and AgentExecutionLogs DbSets
```

## 11. DI Registration

```csharp
// Test: IChatClientFactory resolves as singleton
// Test: IPromptTemplateService resolves as singleton
// Test: ITokenTracker resolves as scoped
// Test: IAgentOrchestrator resolves as scoped
// Test: All 5 IAgentCapability implementations resolve
```

## 13. Integration Tests (MockChatClient for CI)

```csharp
// Test: Full pipeline — orchestrator receives task → capability generates → content created → workflow transition
// Test: Budget enforcement — execution rejected when budget exceeded
// Test: Retry flow — transient error → retry with downgraded model → success
// Test: Streaming — SSE endpoint delivers token events via MockChatClient
// Test: Agent execution persisted with correct status transitions (Pending → Running → Completed)
```
