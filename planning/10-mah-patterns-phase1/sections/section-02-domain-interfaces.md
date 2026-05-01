# Section 02 — Domain Interfaces

## Overview

This section defines all Application-layer contracts that the rest of Phase 1 depends on. No infrastructure code is written here — only interfaces, records, and enums in `PersonalBrandAssistant.Application`. Sections 03, 05, and 06 all block on this section completing first.

**Dependencies:** section-01-project-setup (NuGet packages added, csproj configured)
**Blocks:** section-03-skill-parser, section-05-context-budget-tracker, section-06-sidecar-prompt-extensions

---

## Tests First

**Test file:** `tests/PersonalBrandAssistant.Application.Tests/Models/SkillDefinitionTests.cs`

Write these tests before writing any Application-layer code (xUnit):

```
# Immutability
SkillDefinition_WithExpression_ProducesNewInstance
SkillDefinition_NoFilePath_CannotBeConstructed
  // Assert via reflection: typeof(SkillDefinition).GetProperty("FilePath") returns null

# Defaults
SkillDefinition_Tags_DefaultsToEmpty
SkillDefinition_AllowedTools_DefaultsToEmpty
SkillDefinition_ModelId_DefaultsToNull
```

No mocking needed — use real `SkillDefinition` instances.

---

## Files to Create

All new files in `src/PersonalBrandAssistant.Application/`.

### 1. `Common/Models/Skills/SkillDefinition.cs`

Immutable record. No `FilePath` — that is an Infrastructure concern stored in `SkillCacheEntry`.

```csharp
public record SkillDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required string SkillType { get; init; }
    public string? ModelId { get; init; }
    public required IReadOnlyList<string> AllowedTools { get; init; }
    public int SchemaVersion { get; init; }
}
```

Key invariants:
- `Id` is always lowercase and filesystem-safe — the parser normalizes it. The record trusts Infrastructure.
- `Tags` and `AllowedTools` default to empty lists when initialized by the parser; `required` here so callers must be explicit.
- `SchemaVersion` is stored but not validated here — validation happens in `SkillMetadataParser`.

### 2. `Common/Models/Skills/BudgetAssessment.cs`

Two types in this file:

```csharp
public enum BudgetDecision { Continue, Nudge, Stop }

public record BudgetAssessment(
    BudgetDecision Decision,
    string Reason,
    int TokensUsed,
    int TokensRemaining);
```

### 3. `Common/Models/Skills/ContextBudgetOptions.cs`

Options-pattern config record. Defaults match `appsettings.json` values from section-01:

```csharp
public class ContextBudgetOptions
{
    public const string SectionName = "ContextBudget";

    // Thresholds assume a 200k context window model.
    // Adjust if using a model with a different context window.
    public int NudgeThreshold { get; init; } = 80_000;
    public int StopThreshold { get; init; } = 180_000;
    public int HardMaxTokens { get; init; } = 200_000;
}
```

### 4. `Common/Interfaces/ISkillRegistry.cs`

```csharp
public interface ISkillRegistry
{
    SkillDefinition? GetSkillById(string id);
    IReadOnlyCollection<SkillDefinition> GetAllSkills();
    string LoadLevel2(string skillId);
}
```

`GetSkillById` returns null when not found — do not throw. `LoadLevel2` returns the raw Liquid template body string; rendering happens in `AgentCapabilityBase`, not here.

### 5. `Common/Interfaces/IContextBudgetTracker.cs`

```csharp
public interface IContextBudgetTracker
{
    void RecordTokens(string component, int tokens);
    BudgetAssessment AssessContinuation();
    int TotalTokens { get; }
}
```

This tracker is additive alongside `ITokenTracker`. `ITokenTracker` handles cost persistence. `IContextBudgetTracker` tracks context-window token consumption within a single scoped agent execution only.

---

## Files to Modify

### 6. `Common/Interfaces/ISidecarClient.cs`

Add `string? modelId` as a new parameter to `SendTaskAsync`, before `CancellationToken ct`:

Before:
```csharp
IAsyncEnumerable<SidecarEvent> SendTaskAsync(
    string task, string? systemPrompt, string? sessionId, CancellationToken ct);
```

After:
```csharp
IAsyncEnumerable<SidecarEvent> SendTaskAsync(
    string task, string? systemPrompt, string? sessionId, string? modelId, CancellationToken ct);
```

Update all existing call sites to pass `null` for `modelId` — the compiler will find them all.

### 7. `Common/Interfaces/IPromptTemplateService.cs`

Add one method for rendering a raw Liquid template string:

```csharp
Task<string> RenderRawAsync(string templateContent, Dictionary<string, object> variables);
```

Used by `AgentCapabilityBase` to render the SKILL.md Level 2 body through the same Fluid pipeline as file-based templates, preserving `{{ brand_voice_block }}` injection.

---

## Update MockSidecarClient

Find `tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockSidecarClient.cs` and update `SendTaskAsync` to include the new `modelId` parameter. The mock accepts and ignores it — just prevents compile failures across the existing test suite.

---

## What NOT to Do in This Section

- Do not implement `SkillMetadataParser` (section-03)
- Do not implement `ContextBudgetTracker` (section-05)
- Do not implement `SidecarClient` JSON changes (section-06)
- Do not implement `PromptTemplateService.RenderRawAsync` (section-06)
- Do not create SKILL.md files (section-07)
- Do not modify `AgentCapabilityBase` (section-08)

---

## Acceptance Checklist

- [x] `dotnet build` passes with zero errors after all changes
- [x] `MockSidecarClient` updated — all existing tests still compile
- [x] `SkillDefinitionTests` pass: immutability, no FilePath, correct defaults
- [x] `ISkillRegistry` has three methods with correct return types
- [x] `IContextBudgetTracker` has `RecordTokens`, `AssessContinuation`, `TotalTokens`
- [x] `BudgetDecision` enum has exactly three values: `Continue`, `Nudge`, `Stop`
- [x] `ContextBudgetOptions.SectionName` is `"ContextBudget"`
- [x] `ISidecarClient.SendTaskAsync` has `string? modelId` as fifth parameter
- [x] `IPromptTemplateService` has `RenderRawAsync` method signature

---

## As-Built Notes

**Implemented exactly as planned** with the following deviations:

### Deviations from Plan
1. **`PromptTemplateService.RenderRawAsync` stub added** — The plan said "do not implement RenderRawAsync (section-06)" but the interface change required the concrete class to compile. Added stub: `throw new NotImplementedException("RenderRawAsync implemented in section-06")`.

2. **`SidecarClient.SendTaskAsync` LogWarning added** — Auto-fix from code review. When `modelId is not null`, logs a warning that it's not yet wired. Aids debugging until section-06 wires the JSON payload.

3. **12 infrastructure service call sites updated** — All callers of `SendTaskAsync` had `null` inserted as the `modelId` argument (4th positional param). Files: `AgentCapabilityBase.cs`, `BlogChatService.cs` (3 calls), `BrandVoiceService.cs`, `ContentIdeaService.cs`, `ContentPipeline.cs`, `ArticleAnalyzer.cs`, `DailyContentOrchestrator.cs`, `ImagePromptService.cs`, `RepurposingService.cs`, `SocialEngagementService.cs`, `SocialInboxService.cs`, `TrendMonitor.cs`.

4. **10 test files updated for Moq arity** — Added `It.IsAny<string?>()` to all `Setup`/`Verify`/`Callback` patterns matching `SendTaskAsync`. `ConversationWindowingTests` required Callback generic params upgraded from 4 to 5 types.

### Test Results
- Application.Tests: 137/137 pass (5 new SkillDefinitionTests)
- Infrastructure.Tests: 645/698 pass — 53 pre-existing failures (confirmed via git stash baseline)
- Domain.Tests: 2 pre-existing failures
