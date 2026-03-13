<!-- PROJECT_CONFIG
runtime: dotnet
test_command: dotnet test
END_PROJECT_CONFIG -->

<!-- SECTION_MANIFEST
section-01-domain-entities
section-02-enums-events
section-03-interfaces
section-04-ef-core-config
section-05-prompt-system
section-06-chat-client-factory
section-07-token-tracker
section-08-agent-capabilities
section-09-orchestrator
section-10-api-endpoints
section-11-di-config
END_MANIFEST -->

# Phase 03 — AI Agent Orchestration: Implementation Sections

## Dependency Graph

| Section | Depends On | Blocks | Parallelizable |
|---------|------------|--------|----------------|
| section-01-domain-entities | - | 03, 04, 07, 08, 09 | Yes |
| section-02-enums-events | - | 01, 03, 08, 09 | Yes |
| section-03-interfaces | 01, 02 | 05, 06, 07, 08, 09, 10 | No |
| section-04-ef-core-config | 01 | 07, 09, 11 | Yes (with 03) |
| section-05-prompt-system | 03 | 08, 09 | Yes |
| section-06-chat-client-factory | 03 | 08, 09 | Yes |
| section-07-token-tracker | 03, 04 | 09 | Yes (with 05, 06) |
| section-08-agent-capabilities | 03, 05, 06 | 09 | No |
| section-09-orchestrator | 03, 04, 07, 08 | 10 | No |
| section-10-api-endpoints | 09 | 11 | No |
| section-11-di-config | all | - | No |

## Execution Order

1. **Batch 1:** section-01-domain-entities, section-02-enums-events (parallel, no dependencies)
2. **Batch 2:** section-03-interfaces, section-04-ef-core-config (parallel after batch 1)
3. **Batch 3:** section-05-prompt-system, section-06-chat-client-factory, section-07-token-tracker (parallel after batch 2)
4. **Batch 4:** section-08-agent-capabilities (after batch 3)
5. **Batch 5:** section-09-orchestrator (after batch 4)
6. **Batch 6:** section-10-api-endpoints (after batch 5)
7. **Batch 7:** section-11-di-config (final, wires everything together)

## Section Summaries

### section-01-domain-entities
AgentExecution and AgentExecutionLog entities with lifecycle methods (Create, MarkRunning, Complete, Fail, Cancel, RecordUsage). Follows existing AuditableEntityBase/EntityBase patterns. Unit tests for all state transitions.

### section-02-enums-events
AgentCapabilityType, AgentExecutionStatus, ModelTier enums. AgentExecutionCompletedEvent and AgentExecutionFailedEvent domain events. Enum value tests.

### section-03-interfaces
All Application layer interfaces: IAgentOrchestrator, IAgentCapability, IPromptTemplateService, ITokenTracker, IChatClientFactory. Supporting records: AgentTask, AgentExecutionResult, AgentContext, AgentOutput. Prompt view model DTOs: BrandProfilePromptModel, ContentPromptModel.

### section-04-ef-core-config
EF Core configurations for AgentExecution and AgentExecutionLog. DbSet additions to IApplicationDbContext and ApplicationDbContext. Indexes on (Status, AgentType), ContentId, AgentExecutionId.

### section-05-prompt-system
PromptTemplateService implementation using Fluid library. Template loading from prompts/ directory, caching with ConcurrentDictionary, brand voice injection. File watcher for dev only. All Liquid template files. Unit tests for rendering and caching.

### section-06-chat-client-factory
ChatClientFactory creating IChatClient per ModelTier. Model ID mapping from configuration. TokenTrackingDecorator wrapping each client. Unit tests for tier mapping.

### section-07-token-tracker
TokenTracker implementation with cost calculation from pricing config. Budget enforcement (daily/monthly). RecordUsageAsync persists to AgentExecution. Unit tests for cost calculation and budget checks.

### section-08-agent-capabilities
All five capability implementations: WriterAgentCapability (agentic loop), SocialAgentCapability (single call), RepurposeAgentCapability (multi-step), EngagementAgentCapability (single call), AnalyticsAgentCapability (single call). Each returns AgentOutput with appropriate CreatesContent flag. Unit tests with mocked IChatClient.

### section-09-orchestrator
AgentOrchestrator implementation: task routing, budget check, execution lifecycle, timeout handling, retry with model downgrade, Content creation from AgentOutput, workflow submission. Unit tests for routing, retry, budget enforcement.

### section-10-api-endpoints
Minimal API endpoints: POST /api/agents/stream (SSE), POST /api/agents/execute (202 Accepted), GET /api/agents/executions/{id}, GET /api/agents/executions, GET /api/agents/usage, GET /api/agents/budget. SSE disconnect handling. Endpoint tests.

### section-11-di-config
DI registration in Infrastructure/DependencyInjection.cs. NuGet packages (Anthropic, Fluid.Core). appsettings.json configuration section. MockChatClient for CI. Integration test setup. Final wiring verification.
