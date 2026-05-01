# Section 08 Code Review — Skill Registry Wiring into AgentCapabilityBase

**Reviewer:** code-reviewer (automated)
**Verdict:** Approve with minor items

---

## IMPORTANT — Redundant null-guard followed by throwing call

**File:** `AgentCapabilityBase.cs:35-40`

```csharp
var skill = _skillRegistry.GetSkillById(SkillName);
if (skill is null)
    return Result<AgentOutput>.Failure(...);

var level2Body = _skillRegistry.LoadLevel2(SkillName);  // throws KeyNotFoundException if not found
```

`GetSkillById` returns null when not found. The code correctly null-checks and returns a `Result.Failure`. However, `LoadLevel2` throws `KeyNotFoundException` for the same condition. Since the null check on line 36 guarantees the skill exists before reaching line 40, this is **safe at runtime** — the throw path in `LoadLevel2` is unreachable after the guard.

But there is a subtle contract coupling: the code assumes that if `GetSkillById` returns non-null, `LoadLevel2` will not throw. This holds today because both read from the same `_skills` dictionary. If the registry ever becomes async/invalidatable (e.g., hot-reload), this assumption breaks. Not a blocker for this section, but worth noting.

**Risk:** Low. Current implementation is sound.
**Recommendation:** No action needed now. If skill hot-reload lands later, consider a single `TryLoadLevel2` that returns `string?`.

---

## MINOR — Test helper `SetupSkillRegistry` duplication across 6 test files

Each of the 5 capability test files and the new `AgentCapabilityBaseTests.cs` contain near-identical `SetupSkillRegistry` / `SetupPrompts` helpers that construct a `SkillDefinition` with dummy values and wire up mock returns. This is ~15 lines duplicated 6 times.

**Recommendation:** Consider extracting a shared `TestSkillRegistryHelper` or a `SkillDefinitionBuilder` in the test project. Not blocking — just noise reduction for future sections that will keep touching these files.

---

## MINOR — Theory test uses shared `_sidecarClient` and `_promptService` from class fields alongside local `registry`

**File:** `AgentCapabilityBaseTests.cs:381-413`

The `ExecuteAsync_AllFiveCapabilities_ReturnSuccessWithMockedSidecar` theory creates a local `Mock<ISkillRegistry>` per iteration, but reuses the class-level `_sidecarClient` and `_promptService`. The `_promptService.Setup` calls inside the theory body overwrite any prior setups on the shared mock. This works because xUnit creates a new class instance per test method, but it's confusing to read — the local `registry` suggests the test is self-contained when it actually depends on shared state.

**Recommendation:** Either use all-local mocks in the theory (consistent pattern), or add a brief comment explaining the shared mock reuse is safe due to xUnit's per-test instantiation.

---

## Correctness Verification

### Skill load sequence: PASS
`GetSkillById` -> null check -> `LoadLevel2` -> `RenderRawAsync`. Matches spec exactly. The null guard returns `Result.Failure` with `ErrorCode.InternalError` and a descriptive message. The `LoadLevel2` call correctly passes `SkillName` (not `AgentName`), and the result feeds directly into `RenderRawAsync` with the same `variables` dictionary.

### ModelId threading: PASS
`skill.ModelId` is `string?` on `SkillDefinition` (line 11). It is passed directly to `SendTaskAsync` with no null-coalescing — exactly as spec requires. The sidecar receives null when no model override is configured. Both paths (null and non-null) are tested with callback capture assertions.

### system.liquid removal: PASS
The old `context.PromptService.RenderAsync(AgentName, "system", variables)` call is gone. No new `RenderAsync` call with `"system"` exists. Task prompt still uses the original `RenderAsync(AgentName, templateName, variables)` path — correctly unchanged.

### SkillName values: PASS
All five capabilities define `SkillName` matching their `AgentName`:
- writer -> "writer"
- social -> "social"
- repurpose -> "repurpose"
- engagement -> "engagement"
- analytics -> "analytics"

This is correct if the SKILL.md files use these as their `id:` field. The theory test in `AgentCapabilityBaseTests` validates all five can execute successfully.

### TestCapability helper: PASS
`private sealed class TestCapability` is minimal, hardcodes "writer" for both `AgentName` and `SkillName`, uses concrete type overrides for all abstract members. Using `NullLogger.Instance` is appropriate for a test double — no logger verification needed on the base class tests.

---

## Test Coverage Assessment

### New tests (AgentCapabilityBaseTests.cs) — 8 test cases:
| Test | What it covers |
|------|---------------|
| SkillFoundAndLevel2Loaded | Happy path: full load sequence verified with `Verify` calls |
| SkillNotFound | Null skill returns failure result |
| SkillWithModelId | Non-null modelId forwarded to sidecar |
| SkillWithNullModelId | Null modelId forwarded as null |
| SkillBodyContainsBrandVoiceBlock | Level2 body with Liquid vars passed to RenderRawAsync |
| RenderedSystemPrompt_ContainsBrandVoiceContent | Rendered system prompt forwarded to sidecar |
| TaskPromptRendering_StillUsesLiquidTemplates | Task prompt path unchanged |
| AllFiveCapabilities (theory x5) | Regression: all concrete types execute successfully |

### Existing test updates — no coverage loss:
- All 5 capability test files updated `SetupPrompts` to use skill registry mocks instead of `RenderAsync("system")`.
- Verification assertions updated from `RenderAsync(agent, "system", ...)` to `GetSkillById` + `LoadLevel2` + `RenderRawAsync`.
- The `WriterAgentCapabilityTests` variable-capture tests (`InjectsBrandProfileIntoVariables`, `NamespacesParametersUnderTaskKey`) correctly updated callback signatures from `(string, string, Dict)` to `(string, Dict)` to match `RenderRawAsync`.

### Missing test scenario:
- **`LoadLevel2` returns empty string:** If `ExtractLevel2Body` returns `""` (no Level 2 section in SKILL.md), `RenderRawAsync` receives an empty template. This would produce an empty system prompt sent to the sidecar. Not necessarily a bug — the sidecar may handle it — but there's no test asserting what happens. Consider whether this should return a failure or is an acceptable edge case.

---

## Verdict: APPROVE

No critical or high-severity issues. The implementation correctly follows the spec for skill loading, modelId threading, and system.liquid removal. Test coverage is thorough with 8 new tests plus updates to all 5 existing test files. The minor items (duplication, theory mock pattern, empty Level2 edge case) are non-blocking.
