# Phase 03 Agent Orchestration — Research Findings

## Part 1: Codebase Analysis

### Project Structure & Clean Architecture
- **Domain Layer:** Pure business logic, enums, entities, events
- **Application Layer:** Use cases via MediatR commands/queries, interfaces, DTOs, validation pipeline
- **Infrastructure Layer:** Data access (EF Core + Npgsql), background services, external service implementations
- **API Layer:** Minimal API endpoints, middleware, exception handling

### Existing Service Patterns
All service contracts defined in `Application/Common/Interfaces/`. Implementations in `Infrastructure/Services/`.
- Scoped: IWorkflowEngine, IApprovalService, IContentScheduler, INotificationService, IPublishingPipeline
- Singleton: IDateTimeProvider, IEncryptionService
- HostedServices: DataSeeder, AuditLogCleanupService, ScheduledPublishProcessor, RetryFailedProcessor, WorkflowRehydrator, RetentionCleanupService

DI registration centralized in `Infrastructure/DependencyInjection.cs`.

### Domain Model — Key Entities
- **Content** — State machine with TransitionTo(), CapturedAutonomyLevel, domain events
- **BrandProfile** — Brand identity, tone, vocabulary, topics, persona
- **Platform** — OAuth tokens (encrypted), rate limit state
- **User** — email, display name, timezone
- **AutonomyConfiguration** — Hierarchical override resolution (global → platform → content-type → combo)

### Key Enums
- **ContentType:** BlogPost, SocialPost, Thread, VideoDescription
- **PlatformType:** TwitterX, LinkedIn, Instagram, YouTube
- **AutonomyLevel:** Manual, Assisted, SemiAuto, Autonomous
- **ActorType:** User, System, **Agent** (already exists!)
- **NotificationType:** ContentReadyForReview, ContentApproved, ContentRejected, ContentPublished, ContentFailed

### Workflow Engine Integration
```csharp
IWorkflowEngine:
  TransitionAsync(contentId, targetStatus, reason?, actor?, ct)
  GetAllowedTransitionsAsync(contentId, ct)
  ShouldAutoApproveAsync(contentId, ct)
```
Uses Stateless v5.20.1 for state machine. Auto-approval chains for Autonomous/SemiAuto levels. WorkflowTransitionLog for audit.

### Result<T> Pattern
Standard error handling: Success(value), Failure(ErrorCode, errors), NotFound, Conflict, ValidationFailure. ErrorCode enum: None, ValidationFailed, NotFound, Conflict, Unauthorized, InternalError.

### MediatR Pipeline Behaviors
1. LoggingBehavior — logs request name, sanitizes sensitive fields, measures execution time
2. ValidationBehavior — auto-validates via FluentValidation, returns Result.ValidationFailure()

### Background Service Patterns
- PeriodicTimer for polling intervals
- IServiceScopeFactory.CreateScope() per iteration
- try/catch with error logging and retry delays
- CancellationToken for graceful shutdown

### Testing Setup
- xUnit 2.9.3, Moq 4.20.72, MockQueryable.Moq 7.0.3
- Testcontainers (PostgreSQL) for integration tests
- AAA pattern with mock verification

### Existing NuGet Packages
MediatR 14.1.0, FluentValidation 12.1.1, EF Core 10.0.5, Npgsql 10.0.1, Stateless 5.20.1, Serilog 10.0.0, Swashbuckle 10.1.5

---

## Part 2: Web Research Findings

### 1. Anthropic .NET SDK

**Recommendation: Use official `Anthropic` NuGet package (v12.8.0)**

| Package | Status | Recommendation |
|---------|--------|----------------|
| `Anthropic` (official) | Active, v12.8.0 | **Use this** |
| `Anthropic.SDK` (community) | Active, v5.10.0 | Viable alternative |
| `Claudia` (Cysharp) | Archived Nov 2025 | Do not use |

Key capabilities:
- `AnthropicClient` with `Messages.Create()` and `Messages.CreateStreaming()`
- `IChatClient` integration via `Microsoft.Extensions.AI`
- Tool use / function calling via IChatClient builder
- Typed exceptions: AnthropicRateLimitException, etc.
- Targets .NET Standard 2.0+

Sources: [Official SDK GitHub](https://github.com/anthropics/anthropic-sdk-csharp), [NuGet](https://www.nuget.org/packages/Anthropic), [Docs](https://platform.claude.com/docs/en/api/sdks/csharp)

### 2. Agent Orchestration Patterns

**Recommendation: Custom orchestrator on IChatClient abstractions**

Three tiers identified:
1. **Microsoft.Extensions.AI (MEAI)** — Foundation abstraction layer (IChatClient). Use now.
2. **Microsoft Agent Framework** — Successor to Semantic Kernel + AutoGen, targeting GA Q1 2026. Watch but don't depend on yet.
3. **Custom Orchestrator** — Best fit for this project. Full control, Claude-native.

Key patterns:
- **Agentic Loop:** Send input + tools → execute tool calls → feed back → repeat until final response
- **Skills as Markdown:** Encode domain knowledge in YAML-frontmatter markdown files
- **Router Pattern:** Central orchestrator classifies tasks, routes to specialized sub-agents

Sources: [MS Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/), [MEAI Docs](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai)

### 3. Token Tracking & Cost Management

Every Claude API response includes `usage` object with input_tokens, output_tokens, cache tokens.

Current pricing (USD per million tokens):
| Model | Input | Output |
|-------|-------|--------|
| Claude Opus 4.6 | $5 | $25 |
| Claude Sonnet 4.6 | $3 | $15 |
| Claude Haiku 4.5 | $1 | $5 |

Implementation pattern:
- IChatClient decorator intercepts responses, extracts usage
- ITokenTracker service records per-request, per-agent, per-content
- Budget circuit breaker with configurable daily/monthly limits
- Model downgrade strategy when approaching limits

Sources: [Claude Pricing](https://platform.claude.com/docs/en/about-claude/pricing), [Usage API](https://platform.claude.com/docs/en/build-with-claude/usage-cost-api)

### 4. Prompt Template Management

**Recommendation: Liquid templates (via Fluid library) with file-based Git versioning**

Architecture:
- File-based storage with YAML frontmatter
- Version control via Git (prompts live in repo)
- Fluid library for Liquid template rendering (same engine as Semantic Kernel)
- IPromptTemplateService for loading and rendering

### 5. Streaming LLM Responses

**Recommendation: Server-Sent Events (SSE) via Minimal API with `text/event-stream`**

Key implementation details:
- Headers: Content-Type: text/event-stream, Cache-Control: no-cache
- Flush after every chunk
- SSE frame format: `data: <payload>\n\n`
- Angular client: fetch + ReadableStream (supports POST bodies)
- SSE delivers tokens within ~200ms vs 1-2+ seconds for complete response

---

## Key Decisions Summary

| Decision | Recommendation |
|----------|---------------|
| Claude SDK | Official `Anthropic` NuGet (v12.8.0) with IChatClient bridge |
| Orchestration | Custom orchestrator on IChatClient; evaluate MS Agent Framework at GA |
| Agent pattern | Agentic loop with router, skills-as-files |
| Token tracking | Decorator on IChatClient, internal cost calculator entity |
| Prompt templates | Liquid templates (Fluid library), file-based with Git versioning |
| Streaming | SSE via Minimal API, Angular fetch + ReadableStream |
| Cost optimization | Model tiering (Haiku/Sonnet/Opus by task), prompt caching, batch API |
