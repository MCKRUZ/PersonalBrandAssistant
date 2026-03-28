# Section 01 -- Domain Entities: AgentExecution and AgentExecutionLog

## Overview

This section adds two new domain entities to the `PersonalBrandAssistant.Domain` project that track AI agent execution lifecycle and detailed step-level logging. These entities are foundational -- nearly every other section in Phase 03 depends on them.

**What you are building:**

- `AgentExecution` -- Tracks each agent execution run (status, model used, token counts, cost, timing)
- `AgentExecutionLog` -- Audit log of individual steps within an execution (prompts, tool calls, completions)

**Dependencies:** None. This section has no prerequisites.

**Blocked by this section:** section-03 (interfaces), section-04 (EF Core config), section-07 (token tracker), section-08 (agent capabilities), section-09 (orchestrator).

---

## Background: Existing Patterns

Both entities follow established project conventions. Here are the base classes from `src/PersonalBrandAssistant.Domain/Common/EntityBase.cs`:

```csharp
public abstract class EntityBase
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; protected init; } = Guid.CreateVersion7();

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

public abstract class AuditableEntityBase : EntityBase, IAuditable
{
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

Key conventions observed in existing entities (e.g., `Content`, `WorkflowTransitionLog`):

- Private parameterless constructor for EF Core
- Static `Create()` factory method for entity construction
- `private init` setters on properties set during creation
- `private set` on properties that change via lifecycle methods
- UUIDv7 IDs generated automatically via `Guid.CreateVersion7()`
- Domain events raised via `AddDomainEvent()` for important state changes
- `IDomainEvent` is a marker interface: `public interface IDomainEvent;`

---

## Enums Required (from section-02)

This section references three enums that are built in section-02. They live in `src/PersonalBrandAssistant.Domain/Enums/`:

- `AgentCapabilityType` -- values: `Writer`, `Social`, `Repurpose`, `Engagement`, `Analytics`
- `AgentExecutionStatus` -- values: `Pending`, `Running`, `Completed`, `Failed`, `Cancelled`
- `ModelTier` -- values: `Fast`, `Standard`, `Advanced`

Since section-01 and section-02 are in the same parallel batch, either create stub enums yourself or coordinate. The enum files are trivial single-line definitions.

---

## Tests FIRST

All tests go in `tests/PersonalBrandAssistant.Domain.Tests/Entities/`.

### File: `AgentExecutionTests.cs`

Follow the same xUnit + AAA pattern as the existing `ContentTests.cs`. Test stubs:

```csharp
// Test: Create() sets Id (UUIDv7 -- non-empty Guid), Status=Pending, StartedAt to current time
// Test: Create() sets AgentType and ModelUsed from parameters
// Test: Create() with null ContentId is valid (analytics/engagement tasks have no content)
// Test: Create() with a ContentId stores it correctly
// Test: MarkRunning() sets Status to Running
// Test: MarkRunning() throws InvalidOperationException when Status is not Pending
// Test: Complete() sets Status=Completed, CompletedAt, and Duration (CompletedAt - StartedAt)
// Test: Complete() with outputSummary stores it
// Test: Complete() throws when Status is not Running
// Test: Fail() sets Status=Failed, Error message, and CompletedAt
// Test: Fail() throws when Status is not Running or Pending
// Test: Cancel() sets Status=Cancelled and CompletedAt
// Test: Cancel() throws when Status is already Completed or Failed
// Test: RecordUsage() sets InputTokens, OutputTokens, CacheReadTokens, CacheCreationTokens, Cost, ModelId
// Test: RecordUsage() can be called on any non-terminal status (usage is recorded during execution)
// Test: Status transitions -- only valid transitions allowed (Pending->Running, Running->Completed, Running->Failed, Running->Cancelled, Pending->Cancelled, Pending->Failed)
```

### File: `AgentExecutionLogTests.cs`

```csharp
// Test: Create() sets Id (UUIDv7), AgentExecutionId, StepNumber, StepType, Timestamp
// Test: Create() with Content longer than 2000 chars truncates to 2000
// Test: Create() with Content at exactly 2000 chars stores as-is
// Test: Create() with Content shorter than 2000 chars stores as-is
// Test: Create() with null Content (logging disabled) stores null
// Test: Create() sets TokensUsed from parameter
```

---

## Implementation Details

### File: `AgentExecution.cs`

**Path:** `src/PersonalBrandAssistant.Domain/Entities/AgentExecution.cs`

`AgentExecution` extends `AuditableEntityBase` (it needs `CreatedAt`/`UpdatedAt` for audit trails).

**Properties (all with `private set` or `private init`):**

| Property | Type | Set By | Notes |
|----------|------|--------|-------|
| ContentId | `Guid?` | Create | Null for analytics/engagement |
| AgentType | `AgentCapabilityType` | Create | Which capability ran |
| Status | `AgentExecutionStatus` | Lifecycle methods | Starts Pending |
| ModelUsed | `ModelTier` | Create | Tier selected for execution |
| ModelId | `string?` | RecordUsage | Exact model string, e.g. "claude-sonnet-4-5-20250929" |
| InputTokens | `int` | RecordUsage | |
| OutputTokens | `int` | RecordUsage | |
| CacheReadTokens | `int` | RecordUsage | |
| CacheCreationTokens | `int` | RecordUsage | |
| Cost | `decimal` | RecordUsage | Computed cost in USD |
| StartedAt | `DateTimeOffset` | Create | Set to UtcNow at creation |
| CompletedAt | `DateTimeOffset?` | Complete/Fail/Cancel | |
| Duration | `TimeSpan?` | Complete/Fail/Cancel | `CompletedAt - StartedAt` |
| Error | `string?` | Fail | Error message |
| OutputSummary | `string?` | Complete | Brief summary of output |

**Factory method:** `static AgentExecution Create(AgentCapabilityType agentType, ModelTier modelTier, Guid? contentId = null)`

- Sets `Status = AgentExecutionStatus.Pending`
- Sets `StartedAt = DateTimeOffset.UtcNow`
- All token/cost fields default to 0

**Lifecycle methods:**

- `MarkRunning()` -- Guard: Status must be `Pending`. Sets `Status = Running`.
- `Complete(string? outputSummary = null)` -- Guard: Status must be `Running`. Sets `Status = Completed`, `CompletedAt = UtcNow`, `Duration = CompletedAt - StartedAt`, `OutputSummary`.
- `Fail(string error)` -- Guard: Status must be `Running` or `Pending`. Sets `Status = Failed`, `Error`, `CompletedAt`, `Duration`.
- `Cancel()` -- Guard: Status must not be `Completed` or `Failed`. Sets `Status = Cancelled`, `CompletedAt`, `Duration`.
- `RecordUsage(string modelId, int inputTokens, int outputTokens, int cacheReadTokens, int cacheCreationTokens, decimal cost)` -- Sets all token/cost fields and `ModelId`. No status guard needed (usage can be recorded at any point during the lifecycle).

**Valid status transitions:**

| From | Allowed To |
|------|------------|
| Pending | Running, Failed, Cancelled |
| Running | Completed, Failed, Cancelled |
| Completed | (terminal) |
| Failed | (terminal) |
| Cancelled | (terminal) |

The lifecycle methods enforce these transitions by throwing `InvalidOperationException` on invalid attempts. Follow the same pattern as `Content.TransitionTo()`.

### File: `AgentExecutionLog.cs`

**Path:** `src/PersonalBrandAssistant.Domain/Entities/AgentExecutionLog.cs`

`AgentExecutionLog` extends `EntityBase` (no audit fields needed -- it has its own `Timestamp`).

**Properties (all `private init`):**

| Property | Type | Notes |
|----------|------|-------|
| AgentExecutionId | `Guid` | FK to AgentExecution |
| StepNumber | `int` | Sequential step within execution |
| StepType | `string` | "prompt", "tool_call", "tool_result", "completion" |
| Content | `string?` | Truncated to 2000 chars max; null when logging disabled |
| TokensUsed | `int` | Tokens consumed by this step |
| Timestamp | `DateTimeOffset` | Set to UtcNow at creation |

**Factory method:** `static AgentExecutionLog Create(Guid agentExecutionId, int stepNumber, string stepType, string? content, int tokensUsed)`

- Sets `Timestamp = DateTimeOffset.UtcNow`
- Truncates `content` to 2000 characters if longer: `content?.Length > 2000 ? content[..2000] : content`

**No lifecycle methods** -- this is a write-once audit record.

---

## File Summary

| Action | File Path |
|--------|-----------|
| Create | `src/PersonalBrandAssistant.Domain/Entities/AgentExecution.cs` |
| Create | `src/PersonalBrandAssistant.Domain/Entities/AgentExecutionLog.cs` |
| Create | `tests/PersonalBrandAssistant.Domain.Tests/Entities/AgentExecutionTests.cs` |
| Create | `tests/PersonalBrandAssistant.Domain.Tests/Entities/AgentExecutionLogTests.cs` |

---

## Verification

After implementation, run:

```bash
dotnet test tests/PersonalBrandAssistant.Domain.Tests/ --filter "FullyQualifiedName~AgentExecution"
```

All tests should pass. The entities should compile with no dependencies on other Phase 03 sections beyond the three enums (which are in section-02, the other parallel batch-1 item).

---

## Implementation Notes (Post-Build)

**Deviations from plan:**

1. **Enum stubs created early:** `AgentCapabilityType`, `AgentExecutionStatus`, `ModelTier` enums were created in this section (not section-02) since they are required for compilation. Section-02 will validate/extend these.
2. **RecordUsage input validation added (code review W-01):** Added `ArgumentException.ThrowIfNullOrWhiteSpace(modelId)` and `ArgumentOutOfRangeException.ThrowIfNegative` guards for all numeric parameters. 3 additional tests.
3. **Error string truncation added (code review W-02):** `Fail()` truncates error messages to 4000 chars. 1 additional test.

**Final test count:** 45 tests (41 planned + 4 from code review fixes). All passing.
