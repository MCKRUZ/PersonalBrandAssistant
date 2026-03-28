# Section 04 -- EF Core Configuration for AgentExecution and AgentExecutionLog

## Overview

This section adds Entity Framework Core persistence configuration for the two new domain entities introduced in section-01: `AgentExecution` and `AgentExecutionLog`. It covers:

- Two new `IEntityTypeConfiguration<T>` classes with table mappings, property constraints, indexes, and relationships
- Two new `DbSet<T>` properties on `IApplicationDbContext` and `ApplicationDbContext`
- Infrastructure-level tests verifying index presence, foreign keys, and DbSet availability

## Dependencies

- **section-01-domain-entities** must be completed first. That section creates `AgentExecution` (extends `AuditableEntityBase`) and `AgentExecutionLog` (extends `EntityBase`) in the Domain layer.
- **section-02-enums-events** must be completed first. That section creates the `AgentCapabilityType`, `AgentExecutionStatus`, and `ModelTier` enums used as properties on the entities.

## Entity Shape Reference

These are the entity fields from section-01 that the EF Core configuration must map.

**AgentExecution** (extends `AuditableEntityBase` which provides `Id`, `CreatedAt`, `UpdatedAt`):

- `Guid? ContentId` -- nullable FK to Content (null for analytics/engagement tasks)
- `AgentCapabilityType AgentType` -- enum
- `AgentExecutionStatus Status` -- enum
- `ModelTier ModelUsed` -- enum
- `string? ModelId` -- exact model string, e.g. "claude-sonnet-4-5-20250929"
- `int InputTokens`
- `int OutputTokens`
- `int CacheReadTokens`
- `int CacheCreationTokens`
- `decimal Cost`
- `DateTimeOffset StartedAt`
- `DateTimeOffset? CompletedAt`
- `TimeSpan? Duration`
- `string? Error`
- `string? OutputSummary`

**AgentExecutionLog** (extends `EntityBase` which provides `Id`):

- `Guid AgentExecutionId` -- required FK to AgentExecution
- `int StepNumber`
- `string StepType` -- e.g. "prompt", "tool_call", "tool_result", "completion"
- `string? Content` -- truncated to 2000 chars; null when logging disabled
- `int TokensUsed`
- `DateTimeOffset Timestamp`

## Tests First

All tests go in the existing test file for DB configuration. The project already has a pattern for model-level configuration tests that uses a fake connection string context (no actual database needed).

### File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs`

Add the following test methods to the existing `ApplicationDbContextConfigurationTests` class. Each test creates an `ApplicationDbContext` with a fake Npgsql connection string and inspects the compiled EF Core model metadata.

```csharp
// Test: AgentExecution has composite index on (Status, AgentType)
// Verify entityType.GetIndexes() contains an index whose Properties include both "Status" and "AgentType"

// Test: AgentExecution has index on ContentId
// Verify entityType.GetIndexes() contains an index whose Properties include "ContentId"

// Test: AgentExecutionLog has index on AgentExecutionId
// Verify entityType.GetIndexes() contains an index whose Properties include "AgentExecutionId"

// Test: DbContext includes AgentExecutions DbSet
// Verify context.Model.FindEntityType(typeof(AgentExecution)) is not null

// Test: DbContext includes AgentExecutionLogs DbSet
// Verify context.Model.FindEntityType(typeof(AgentExecutionLog)) is not null

// Test: AgentExecution has FK relationship to Content (optional)
// Verify the navigation/FK from AgentExecution.ContentId -> Content with SetNull delete behavior

// Test: AgentExecutionLog has FK relationship to AgentExecution (required)
// Verify the navigation/FK from AgentExecutionLog.AgentExecutionId -> AgentExecution with Cascade delete behavior
```

Follow the exact pattern already established in the file: use `CreateInMemoryContext()`, call `context.Model.FindEntityType(typeof(T))`, and assert against indexes and properties.

## Implementation Details

### 1. AgentExecution EF Core Configuration

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/AgentExecutionConfiguration.cs`

Create a class `AgentExecutionConfiguration : IEntityTypeConfiguration<AgentExecution>` following the same patterns used by `ContentConfiguration` and `WorkflowTransitionLogConfiguration`.

Key configuration points:

- **Table name:** `"AgentExecutions"`
- **Primary key:** `Id` (inherited from `EntityBase`)
- **Required properties:** `AgentType`, `Status`, `ModelUsed`, `StartedAt`, `Cost`
- **Optional properties:** `ContentId`, `ModelId`, `CompletedAt`, `Duration`, `Error`, `OutputSummary`
- **String max lengths:** `ModelId` -- 100, `Error` -- 4000, `OutputSummary` -- 2000
- **Decimal precision:** `Cost` should use `.HasPrecision(18, 6)` for accurate sub-cent cost tracking
- **Token count defaults:** `InputTokens`, `OutputTokens`, `CacheReadTokens`, `CacheCreationTokens` should have `.HasDefaultValue(0)`
- **Enum storage:** EF Core stores enums as integers by default. Follow the same convention as existing configurations.
- **FK to Content:** Optional relationship. `HasOne<Content>().WithMany().HasForeignKey(e => e.ContentId).OnDelete(DeleteBehavior.SetNull)`
- **Composite index:** `builder.HasIndex(e => new { e.Status, e.AgentType })` -- for querying executions by status and agent type
- **ContentId index:** `builder.HasIndex(e => e.ContentId)` -- for looking up all executions for a piece of content
- **Concurrency token:** Add the PostgreSQL `xmin` concurrency token following the same pattern as `ContentConfiguration`:
  ```csharp
  builder.Property<uint>("xmin")
      .HasColumnType("xid")
      .ValueGeneratedOnAddOrUpdate()
      .IsConcurrencyToken();
  ```
- **Ignore DomainEvents:** `builder.Ignore(e => e.DomainEvents)` -- standard pattern across all configurations

### 2. AgentExecutionLog EF Core Configuration

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/AgentExecutionLogConfiguration.cs`

Create a class `AgentExecutionLogConfiguration : IEntityTypeConfiguration<AgentExecutionLog>`.

Key configuration points:

- **Table name:** `"AgentExecutionLogs"`
- **Primary key:** `Id` (inherited from `EntityBase`)
- **Required properties:** `AgentExecutionId`, `StepNumber`, `StepType`, `TokensUsed`, `Timestamp`
- **Optional properties:** `Content` (nullable -- null when prompt logging is disabled)
- **String max lengths:** `StepType` -- 50, `Content` -- 2000
- **Token default:** `TokensUsed` should have `.HasDefaultValue(0)`
- **FK to AgentExecution:** Required relationship. `HasOne<AgentExecution>().WithMany().HasForeignKey(l => l.AgentExecutionId).OnDelete(DeleteBehavior.Cascade)` -- deleting an execution cascades to its logs
- **Index on AgentExecutionId:** `builder.HasIndex(l => l.AgentExecutionId)` -- for querying all logs for a given execution
- **Ignore DomainEvents:** `builder.Ignore(l => l.DomainEvents)`

### 3. DbSet Additions

**File:** `src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs`

Add two new DbSet properties to the interface:

```csharp
DbSet<AgentExecution> AgentExecutions { get; }
DbSet<AgentExecutionLog> AgentExecutionLogs { get; }
```

**File:** `src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs`

Add the corresponding implementations:

```csharp
public DbSet<AgentExecution> AgentExecutions => Set<AgentExecution>();
public DbSet<AgentExecutionLog> AgentExecutionLogs => Set<AgentExecutionLog>();
```

No changes to `OnModelCreating` are needed -- the existing `ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly())` call automatically discovers the new `IEntityTypeConfiguration<T>` classes.

### 4. Test Entity Factory Update

**File:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Utilities/TestEntityFactory.cs`

Add factory methods for creating test instances of `AgentExecution` and `AgentExecutionLog`. These will be used by integration tests in this section and by later sections (section-07 TokenTracker, section-09 Orchestrator).

## File Summary

| File | Action |
|------|--------|
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/AgentExecutionConfiguration.cs` | Create |
| `src/PersonalBrandAssistant.Infrastructure/Data/Configurations/AgentExecutionLogConfiguration.cs` | Create |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IApplicationDbContext.cs` | Modify (add 2 DbSets) |
| `src/PersonalBrandAssistant.Infrastructure/Data/ApplicationDbContext.cs` | Modify (add 2 DbSets) |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Persistence/ApplicationDbContextConfigurationTests.cs` | Modify (add 5-7 test methods) |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Utilities/TestEntityFactory.cs` | Modify (add factory methods) |

## Verification

After implementation, run:

```bash
dotnet test tests/PersonalBrandAssistant.Infrastructure.Tests --filter "ApplicationDbContextConfigurationTests"
```

All new tests should pass. The existing tests in the same class must continue to pass.
