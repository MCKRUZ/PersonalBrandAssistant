# Section 06 — Sidecar Prompt Extensions

## Overview

Two targeted extensions to existing services:

1. **`ISidecarClient.SendTaskAsync`** — add `string? modelId` so skills can forward a preferred model to the sidecar.
2. **`IPromptTemplateService.RenderRawAsync`** — render a raw Liquid template string with the same pipeline as file-based templates, including `{{ brand_voice_block }}` injection.

Both changes are fully backward-compatible — all existing callers pass null / are unaffected.

**Dependencies:** section-02 (domain interfaces, `SkillDefinition.ModelId` must exist)
**Blocks:** section-08 (AgentCapabilityBase), section-09 (ObservabilityMiddleware)
**Parallelizable with:** section-03, section-05

---

## Files to Modify

| File | Action |
|------|--------|
| `src/PersonalBrandAssistant.Application/Common/Interfaces/ISidecarClient.cs` | Add `string? modelId` to `SendTaskAsync` |
| `src/PersonalBrandAssistant.Application/Common/Interfaces/IPromptTemplateService.cs` | Add `RenderRawAsync` method |
| `src/PersonalBrandAssistant.Infrastructure/Services/SidecarClient.cs` | Include `"modelId"` (camelCase) in outbound JSON when non-null |
| `src/PersonalBrandAssistant.Infrastructure/Services/PromptTemplateService.cs` | Implement `RenderRawAsync` |
| `tests/.../Services/SidecarClientTests.cs` | Extend with modelId wire format tests |
| `tests/.../Services/PromptTemplateServiceTests.cs` | Extend with `RenderRawAsync` tests |
| `tests/.../Mocks/MockSidecarClient.cs` | Add `modelId` parameter (accept and ignore) |

---

## Tests First

### SidecarClient — modelId wire format

The existing `SidecarClientTests` uses a real in-process WebSocket server to capture raw JSON frames. New tests follow the same pattern: start test server, call `SendTaskAsync` with non-null `modelId`, capture the `send-message` frame on the server side, inspect the `payload` property.

```
SendTaskAsync_WithModelId_IncludesModelIdInPayload
  // Assert payload["modelId"] exists and equals the passed value.

SendTaskAsync_WithModelId_UsesExactFieldNameModelIdCamelCase
  // Assert exact JSON property name is "modelId" (camelCase).
  // Assert payload.TryGetProperty("modelId") succeeds.
  // Assert payload.TryGetProperty("model_id") fails.

SendTaskAsync_NullModelId_OmitsFieldFromPayload
  // Assert outbound payload does NOT contain "modelId".

SendTaskAsync_NullModelId_BackwardCompatible
  // Stream completes normally when modelId is null.
```

The exact camelCase test is critical — the sidecar node.js process parses by exact name.

### PromptTemplateService — RenderRawAsync

Extend existing `PromptTemplateServiceTests`. Reuse the `_tempDir`, `_hostEnv`, `_logger` helpers already in the class.

```
RenderRawAsync_SimpleTemplate_RendersVariables
  // Pass "Hello {{ name }}" with variables = { "name": "World" }.
  // Assert result == "Hello World".

RenderRawAsync_BrandVoiceBlock_InjectedCorrectly
  // Write shared/brand-voice.liquid to _tempDir with content "Voice: {{ brand.Name }}".
  // Call RenderRawAsync with template "{{ brand_voice_block }}" and brand variable.
  // Assert result contains rendered brand voice content.

RenderRawAsync_InvalidLiquidSyntax_ThrowsOrReturnsError
  // Pass "{% if unclosed %}" as template.
  // Assert throws InvalidOperationException.

RenderRawAsync_EmptyTemplate_ReturnsEmpty
  // Pass "" with empty variables.
  // Assert result == "".
```

---

## Implementation

### 1. ISidecarClient — Add modelId Parameter

In `ISidecarClient.cs`, add `string? modelId` as the fourth positional parameter (before `CancellationToken`):

```csharp
IAsyncEnumerable<SidecarEvent> SendTaskAsync(
    string task, string? systemPrompt, string? sessionId, string? modelId, CancellationToken ct);
```

`CancellationToken` stays last (required for `[EnumeratorCancellation]`). Update all existing call sites to pass `null`.

### 2. SidecarClient — Serialize modelId in Outbound Payload

Update `SidecarClient.SendTaskAsync` signature. Include `"modelId"` in the outbound JSON payload when non-null. The `_jsonOptions` already use `JsonNamingPolicy.CamelCase`, so a C# property named `modelId` serializes as `"modelId"`.

Consider using a `Dictionary<string, object?>` builder approach rather than a large switch expression for the three nullable parameters (`systemPrompt`, `sessionId`, `modelId`) — cleaner for N optional fields and consistent if more are added later.

Omit null keys from the payload rather than including them as `null`.

### 3. IPromptTemplateService — Add RenderRawAsync

```csharp
Task<string> RenderRawAsync(string templateContent, Dictionary<string, object> variables);
```

### 4. PromptTemplateService — Implement RenderRawAsync

`RenderRawAsync` renders an in-memory Liquid template string using the same Fluid pipeline as file-based templates. The key requirement: `{{ brand_voice_block }}` injection must work identically — SKILL.md bodies rely on this.

Implementation steps:
1. Parse `templateContent` with `new FluidParser().TryParse(...)`. If parse fails, throw `InvalidOperationException`.
2. Build `TemplateContext(_templateOptions)`.
3. Inject `brand_voice_block`: reuse the same logic already in `RenderAsync` — check if `shared/brand-voice.liquid` exists (via cache), render it, set `brand_voice_block` on the context.
4. Set all `variables` on the context.
5. Return `template.RenderAsync(context)`.

Do not cache the parsed template — `RenderRawAsync` is called with SKILL.md Level 2 body, which is already cached by `SkillRegistry`.

**Extract a private `InjectBrandVoiceAsync(TemplateContext, Dictionary<string, object>)` helper** to avoid duplicating the brand voice injection logic from `RenderAsync`. This is a local refactor within the same file — acceptable scope.

---

## Backward Compatibility

All existing `SendTaskAsync` callers pass `null` for `modelId` until section-08. All existing `RenderAsync` callers are unaffected — `RenderRawAsync` is additive.

After this section, `dotnet build` must pass with zero errors. Run `dotnet test` to confirm existing tests still pass (update any `SidecarClientTests` call sites that use three arguments to pass `null` as the fourth).

---

## Acceptance Criteria

1. `dotnet build` passes zero errors.
2. All four `RenderRawAsync` tests pass.
3. All four `SendTaskAsync` modelId wire format tests pass — including exact camelCase field name assertion.
4. All pre-existing `SidecarClientTests` and `PromptTemplateServiceTests` pass.
5. `RenderRawAsync` with `{{ brand_voice_block }}` produces rendered brand voice content when `shared/brand-voice.liquid` is present.

---

## Actual Implementation Notes

**Files modified:**
- `src/PersonalBrandAssistant.Infrastructure/Services/SidecarClient.cs` — removed warning log, replaced 4-case switch with dict builder including modelId
- `src/PersonalBrandAssistant.Infrastructure/Services/PromptTemplateService.cs` — extracted `InjectBrandVoiceAsync`, implemented `RenderRawAsync`, added static `FluidParser _parser` field
- `tests/.../Services/SidecarClientTests.cs` — 4 new modelId wire format tests
- `tests/.../Services/PromptTemplateServiceTests.cs` — 5 new RenderRawAsync tests (4 planned + null template)

**Deviations from plan:**
- Added `private static readonly FluidParser _parser` to eliminate per-call allocation (code review)
- Added `RenderRawAsync_NullTemplate_ReturnsEmpty` test to document null-tolerant behavior (code review)

**Final test count:** 34/34 passing (SidecarClientTests + PromptTemplateServiceTests)
