# 03 — AI Agent Orchestration

## Overview
Hybrid AI agent layer — a central orchestrator that handles simple tasks directly and spawns specialized sub-agents for complex work. Integrates with Claude API via the Anthropic .NET SDK.

## Requirements Reference
See `../requirements.md` for full project context and `../deep_project_interview.md` for design decisions.

Key interview insight: Hybrid architecture — "single agent for simple tasks, spawns specialized sub-agents for complex work (e.g., long-form blog writing)."

## Scope

### Agent Orchestrator
- Central entry point for all AI-powered operations
- Task routing logic: determines if a task is simple (handle inline) or complex (spawn sub-agent)
- Manages agent lifecycle (create, monitor, complete, fail)
- Coordinates between multiple active agents

### Sub-Agent Framework
- `IAgentCapability` interface — all capabilities implement this
- Capability types:
  - **WriterAgent** — Long-form content (blog posts, articles)
  - **SocialAgent** — Social media post generation, thread creation
  - **RepurposeAgent** — Content transformation (blog → thread, thread → posts)
  - **EngagementAgent** — Response suggestions, trend analysis
  - **AnalyticsAgent** — Performance insights, recommendations
- Each agent has its own system prompt, tool set, and output schema

### Claude API Integration
- Anthropic .NET SDK (`Anthropic.SDK` NuGet package)
- Message API with tool use for agent capabilities
- Streaming support for real-time content generation (feeds into dashboard)
- Model selection per task type (Haiku for simple, Sonnet for standard, Opus for complex)

### Prompt Management
- Prompt templates stored in code/config (not database — versioned with code)
- Brand voice injection into all content-generating prompts
- Context assembly: template + brand voice + task-specific context + constraints
- Few-shot examples for consistent output quality

### Token & Cost Management
- Track tokens per request (input + output)
- Track cost per operation (model-specific pricing)
- Daily/monthly cost budgets with alerts
- Cost-per-content-piece analytics
- Model downgrade strategy when approaching budget limits

### Output Handling
- Structured output parsing (JSON mode or tool use)
- Output validation before passing to workflow engine
- Retry with refined prompt on validation failure
- Fallback to simpler model on repeated failures

## Out of Scope
- Specific content creation logic (→ 05) — this provides the framework, 05 uses it
- Platform-specific formatting (→ 04)
- Dashboard for monitoring agents (→ 06)

## Key Decisions Needed During /deep-plan
1. Anthropic .NET SDK vs raw HTTP client for Claude API?
2. How to structure agent prompts — files, embedded resources, or database?
3. Agent state persistence — in-memory vs database (for long-running agents)?
4. How granular should token tracking be (per-request, per-agent, per-content-piece)?

## Dependencies
- **Depends on:** `01-foundation` (domain models, database), `02-workflow-engine` (agent outputs feed into workflow)
- **Blocks:** `05-content-engine` (uses agent framework for all AI generation)

## Interfaces Consumed
- Domain models from 01 (Content, BrandProfile)
- `IWorkflowEngine` from 02 (submit generated content to workflow)

## Interfaces Produced
- `IAgentOrchestrator` — submit tasks, check status, get results
- `IAgentCapability` — interface for all agent capabilities
- `IPromptBuilder` — assemble prompts with brand voice and context
- `ITokenTracker` — track and query usage/costs
- Agent status API endpoints

## Definition of Done
- Orchestrator routes tasks to appropriate handler (inline vs sub-agent)
- At least one sub-agent capability implemented end-to-end (e.g., SocialAgent)
- Claude API integration working with streaming
- Token tracking records usage per request
- Cost budget enforcement working
- Agent outputs validated and submitted to workflow engine
- Unit tests for orchestrator routing logic
- Integration test for Claude API round-trip
