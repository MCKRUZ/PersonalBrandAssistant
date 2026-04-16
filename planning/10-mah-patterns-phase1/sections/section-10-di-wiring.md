# Section 10 — DI Wiring

## Overview

This is the final integration section. All prior sections (04, 05, 08, 09) must be complete before starting here. This section wires all new components into the DI container, configures the OpenTelemetry pipeline in `Program.cs`, and updates the test mock so the full test suite passes.

**Dependencies (must be complete before starting):**
- section-04 — `ISkillRegistry`, `SkillRegistry`, `SkillOptions`
- section-05 — `IContextBudgetTracker`, `ContextBudgetTracker`, `ContextBudgetOptions`
- section-08 — updated capability classes consuming `ISkillRegistry`
- section-09 — `ObservabilityMiddleware`, `AgentTelemetry`

---

## Files to Modify

| File | Action |
|------|--------|
| `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs` | Add new registrations; replace `ISidecarClient` registration with decorator |
| `src/PersonalBrandAssistant.Api/Program.cs` | Add `AddOpenTelemetry()` wiring |
| `src/PersonalBrandAssistant.Api/appsettings.json` | Add `Telemetry`, `Skills`, `ContextBudget` sections |
| `src/PersonalBrandAssistant.Api/appsettings.Development.json` | Set `Telemetry:ConsoleExporter: true` |
| `tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockSidecarClient.cs` | Add `modelId` parameter to `SendTaskAsync` |

---

## Tests First

The "tests" for this section are verification tests, not unit tests for new logic. The primary assertions are:

**Full regression suite must pass:**

```
AgentOrchestratorTests — all routing, budget, retry tests pass
SidecarClientTests — existing tests pass (after adding modelId param to mocks)
TokenTrackerTests — unchanged, must pass
MockSidecarClient — update signature to add modelId, all usages compile
```

**Startup validation tests** (already written in section-04 — SkillRegistryTests):
```
Startup_AllRequiredSkillsPresent_NoException
Startup_LogsSHA256HashOfEachFile
Startup_LogsDiscoveredSkillCount
```

**In-memory OTel integration tests** (already written in section-09 — ObservabilityTelemetryIntegrationTests):
```
AgentCapabilityExecution_EmitsAgentExecuteSpan
AgentCapabilityExecution_SidecarSpanIsChildOfAgentSpan
AllSpans_ContainNoPromptText
```

Run `dotnet test` after each change. The suite must be green before this section is considered done.

---

## Step 1: Update MockSidecarClient

File: `tests/PersonalBrandAssistant.Infrastructure.Tests/Mocks/MockSidecarClient.cs`

Add `string? modelId` as the fourth parameter to `SendTaskAsync`, matching the updated `ISidecarClient` signature added in section-06:

```csharp
// ISidecarClient.SendTaskAsync signature (from section-06):
IAsyncEnumerable<SidecarEvent> SendTaskAsync(
    string task, string? systemPrompt, string? sessionId, string? modelId, CancellationToken ct);
```

The mock implementation should accept and ignore `modelId` unless a specific test needs to assert on it.

Do this step first — it unblocks compilation of all other test files.

---

## Step 2: Infrastructure/DependencyInjection.cs

File: `src/PersonalBrandAssistant.Infrastructure/DependencyInjection.cs`

Register all new services in the order specified below. Order matters — `SidecarClient` must be registered as a concrete singleton before `ISidecarClient` is registered as a factory that depends on it.

Registration order (add these, in order, to the existing `AddInfrastructureDependencies` extension method):

1. **SkillOptions** — bind from `"Skills"` config section:
   ```csharp
   services.Configure<SkillOptions>(configuration.GetSection(SkillOptions.SectionName));
   ```

2. **ContextBudgetOptions** — bind from `"ContextBudget"` config section:
   ```csharp
   services.Configure<ContextBudgetOptions>(configuration.GetSection(ContextBudgetOptions.SectionName));
   ```

3. **ISkillRegistry** — Singleton:
   ```csharp
   services.AddSingleton<ISkillRegistry, SkillRegistry>();
   ```

4. **SidecarClient concrete type** — Singleton (registered by concrete type, not interface):
   ```csharp
   services.AddSingleton<SidecarClient>();
   ```

5. **ISidecarClient via ObservabilityMiddleware decorator** — remove the existing `AddSingleton<ISidecarClient, SidecarClient>()` line and replace with:
   ```csharp
   services.AddSingleton<ISidecarClient>(sp =>
       new ObservabilityMiddleware(
           sp.GetRequiredService<SidecarClient>(),
           sp.GetRequiredService<ILogger<ObservabilityMiddleware>>()));
   ```

6. **IContextBudgetTracker** — Scoped (one instance per request, single-threaded):
   ```csharp
   services.AddScoped<IContextBudgetTracker, ContextBudgetTracker>();
   ```

The existing `AddSingleton<ISidecarClient, SidecarClient>()` line **must be removed** — leaving both registrations would result in two `ISidecarClient` resolutions, bypassing the decorator.

---

## Step 3: Api/Program.cs — OpenTelemetry Wiring

File: `src/PersonalBrandAssistant.Api/Program.cs`

Add the OpenTelemetry registration after infrastructure services are added but before `builder.Build()`:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddSource(AgentTelemetry.SourceName);  // REQUIRED — without this, custom spans are silently dropped
        if (builder.Configuration.GetValue<bool>("Telemetry:ConsoleExporter"))
            tracing.AddConsoleExporter();
        if (builder.Configuration["Telemetry:OtlpEndpoint"] is { } endpoint)
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(endpoint));
    });
```

`AgentTelemetry.SourceName` is `"PersonalBrandAssistant.Agents"` (defined in section-09). The `AddSource` call is non-optional — without it, the `ActivitySource` creates activities that are immediately sampled out and dropped.

**Required using:** `using PersonalBrandAssistant.Infrastructure.Agents;`

---

## Step 4: Configuration

File: `src/PersonalBrandAssistant.Api/appsettings.json`

Add these three top-level sections. Do not overwrite existing sections:

```json
{
  "Telemetry": {
    "ConsoleExporter": false,
    "OtlpEndpoint": null
  },
  "Skills": {
    "SkillsPath": ""
  },
  "ContextBudget": {
    "NudgeThreshold": 80000,
    "StopThreshold": 180000,
    "HardMaxTokens": 200000
  }
}
```

`SkillsPath` empty string means "use the default" — `SkillOptions` defaults to `Path.Combine(AppContext.BaseDirectory, "skills")` at runtime. The config entry is present so operators can override it without a code change.

File: `src/PersonalBrandAssistant.Api/appsettings.Development.json`

Add:
```json
{
  "Telemetry": {
    "ConsoleExporter": true
  }
}
```

This enables console span output in development without OTLP infrastructure.

---

## Verification Checklist

Run these in order. Each must pass before moving to the next:

1. `dotnet build` — zero errors, zero new warnings.
2. `dotnet test` — full suite green. Pay particular attention to any test that previously mocked `ISidecarClient.SendTaskAsync` — all call sites must now pass the `modelId` argument.
3. Startup logs (run the API locally): confirm log lines showing discovery of exactly 5 SKILL.md files and their SHA-256 hashes.
4. In Development mode: confirm OTel console exporter emits `sidecar.send_task` spans when an agent executes.

---

## Known Limitation

Single-file publish (`dotnet publish --self-contained -p:PublishSingleFile=true`) is not a supported deployment mode in Phase 1. The `skills/` directory is copied alongside the output via `<Content CopyToOutputDirectory="Always" />` (section-01), which does not embed files into a single executable. Document this in deployment runbooks.

---

## Checklist

- [x] `MockSidecarClient.SendTaskAsync` has `modelId` parameter — already done from prior session
- [x] `SkillOptions` bound from `"Skills"` config section
- [x] `ContextBudgetOptions` bound from `"ContextBudget"` config section
- [x] `ISkillRegistry` registered as Singleton
- [x] `SidecarClient` registered as concrete Singleton (with decorator-only comment)
- [x] Old `AddSingleton<ISidecarClient, SidecarClient>()` removed
- [x] `ISidecarClient` registered via `ObservabilityMiddleware` factory
- [x] `IContextBudgetTracker` registered as Scoped
- [x] `AddOpenTelemetry()` wired in `Program.cs` with `AddSource(AgentTelemetry.SourceName)`
- [x] `appsettings.json` has Telemetry, Skills, ContextBudget sections (already present from prior work)
- [x] `appsettings.Development.json` has `ConsoleExporter: true` (already present)
- [x] `dotnet build` zero errors
- [x] 26 section-relevant tests pass (21 observability + 5 DI registration)

## Notes

- `using OpenTelemetry.Trace;` required in `Program.cs` — extension methods (`AddAspNetCoreInstrumentation`, `AddConsoleExporter`, `AddOtlpExporter`) are in this namespace and are NOT in the global implicit usings. All three packages must also be directly referenced in the Api.csproj (OTel extension methods don't flow transitively across project references).
- `OpenTelemetry.Instrumentation.AspNetCore` 1.11.2 does not exist on NuGet — intentional version gap vs 1.11.2 for other OTel packages. Commented in csproj.
- `SkillOptions.SkillsPath` default changed from computed path to `""` — `SkillRegistry` is now the single source of truth for the fallback to `AppContext.BaseDirectory/skills`. Eliminates double-default across 3 files.
- `SkillRegistry` empty-path fallback was required to fix `AgentServiceRegistrationTests` which use `WebApplicationFactory<Program>`. The config binds `"SkillsPath": ""` overriding the old C# default, which previously caused `DirectoryNotFoundException` at DI resolution time.
- `ObservabilityMiddleware` constructor takes only `ISidecarClient inner` — the section plan's factory snippet incorrectly included `ILogger<ObservabilityMiddleware>` (removed in section-09 code review MINOR-1). DI factory updated accordingly.
