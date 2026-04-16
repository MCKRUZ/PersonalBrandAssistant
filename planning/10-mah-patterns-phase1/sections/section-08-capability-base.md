# Section 08 — Capability Base

## Overview

This section updates `AgentCapabilityBase` and all five concrete capability classes to load system prompts from SKILL.md files via `ISkillRegistry` instead of `system.liquid` templates. It also threads `skill.ModelId` through to `SendTaskAsync`.

**Dependencies (must be complete before starting this section):**
- **section-04-skill-registry**: `ISkillRegistry`, `SkillDefinition`, `SkillRegistry` — the registry used to look up skills
- **section-06-sidecar-prompt-extensions**: `RenderRawAsync` on `IPromptTemplateService` and `modelId` parameter on `ISidecarClient.SendTaskAsync`
- **section-07-skill-files**: The five `SKILL.md` files under `Infrastructure/skills/` must exist and be parseable

**Blocks:** section-10-di-wiring

---

## Files to Modify

| File | Change |
|------|--------|
| `src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AgentCapabilityBase.cs` | Accept `ISkillRegistry`; add abstract `SkillName`; replace `system.liquid` call with skill load + render; pass `modelId` |
| `src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/WriterAgentCapability.cs` | Add `override string SkillName => "writer"` |
| `src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/SocialAgentCapability.cs` | Add `override string SkillName => "social"` |
| `src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/RepurposeAgentCapability.cs` | Add `override string SkillName => "repurpose"` |
| `src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/EngagementAgentCapability.cs` | Add `override string SkillName => "engagement"` |
| `src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AnalyticsAgentCapability.cs` | Add `override string SkillName => "analytics"` |

No new files are created in this section.

---

## Tests First

**Test file:** `tests/PersonalBrandAssistant.Infrastructure.Tests/Agents/Capabilities/AgentCapabilityBaseTests.cs`

Extend the existing test file (or create it if absent). All tests use a mock `ISkillRegistry`, mock `ISidecarClient` (updated with `modelId` param from section-06), and mock `IPromptTemplateService` with a `RenderRawAsync` implementation.

```
# Skill loading
ExecuteAsync_SkillFoundAndLevel2Loaded_UsesSkillBodyAsSystemPrompt
ExecuteAsync_SkillNotFound_ReturnsFailureResult
ExecuteAsync_SkillWithModelId_PassesModelIdToSidecar
ExecuteAsync_SkillWithNullModelId_PassesNullModelIdToSidecar

# Brand voice preserved
ExecuteAsync_SkillBodyContainsBrandVoiceBlock_IsRendered
ExecuteAsync_RenderedSystemPrompt_ContainsBrandVoiceContent

# Task prompt unchanged
ExecuteAsync_TaskPromptRendering_StillUsesLiquidTemplates

# Regression
ExecuteAsync_AllFiveCapabilities_ReturnSuccessWithMockedSidecar
```

Test stubs:

```csharp
public class AgentCapabilityBaseTests
{
    private readonly Mock<ISkillRegistry> _skillRegistry = new();
    private readonly Mock<ISidecarClient> _sidecarClient = new();
    private readonly Mock<IPromptTemplateService> _promptService = new();
    private readonly Mock<ILogger<TestCapability>> _logger = new();

    /// <summary>
    /// Verifies that when a skill is found, LoadLevel2 is called and the result is
    /// passed through RenderRawAsync to produce the systemPrompt for SendTaskAsync.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SkillFoundAndLevel2Loaded_UsesSkillBodyAsSystemPrompt() { }

    /// <summary>
    /// When GetSkillById returns null, ExecuteAsync must return a Failure result
    /// (not throw) with a message indicating the skill was not found.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SkillNotFound_ReturnsFailureResult() { }

    /// <summary>
    /// When SkillDefinition.ModelId is non-null, SendTaskAsync must be called
    /// with that exact modelId value.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SkillWithModelId_PassesModelIdToSidecar() { }

    /// <summary>
    /// When SkillDefinition.ModelId is null, SendTaskAsync must be called
    /// with null for modelId (not omitted, not defaulted).
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SkillWithNullModelId_PassesNullModelIdToSidecar() { }

    /// <summary>
    /// The Level 2 body returned by LoadLevel2 is passed verbatim to RenderRawAsync,
    /// ensuring {{ brand_voice_block }} in the SKILL.md body gets rendered.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SkillBodyContainsBrandVoiceBlock_IsRendered() { }

    /// <summary>
    /// The rendered system prompt (returned by RenderRawAsync) contains the injected
    /// brand voice content — not the literal {{ brand_voice_block }} token.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_RenderedSystemPrompt_ContainsBrandVoiceContent() { }

    /// <summary>
    /// Task prompt rendering still calls PromptService.RenderAsync(AgentName, templateName, variables)
    /// — this code path is unchanged by this section.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_TaskPromptRendering_StillUsesLiquidTemplates() { }

    /// <summary>
    /// For each of the five concrete capabilities, ExecuteAsync returns a success result
    /// when the registry returns a skill and the sidecar returns a ChatEvent + TaskCompleteEvent.
    /// </summary>
    [Theory]
    [InlineData("writer")]
    [InlineData("social")]
    [InlineData("repurpose")]
    [InlineData("engagement")]
    [InlineData("analytics")]
    public async Task ExecuteAsync_AllFiveCapabilities_ReturnSuccessWithMockedSidecar(string skillId) { }
}
```

The `TestCapability` helper is a minimal concrete subclass of `AgentCapabilityBase` used only in tests:

```csharp
/// <summary>Minimal concrete subclass for testing AgentCapabilityBase directly.</summary>
private sealed class TestCapability : AgentCapabilityBase
{
    public TestCapability(ISkillRegistry registry, ILogger logger) : base(registry, logger) { }
    public override AgentCapabilityType Type => AgentCapabilityType.Writer;
    public override ModelTier DefaultModelTier => ModelTier.Standard;
    protected override string AgentName => "writer";
    protected override string SkillName => "writer";
    protected override string DefaultTemplate => "blog-post";
    protected override bool CreatesContent => true;
}
```

Note: existing tests in `AgentCapabilityBaseTests` that assert `RenderAsync(AgentName, "system", ...)` is called must be updated — that call is removed by this section. The test should instead assert `GetSkillById`, `LoadLevel2`, and `RenderRawAsync` are called.

---

## Implementation

### AgentCapabilityBase Changes

**File:** `src/PersonalBrandAssistant.Infrastructure/Agents/Capabilities/AgentCapabilityBase.cs`

Three targeted changes to the existing class:

**1. Constructor — accept `ISkillRegistry`**

Add `ISkillRegistry skillRegistry` as a constructor parameter and store it as `private readonly ISkillRegistry _skillRegistry`. Each concrete capability already calls `base(logger)` — update all five call sites to `base(skillRegistry, logger)` when updating those files.

**2. Add abstract property `SkillName`**

```csharp
/// <summary>The skill ID used to look up this capability's SKILL.md definition.</summary>
protected abstract string SkillName { get; }
```

**3. Replace `system.liquid` call in `ExecuteAsync`**

The current line:
```csharp
var systemPrompt = await context.PromptService.RenderAsync(AgentName, "system", variables);
```

Replace with the three-step skill load sequence:

```csharp
var skill = _skillRegistry.GetSkillById(SkillName);
if (skill is null)
    return Result<AgentOutput>.Failure(ErrorCode.InternalError,
        $"Skill '{SkillName}' not found in registry. Ensure the SKILL.md file is present.");

var level2Body = _skillRegistry.LoadLevel2(SkillName);
var systemPrompt = await context.PromptService.RenderRawAsync(level2Body, variables);
```

**4. Pass `modelId` to `SendTaskAsync`**

The current call:
```csharp
await foreach (var evt in context.SidecarClient.SendTaskAsync(taskPrompt, systemPrompt, context.SessionId, ct))
```

Update to:
```csharp
await foreach (var evt in context.SidecarClient.SendTaskAsync(taskPrompt, systemPrompt, context.SessionId, skill.ModelId, ct))
```

Note: `skill` is now guaranteed non-null at this point (the null check above returns early).

No other changes to `ExecuteAsync`. The `BuildVariables`, `BuildOutput`, and error handling logic are untouched.

### Five Capability Classes

Each concrete capability needs one addition: `override string SkillName` and an updated constructor. The constructor change threads `ISkillRegistry` up to the base.

**WriterAgentCapability** (`WriterAgentCapability.cs`):
```csharp
public WriterAgentCapability(ISkillRegistry skillRegistry, ILogger<WriterAgentCapability> logger)
    : base(skillRegistry, logger) { }

protected override string SkillName => "writer";
```

**SocialAgentCapability** (`SocialAgentCapability.cs`):
```csharp
public SocialAgentCapability(ISkillRegistry skillRegistry, ILogger<SocialAgentCapability> logger)
    : base(skillRegistry, logger) { }

protected override string SkillName => "social";
```

**RepurposeAgentCapability** (`RepurposeAgentCapability.cs`):
```csharp
public RepurposeAgentCapability(ISkillRegistry skillRegistry, ILogger<RepurposeAgentCapability> logger)
    : base(skillRegistry, logger) { }

protected override string SkillName => "repurpose";
```

**EngagementAgentCapability** (`EngagementAgentCapability.cs`):
```csharp
public EngagementAgentCapability(ISkillRegistry skillRegistry, ILogger<EngagementAgentCapability> logger)
    : base(skillRegistry, logger) { }

protected override string SkillName => "engagement";
```

**AnalyticsAgentCapability** (`AnalyticsAgentCapability.cs`):
```csharp
public AnalyticsAgentCapability(ISkillRegistry skillRegistry, ILogger<AnalyticsAgentCapability> logger)
    : base(skillRegistry, logger) { }

protected override string SkillName => "analytics";
```

All other properties (`Type`, `DefaultModelTier`, `AgentName`, `DefaultTemplate`, `CreatesContent`, any `BuildOutput` overrides) are unchanged.

### DI Impact (handled in section-10, noted here for awareness)

The five capability classes now require `ISkillRegistry` injected. `ISkillRegistry` is registered as a Singleton in `Infrastructure/DependencyInjection.cs` (section-10). No DI changes are made in this section — the constructor update is enough for the DI container to resolve it automatically once section-10 registers `ISkillRegistry`.

---

## Key Invariants

- `SkillName` must match the `id` field of the corresponding `SKILL.md` file exactly (lowercase, no spaces). A mismatch causes a runtime failure with a clear error message.
- `RenderRawAsync` receives the same `variables` dict as the task prompt — this guarantees `{{ brand_voice_block }}` is injected. The SKILL.md bodies (section-07) must include `{{ brand_voice_block }}` for brand voice to appear.
- The `system.liquid` files are no longer called but are **not deleted** in Phase 1 — they remain as fallback reference.
- `modelId` is passed as `skill.ModelId`, which is nullable. `SendTaskAsync` accepts `string?` — pass it through directly without null-coalescing.

---

## Checklist

- [x] `AgentCapabilityBase` constructor accepts `ISkillRegistry`
- [x] Abstract `string SkillName` property declared on base
- [x] `ExecuteAsync` calls `GetSkillById`, returns failure if null
- [x] `ExecuteAsync` calls `LoadLevel2` then `RenderRawAsync` for system prompt
- [x] `ExecuteAsync` passes `skill.ModelId` to `SendTaskAsync`
- [x] `RenderAsync(AgentName, "system", ...)` call removed
- [x] All five capabilities: constructor updated to pass `ISkillRegistry` to base
- [x] All five capabilities: `override string SkillName` added with correct ID string
- [x] Tests: all 8 cases implemented and passing (41/41 capability tests green)
- [x] Existing tests updated: `SetupPrompts` now mocks skill registry + `RenderRawAsync`; "system" verify calls replaced with `GetSkillById`/`LoadLevel2`/`RenderRawAsync` verify
- [x] `MockSidecarClient` in test project has `modelId` parameter (confirmed compiles)
- [x] `dotnet build` zero errors, zero warnings

## Notes

- `TestCapability` in `AgentCapabilityBaseTests` uses `NullLogger.Instance` (not `Mock<ILogger<TestCapability>>`) because Moq cannot create interface proxies for generic args of private classes.
- DI integration tests (`AgentServiceRegistrationTests`, `AnalyticsEndpointTests`, etc.) are temporarily failing because `ISkillRegistry` is not yet registered — this is expected and resolved in section-10.
- New file: `tests/.../Agents/Capabilities/AgentCapabilityBaseTests.cs` (12 tests)
