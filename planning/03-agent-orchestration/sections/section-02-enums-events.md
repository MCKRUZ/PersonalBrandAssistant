# Section 02 -- Enums and Domain Events

## Overview

This section adds three new enums and two new domain events to the Domain layer for the AI Agent Orchestration feature. These types are consumed by the AgentExecution entity (section-01), interfaces (section-03), and all downstream orchestration and capability code.

This section has **no dependencies** and can be implemented in parallel with section-01-domain-entities.

## Background

The project already has established patterns for enums and domain events:

- **Enums** live in `src/PersonalBrandAssistant.Domain/Enums/` as single-line declarations (e.g., `public enum ContentStatus { Draft, Review, ... }`)
- **Domain events** live in `src/PersonalBrandAssistant.Domain/Events/` as sealed records implementing `IDomainEvent` (from `PersonalBrandAssistant.Domain.Common`)
- **Enum tests** are grouped in a single file at `tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs` verifying exact value counts and member names
- **Event tests** are in `tests/PersonalBrandAssistant.Domain.Tests/Events/DomainEventTests.cs` verifying properties and `IDomainEvent` implementation

All new types must follow these same conventions exactly.

## Tests First

All tests go in the existing test files to match project convention.

### Enum Tests

Add to `tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs`:

Three new test methods following the exact pattern of existing tests in that file:

- `AgentCapabilityType_HasExactly5Values` -- asserts 5 values and that each named member (`Writer`, `Social`, `Repurpose`, `Engagement`, `Analytics`) is present via `Assert.Contains`
- `AgentExecutionStatus_HasExactly5Values` -- asserts 5 values and that each named member (`Pending`, `Running`, `Completed`, `Failed`, `Cancelled`) is present
- `ModelTier_HasExactly3Values` -- asserts 3 values and that each named member (`Fast`, `Standard`, `Advanced`) is present

### Event Tests

Add to `tests/PersonalBrandAssistant.Domain.Tests/Events/DomainEventTests.cs`:

- `AgentExecutionCompletedEvent_ContainsExecutionIdAndContentId` -- creates the event with a random `executionId` and a nullable `contentId`, asserts both properties match
- `AgentExecutionFailedEvent_ContainsExecutionIdAndError` -- creates the event with a random `executionId` and an error string, asserts both properties match
- Update the existing `AllEventTypes_ImplementIDomainEvent` test to also assert that both new event types implement `IDomainEvent`

## Implementation

### New Enum Files

All three enums follow the existing single-line declaration pattern.

**File: `src/PersonalBrandAssistant.Domain/Enums/AgentCapabilityType.cs`**

Namespace: `PersonalBrandAssistant.Domain.Enums`

Single-line enum with five values representing the five agent capabilities:
- `Writer` -- long-form content generation (blog posts, articles)
- `Social` -- social media post and thread generation
- `Repurpose` -- content transformation between formats
- `Engagement` -- response suggestions and engagement analysis
- `Analytics` -- performance insights and recommendations

**File: `src/PersonalBrandAssistant.Domain/Enums/AgentExecutionStatus.cs`**

Namespace: `PersonalBrandAssistant.Domain.Enums`

Single-line enum with five values representing agent execution lifecycle states:
- `Pending` -- execution created, not yet started
- `Running` -- capability is actively executing
- `Completed` -- execution finished successfully
- `Failed` -- execution encountered an error
- `Cancelled` -- execution was cancelled (timeout or user-initiated)

**File: `src/PersonalBrandAssistant.Domain/Enums/ModelTier.cs`**

Namespace: `PersonalBrandAssistant.Domain.Enums`

Single-line enum with three values representing Claude model tiers:
- `Fast` -- Haiku, used for classification and simple tasks
- `Standard` -- Sonnet, used for content generation
- `Advanced` -- Opus, used for complex reasoning

### New Domain Event Files

Both events follow the existing sealed record pattern implementing `IDomainEvent`.

**File: `src/PersonalBrandAssistant.Domain/Events/AgentExecutionCompletedEvent.cs`**

Namespace: `PersonalBrandAssistant.Domain.Events`

Sealed record implementing `IDomainEvent` with parameters:
- `Guid ExecutionId` -- the AgentExecution that completed
- `Guid? ContentId` -- the content produced (null for analytics/engagement capabilities that do not create content)

**File: `src/PersonalBrandAssistant.Domain/Events/AgentExecutionFailedEvent.cs`**

Namespace: `PersonalBrandAssistant.Domain.Events`

Sealed record implementing `IDomainEvent` with parameters:
- `Guid ExecutionId` -- the AgentExecution that failed
- `string Error` -- error description

Both files need `using PersonalBrandAssistant.Domain.Common;` for the `IDomainEvent` interface.

## Files to Create

| File | Layer |
|------|-------|
| `src/PersonalBrandAssistant.Domain/Enums/AgentCapabilityType.cs` | Domain |
| `src/PersonalBrandAssistant.Domain/Enums/AgentExecutionStatus.cs` | Domain |
| `src/PersonalBrandAssistant.Domain/Enums/ModelTier.cs` | Domain |
| `src/PersonalBrandAssistant.Domain/Events/AgentExecutionCompletedEvent.cs` | Domain |
| `src/PersonalBrandAssistant.Domain/Events/AgentExecutionFailedEvent.cs` | Domain |

## Files to Modify

| File | Change |
|------|--------|
| `tests/PersonalBrandAssistant.Domain.Tests/Enums/EnumTests.cs` | Add 3 new test methods for the 3 new enums |
| `tests/PersonalBrandAssistant.Domain.Tests/Events/DomainEventTests.cs` | Add 2 new event test methods + update `AllEventTypes_ImplementIDomainEvent` |

## Verification

Run from the repository root:

```
dotnet test tests/PersonalBrandAssistant.Domain.Tests --filter "FullyQualifiedName~EnumTests|FullyQualifiedName~DomainEventTests"
```

All existing tests must continue to pass. The 3 new enum tests and 2 new event tests (plus the updated `AllEventTypes` test) must pass.

## Dependencies

- **None** -- this section has no dependencies and is in Batch 1
- **Blocks**: section-01-domain-entities (uses `AgentExecutionStatus`, `AgentCapabilityType`, `ModelTier`), section-03-interfaces (uses all enums), section-08-agent-capabilities (uses `AgentCapabilityType`, `ModelTier`), section-09-orchestrator (uses all enums and events)
