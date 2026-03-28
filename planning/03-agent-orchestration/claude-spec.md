# Phase 03 — AI Agent Orchestration: Complete Specification

## Overview

Hybrid AI agent layer for the Personal Brand Assistant. A central orchestrator handles simple tasks directly and spawns specialized sub-agents for complex work. Integrates with Claude API via the official Anthropic .NET SDK (v12.8.0). All five agent capabilities (WriterAgent, SocialAgent, RepurposeAgent, EngagementAgent, AnalyticsAgent) are implemented in this phase.

## Technology Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Claude SDK | Official `Anthropic` NuGet (v12.8.0) | IChatClient bridge, streaming, tool use, typed exceptions |
| Agent state | Database (AgentExecution entity) | Enables monitoring, crash recovery, dashboard visibility |
| Prompt storage | `prompts/` directory with Liquid templates | Git-versioned, easy to edit, Fluid library for rendering |
| Streaming | SSE via Minimal API (`text/event-stream`) | Industry standard, ~200ms first-token delivery |
| Orchestration | Custom orchestrator on IChatClient | Full control, Claude-native, no framework lock-in |

## Architecture

### Agent Orchestrator
- Central entry point for all AI-powered operations
- Task routing: classifies incoming tasks by complexity
  - **Simple tasks** (social post, title suggestion): handle inline with single Claude call
  - **Complex tasks** (blog writing, content repurposing): spawn dedicated sub-agent with agentic loop
- Manages agent lifecycle: create → execute → monitor → complete/fail
- Coordinates between multiple active agents
- Respects autonomy level from AutonomyConfiguration

### Sub-Agent Framework

**IAgentCapability interface** — all capabilities implement this:

| Agent | Purpose | Model Tier | Complexity |
|-------|---------|------------|------------|
| WriterAgent | Long-form content (blog posts, articles) | Sonnet/Opus | Complex (agentic loop) |
| SocialAgent | Social media post generation, threads | Haiku/Sonnet | Simple (single call) |
| RepurposeAgent | Content transformation (blog → thread, thread → posts) | Sonnet | Medium |
| EngagementAgent | Response suggestions, trend analysis | Haiku/Sonnet | Simple |
| AnalyticsAgent | Performance insights, recommendations | Haiku | Simple |

Each agent has:
- Own system prompt (Liquid template in `prompts/`)
- Tool set (optional, for agents needing structured output or multi-step reasoning)
- Output schema (validated before submission to workflow engine)
- Model selection (configurable per task type)

### Claude API Integration
- Official Anthropic .NET SDK via `AnthropicClient`
- IChatClient bridge for provider-agnostic abstractions
- Streaming via `Messages.CreateStreaming()` + `await foreach`
- Tool use for structured output extraction
- Model selection per task: Haiku ($1/5M) for classification/simple, Sonnet ($3/15M) for generation, Opus ($5/25M) for complex reasoning
- Retry with exponential backoff on rate limits (AnthropicRateLimitException)

### Prompt Management
- Liquid templates stored in `prompts/` directory, versioned with Git
- Fluid library for template rendering at runtime
- Brand voice injection into all content-generating prompts (from BrandProfile entity)
- Context assembly: template + brand voice + task-specific context + constraints
- Few-shot examples stored as structured data alongside templates
- IPromptTemplateService for loading, rendering, and listing versions

### Token & Cost Management
- Track tokens per request (input + output + cache tokens) via IChatClient decorator
- Calculate cost per operation using model-specific pricing
- AgentTokenUsage entity persists per-execution usage
- Daily/monthly cost budgets with configurable alerts
- Budget circuit breaker: warn → degrade model → hard stop
- Model downgrade strategy: Opus → Sonnet → Haiku when approaching limits
- ITokenTracker service interface for querying usage/costs

### Output Handling
- Structured output parsing via tool use (Claude returns structured JSON as tool call)
- Output validation against expected schema before workflow submission
- Retry with refined prompt on validation failure (max 3 attempts)
- Fallback to simpler model on repeated failures
- Agent outputs feed into IWorkflowEngine.TransitionAsync() with ActorType.Agent

### Agent State Persistence
- AgentExecution entity tracks: id, agentType, contentId, status (Pending/Running/Completed/Failed), startedAt, completedAt, inputTokens, outputTokens, cost, modelUsed, error
- Enables dashboard monitoring (Phase 06)
- Supports crash recovery: incomplete executions can be retried on startup
- AgentExecutionLog for detailed step-by-step reasoning trail

## Interfaces Consumed
- Content, BrandProfile domain entities from Phase 01
- IWorkflowEngine from Phase 02 (submit generated content to workflow)
- IApprovalService from Phase 02 (auto-submit for approval)
- AutonomyConfiguration from Phase 02 (determines agent autonomy)

## Interfaces Produced
- `IAgentOrchestrator` — submit tasks, check status, get results
- `IAgentCapability` — interface for all agent capabilities
- `IPromptTemplateService` — load and render prompt templates
- `ITokenTracker` — track and query usage/costs
- `IChatClientFactory` — create configured IChatClient instances per model tier
- Agent status API endpoints

## Existing Codebase Patterns to Follow
- Result<T> pattern for all service returns
- Interface in Application layer, implementation in Infrastructure layer
- DI registration in Infrastructure/DependencyInjection.cs
- MediatR commands/queries for API endpoint handling
- FluentValidation for input validation
- Structured logging via ILogger<T> (Serilog)
- Background services via BackgroundService + IServiceScopeFactory
- Unit tests: xUnit + Moq + MockQueryable
- Integration tests: Testcontainers + PostgreSQL

## Definition of Done
- Orchestrator routes tasks to appropriate handler (inline vs sub-agent)
- All five sub-agent capabilities implemented end-to-end
- Claude API integration working with streaming
- Prompt templates loading from prompts/ directory
- Token tracking records usage per request with cost calculation
- Cost budget enforcement with configurable limits
- Agent outputs validated and submitted to workflow engine
- Agent execution state persisted to database
- API endpoints for agent operations and status
- SSE streaming endpoint for real-time content generation
- Unit tests for orchestrator routing, each agent capability, token tracking
- Integration test for Claude API round-trip (can be mocked for CI)
