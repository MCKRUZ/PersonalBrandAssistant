# Section 09 — Observability Middleware

## Overview

This section implements the OpenTelemetry instrumentation layer for agent execution via the decorator pattern — wrapping `ISidecarClient` so that all agent calls are instrumented without coupling agent logic to telemetry.

**Dependencies (must be complete before starting):**
- section-02-domain-interfaces — `ISidecarClient` interface with `modelId` parameter on `SendTaskAsync`
- section-06-sidecar-prompt-extensions — `SidecarClient` concrete implementation with updated `SendTaskAsync` signature

**Parallelizable with:** section-08-capability-base

**Blocks:** section-10-di-wiring

---

## Background

PBA currently has zero observability. Agent turns can fail silently, token costs are invisible at the trace level, and HTTP requests cannot be correlated with LLM calls. This section adds OpenTelemetry distributed tracing via the decorator pattern — wrapping `ISidecarClient` so that all agent calls are instrumented without coupling agent logic to telemetry.

The decorator approach means `AgentCapabilityBase` never imports anything from `System.Diagnostics.Activity` directly. All span management lives in `ObservabilityMiddleware`.

### Telemetry Privacy Requirement

Non-negotiable: **never emit raw prompt text, task content, or response text in any span attribute or event.** Error status messages must use generic strings ("sidecar error", "agent execution failed") rather than raw exception messages, which may contain prompt fragments.

---

## Files to Create

### New Files

```
src/PersonalBrandAssistant.Infrastructure/
  Agents/
    AgentTelemetry.cs
  Services/
    ObservabilityMiddleware.cs

tests/PersonalBrandAssistant.Infrastructure.Tests/
  Services/
    ObservabilityMiddlewareTests.cs
    ObservabilityTelemetryIntegrationTests.cs
```

Note: DI registration of `ObservabilityMiddleware` as the `ISidecarClient` decorator is handled in section-10-di-wiring. Program.cs OTel wiring is also handled in section-10.

---

## Tests First

### Test File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ObservabilityMiddlewareTests.cs`

**Framework:** xUnit + Moq  
**Naming convention:** `MethodName_Scenario_ExpectedResult`

Write these tests before implementing `ObservabilityMiddleware`. Use `ActivityListener` to capture activities.

```
# Basic delegation — inner client must be called for all ISidecarClient members
SendTaskAsync_DelegatesToInnerClient
ConnectAsync_DelegatesToInnerClient
IsConnected_DelegatesToInnerClient

# Span creation
SendTaskAsync_StartsActivity_WithCorrectSpanName
    — span name must be exactly "sidecar.send_task"
SendTaskAsync_SetsTokenAttributes_FromTaskCompleteEvent
    — gen_ai.usage.input_tokens, gen_ai.usage.output_tokens,
      gen_ai.usage.cache_read_tokens, cost_usd all sourced from TaskCompleteEvent
SendTaskAsync_SetsHasSystemPromptAttribute
    — has_system_prompt = true when systemPrompt non-null, false when null

# Span lifecycle — Activity must be disposed in all exit paths
SendTaskAsync_ConsumerBreaksEarly_SpanIsClosed
    — consumer calls break after first yielded event; verify Activity is disposed
SendTaskAsync_CancellationToken_SpanIsClosedWithCancellation
    — CancellationTokenSource.Cancel() mid-stream; verify Activity disposed and status set
SendTaskAsync_InnerThrows_SpanIsClosedWithError
    — inner client throws; verify Activity disposed and status = Error

# Privacy assertions
SendTaskAsync_NoPromptContentInSpanAttributes
    — iterate all span tags/attributes; assert none contain the system prompt string
SendTaskAsync_ErrorStatus_UsesGenericMessage
    — exception message containing prompt text → span status description must be "sidecar error"
    — NOT the raw exception message
```

### Test File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Services/ObservabilityTelemetryIntegrationTests.cs`

Use `ActivityListener` to capture emitted spans from a real `ObservabilityMiddleware` instance backed by a mock inner client.

```
# Span emission
AgentCapabilityExecution_EmitsAgentExecuteSpan
AgentCapabilityExecution_SpanHasCapabilityTypeAttribute
AgentCapabilityExecution_SpanHasSkillIdAttribute
AgentCapabilityExecution_SpanHasCostUsdAttribute

# Nested spans
AgentCapabilityExecution_SidecarSpanIsChildOfAgentSpan
    — verify sidecar.send_task span ParentId == agent.{type}.execute span Id
SkillLoad_EmitsSkillLoadSpan_OnlyOnFirstAccess
    — first call to LoadLevel2 emits skill.load span; second call emits no span
SkillLoad_CacheHit_EmitsNoSpan

# Privacy assertion (run against all collected spans)
AllSpans_ContainNoPromptText
AllSpans_ContainNoResponseText
    — iterate all Activity.Tags across all collected spans; assert none contain known prompt phrases
```

---

## Implementation

### `AgentTelemetry.cs`

Location: `src/PersonalBrandAssistant.Infrastructure/Agents/AgentTelemetry.cs`

A static class holding the shared `ActivitySource`. All custom spans in the system are created from this single source.

```csharp
public static class AgentTelemetry
{
    public const string SourceName = "PersonalBrandAssistant.Agents";
    public static readonly ActivitySource Source = new(SourceName, "1.0.0");
}
```

`AddSource(AgentTelemetry.SourceName)` is required in the OTel builder in `Program.cs` (wired in section-10). Without this registration, all custom spans are silently dropped — this is a common OTel gotcha.

### `ObservabilityMiddleware.cs`

Location: `src/PersonalBrandAssistant.Infrastructure/Services/ObservabilityMiddleware.cs`

Implements `ISidecarClient` as a decorator wrapping the concrete `SidecarClient`. Constructor accepts `ISidecarClient inner` and `ILogger<ObservabilityMiddleware> logger`.

**Delegation methods** — all `ISidecarClient` members except `SendTaskAsync` call through to `_inner` directly with no modification.

**`SendTaskAsync` implementation:**

1. Start a `sidecar.send_task` Activity via `AgentTelemetry.Source.StartActivity("sidecar.send_task")`.
2. Set `has_system_prompt` tag immediately (bool: `systemPrompt != null`).
3. Wrap the inner enumerator in `try/finally`:
   - In the loop, `yield return` each event as it arrives.
   - When a `TaskCompleteEvent` is observed, set token/cost attributes on the Activity.
   - `finally` block: `activity?.Dispose()`.
4. On cancellation detected mid-stream: set `ActivityStatusCode.Error` with description `"cancelled"`, then dispose in `finally`.
5. On inner exception: set `ActivityStatusCode.Error` with description `"sidecar error"` (not the raw exception message), then `throw` — the `finally` still disposes.

**Span attributes to set:**

| Attribute | Source | When |
|-----------|--------|------|
| `has_system_prompt` | `systemPrompt != null` | On start |
| `gen_ai.usage.input_tokens` | `TaskCompleteEvent.InputTokens` | On TaskCompleteEvent |
| `gen_ai.usage.output_tokens` | `TaskCompleteEvent.OutputTokens` | On TaskCompleteEvent |
| `gen_ai.usage.cache_read_tokens` | `TaskCompleteEvent.CacheReadTokens` | On TaskCompleteEvent |
| `cost_usd` | `TaskCompleteEvent.Cost` | On TaskCompleteEvent |

Do not add `capability_type`, `skill_id`, or `model_id` in this middleware — those belong on the `agent.{type}.execute` span, which is created by `AgentCapabilityBase` (section-08).

**Privacy rule:** The only string attributes allowed are the boolean `has_system_prompt` and error status descriptions. No task text, no system prompt text, no response text.

**Signature stub:**

```csharp
public sealed class ObservabilityMiddleware : ISidecarClient
{
    /// <summary>
    /// Wraps SendTaskAsync with a sidecar.send_task OTel span.
    /// Sets token/cost attributes from TaskCompleteEvent.
    /// Never emits prompt or response text in span attributes.
    /// Disposes Activity in try/finally to handle early-break and cancellation.
    /// </summary>
    public async IAsyncEnumerable<SidecarEvent> SendTaskAsync(
        string task,
        string? systemPrompt,
        string? sessionId,
        string? modelId,
        [EnumeratorCancellation] CancellationToken ct) { ... }
}
```

Note the `[EnumeratorCancellation]` attribute is required on the `ct` parameter for correct async enumerable cancellation propagation.

### `skill.load` Span

The `skill.load` span is NOT created in `ObservabilityMiddleware`. It is created inside `SkillRegistry.LoadLevel2`'s `Lazy<string>` factory (section-04-skill-registry). It is included here because the integration tests in this section verify it. The factory runs exactly once per skill ID — the span only appears on the cold path.

```csharp
// Inside Lazy<string> factory (in SkillRegistry — for reference only):
using var activity = AgentTelemetry.Source?.StartActivity("skill.load");
activity?.SetTag("skill_id", skillId);
// ... read file ...
```

### `agent.{type}.execute` Span

This span is created in `AgentCapabilityBase.ExecuteAsync` (section-08), not here. It wraps the full agent turn and is the parent of the `sidecar.send_task` span. Included for context so integration tests make sense.

---

## NuGet Packages Required

These should already be added by section-01-project-setup. If not, add to `Infrastructure.csproj`:

```
OpenTelemetry
OpenTelemetry.Extensions.Hosting
OpenTelemetry.Instrumentation.AspNetCore
OpenTelemetry.Exporter.Console
OpenTelemetry.Exporter.OpenTelemetryProtocol
```

Test project only:
```
OpenTelemetry.Exporter.InMemory
```

---

## Key Implementation Risks

**Activity disposal on early break:** When an `IAsyncEnumerable` consumer calls `break`, the enumerator's `DisposeAsync` is called, which triggers the `finally` block. The `try/finally` pattern correctly handles this. Do not use `using var activity` at the method level — it would dispose before the enumerator is fully consumed by callers that break early. Manage the Activity lifetime manually within the `try/finally`.

**Silent span dropping:** If `AddSource(AgentTelemetry.SourceName)` is omitted from the OTel builder, `StartActivity` returns null and all spans are silently dropped. The code handles null Activity gracefully (all attribute sets use `activity?.SetTag(...)`) but no spans appear. The integration test catches this.

**Generic error messages:** Exception messages may contain prompt fragments. Always use hardcoded strings for `SetStatus` descriptions. The privacy test in `ObservabilityMiddlewareTests` explicitly verifies this.

---

## Checklist

- [x] `AgentTelemetry.cs` created with `SourceName` const and static `ActivitySource`
- [x] `ObservabilityMiddleware` implements `ISidecarClient`
- [x] Constructor accepts `ISidecarClient inner` (logger removed — dead code, see review MINOR-1)
- [x] All non-`SendTaskAsync` members delegate to `_inner` directly
- [x] `SendTaskAsync` starts `sidecar.send_task` activity
- [x] `has_system_prompt` tag set on activity start
- [x] Token/cost attributes set when `TaskCompleteEvent` observed
- [x] Activity disposed in `finally` block (handles early break, cancellation, exception)
- [x] Error status uses hardcoded strings, not raw exception messages
- [x] `[EnumeratorCancellation]` on `ct` parameter
- [x] All unit tests pass (12 cases in ObservabilityMiddlewareTests)
- [x] All integration tests pass (9 tests in ObservabilityTelemetryIntegrationTests)
- [x] `dotnet build` zero errors, zero warnings

## Notes

- `yield return` inside `try/catch` is illegal in C# even for async iterators (CS1626). Used manual enumerator pattern: `MoveNextAsync` in inner `try/catch`, `yield return` in outer `try/finally`.
- `[Collection("ObservabilityTests")]` + `[CollectionDefinition]` marker class added to prevent cross-test span pollution from parallel xUnit class instantiation.
- `ConcurrentBag<Activity>` used in tests because `ActivityStopped` fires from any thread (async context).
- `agent.{agentName}.execute` span added to `AgentCapabilityBase.ExecuteAsync` (not in original section-08 plan but required for integration tests).
- `skill.load` span added to `SkillRegistry.LoadLevel2` Lazy factory — emits only on cold path.
- DI registration of `ObservabilityMiddleware` as `ISidecarClient` decorator is handled in section-10.
